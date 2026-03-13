using Common.Wpf.ViewModels;
///////////////////////////////////////////////
namespace Spark.ViewModels;

sealed class GenerationSettingsViewModel : ObservableObject
{
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
    bool m_creativeMode;
    bool m_injectStoryContext = true;

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
    public bool CreativeMode { get => m_creativeMode; set => SetField(ref m_creativeMode, value); }
    public bool InjectStoryContext { get => m_injectStoryContext; set => SetField(ref m_injectStoryContext, value); }

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

    /// <summary>Apply project default settings if present.</summary>
    public void ApplyDefaults(ProjectSettings? ds)
    {
        if (ds is null) return;
        if (ds.Width.HasValue) m_width = ds.Width.Value;
        if (ds.Height.HasValue) m_height = ds.Height.Value;
        if (ds.Steps.HasValue) m_steps = ds.Steps.Value;
        if (ds.CfgScale.HasValue) m_cfgScale = ds.CfgScale.Value;
        if (ds.Sampler is not null) m_sampler = ds.Sampler;
        if (ds.Scheduler is not null) m_scheduler = ds.Scheduler;
    }

    public ImageGeneratorSettings ToSettings() => new()
    {
        Width = m_width, Height = m_height, Steps = m_steps,
        CfgScale = m_cfgScale, Sampler = m_sampler, Scheduler = m_scheduler, Seed = m_seed,
    };
}
