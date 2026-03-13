using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Spark.ViewModels;
///////////////////////////////////////////////
namespace Spark;

sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    readonly GenerationService m_genService;
    readonly LoraService m_loraService;
    readonly LogViewModel m_log;
    readonly GenerationSettingsViewModel m_settings;
    readonly LoraViewModel m_lora;
    readonly DetailViewModel m_detail;
    readonly GalleryViewModel m_gallery;
    readonly StatusViewModel m_status;

    SparkProject m_project;
    DocumentStore m_docs;
    List<ArtPrompt> m_prompts = [];
    ImageCatalog? m_catalog;
    PreferenceTracker? m_preferences;
    string m_storyContext = "";

    // ── Sub-VMs exposed as DataContext sources for panels ────────

    public LogViewModel Log => m_log;
    public GenerationSettingsViewModel Settings => m_settings;
    public LoraViewModel Lora => m_lora;
    public DetailViewModel Detail => m_detail;
    public GalleryViewModel Gallery => m_gallery;
    public StatusViewModel Status => m_status;

    // ── Properties that remain on the root VM ───────────────────

    public string ProjectName => m_project?.Name ?? "";

    // ── Commands ────────────────────────────────────────────────

    public ICommand GenerateAllCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand NewProjectCommand { get; }
    public ICommand SwitchProjectCommand { get; }
    public ICommand EditPromptsCommand { get; }
    public ICommand EditStoryCommand { get; }
    public ICommand OptionsCommand { get; }

    // ── Constructor ─────────────────────────────────────────────

    public MainViewModel(
        GenerationService genService,
        LoraService loraService,
        LogViewModel log,
        GenerationSettingsViewModel settings,
        LoraViewModel lora,
        DetailViewModel detail,
        GalleryViewModel gallery,
        StatusViewModel status)
    {
        m_genService = genService;
        m_loraService = loraService;
        m_log = log;
        m_settings = settings;
        m_lora = lora;
        m_detail = detail;
        m_gallery = gallery;
        m_status = status;

        // Wire service events
        m_genService.LogMessage += msg => m_log.Log(msg);
        m_genService.StatusChanged += () => UpdateStatus();
        m_genService.GeneratingChanged += gen => m_status.IsGenerating = gen;

        // Wire LoRA trigger word injection into detail's prompt augment
        m_lora.TriggerWordsInjected += tw => m_detail.InjectTriggerWords(tw);

        // Wire detail image seen marking
        m_detail.ImageSeen += MarkSeen;

        // Root commands
        GenerateAllCommand = new RelayCommand(_ => GenerateAll(), _ => !m_status.IsGenerating);
        CancelCommand = new RelayCommand(_ => m_genService.Cancel(), _ => m_status.IsGenerating);
        RefreshCommand = new RelayCommand(_ => RebuildStacks());
        NewProjectCommand = new RelayCommand(_ => NewProject());
        SwitchProjectCommand = new RelayCommand(_ => SwitchProject());
        EditPromptsCommand = new RelayCommand(_ => EditPrompts());
        EditStoryCommand = new RelayCommand(_ => EditDocuments());
        OptionsCommand = new RelayCommand(_ => ShowOptions());

        // Detail commands — wired here because they need access to catalog/preferences/prompts
        m_detail.SaveDetailCommand = new RelayCommand(_ => SaveDetail(), _ => m_detail.DetailImage is not null);
        m_detail.DeleteDetailCommand = new RelayCommand(_ => DeleteDetail(), _ => m_detail.DetailImage is not null);
        m_detail.RegenDetailCommand = new RelayCommand(_ => EnqueueRegen(), _ => m_detail.DetailImage is not null);
        m_detail.RateDetailCommand = new RelayCommand(p => RateDetail(p), _ => m_detail.DetailImage is not null);
        m_detail.VariantBiggerCommand = new RelayCommand(_ => EnqueueUpscaleVariant(), _ => m_detail.DetailImage is { Seed: > 0 });
        m_detail.VariantSmallerCommand = new RelayCommand(_ => EnqueueVariant(0.75), _ => m_detail.DetailImage is { Seed: > 0 });
        m_detail.CreativeRegenCommand = new RelayCommand(_ => CreativeRegen(), _ => m_detail.DetailImage is not null);
        m_detail.DirectedRegenCommand = new RelayCommand(p => DirectedRegen(p as string));
        m_detail.ShowLightboxCommand = new RelayCommand(_ => OpenLightbox(), _ => m_detail.DetailImage is not null);

        // Bootstrap project
        m_project = SparkProject.FindOrCreate(AppContext.BaseDirectory);
        m_docs = new DocumentStore(m_project.ProjectDir);
        m_log.Log($"Project: {m_project.Name} ({m_project.ProjectDir})");

        m_settings.ApplyDefaults(m_project.DefaultSettings);

        string[] docPatterns = m_project.StoryFiles.Length > 0
            ? m_project.StoryFiles : ["*.txt", "*.md"];
        int docsIngested = m_docs.Ingest(docPatterns);
        if (docsIngested > 0) m_log.Log($"Indexed {docsIngested} documents ({m_docs.Count} total)");

        m_storyContext = StoryContext.LoadProjectContext(m_project.ProjectDir);
        m_log.Log($"Story context: {(m_storyContext.Length > 0 ? "loaded" : "none found")}");

        string promptsFile = m_project.ResolvedPromptsFile;
        if (File.Exists(promptsFile))
        {
            m_prompts = PromptParser.Parse(promptsFile);
            m_log.Log($"Loaded {m_prompts.Count} prompts");
        }
        else
            m_log.Log($"{m_project.PromptsFile} not found.");

        string outputDir = m_project.ResolvedOutputDir;
        m_catalog = new ImageCatalog(outputDir);
        m_preferences = new PreferenceTracker(outputDir);

        int ingested = m_catalog.IngestExisting(m_prompts);
        if (ingested > 0) m_log.Log($"Ingested {ingested} existing images");

        RebuildStacks();
        UpdatePreferencesSummary();
        m_lora.LoadLoras();
    }

    // ── Story context ───────────────────────────────────────────

    string BuildContextForPrompt(string promptText)
    {
        if (!m_settings.InjectStoryContext) return "";
        string ragContext = m_docs.BuildContext(promptText);
        if (ragContext.Length > 0)
        {
            string glossary = StoryContext.UniverseGlossary;
            return glossary.Length > 0 ? glossary + " " + ragContext : ragContext;
        }
        return StoryContext.UniverseGlossary + " " + m_storyContext;
    }

    // ── Gallery ─────────────────────────────────────────────────

    void RebuildStacks()
    {
        m_gallery.RebuildStacks(m_prompts, m_catalog);
        UpdateStatus();
    }

    void AddResultToStacks(GenerateResult result, ArtPrompt prompt,
        ImageGeneratorSettings settings, string preset, string? loraTag, string? augment, string? modifiedPrompt = null)
    {
        m_gallery.AddResultToStacks(result, prompt, settings, preset, loraTag, augment, m_catalog, modifiedPrompt);
        RebuildStacks();
        m_gallery.SelectStack(prompt.Number);
    }

    // ── Detail actions ──────────────────────────────────────────

    void RateDetail(object? param)
    {
        if (m_detail.DetailImage is null || m_catalog is null || m_preferences is null) return;
        if (param is not string s || !int.TryParse(s, out int rating)) return;

        int prevRating = m_detail.DetailImage.Rating;
        if (prevRating > 0) BackOutPreference(m_detail.DetailImage, prevRating);

        m_detail.DetailImage.Rating = Math.Clamp(rating, 1, 5);

        if (rating >= 3)
        {
            double strength = rating >= 4 ? 1.5 : 0.5;
            m_preferences.RecordPositive(m_detail.DetailImage, strength);
        }
        else
        {
            m_preferences.RecordNegative(m_detail.DetailImage, 0.5);
        }
        m_catalog.Update();

        m_gallery.Stacks.FirstOrDefault(st => st.PromptNumber == m_detail.DetailImage.PromptNumber)?.RefreshCards();
        m_log.Log($"★ Rated {m_detail.DetailImage.DisplayName}: {m_detail.DetailImage.RatingStars}");
        m_detail.NotifyRatingChanged();
        UpdatePreferencesSummary();

        if (rating <= 2)
        {
            m_log.Log($"⚡ Low rating — soft-deleting and queueing mutant regen…");
            m_detail.DetailImage.SoftDelete();
            m_catalog.Update();
            EnqueueRegen();
            RebuildStacks();
        }
    }

    void BackOutPreference(ImageRecord record, int prevRating)
    {
        if (m_preferences is null) return;
        if (prevRating >= 3)
        {
            double strength = prevRating >= 4 ? 1.5 : 0.5;
            m_preferences.RecordNegative(record, strength);
        }
        else
        {
            m_preferences.RecordPositive(record, 0.5);
        }
    }

    void SaveDetail()
    {
        if (m_detail.DetailImage is null || m_catalog is null || m_preferences is null) return;
        m_detail.DetailImage.Saved = true;
        m_preferences.RecordPositive(m_detail.DetailImage);
        m_catalog.Update();
        m_log.Log($"💾 Saved: {m_detail.DetailImage.DisplayName}");
        UpdatePreferencesSummary();
        UpdateStatus();
    }

    void DeleteDetail()
    {
        try
        {
            if (m_detail.DetailImage is null || m_catalog is null) return;
            m_detail.DetailImage.SoftDelete();
            m_catalog.Update();
            m_log.Log($"🗑 Soft-deleted: {m_detail.DetailImage.DisplayName} (recoverable for 24h)");
            m_detail.DetailImage = null;
            RebuildStacks();
        }
        catch (Exception ex) { m_log.Log($"Error deleting: {ex.Message}"); }
    }

    void MarkSeen(ImageRecord record)
    {
        if (record.Seen || m_catalog is null) return;
        record.Seen = true;
        m_catalog.Update();
    }

    // ── Generation helpers ──────────────────────────────────────

    static ImageGeneratorSettings ApplyCreativeSettings(ImageGeneratorSettings settings)
    {
        (_, double cfgNudge, int stepsNudge, string samplerHint) = CreativeEngine.Generate();
        ImageGeneratorSettings s = settings with
        {
            Seed = -1,
            CfgScale = Math.Clamp(settings.CfgScale + cfgNudge, 3, 15),
            Steps = Math.Clamp(settings.Steps + stepsNudge, 10, 50),
        };
        return samplerHint.Length > 0 ? s with { Sampler = samplerHint } : s;
    }

    // ── Directed regen ──────────────────────────────────────────

    void DirectedRegen(string? directionLabel)
    {
        if (directionLabel is null || m_detail.DetailImage is null) return;
        ArtDirections.Direction? dir = null;
        foreach ((_, ArtDirections.Direction d) in ArtDirections.All())
            if (d.Label == directionLabel) { dir = d; break; }
        if (dir is null) return;

        ArtPrompt? prompt = m_prompts.FirstOrDefault(p => p.Number == m_detail.DetailImage.PromptNumber);
        if (prompt is null) return;

        ImageGeneratorSettings settings = m_settings.ToSettings();
        if (dir.CfgNudge.HasValue)
            settings = settings with { CfgScale = Math.Clamp(settings.CfgScale + dir.CfgNudge.Value, 3, 15) };
        if (dir.StepsNudge.HasValue)
            settings = settings with { Steps = Math.Clamp(settings.Steps + dir.StepsNudge.Value, 10, 50) };
        if (dir.NegativeAdd.Length > 0)
            settings = settings with { NegativePrompt = settings.NegativePrompt + ", " + dir.NegativeAdd };
        settings = settings with { Seed = -1 };

        string preset = m_settings.RefinePreset;
        string? loraTag = m_lora.BuildLoraTag();
        string augment = dir.PromptAdd + (m_detail.PromptAugment.Length > 0 ? ", " + m_detail.PromptAugment : "");
        int promptNum = prompt.Number;
        string contextPrefix = BuildContextForPrompt(prompt.FullText);
        string outputDir = m_project.ResolvedOutputDir;

        string queueLabel = $"🎨 {dir.Label} → {prompt.Title}";
        m_status.QueueItems.Add(queueLabel);
        m_log.Log($"🎨 Directed regen: {dir.Label} → {prompt.Title}");

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            string? finalPromptOverride = contextPrefix.Length > 0
                ? contextPrefix + "\n" + prompt.FullText : null;

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, outputDir, runIndex: nextRun, refinePreset: preset,
                promptOverride: finalPromptOverride, loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => m_log.Log(msg)), ct: ct);

            Dispatch(() => { m_status.QueueItems.Remove(queueLabel); AddResultToStacks(result, prompt, settings, preset, loraTag, augment); });
        });
    }

    // ── Creative regen ──────────────────────────────────────────

    void CreativeRegen()
    {
        if (m_detail.DetailImage is null) return;
        ArtPrompt? prompt = m_prompts.FirstOrDefault(p => p.Number == m_detail.DetailImage.PromptNumber);
        if (prompt is null) return;

        (string creativeAugment, double cfgNudge, int stepsNudge, string samplerHint) = CreativeEngine.Generate();
        ImageGeneratorSettings settings = m_settings.ToSettings() with
        {
            Seed = -1,
            CfgScale = Math.Clamp(m_settings.CfgScale + cfgNudge, 3, 15),
            Steps = Math.Clamp(m_settings.Steps + stepsNudge, 10, 50),
        };
        if (samplerHint.Length > 0) settings = settings with { Sampler = samplerHint };

        string preset = m_settings.RefinePreset;
        string? loraTag = m_lora.BuildLoraTag();
        string augment = creativeAugment + (m_detail.PromptAugment.Length > 0 ? ", " + m_detail.PromptAugment : "");
        int promptNum = prompt.Number;
        string contextPrefix = BuildContextForPrompt(prompt.FullText);
        string outputDir = m_project.ResolvedOutputDir;

        string queueLabel = $"🧬 Mutate {prompt.Title}";
        m_status.QueueItems.Add(queueLabel);
        m_log.Log($"🧬 Mutate: {prompt.Title} — {creativeAugment[..Math.Min(60, creativeAugment.Length)]}...");

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            string? finalPromptOverride = contextPrefix.Length > 0
                ? contextPrefix + "\n" + prompt.FullText : null;

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, outputDir, runIndex: nextRun, refinePreset: preset,
                promptOverride: finalPromptOverride, loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => m_log.Log(msg)), ct: ct);

            Dispatch(() => { m_status.QueueItems.Remove(queueLabel); AddResultToStacks(result, prompt, settings, preset, loraTag, augment); });
        });
    }

    // ── Regen ───────────────────────────────────────────────────

    void EnqueueRegen()
    {
        if (m_detail.DetailImage is null) return;
        ArtPrompt? prompt = m_prompts.FirstOrDefault(p => p.Number == m_detail.DetailImage.PromptNumber);
        if (prompt is null) return;

        ImageGeneratorSettings settings = m_settings.CreativeMode
            ? ApplyCreativeSettings(m_settings.ToSettings())
            : m_settings.ToSettings() with { Seed = -1 };

        string preset = m_settings.RefinePreset;
        string? loraTag = m_lora.BuildLoraTag();
        string augment = m_settings.CreativeMode
            ? CreativeEngine.PickOne() + (m_detail.PromptAugment.Length > 0 ? ", " + m_detail.PromptAugment : "")
            : m_detail.PromptAugment;
        int promptNum = prompt.Number;
        string contextPrefix = BuildContextForPrompt(prompt.FullText);
        string outputDir = m_project.ResolvedOutputDir;

        string queueLabel = $"🔄 Regen {prompt.Title}";
        m_status.QueueItems.Add(queueLabel);

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            Dispatch(() => m_log.Log($"Queue: regen {prompt.Title} run {nextRun}…"));

            string? modifiedPrompt = null;
            if (m_settings.UsePreferences && m_preferences is not null)
            {
                (modifiedPrompt, string explanation) = m_preferences.AdjustPrompt(prompt.FullText);
                if (explanation != "no adjustments" && explanation != "not enough data yet")
                    Dispatch(() => m_log.Log($"  Pref: {explanation}"));
            }

            string? finalOverride = null;
            if (contextPrefix.Length > 0 || modifiedPrompt is not null)
                finalOverride = contextPrefix + (modifiedPrompt ?? prompt.FullText);

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, outputDir, runIndex: nextRun, refinePreset: preset,
                promptOverride: finalOverride, loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => m_log.Log(msg)), ct: ct);

            Dispatch(() => { m_status.QueueItems.Remove(queueLabel); AddResultToStacks(result, prompt, settings, preset, loraTag, augment, modifiedPrompt); });
        });
    }

    void EnqueueUpscaleVariant()
    {
        if (m_detail.DetailImage is null || m_detail.DetailImage.Seed <= 0) return;
        ArtPrompt? prompt = m_prompts.FirstOrDefault(p => p.Number == m_detail.DetailImage.PromptNumber);
        if (prompt is null) return;

        int srcW = m_detail.DetailImage.SourceWidth > 0 ? m_detail.DetailImage.SourceWidth : m_settings.Width;
        int srcH = m_detail.DetailImage.SourceHeight > 0 ? m_detail.DetailImage.SourceHeight : m_settings.Height;
        (int newW, int newH) = SdxlResolutions.ScaleBucket(srcW, srcH, 1.5);

        ImageGeneratorSettings settings = new()
        {
            Width = newW, Height = newH,
            Steps = Math.Max(m_detail.DetailImage.RefinePreset != "none" ? 30 : m_settings.Steps, 25),
            CfgScale = m_settings.CfgScale,
            Sampler = m_settings.Sampler,
            Scheduler = m_settings.Scheduler,
            Seed = m_detail.DetailImage.Seed,
        };
        string preset = m_detail.DetailImage.RefinePreset;
        string? loraTag = m_detail.DetailImage.LoraTag.Length > 0 ? m_detail.DetailImage.LoraTag : m_lora.BuildLoraTag();
        string augment = m_detail.DetailImage.PromptAugment;
        long seed = m_detail.DetailImage.Seed;
        int promptNum = prompt.Number;
        string outputDir = m_project.ResolvedOutputDir;
        string originalPrompt = m_detail.DetailImage.PromptText;

        string queueLabel = $"⬆ {prompt.Title} ({newW}×{newH}, seed {seed})";
        m_status.QueueItems.Add(queueLabel);

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            Dispatch(() => m_log.Log($"Queue: upscale {prompt.Title} ({newW}×{newH}, seed {seed})…"));

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, outputDir, runIndex: nextRun, refinePreset: preset,
                promptOverride: originalPrompt, loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => m_log.Log(msg)), ct: ct);

            Dispatch(() => { m_status.QueueItems.Remove(queueLabel); AddResultToStacks(result, prompt, settings, preset, loraTag, augment); });
        });
    }

    void EnqueueVariant(double scaleFactor)
    {
        if (m_detail.DetailImage is null || m_detail.DetailImage.Seed <= 0) return;
        ArtPrompt? prompt = m_prompts.FirstOrDefault(p => p.Number == m_detail.DetailImage.PromptNumber);
        if (prompt is null) return;

        int srcW = m_detail.DetailImage.SourceWidth > 0 ? m_detail.DetailImage.SourceWidth : m_settings.Width;
        int srcH = m_detail.DetailImage.SourceHeight > 0 ? m_detail.DetailImage.SourceHeight : m_settings.Height;
        (int newW, int newH) = SdxlResolutions.ScaleBucket(srcW, srcH, scaleFactor);

        ImageGeneratorSettings settings = m_settings.ToSettings() with { Width = newW, Height = newH, Seed = m_detail.DetailImage.Seed };
        string preset = m_detail.DetailImage.RefinePreset;
        string? loraTag = m_detail.DetailImage.LoraTag.Length > 0 ? m_detail.DetailImage.LoraTag : null;
        string augment = m_detail.DetailImage.PromptAugment;
        long seed = m_detail.DetailImage.Seed;
        int promptNum = prompt.Number;
        string outputDir = m_project.ResolvedOutputDir;

        string queueLabel = $"⬇ {prompt.Title} ({newW}×{newH})";
        m_status.QueueItems.Add(queueLabel);

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            Dispatch(() => m_log.Log($"Queue: smaller variant {prompt.Title} ({newW}×{newH}, seed {seed})…"));

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, outputDir, runIndex: nextRun, refinePreset: preset,
                loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => m_log.Log(msg)), ct: ct);

            Dispatch(() => { m_status.QueueItems.Remove(queueLabel); AddResultToStacks(result, prompt, settings, preset, loraTag, augment ); });
        });
    }

    // ── Generate all ────────────────────────────────────────────

    void GenerateAll()
    {
        if (m_prompts.Count == 0) { m_log.Log("No prompts loaded."); return; }

        m_genService.ResetCts();
        m_status.IsGenerating = true;

        ImageGeneratorSettings settings = m_settings.ToSettings();
        int runs = m_settings.RunsPerPrompt;
        string preset = m_settings.RefinePreset;
        bool usePref = m_settings.UsePreferences;
        bool creative = m_settings.CreativeMode;
        string? globalLoraTag = m_lora.BuildLoraTag();
        string baseAugment = m_detail.PromptAugment;
        bool injectContext = m_settings.InjectStoryContext;
        string outputDir = m_project.ResolvedOutputDir;

        foreach (ArtPrompt prompt in m_prompts)
        {
            string? loraTag = prompt.LoraTag ?? globalLoraTag;
            string contextPrefix = injectContext ? BuildContextForPrompt(prompt.FullText) : "";

            int existingCount = m_catalog?.GetStack(prompt.Number).Count ?? 0;
            for (int run = 0; run < runs; run++)
            {
                int runIdx = existingCount + run;
                ArtPrompt p = prompt;
                int r = runIdx;
                string? capturedLoraTag = loraTag;
                string capturedContext = contextPrefix;
                m_genService.Enqueue(async ct =>
                {
                    ImageGeneratorSettings runSettings = creative ? ApplyCreativeSettings(settings) : settings;
                    string augment = baseAugment;
                    if (creative)
                    {
                        string extra = CreativeEngine.PickOne();
                        augment = augment.Length > 0 ? extra + ", " + augment : extra;
                    }

                    string? modifiedPrompt = null;
                    if (usePref && m_preferences is not null)
                    {
                        (modifiedPrompt, string explanation) = m_preferences.AdjustPrompt(p.FullText);
                        if (explanation != "no adjustments" && explanation != "not enough data yet")
                            Dispatch(() => m_log.Log($"  Pref: {explanation}"));
                    }

                    string? finalOverride = null;
                    if (capturedContext.Length > 0 || modifiedPrompt is not null)
                        finalOverride = capturedContext + (modifiedPrompt ?? p.FullText);

                    Dispatch(() => m_status.StatusText = $"Generating {p.Title} (run {r})…");

                    GenerateResult result = await m_genService.Generator.GenerateAsync(
                        p, runSettings, outputDir, runIndex: r, refinePreset: preset,
                        promptOverride: finalOverride, loraTag: capturedLoraTag, promptAugment: augment,
                        onStatus: msg => Dispatch(() => m_log.Log(msg)), ct: ct);

                    if (result.Success && result.FilePath is not null && m_catalog is not null)
                    {
                        Dispatch(() =>
                        {
                            m_gallery.AddResultToStacks(result, p, runSettings, preset, capturedLoraTag, augment, m_catalog, modifiedPrompt);
                            RebuildStacks();
                        });
                    }
                });
            }
        }

        m_log.Log($"Queued {m_prompts.Count * runs} generations ({preset}, {settings.Width}×{settings.Height}{(creative ? ", CREATIVE" : "")})");
    }

    // ── Status ──────────────────────────────────────────────────

    void UpdateStatus() => m_status.UpdateStatus(m_catalog, m_genService.QueuedCount);
    void UpdatePreferencesSummary() => m_status.UpdatePreferencesSummary(m_preferences);

    // ── Toolbar actions ─────────────────────────────────────────

    void NewProject()
    {
        Wizard.WizardWindow wizard = new();
        if (wizard.ShowDialog() == true && wizard.CreatedProjectPath is not null)
        {
            SparkProject? proj = SparkProject.Load(wizard.CreatedProjectPath);
            if (proj is not null)
            {
                LoadProject(proj);
                m_log.Log($"✨ Created new project: {m_project.Name}");
            }
        }
    }

    void SwitchProject()
    {
        Microsoft.Win32.OpenFileDialog dlg = new()
        {
            Title = "Open Spark Project",
            Filter = "Spark Project|spark_project.json|Art Prompts|ArtPrompts.txt|All Files|*.*",
            InitialDirectory = m_project.ProjectDir,
        };

        if (dlg.ShowDialog() != true) return;

        string file = dlg.FileName;
        string dir = Path.GetDirectoryName(file) ?? "";

        SparkProject? newProj;
        if (Path.GetFileName(file).Equals("spark_project.json", StringComparison.OrdinalIgnoreCase))
            newProj = SparkProject.Load(file);
        else
            newProj = SparkProject.FindOrCreate(dir);

        if (newProj is null) { m_log.Log("Failed to load project."); return; }

        LoadProject(newProj);
        m_log.Log($"Switched to project: {m_project.Name}");
    }

    void LoadProject(SparkProject proj)
    {
        m_project = proj;
        m_docs = new DocumentStore(m_project.ProjectDir);
        OnPropertyChanged(nameof(ProjectName));

        // Apply project defaults to settings
        m_settings.ApplyDefaults(m_project.DefaultSettings);

        // Reload data-driven configs
        StoryContext.Reload();
        ArtDirections.Reload();
        CreativeEngine.Reload();

        string[] docPatterns = m_project.StoryFiles.Length > 0
            ? m_project.StoryFiles : ["*.txt", "*.md"];
        m_docs.Ingest(docPatterns);

        m_storyContext = StoryContext.LoadProjectContext(m_project.ProjectDir);
        string promptsFile = m_project.ResolvedPromptsFile;
        m_prompts = File.Exists(promptsFile) ? PromptParser.Parse(promptsFile) : [];
        m_log.Log($"Loaded {m_prompts.Count} prompts");

        string outputDir = m_project.ResolvedOutputDir;
        m_catalog = new ImageCatalog(outputDir);
        m_preferences = new PreferenceTracker(outputDir);
        int ingested = m_catalog.IngestExisting(m_prompts);
        if (ingested > 0) m_log.Log($"Ingested {ingested} existing images");

        RebuildStacks();
        UpdatePreferencesSummary();
        m_lora.LoadLoras();
    }

    void EditPrompts()
    {
        string promptsFile = m_project.ResolvedPromptsFile;
        string content = File.Exists(promptsFile) ? File.ReadAllText(promptsFile) : "";

        TextEditorDialog editor = new(
            title: "Art Prompts",
            fileName: m_project.PromptsFile,
            content: content,
            hint: "Format: PROMPT 01 — \"Title\"\\nScene description.\\n\\nStyle directions.\\n\\n" +
                  "Optional: LORA: name:weight on its own line within a prompt block.",
            onSave: text =>
            {
                File.WriteAllText(promptsFile, text);
                m_prompts = PromptParser.Parse(promptsFile);
                m_log.Log($"Prompts saved and reloaded: {m_prompts.Count} prompts");
                RebuildStacks();
            });

        editor.ShowDialog();
    }

    void EditDocuments()
    {
        DocumentManagerDialog dialog = new(m_docs, m_project, m_log.Log);
        dialog.ShowDialog();

        string[] docPatterns = m_project.StoryFiles.Length > 0
            ? m_project.StoryFiles : ["*.txt", "*.md"];
        m_docs.Ingest(docPatterns);
        m_storyContext = StoryContext.LoadProjectContext(m_project.ProjectDir);
    }

    void ShowOptions()
    {
        string content = File.Exists(m_project.ProjectFilePath)
            ? File.ReadAllText(m_project.ProjectFilePath) : "{}";

        TextEditorDialog editor = new(
            title: "Project Settings",
            fileName: "spark_project.json",
            content: content,
            hint: "Edit project configuration. Changes take effect after save (some may require restart).",
            onSave: text =>
            {
                File.WriteAllText(m_project.ProjectFilePath, text);
                SparkProject? reloaded = SparkProject.Load(m_project.ProjectFilePath);
                if (reloaded is not null)
                {
                    m_project = reloaded;
                    OnPropertyChanged(nameof(ProjectName));
                    m_log.Log("Project settings updated.");
                }
            });

        editor.ShowDialog();
    }

    void OpenLightbox()
    {
        if (m_detail.DetailImage is null) return;
        LightboxWindow lightbox = new(m_detail.DetailImage, m_gallery.SelectedStack, img => m_detail.DetailImage = img);
        lightbox.ShowDialog();
    }

    static void Dispatch(Action action)
        => System.Windows.Application.Current.Dispatcher.Invoke(action);

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    #endregion

    public void Dispose() => m_genService.Dispose();
}
