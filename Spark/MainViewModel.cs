using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
///////////////////////////////////////////////
namespace Spark;

sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    CancellationTokenSource? m_cts;
    readonly ImageGenerator m_generator = new();
    List<ArtPrompt> m_prompts = [];
    string m_outputDir = "";
    string m_promptsFile = "";

    string m_statusText = "Ready.";
    bool m_isGenerating;
    GeneratedImage? m_selectedImage;
    string m_selectedPromptText = "";

    // Generation settings — default to user's exact running config
    int m_width = 1344;
    int m_height = 768;
    int m_steps = 20;
    double m_cfgScale = 7.0;
    string m_sampler = "DPM++ 2M SDE";
    string m_scheduler = "karras";
    long m_seed = -1;

    public string StatusText { get => m_statusText; set => SetField(ref m_statusText, value); }
    public bool IsGenerating { get => m_isGenerating; set => SetField(ref m_isGenerating, value); }

    public GeneratedImage? SelectedImage
    {
        get => m_selectedImage;
        set
        {
            if (SetField(ref m_selectedImage, value))
                SelectedPromptText = value?.PromptText ?? "";
        }
    }

    public string SelectedPromptText { get => m_selectedPromptText; set => SetField(ref m_selectedPromptText, value); }
    public ObservableCollection<GeneratedImage> Images { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];

    public int Width { get => m_width; set => SetField(ref m_width, value); }
    public int Height { get => m_height; set => SetField(ref m_height, value); }
    public int Steps { get => m_steps; set => SetField(ref m_steps, value); }
    public double CfgScale { get => m_cfgScale; set => SetField(ref m_cfgScale, value); }
    public string Sampler { get => m_sampler; set => SetField(ref m_sampler, value); }
    public string Scheduler { get => m_scheduler; set => SetField(ref m_scheduler, value); }
    public long Seed { get => m_seed; set => SetField(ref m_seed, value); }

    // Samplers exactly as returned by /sdapi/v1/samplers on this install
    public string[] Samplers { get; } =
    [
        "DPM++ 2M SDE", "DPM++ 2M", "DPM++ SDE", "DPM++ 2M SDE Heun",
        "DPM++ 3M SDE", "Euler a", "Euler", "DDIM", "UniPC", "Heun", "LMS",
    ];

    // Schedulers exactly as returned by /sdapi/v1/schedulers
    public string[] Schedulers { get; } =
    [
        "karras", "automatic", "exponential", "polyexponential",
        "sgm_uniform", "normal", "simple", "ddim", "beta",
    ];

    public ICommand GenerateAllCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RefreshGalleryCommand { get; }

    public MainViewModel()
    {
        GenerateAllCommand = new RelayCommand(_ => GenerateAll(), _ => !m_isGenerating);
        CancelCommand = new RelayCommand(_ => m_cts?.Cancel(), _ => m_isGenerating);
        RefreshGalleryCommand = new RelayCommand(_ => RefreshGallery());

        string projectDir = FindProjectDir();
        m_promptsFile = Path.Combine(projectDir, "ArtPrompts.txt");
        m_outputDir = Path.Combine(projectDir, "Concept");

        if (File.Exists(m_promptsFile))
        {
            m_prompts = PromptParser.Parse(m_promptsFile);
            Log($"Loaded {m_prompts.Count} prompts from ArtPrompts.txt");
        }
        else
            Log("ArtPrompts.txt not found.");

        RefreshGallery();
    }

    void GenerateAll()
    {
        if (m_prompts.Count == 0) { Log("No prompts loaded."); return; }

        m_cts?.Cancel();
        m_cts = new CancellationTokenSource();
        CancellationToken ct = m_cts.Token;
        IsGenerating = true;

        ImageGeneratorSettings settings = CurrentSettings();

        Task.Run(async () =>
        {
            try
            {
                Dispatch(() => Log("Checking DiffusionForge at localhost:7860…"));
                bool available = await m_generator.IsAvailableAsync();
                if (!available)
                {
                    Dispatch(() =>
                    {
                        Log("DiffusionForge not running — start D:\\AI\\DiffusionForge\\run.bat");
                        IsGenerating = false;
                    });
                    return;
                }

                string ckpt = await m_generator.GetCurrentCheckpointAsync();
                Dispatch(() => Log($"Model: {ckpt}  |  {settings.SettingsTag}"));
                Dispatch(() => Log($"Generating {m_prompts.Count} images…"));

                int success = 0;
                int skip = 0;
                int fail = 0;

                for (int i = 0; i < m_prompts.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    ArtPrompt prompt = m_prompts[i];
                    int idx = i + 1;
                    Dispatch(() => StatusText = $"{idx}/{m_prompts.Count} — {prompt.Title}");

                    GenerateResult result = await m_generator.GenerateAsync(
                        prompt, settings, m_outputDir,
                        msg => Dispatch(() => Log(msg)),
                        ct);

                    if (result.Success)
                    {
                        if (result.ActualSeed == 0 && result.FilePath is not null
                            && !result.FilePath.Contains("Cached"))
                            skip++;   // was cached
                        else
                            success++;
                        Dispatch(() => RefreshGallery());
                    }
                    else
                        fail++;
                }

                Dispatch(() =>
                {
                    StatusText = $"Done — {success} new, {skip} cached, {fail} failed.";
                    IsGenerating = false;
                    RefreshGallery();
                });
            }
            catch (OperationCanceledException)
            {
                Dispatch(() => { StatusText = "Cancelled."; IsGenerating = false; });
            }
            catch (Exception ex)
            {
                Dispatch(() => { Log($"Fatal: {ex.Message}"); IsGenerating = false; });
            }
        });
    }

    ImageGeneratorSettings CurrentSettings() => new()
    {
        Width = m_width,
        Height = m_height,
        Steps = m_steps,
        CfgScale = m_cfgScale,
        Sampler = m_sampler,
        Scheduler = m_scheduler,
        Seed = m_seed,
    };

    void RefreshGallery()
    {
        Images.Clear();
        if (!Directory.Exists(m_outputDir)) return;

        List<GeneratedImage> all = [];
        foreach (string settingsDir in Directory.GetDirectories(m_outputDir))
        {
            string settingsTag = Path.GetFileName(settingsDir);
            foreach (string file in Directory.GetFiles(settingsDir, "*.png"))
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                ArtPrompt? matched = m_prompts.FirstOrDefault(p => p.Filename == fileName);
                all.Add(new GeneratedImage
                {
                    PromptNumber = matched?.Number ?? 0,
                    Title = matched?.Title ?? fileName,
                    Series = matched?.Series ?? "",
                    FilePath = file,
                    SettingsTag = settingsTag,
                    PromptText = matched?.FullText ?? "",
                    Style = matched?.Style ?? "",
                });
            }
        }

        // Sort by prompt number, then settings tag — so same prompt across runs group nicely
        foreach (GeneratedImage img in all.OrderBy(x => x.PromptNumber).ThenBy(x => x.SettingsTag))
            Images.Add(img);

        int runCount = Directory.GetDirectories(m_outputDir).Length;
        StatusText = Images.Count > 0
            ? $"{Images.Count} images across {runCount} run{(runCount == 1 ? "" : "s")}"
            : "No images yet — hit Generate All.";
    }

    void Log(string message)
    {
        LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (LogLines.Count > 300)
            LogLines.RemoveAt(0);
    }

    static string FindProjectDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            if (File.Exists(Path.Combine(dir, "ArtPrompts.txt")))
                return dir;
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

    public void Dispose()
    {
        m_cts?.Cancel();
        m_generator.Dispose();
    }
}
