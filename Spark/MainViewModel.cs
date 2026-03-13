using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
///////////////////////////////////////////////
namespace Spark;

sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    readonly GenerationService m_genService = new();
    readonly LoraService m_loraService;
    SparkProject m_project;
    DocumentStore m_docs;
    List<ArtPrompt> m_prompts = [];
    ImageCatalog? m_catalog;
    PreferenceTracker? m_preferences;
    string m_storyContext = "";

    string m_statusText = "Ready.";
    PromptStack? m_selectedStack;
    ImageRecord? m_detailImage;
    string m_detailPromptText = "";
    string m_detailInfoLine = "";
    string m_preferencesSummary = "";
    string m_promptAugment = "";
    string m_loraUrl = "";
    bool m_creativeMode;
    bool m_injectStoryContext = true;

    int m_width = 1344;
    int m_height = 768;
    int m_steps = 20;
    double m_cfgScale = 7.0;
    string m_sampler = "DPM++ 2M SDE";
    string m_scheduler = "karras";
    long m_seed = -1;
    int m_runsPerPrompt = 1;
    string m_refinePreset = "none";
    bool m_usePreferences;
    bool m_isGenerating;

    // ── Properties ──────────────────────────────────────────────

    public string StatusText { get => m_statusText; set => SetField(ref m_statusText, value); }
    public bool IsGenerating { get => m_isGenerating; set => SetField(ref m_isGenerating, value); }
    public string PreferencesSummary { get => m_preferencesSummary; set => SetField(ref m_preferencesSummary, value); }
    public string ProjectName => m_project?.Name ?? "";

    public PromptStack? SelectedStack
    {
        get => m_selectedStack;
        set { if (SetField(ref m_selectedStack, value)) DetailImage = value?.TopCard; }
    }

    public ImageRecord? DetailImage
    {
        get => m_detailImage;
        set
        {
            if (!SetField(ref m_detailImage, value)) return;
            DetailPromptText = value?.PromptText ?? "";
            UpdateDetailInfoLine();
            if (value is not null) MarkSeen(value);
        }
    }

    public string DetailPromptText { get => m_detailPromptText; set => SetField(ref m_detailPromptText, value); }
    public string DetailInfoLine { get => m_detailInfoLine; set => SetField(ref m_detailInfoLine, value); }
    public string PromptAugment { get => m_promptAugment; set => SetField(ref m_promptAugment, value); }
    public string SelectedLora { get => m_loraService.SelectedLora; set { m_loraService.SelectedLora = value; OnPropertyChanged(); } }
    public double LoraWeight { get => m_loraService.LoraWeight; set { m_loraService.LoraWeight = Math.Clamp(value, 0, 2); OnPropertyChanged(); } }
    public string LoraUrl { get => m_loraUrl; set => SetField(ref m_loraUrl, value); }
    public bool CreativeMode { get => m_creativeMode; set => SetField(ref m_creativeMode, value); }
    public bool InjectStoryContext { get => m_injectStoryContext; set => SetField(ref m_injectStoryContext, value); }

    public ObservableCollection<PromptStack> Stacks { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];
    public ObservableCollection<string> QueueItems { get; } = [];
    public ObservableCollection<string> AvailableLoras => m_loraService.AvailableLoras;

    public ArtDirections.DirectionGroup[] DirectionGroups { get; } = ArtDirections.Groups;

    public int Width { get => m_width; set => SetField(ref m_width, value); }
    public int Height { get => m_height; set => SetField(ref m_height, value); }
    public int Steps { get => m_steps; set => SetField(ref m_steps, value); }
    public double CfgScale { get => m_cfgScale; set => SetField(ref m_cfgScale, value); }
    public string Sampler { get => m_sampler; set => SetField(ref m_sampler, value); }
    public string Scheduler { get => m_scheduler; set => SetField(ref m_scheduler, value); }
    public long Seed { get => m_seed; set => SetField(ref m_seed, value); }
    public int RunsPerPrompt { get => m_runsPerPrompt; set => SetField(ref m_runsPerPrompt, Math.Max(1, value)); }
    public string RefinePreset { get => m_refinePreset; set => SetField(ref m_refinePreset, value); }
    public bool UsePreferences { get => m_usePreferences; set => SetField(ref m_usePreferences, value); }

    // Rating display — number of filled stars for the detail image
    public int DetailRating => m_detailImage?.Rating ?? 0;

    public string[] Samplers { get; } =
    [
        "DPM++ 2M SDE", "DPM++ 2M", "DPM++ SDE", "DPM++ 2M SDE Heun",
        "DPM++ 3M SDE", "Euler a", "Euler", "DDIM", "UniPC", "Heun", "LMS",
    ];
    public string[] Schedulers { get; } =
    [
        "karras", "automatic", "exponential", "polyexponential",
        "sgm_uniform", "normal", "simple", "ddim", "beta",
    ];
    public string[] RefinePresets { get; } = Spark.RefinePresets.Names;

    // ── Commands ────────────────────────────────────────────────

    public ICommand GenerateAllCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SaveDetailCommand { get; }
    public ICommand DeleteDetailCommand { get; }
    public ICommand RegenDetailCommand { get; }
    public ICommand RateDetailCommand { get; }
    public ICommand VariantBiggerCommand { get; }
    public ICommand VariantSmallerCommand { get; }
    public ICommand DownloadLoraCommand { get; }
    public ICommand RefreshLorasCommand { get; }
    public ICommand DirectedRegenCommand { get; }
    public ICommand CreativeRegenCommand { get; }
    public ICommand BrowseLoraSiteCommand { get; }
    public ICommand SwitchProjectCommand { get; }
    public ICommand EditPromptsCommand { get; }
    public ICommand EditStoryCommand { get; }
    public ICommand OptionsCommand { get; }
    public ICommand ShowLightboxCommand { get; }

    // ── Constructor ─────────────────────────────────────────────

    public MainViewModel()
    {
        m_loraService = new LoraService(m_genService.Generator);

        // Wire service events
        m_genService.LogMessage += msg => Log(msg);
        m_genService.StatusChanged += UpdateStatus;
        m_genService.GeneratingChanged += gen => IsGenerating = gen;

        // Commands
        GenerateAllCommand = new RelayCommand(_ => GenerateAll(), _ => !m_isGenerating);
        CancelCommand = new RelayCommand(_ => m_genService.Cancel(), _ => m_isGenerating);
        RefreshCommand = new RelayCommand(_ => RebuildStacks());
        SaveDetailCommand = new RelayCommand(_ => SaveDetail(), _ => m_detailImage is not null);
        DeleteDetailCommand = new RelayCommand(_ => DeleteDetail(), _ => m_detailImage is not null);
        RegenDetailCommand = new RelayCommand(_ => EnqueueRegen(), _ => m_detailImage is not null);
        RateDetailCommand = new RelayCommand(p => RateDetail(p), _ => m_detailImage is not null);
        VariantBiggerCommand = new RelayCommand(_ => EnqueueUpscaleVariant(), _ => m_detailImage is { Seed: > 0 });
        VariantSmallerCommand = new RelayCommand(_ => EnqueueVariant(0.75), _ => m_detailImage is { Seed: > 0 });
        DownloadLoraCommand = new RelayCommand(_ => m_loraService.DownloadLora(m_loraUrl, Log), _ => m_loraUrl.Length > 0);
        RefreshLorasCommand = new RelayCommand(_ => m_loraService.LoadLoras(Log));
        DirectedRegenCommand = new RelayCommand(p => DirectedRegen(p as string));
        CreativeRegenCommand = new RelayCommand(_ => CreativeRegen(), _ => m_detailImage is not null);
        BrowseLoraSiteCommand = new RelayCommand(_ => BrowseLoras());
        SwitchProjectCommand = new RelayCommand(_ => SwitchProject());
        EditPromptsCommand = new RelayCommand(_ => EditPrompts());
        EditStoryCommand = new RelayCommand(_ => EditDocuments());
        OptionsCommand = new RelayCommand(_ => ShowOptions());
        ShowLightboxCommand = new RelayCommand(_ => OpenLightbox(), _ => m_detailImage is not null);

        // Bootstrap via project system
        m_project = SparkProject.FindOrCreate(AppContext.BaseDirectory);
        m_docs = new DocumentStore(m_project.ProjectDir);
        Log($"Project: {m_project.Name} ({m_project.ProjectDir})");

        // Apply project default settings if present
        if (m_project.DefaultSettings is ProjectSettings ds)
        {
            if (ds.Width.HasValue) m_width = ds.Width.Value;
            if (ds.Height.HasValue) m_height = ds.Height.Value;
            if (ds.Steps.HasValue) m_steps = ds.Steps.Value;
            if (ds.CfgScale.HasValue) m_cfgScale = ds.CfgScale.Value;
            if (ds.Sampler is not null) m_sampler = ds.Sampler;
            if (ds.Scheduler is not null) m_scheduler = ds.Scheduler;
        }

        // Ingest project documents for RAG-based prompt conditioning
        string[] docPatterns = m_project.StoryFiles.Length > 0
            ? m_project.StoryFiles : ["*.txt", "*.md"];
        int docsIngested = m_docs.Ingest(docPatterns);
        if (docsIngested > 0) Log($"Indexed {docsIngested} documents ({m_docs.Count} total)");

        // Load story context from documents
        m_storyContext = StoryContext.LoadProjectContext(m_project.ProjectDir);
        Log($"Story context: {(m_storyContext.Length > 0 ? "loaded" : "none found")}");

        string promptsFile = m_project.ResolvedPromptsFile;
        if (File.Exists(promptsFile))
        {
            m_prompts = PromptParser.Parse(promptsFile);
            Log($"Loaded {m_prompts.Count} prompts");
        }
        else
            Log($"{m_project.PromptsFile} not found.");

        string outputDir = m_project.ResolvedOutputDir;
        m_catalog = new ImageCatalog(outputDir);
        m_preferences = new PreferenceTracker(outputDir);

        int ingested = m_catalog.IngestExisting(m_prompts);
        if (ingested > 0) Log($"Ingested {ingested} existing images");

        RebuildStacks();
        UpdatePreferencesSummary();
        m_loraService.LoadLoras(Log);
    }

    // ── Story context ───────────────────────────────────────────

    string BuildContextPrefix()
    {
        if (!m_injectStoryContext) return "";
        return StoryContext.UniverseGlossary + " " + m_storyContext;
    }

    /// <summary>
    /// Builds prompt-specific context by querying the document store for
    /// chunks relevant to this particular prompt (lightweight RAG).
    /// Falls back to the static glossary + story context if no docs indexed.
    /// </summary>
    string BuildContextForPrompt(string promptText)
    {
        if (!m_injectStoryContext) return "";

        // Try document store RAG first
        string ragContext = m_docs.BuildContext(promptText);
        if (ragContext.Length > 0)
        {
            string glossary = StoryContext.UniverseGlossary;
            return glossary.Length > 0
                ? glossary + " " + ragContext
                : ragContext;
        }

        // Fall back to static context
        return StoryContext.UniverseGlossary + " " + m_storyContext;
    }

    // ── Gallery ─────────────────────────────────────────────────

    void RebuildStacks()
    {
        int? selectedPrompt = m_selectedStack?.PromptNumber;
        Stacks.Clear();
        if (m_catalog is null) return;

        foreach (ArtPrompt prompt in m_prompts)
        {
            PromptStack stack = new(prompt.Number, prompt.Title, prompt.Series, OnStackSelected);
            stack.Cards = m_catalog.GetStack(prompt.Number);
            stack.RefreshCards();
            Stacks.Add(stack);
        }

        if (selectedPrompt.HasValue)
            SelectedStack = Stacks.FirstOrDefault(s => s.PromptNumber == selectedPrompt.Value);
        UpdateStatus();
    }

    void OnStackSelected(PromptStack stack)
    {
        SelectedStack = stack;
        DetailImage = stack.TopCard;
    }

    void AddResultToStacks(GenerateResult result, ArtPrompt prompt,
        ImageGeneratorSettings settings, string preset, string? loraTag, string? augment)
    {
        if (!result.Success || result.FilePath is null || m_catalog is null) return;
        m_catalog.Add(new ImageRecord
        {
            PromptNumber = prompt.Number, Title = prompt.Title, Series = prompt.Series,
            FilePath = result.FilePath, SettingsTag = settings.SettingsTag,
            PromptText = prompt.FullText, Style = prompt.Style,
            Seed = result.ActualSeed, RefinePreset = preset,
            LoraTag = loraTag ?? "", PromptAugment = augment ?? "",
            SourceWidth = settings.Width, SourceHeight = settings.Height,
        });
        RebuildStacks();
        PromptStack? stack = Stacks.FirstOrDefault(s => s.PromptNumber == prompt.Number);
        if (stack is not null) { stack.SetTopIndex(0); SelectedStack = stack; DetailImage = stack.TopCard; }
    }

    // ── Detail actions ──────────────────────────────────────────

    void RateDetail(object? param)
    {
        if (m_detailImage is null || m_catalog is null || m_preferences is null) return;
        if (param is not string s || !int.TryParse(s, out int rating)) return;

        // If re-rating, back out the previous preference signal
        int prevRating = m_detailImage.Rating;
        if (prevRating > 0) BackOutPreference(m_detailImage, prevRating);

        m_detailImage.Rating = Math.Clamp(rating, 1, 5);

        // 3+ is positive (3 = mild, 4-5 = strong). ≤2 is negative.
        if (rating >= 3)
        {
            double strength = rating >= 4 ? 1.5 : 0.5;
            m_preferences.RecordPositive(m_detailImage, strength);
        }
        else
        {
            m_preferences.RecordNegative(m_detailImage, 0.5);
        }
        m_catalog.Update();

        Stacks.FirstOrDefault(st => st.PromptNumber == m_detailImage.PromptNumber)?.RefreshCards();
        Log($"★ Rated {m_detailImage.DisplayName}: {m_detailImage.RatingStars}");
        OnPropertyChanged(nameof(DetailRating));
        UpdatePreferencesSummary();

        if (rating <= 2)
        {
            Log($"⚡ Low rating — soft-deleting and queueing mutant regen…");
            m_detailImage.SoftDelete();
            m_catalog.Update();
            EnqueueRegen();
            RebuildStacks();
        }
    }

    void BackOutPreference(ImageRecord record, int prevRating)
    {
        if (m_preferences is null) return;
        // Reverse the previous signal
        if (prevRating >= 3)
        {
            double strength = prevRating >= 4 ? 1.5 : 0.5;
            m_preferences.RecordNegative(record, strength); // reverse positive
        }
        else
        {
            m_preferences.RecordPositive(record, 0.5); // reverse negative
        }
    }

    void SaveDetail()
    {
        if (m_detailImage is null || m_catalog is null || m_preferences is null) return;
        m_detailImage.Saved = true;
        m_preferences.RecordPositive(m_detailImage);
        m_catalog.Update();
        Log($"💾 Saved: {m_detailImage.DisplayName}");
        UpdatePreferencesSummary();
        UpdateStatus();
    }

    void DeleteDetail()
    {
        try
        {
            if (m_detailImage is null || m_catalog is null) return;
            m_detailImage.SoftDelete();
            m_catalog.Update();
            Log($"🗑 Soft-deleted: {m_detailImage.DisplayName} (recoverable for 24h)");
            DetailImage = null;
            RebuildStacks();
        }
        catch (Exception ex) { Log($"Error deleting: {ex.Message}"); }
    }

    void MarkSeen(ImageRecord record)
    {
        if (record.Seen || m_catalog is null) return;
        record.Seen = true;
        m_catalog.Update();
    }

    void UpdateDetailInfoLine()
    {
        if (m_detailImage is null) { DetailInfoLine = ""; OnPropertyChanged(nameof(DetailRating)); return; }
        ImageRecord r = m_detailImage;
        string seed = r.Seed > 0 ? r.Seed.ToString() : "random";
        string size = r.SourceWidth > 0 ? $"{r.SourceWidth}×{r.SourceHeight}" : "";
        string preset = r.RefinePreset != "none" ? r.RefinePreset : "";
        string lora = r.LoraTag.Length > 0 ? r.LoraTag : "";
        string parts = string.Join("  •  ",
            new[] { $"Seed: {seed}", preset, size, lora }
            .Where(s => s.Length > 0));
        DetailInfoLine = parts;
        OnPropertyChanged(nameof(DetailRating));
    }

    // ── Generation helpers ──────────────────────────────────────

    ImageGeneratorSettings CurrentSettings() => new()
    {
        Width = m_width, Height = m_height, Steps = m_steps,
        CfgScale = m_cfgScale, Sampler = m_sampler, Scheduler = m_scheduler, Seed = m_seed,
    };

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
        if (directionLabel is null || m_detailImage is null) return;
        ArtDirections.Direction? dir = null;
        foreach ((_, ArtDirections.Direction d) in ArtDirections.All())
            if (d.Label == directionLabel) { dir = d; break; }
        if (dir is null) return;

        ArtPrompt? prompt = m_prompts.FirstOrDefault(p => p.Number == m_detailImage.PromptNumber);
        if (prompt is null) return;

        ImageGeneratorSettings settings = CurrentSettings();
        if (dir.CfgNudge.HasValue)
            settings = settings with { CfgScale = Math.Clamp(settings.CfgScale + dir.CfgNudge.Value, 3, 15) };
        if (dir.StepsNudge.HasValue)
            settings = settings with { Steps = Math.Clamp(settings.Steps + dir.StepsNudge.Value, 10, 50) };
        if (dir.NegativeAdd.Length > 0)
            settings = settings with { NegativePrompt = settings.NegativePrompt + ", " + dir.NegativeAdd };
        settings = settings with { Seed = -1 };

        string preset = m_refinePreset;
        string? loraTag = m_loraService.BuildLoraTag();
        string augment = dir.PromptAdd + (m_promptAugment.Length > 0 ? ", " + m_promptAugment : "");
        int promptNum = prompt.Number;
        string contextPrefix = BuildContextForPrompt(prompt.FullText);
        string outputDir = m_project.ResolvedOutputDir;

        string queueLabel = $"🎨 {dir.Label} → {prompt.Title}";
        QueueItems.Add(queueLabel);
        Log($"🎨 Directed regen: {dir.Label} → {prompt.Title}");

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            string? finalPromptOverride = contextPrefix.Length > 0
                ? contextPrefix + "\n" + prompt.FullText : null;

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, outputDir, runIndex: nextRun, refinePreset: preset,
                promptOverride: finalPromptOverride, loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => Log(msg)), ct: ct);

            Dispatch(() => { QueueItems.Remove(queueLabel); AddResultToStacks(result, prompt, settings, preset, loraTag, augment); });
        });
    }

    // ── Mutate regen ────────────────────────────────────────────

    void CreativeRegen()
    {
        if (m_detailImage is null) return;
        ArtPrompt? prompt = m_prompts.FirstOrDefault(p => p.Number == m_detailImage.PromptNumber);
        if (prompt is null) return;

        (string creativeAugment, double cfgNudge, int stepsNudge, string samplerHint) = CreativeEngine.Generate();
        ImageGeneratorSettings settings = CurrentSettings() with
        {
            Seed = -1,
            CfgScale = Math.Clamp(m_cfgScale + cfgNudge, 3, 15),
            Steps = Math.Clamp(m_steps + stepsNudge, 10, 50),
        };
        if (samplerHint.Length > 0) settings = settings with { Sampler = samplerHint };

        string preset = m_refinePreset;
        string? loraTag = m_loraService.BuildLoraTag();
        string augment = creativeAugment + (m_promptAugment.Length > 0 ? ", " + m_promptAugment : "");
        int promptNum = prompt.Number;
        string contextPrefix = BuildContextForPrompt(prompt.FullText);
        string outputDir = m_project.ResolvedOutputDir;

        string queueLabel = $"🧬 Mutate {prompt.Title}";
        QueueItems.Add(queueLabel);
        Log($"🧬 Mutate: {prompt.Title} — {creativeAugment[..Math.Min(60, creativeAugment.Length)]}...");

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            string? finalPromptOverride = contextPrefix.Length > 0
                ? contextPrefix + "\n" + prompt.FullText : null;

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, outputDir, runIndex: nextRun, refinePreset: preset,
                promptOverride: finalPromptOverride, loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => Log(msg)), ct: ct);

            Dispatch(() => { QueueItems.Remove(queueLabel); AddResultToStacks(result, prompt, settings, preset, loraTag, augment); });
        });
    }

    // ── Regen ───────────────────────────────────────────────────

    void EnqueueRegen()
    {
        if (m_detailImage is null) return;
        ArtPrompt? prompt = m_prompts.FirstOrDefault(p => p.Number == m_detailImage.PromptNumber);
        if (prompt is null) return;

        ImageGeneratorSettings settings = m_creativeMode
            ? ApplyCreativeSettings(CurrentSettings())
            : CurrentSettings() with { Seed = -1 };

        string preset = m_refinePreset;
        string? loraTag = m_loraService.BuildLoraTag();
        string augment = m_creativeMode
            ? CreativeEngine.PickOne() + (m_promptAugment.Length > 0 ? ", " + m_promptAugment : "")
            : m_promptAugment;
        int promptNum = prompt.Number;
        string contextPrefix = BuildContextForPrompt(prompt.FullText);
        string outputDir = m_project.ResolvedOutputDir;

        string queueLabel = $"🔄 Regen {prompt.Title}";
        QueueItems.Add(queueLabel);

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            Dispatch(() => Log($"Queue: regen {prompt.Title} run {nextRun}…"));

            string? modifiedPrompt = null;
            if (m_usePreferences && m_preferences is not null)
            {
                (modifiedPrompt, string explanation) = m_preferences.AdjustPrompt(prompt.FullText);
                if (explanation != "no adjustments" && explanation != "not enough data yet")
                    Dispatch(() => Log($"  Pref: {explanation}"));
            }

            string? finalOverride = null;
            if (contextPrefix.Length > 0 || modifiedPrompt is not null)
                finalOverride = contextPrefix + (modifiedPrompt ?? prompt.FullText);

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, outputDir, runIndex: nextRun, refinePreset: preset,
                promptOverride: finalOverride, loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => Log(msg)), ct: ct);

            Dispatch(() => { QueueItems.Remove(queueLabel); AddResultToStacks(result, prompt, settings, preset, loraTag, augment); });
        });
    }

    /// <summary>
    /// Upscale variant — preserves the exact seed and prompt so the composition
    /// stays the same, just at a higher resolution with more detail.
    /// </summary>
    void EnqueueUpscaleVariant()
    {
        if (m_detailImage is null || m_detailImage.Seed <= 0) return;
        ArtPrompt? prompt = m_prompts.FirstOrDefault(p => p.Number == m_detailImage.PromptNumber);
        if (prompt is null) return;

        int srcW = m_detailImage.SourceWidth > 0 ? m_detailImage.SourceWidth : m_width;
        int srcH = m_detailImage.SourceHeight > 0 ? m_detailImage.SourceHeight : m_height;
        (int newW, int newH) = SdxlResolutions.ScaleBucket(srcW, srcH, 1.5);

        // Use same seed, same sampler, same scheduler, same CFG — just bigger
        ImageGeneratorSettings settings = new()
        {
            Width = newW, Height = newH,
            Steps = Math.Max(m_detailImage.RefinePreset != "none" ? 30 : m_steps, 25),
            CfgScale = m_cfgScale,
            Sampler = m_sampler,
            Scheduler = m_scheduler,
            Seed = m_detailImage.Seed,
        };
        string preset = m_detailImage.RefinePreset;
        string? loraTag = m_detailImage.LoraTag.Length > 0 ? m_detailImage.LoraTag : m_loraService.BuildLoraTag();
        string augment = m_detailImage.PromptAugment;
        long seed = m_detailImage.Seed;
        int promptNum = prompt.Number;
        string outputDir = m_project.ResolvedOutputDir;

        // Use the original prompt text, not the current one
        string originalPrompt = m_detailImage.PromptText;

        string queueLabel = $"⬆ {prompt.Title} ({newW}×{newH}, seed {seed})";
        QueueItems.Add(queueLabel);

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            Dispatch(() => Log($"Queue: upscale {prompt.Title} ({newW}×{newH}, seed {seed})…"));

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, outputDir, runIndex: nextRun, refinePreset: preset,
                promptOverride: originalPrompt, loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => Log(msg)), ct: ct);

            Dispatch(() =>
            {
                QueueItems.Remove(queueLabel);
                AddResultToStacks(result, prompt, settings, preset, loraTag, augment);
            });
        });
    }

    void EnqueueVariant(double scaleFactor)
    {
        if (m_detailImage is null || m_detailImage.Seed <= 0) return;
        ArtPrompt? prompt = m_prompts.FirstOrDefault(p => p.Number == m_detailImage.PromptNumber);
        if (prompt is null) return;

        int srcW = m_detailImage.SourceWidth > 0 ? m_detailImage.SourceWidth : m_width;
        int srcH = m_detailImage.SourceHeight > 0 ? m_detailImage.SourceHeight : m_height;
        (int newW, int newH) = SdxlResolutions.ScaleBucket(srcW, srcH, scaleFactor);

        ImageGeneratorSettings settings = CurrentSettings() with { Width = newW, Height = newH, Seed = m_detailImage.Seed };
        string preset = m_detailImage.RefinePreset;
        string? loraTag = m_detailImage.LoraTag.Length > 0 ? m_detailImage.LoraTag : null;
        string augment = m_detailImage.PromptAugment;
        long seed = m_detailImage.Seed;
        int promptNum = prompt.Number;
        string label = "smaller";
        string outputDir = m_project.ResolvedOutputDir;

        string queueLabel = $"⬇ {prompt.Title} ({newW}×{newH})";
        QueueItems.Add(queueLabel);

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            Dispatch(() => Log($"Queue: {label} variant {prompt.Title} ({newW}×{newH}, seed {seed})…"));

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, outputDir, runIndex: nextRun, refinePreset: preset,
                loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => Log(msg)), ct: ct);

            Dispatch(() =>
            {
                QueueItems.Remove(queueLabel);
                AddResultToStacks(result, prompt, settings, preset, loraTag, augment);
            });
        });
    }

    // ── Generate all ────────────────────────────────────────────

    void GenerateAll()
    {
        if (m_prompts.Count == 0) { Log("No prompts loaded."); return; }

        m_genService.ResetCts();
        IsGenerating = true;

        ImageGeneratorSettings settings = CurrentSettings();
        int runs = m_runsPerPrompt;
        string preset = m_refinePreset;
        bool usePref = m_usePreferences;
        bool creative = m_creativeMode;
        string? globalLoraTag = m_loraService.BuildLoraTag();
        string baseAugment = m_promptAugment;
        bool injectContext = m_injectStoryContext;
        string outputDir = m_project.ResolvedOutputDir;

        foreach (ArtPrompt prompt in m_prompts)
        {
            // Per-prompt LoRA overrides the global LoRA
            string? loraTag = prompt.LoraTag ?? globalLoraTag;

            // Per-prompt RAG context from document store
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
                            Dispatch(() => Log($"  Pref: {explanation}"));
                    }

                    string? finalOverride = null;
                    if (capturedContext.Length > 0 || modifiedPrompt is not null)
                        finalOverride = capturedContext + (modifiedPrompt ?? p.FullText);

                    Dispatch(() => StatusText = $"Generating {p.Title} (run {r})…");

                    GenerateResult result = await m_genService.Generator.GenerateAsync(
                        p, runSettings, outputDir, runIndex: r, refinePreset: preset,
                        promptOverride: finalOverride, loraTag: capturedLoraTag, promptAugment: augment,
                        onStatus: msg => Dispatch(() => Log(msg)), ct: ct);

                    if (result.Success && result.FilePath is not null && m_catalog is not null)
                    {
                        Dispatch(() =>
                        {
                            m_catalog.Add(new ImageRecord
                            {
                                PromptNumber = p.Number, Title = p.Title, Series = p.Series,
                                FilePath = result.FilePath, SettingsTag = runSettings.SettingsTag,
                                PromptText = modifiedPrompt ?? p.FullText, Style = p.Style,
                                Seed = result.ActualSeed, RefinePreset = preset,
                                ModifiedPrompt = modifiedPrompt ?? "",
                                LoraTag = capturedLoraTag ?? "", PromptAugment = augment,
                                SourceWidth = runSettings.Width, SourceHeight = runSettings.Height,
                            });
                            RebuildStacks();
                        });
                    }
                });
            }
        }

        Log($"Queued {m_prompts.Count * runs} generations ({preset}, {settings.Width}×{settings.Height}{(creative ? ", CREATIVE" : "")})");
    }

    // ── Status ──────────────────────────────────────────────────

    void UpdateStatus()
    {
        if (m_catalog is null) return;
        int total = m_catalog.All.Count(r => !r.Deleted);
        int queued = m_genService.QueuedCount;
        string queueStr = queued > 0 ? $"  |  ⏳ {queued} queued" : "";
        StatusText = $"{total} images  |  🆕 {m_catalog.UnseenCount} unseen  |  💾 {m_catalog.SavedCount} saved{queueStr}";
    }

    void UpdatePreferencesSummary()
    {
        if (m_preferences is null || m_preferences.TotalSignals == 0)
        {
            PreferencesSummary = "No preference data yet.";
            return;
        }
        (List<(string token, double weight)> likes, List<(string token, double weight)> dislikes) =
            m_preferences.GetTopPreferences();
        string likesStr = likes.Count > 0
            ? string.Join(", ", likes.Select(l => $"{l.token}({l.weight:+0.0})"))
            : "—";
        string dislikesStr = dislikes.Count > 0
            ? string.Join(", ", dislikes.Select(d => $"{d.token}({d.weight:+0.0;-0.0})"))
            : "—";
        PreferencesSummary = $"👍 {likesStr}\n👎 {dislikesStr}\nPreset: {m_preferences.SuggestedPreset()}  |  Signals: {m_preferences.TotalSignals}";
    }

    void Log(string message)
    {
        LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (LogLines.Count > 300) LogLines.RemoveAt(0);
    }

    static void Dispatch(Action action)
        => System.Windows.Application.Current.Dispatcher.Invoke(action);

    void BrowseLoras()
    {
        (string? loraName, string? triggerWords) = m_loraService.BrowseAndInstall(Log);
        if (loraName is not null)
        {
            // Auto-select the installed LoRA if it appeared in the list
            if (AvailableLoras.Contains(loraName))
            {
                SelectedLora = loraName;
                OnPropertyChanged(nameof(SelectedLora));
            }
            // Inject trigger words into prompt augment if present
            if (triggerWords is { Length: > 0 })
            {
                string existing = m_promptAugment.Trim();
                PromptAugment = existing.Length > 0
                    ? triggerWords + ", " + existing
                    : triggerWords;
                Log($"LoRA trigger words injected: {triggerWords}");
            }
        }
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    #endregion

    public void Dispose() => m_genService.Dispose();

    // ── Toolbar actions ────────────────────────────────────────

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

        if (newProj is null) { Log("Failed to load project."); return; }

        m_project = newProj;
        m_docs = new DocumentStore(m_project.ProjectDir);
        OnPropertyChanged(nameof(ProjectName));
        Log($"Switched to project: {m_project.Name}");

        // Reload everything
        string[] docPatterns = m_project.StoryFiles.Length > 0
            ? m_project.StoryFiles : ["*.txt", "*.md"];
        m_docs.Ingest(docPatterns);

        m_storyContext = StoryContext.LoadProjectContext(m_project.ProjectDir);
        string promptsFile = m_project.ResolvedPromptsFile;
        m_prompts = File.Exists(promptsFile) ? PromptParser.Parse(promptsFile) : [];
        Log($"Loaded {m_prompts.Count} prompts");

        string outputDir = m_project.ResolvedOutputDir;
        m_catalog = new ImageCatalog(outputDir);
        m_preferences = new PreferenceTracker(outputDir);
        int ingested = m_catalog.IngestExisting(m_prompts);
        if (ingested > 0) Log($"Ingested {ingested} existing images");

        RebuildStacks();
        UpdatePreferencesSummary();
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
                Log($"Prompts saved and reloaded: {m_prompts.Count} prompts");
                RebuildStacks();
            });

        editor.ShowDialog();
    }

    void EditDocuments()
    {
        DocumentManagerDialog dialog = new(m_docs, m_project, Log);
        dialog.ShowDialog();

        // After closing, re-ingest to pick up any changes and reload story context
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
                    Log("Project settings updated.");
                }
            });

        editor.ShowDialog();
    }

    void OpenLightbox()
    {
        if (m_detailImage is null) return;
        LightboxWindow lightbox = new(m_detailImage, m_selectedStack, img => DetailImage = img);
        lightbox.ShowDialog();
    }
}
