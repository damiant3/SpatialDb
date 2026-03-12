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
    List<ArtPrompt> m_prompts = [];
    ImageCatalog? m_catalog;
    PreferenceTracker? m_preferences;
    string m_outputDir = "";
    string m_projectDir = "";
    string m_storyContext = "";

    string m_statusText = "Ready.";
    PromptStack? m_selectedStack;
    ImageRecord? m_detailImage;
    string m_detailPromptText = "";
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
            if (value is not null) MarkSeen(value);
        }
    }

    public string DetailPromptText { get => m_detailPromptText; set => SetField(ref m_detailPromptText, value); }
    public string PromptAugment { get => m_promptAugment; set => SetField(ref m_promptAugment, value); }
    public string SelectedLora { get => m_loraService.SelectedLora; set { m_loraService.SelectedLora = value; OnPropertyChanged(); } }
    public double LoraWeight { get => m_loraService.LoraWeight; set { m_loraService.LoraWeight = Math.Clamp(value, 0, 2); OnPropertyChanged(); } }
    public string LoraUrl { get => m_loraUrl; set => SetField(ref m_loraUrl, value); }
    public bool CreativeMode { get => m_creativeMode; set => SetField(ref m_creativeMode, value); }
    public bool InjectStoryContext { get => m_injectStoryContext; set => SetField(ref m_injectStoryContext, value); }

    public ObservableCollection<PromptStack> Stacks { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];
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
        VariantBiggerCommand = new RelayCommand(_ => EnqueueVariant(1.5), _ => m_detailImage is { Seed: > 0 });
        VariantSmallerCommand = new RelayCommand(_ => EnqueueVariant(0.75), _ => m_detailImage is { Seed: > 0 });
        DownloadLoraCommand = new RelayCommand(_ => m_loraService.DownloadLora(m_loraUrl, Log), _ => m_loraUrl.Length > 0);
        RefreshLorasCommand = new RelayCommand(_ => m_loraService.LoadLoras(Log));
        DirectedRegenCommand = new RelayCommand(p => DirectedRegen(p as string));
        CreativeRegenCommand = new RelayCommand(_ => CreativeRegen(), _ => m_detailImage is not null);
        BrowseLoraSiteCommand = new RelayCommand(_ => LoraService.BrowseCivitAI());

        // Bootstrap
        m_projectDir = FindProjectDir();
        m_outputDir = Path.Combine(m_projectDir, "Concept");

        m_storyContext = StoryContext.LoadProjectContext(m_projectDir);
        Log($"Story context: {(m_storyContext.Length > 0 ? "loaded" : "none found")}");

        string promptsFile = Path.Combine(m_projectDir, "ArtPrompts.txt");
        if (File.Exists(promptsFile))
        {
            m_prompts = PromptParser.Parse(promptsFile);
            Log($"Loaded {m_prompts.Count} prompts");
        }
        else
            Log("ArtPrompts.txt not found.");

        m_catalog = new ImageCatalog(m_outputDir);
        m_preferences = new PreferenceTracker(m_outputDir);

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

        m_detailImage.Rating = Math.Clamp(rating, 1, 5);
        double strength = rating >= 4 ? 1.5 : rating <= 2 ? -0.5 : 0;
        if (strength > 0) m_preferences.RecordPositive(m_detailImage, strength);
        if (strength < 0) m_preferences.RecordNegative(m_detailImage, Math.Abs(strength));
        m_catalog.Update();

        Stacks.FirstOrDefault(st => st.PromptNumber == m_detailImage.PromptNumber)?.RefreshCards();
        Log($"★ Rated {m_detailImage.DisplayName}: {m_detailImage.RatingStars}");
        UpdatePreferencesSummary();

        if (rating <= 2)
        {
            Log($"⚡ Low rating — soft-deleting and queueing regen…");
            m_detailImage.SoftDelete();
            m_catalog.Update();
            EnqueueRegen();
            RebuildStacks();
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
        string contextPrefix = BuildContextPrefix();

        Log($"🎨 Directed regen: {dir.Label} → {prompt.Title}");

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            string? finalPromptOverride = contextPrefix.Length > 0
                ? contextPrefix + "\n" + prompt.FullText : null;

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, m_outputDir, runIndex: nextRun, refinePreset: preset,
                promptOverride: finalPromptOverride, loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => Log(msg)), ct: ct);

            Dispatch(() => AddResultToStacks(result, prompt, settings, preset, loraTag, augment));
        });
    }

    // ── Creative regen ──────────────────────────────────────────

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
        string contextPrefix = BuildContextPrefix();

        Log($"🎲 Creative regen: {prompt.Title} — {creativeAugment[..Math.Min(60, creativeAugment.Length)]}…");

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            string? finalPromptOverride = contextPrefix.Length > 0
                ? contextPrefix + "\n" + prompt.FullText : null;

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, m_outputDir, runIndex: nextRun, refinePreset: preset,
                promptOverride: finalPromptOverride, loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => Log(msg)), ct: ct);

            Dispatch(() => AddResultToStacks(result, prompt, settings, preset, loraTag, augment));
        });
    }

    // ── Regen / Variant ─────────────────────────────────────────

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
        string contextPrefix = BuildContextPrefix();

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
                prompt, settings, m_outputDir, runIndex: nextRun, refinePreset: preset,
                promptOverride: finalOverride, loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => Log(msg)), ct: ct);

            Dispatch(() => AddResultToStacks(result, prompt, settings, preset, loraTag, augment));
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
        string label = scaleFactor > 1 ? "bigger" : "smaller";

        m_genService.Enqueue(async ct =>
        {
            int nextRun = m_catalog?.GetStack(promptNum).Count ?? 0;
            Dispatch(() => Log($"Queue: {label} variant {prompt.Title} ({newW}×{newH}, seed {seed})…"));

            GenerateResult result = await m_genService.Generator.GenerateAsync(
                prompt, settings, m_outputDir, runIndex: nextRun, refinePreset: preset,
                loraTag: loraTag, promptAugment: augment,
                onStatus: msg => Dispatch(() => Log(msg)), ct: ct);

            Dispatch(() => AddResultToStacks(result, prompt, settings, preset, loraTag, augment));
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
        string? loraTag = m_loraService.BuildLoraTag();
        string baseAugment = m_promptAugment;
        string contextPrefix = BuildContextPrefix();

        foreach (ArtPrompt prompt in m_prompts)
        {
            int existingCount = m_catalog?.GetStack(prompt.Number).Count ?? 0;
            for (int run = 0; run < runs; run++)
            {
                int runIdx = existingCount + run;
                ArtPrompt p = prompt;
                int r = runIdx;
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
                    if (contextPrefix.Length > 0 || modifiedPrompt is not null)
                        finalOverride = contextPrefix + (modifiedPrompt ?? p.FullText);

                    Dispatch(() => StatusText = $"Generating {p.Title} (run {r})…");

                    GenerateResult result = await m_genService.Generator.GenerateAsync(
                        p, runSettings, m_outputDir, runIndex: r, refinePreset: preset,
                        promptOverride: finalOverride, loraTag: loraTag, promptAugment: augment,
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
                                LoraTag = loraTag ?? "", PromptAugment = augment,
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

    static string FindProjectDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            if (File.Exists(Path.Combine(dir, "ArtPrompts.txt"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return AppContext.BaseDirectory;
    }

    static void Dispatch(Action action)
        => System.Windows.Application.Current.Dispatcher.Invoke(action);

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
}
