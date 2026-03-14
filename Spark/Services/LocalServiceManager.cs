using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using Common.Core.Net;
///////////////////////////////////////////////
namespace Spark.Services;

enum ServicePhase { Unknown, Probing, Starting, Online, Offline }

sealed class ServiceStatus : INotifyPropertyChanged
{
    string m_name = "";
    bool m_isAvailable;
    string m_statusText = "Not checked";
    string m_detailText = "";
    DateTime m_lastChecked;
    List<string> m_models = [];
    ServicePhase m_phase = ServicePhase.Unknown;

    public string Name { get => m_name; set => SetField(ref m_name, value); }
    public bool IsAvailable { get => m_isAvailable; set => SetField(ref m_isAvailable, value); }
    public string StatusText { get => m_statusText; set => SetField(ref m_statusText, value); }
    public string DetailText { get => m_detailText; set => SetField(ref m_detailText, value); }
    public DateTime LastChecked { get => m_lastChecked; set => SetField(ref m_lastChecked, value); }
    public List<string> Models { get => m_models; set => SetField(ref m_models, value); }
    public ServicePhase Phase { get => m_phase; set => SetField(ref m_phase, value); }

    public void MarkProbing()
    {
        Phase = ServicePhase.Probing;
        StatusText = "Checking…";
    }

    public void MarkStarting()
    {
        Phase = ServicePhase.Starting;
        StatusText = "Starting…";
    }

    public void Apply(ProbeResult result)
    {
        IsAvailable = result.IsAvailable;
        Phase = result.IsAvailable ? ServicePhase.Online : ServicePhase.Offline;
        StatusText = result.IsAvailable ? $"✓ {result.StatusText}" : $"🛑 {result.StatusText}";
        DetailText = result.IsAvailable ? result.DetailText : "Right-click for options";
        Models = result.IsAvailable ? [.. result.Models] : [];
        LastChecked = DateTime.Now;
    }

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
}

sealed class LocalServiceManager : INotifyPropertyChanged, IDisposable
{
    readonly OllamaClient m_ollamaClient;
    readonly ImageGenerator m_sdClient;
    readonly MusicGenClient m_musicClient;
    readonly DispatcherTimer m_timer;
    readonly ServiceEndpointConfig m_config;
    readonly Dictionary<string, Process> m_launchedProcesses = [];
    bool m_isProbing;

    public ServiceStatus Ollama { get; } = new() { Name = "Ollama" };
    public ServiceStatus StableDiffusion { get; } = new() { Name = "Stable Diffusion" };
    public ServiceStatus MusicGen { get; } = new() { Name = "MusicGen" };
    public ServiceEndpointConfig Config => m_config;
    public bool IsProbing { get => m_isProbing; private set => SetField(ref m_isProbing, value); }

    public LocalServiceManager(
        ServiceEndpointConfig config,
        OllamaClient ollamaClient,
        ImageGenerator sdClient,
        MusicGenClient musicClient)
    {
        m_config = config;
        m_ollamaClient = ollamaClient;
        m_sdClient = sdClient;
        m_musicClient = musicClient;
        m_timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(config.ProbeIntervalSeconds) };
        m_timer.Tick += async (_, _) => await ProbeAllAsync();
    }

    public async Task StartAsync()
    {
        await ProbeAllAsync();
        m_timer.Start();
    }

    public void Stop() => m_timer.Stop();

    public async Task ProbeAllAsync()
    {
        if (IsProbing) return;
        IsProbing = true;

        if (Ollama.Phase is not ServicePhase.Online)
            Ollama.MarkProbing();
        if (StableDiffusion.Phase is not ServicePhase.Online)
            StableDiffusion.MarkProbing();
        if (MusicGen.Phase is not ServicePhase.Online)
            MusicGen.MarkProbing();

        try
        {
            await Task.WhenAll(ProbeOllamaAsync(), ProbeStableDiffusionAsync(), ProbeMusicGenAsync());
        }
        finally
        {
            IsProbing = false;
        }
    }

    async Task ProbeOllamaAsync()
    {
        ProbeResult result = await m_ollamaClient.ProbeAsync("api/tags", body =>
        {
            JsonArray? models = JsonNode.Parse(body)?["models"]?.AsArray();
            List<string> names = [];
            if (models is not null)
            {
                foreach (JsonNode? m in models)
                {
                    string? n = m?["name"]?.GetValue<string>();
                    if (n is not null) names.Add(n);
                }
            }
            string count = $"{names.Count} model{(names.Count == 1 ? "" : "s")}";
            string detail = names.Count > 0 ? string.Join(", ", names) : "No models installed";
            return ProbeResult.Online($"Online — {count}", detail, [.. names]);
        });
        Ollama.Apply(result);
    }

    async Task ProbeStableDiffusionAsync()
    {
        ProbeResult result = await m_sdClient.ProbeAsync("sdapi/v1/options", body =>
        {
            string checkpoint = JsonNode.Parse(body)?["sd_model_checkpoint"]?.GetValue<string>() ?? "unknown";
            return ProbeResult.Online("Online", $"Checkpoint: {checkpoint}", [checkpoint]);
        });
        StableDiffusion.Apply(result);
    }

    async Task ProbeMusicGenAsync()
    {
        string detail = $"Gradio API at {m_config.MusicGen.BaseUrl}";
        // Newer Gradio (4.x+) uses /gradio_api/info; older uses /info
        ProbeResult result = await m_musicClient.ProbeAsync("gradio_api/info", detail);
        if (!result.IsAvailable)
            result = await m_musicClient.ProbeAsync("info", detail);
        MusicGen.Apply(result);
    }

    // ── Auto-start ──────────────────────────────────────────────

    public async Task<string> TryStartServiceAsync(string serviceName)
    {
        ServiceEndpoint? ep = ResolveEndpoint(serviceName);
        if (ep is null) return $"Unknown service: {serviceName}";

        string exePath = ep.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exePath))
            exePath = AutoDetectExecutable(serviceName);

        if (string.IsNullOrWhiteSpace(exePath))
            return $"No executable path configured for {serviceName}. Right-click → Configure to set it.";

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = exePath,
                Arguments = ep.StartArguments,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
            };

            // For bat files, set working directory to the bat's directory
            if (exePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                psi.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";

            // For python.exe (MusicGen), set working directory to the source dir
            // (two levels up from venv\Scripts\python.exe)
            if (exePath.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase))
            {
                string? scriptsDir = Path.GetDirectoryName(exePath);
                string? venvDir = scriptsDir is not null ? Path.GetDirectoryName(scriptsDir) : null;
                string? sourceDir = venvDir is not null ? Path.GetDirectoryName(venvDir) : null;
                if (sourceDir is not null && Directory.Exists(sourceDir))
                    psi.WorkingDirectory = sourceDir;
            }

            Process? proc = Process.Start(psi);
            if (proc is not null)
                m_launchedProcesses[serviceName] = proc;

            // Start monitoring for the service to come online — blue "Starting" phase
            ServiceStatus status = ResolveStatus(serviceName);
            status.MarkStarting();

            // SD Forge can take 2+ minutes; Ollama is quick; MusicGen is moderate
            int maxAttempts = serviceName == "StableDiffusion" ? 48 : 24;
            for (int i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(2500);
                await ProbeServiceAsync(serviceName);
                if (status.IsAvailable)
                    return $"✓ {serviceName} started successfully.";
            }

            return $"{serviceName} launched but not yet responding — it may need more time to initialize.";
        }
        catch (Exception ex)
        {
            return $"Failed to start {serviceName}: {ex.Message}";
        }
    }

    public string TryStopService(string serviceName)
    {
        if (m_launchedProcesses.TryGetValue(serviceName, out Process? proc))
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
                m_launchedProcesses.Remove(serviceName);
                ServiceStatus status = ResolveStatus(serviceName);
                status.Phase = ServicePhase.Offline;
                status.IsAvailable = false;
                status.StatusText = "Stopped";
                status.DetailText = "";
                return $"✓ {serviceName} stopped.";
            }
            catch (Exception ex)
            {
                return $"Failed to stop {serviceName}: {ex.Message}";
            }
        }
        return $"{serviceName} was not launched by Spark.";
    }

    public bool IsServiceLaunchedByUs(string serviceName)
    {
        return m_launchedProcesses.TryGetValue(serviceName, out Process? proc)
            && !proc.HasExited;
    }

    async Task ProbeServiceAsync(string serviceName)
    {
        switch (serviceName)
        {
            case "Ollama": await ProbeOllamaAsync(); break;
            case "StableDiffusion": await ProbeStableDiffusionAsync(); break;
            case "MusicGen": await ProbeMusicGenAsync(); break;
        }
    }

    public void OpenDownloadPage(string serviceName)
    {
        string url = ResolveEndpoint(serviceName)?.DownloadUrl ?? "";
        if (url.Length == 0) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* non-fatal */ }
    }

    public async Task<string> PullOllamaModelAsync(string modelName, Action<string>? onProgress = null)
    {
        string exePath = m_config.Ollama.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exePath))
            exePath = AutoDetectExecutable("Ollama");
        if (string.IsNullOrWhiteSpace(exePath))
            return "Ollama executable not found. Install Ollama first.";

        try
        {
            onProgress?.Invoke($"Pulling {modelName}…");
            Process? proc = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"pull {modelName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (proc is null) return "Failed to start ollama pull process.";

            Task<string> stdOut = proc.StandardOutput.ReadToEndAsync();
            Task<string> stdErr = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            string output = await stdOut;
            string error = await stdErr;

            if (proc.ExitCode == 0)
            {
                await ProbeAllAsync();
                return $"✓ Model {modelName} pulled successfully.";
            }
            return $"Pull failed (exit {proc.ExitCode}): {(error.Length > 0 ? error : output)}";
        }
        catch (Exception ex)
        {
            return $"Failed to pull model: {ex.Message}";
        }
    }

    public ServiceEndpoint? ResolveEndpoint(string serviceName) => serviceName switch
    {
        "Ollama" => m_config.Ollama,
        "StableDiffusion" => m_config.StableDiffusion,
        "MusicGen" => m_config.MusicGen,
        _ => null,
    };

    public ServiceStatus ResolveStatus(string serviceName) => serviceName switch
    {
        "Ollama" => Ollama,
        "StableDiffusion" => StableDiffusion,
        "MusicGen" => MusicGen,
        _ => Ollama,
    };

    string AutoDetectExecutable(string serviceName)
    {
        string baseDir = m_config.AiBaseDir;
        switch (serviceName)
        {
            case "Ollama":
                string? ollamaPath = FindInPath("ollama.exe");
                if (ollamaPath is not null) return ollamaPath;
                string ollamaLocal = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Ollama", "ollama.exe");
                if (File.Exists(ollamaLocal)) return ollamaLocal;
                string ollamaProgFiles = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Ollama", "ollama.exe");
                if (File.Exists(ollamaProgFiles)) return ollamaProgFiles;
                break;

            case "StableDiffusion":
                string[] forgePaths =
                [
                    Path.Combine(baseDir, "DiffusionForge", "run.bat"),
                    Path.Combine(baseDir, "DiffusionForge", "webui", "webui.bat"),
                    Path.Combine(baseDir, "stable-diffusion-webui-forge", "run.bat"),
                    Path.Combine(baseDir, "stable-diffusion-webui", "webui.bat"),
                    @"C:\AI\DiffusionForge\run.bat",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "stable-diffusion-webui-forge", "webui.bat"),
                ];
                foreach (string p in forgePaths)
                {
                    if (File.Exists(p)) return p;
                }
                break;

            case "MusicGen":
                string[] musicPaths =
                [
                    Path.Combine(baseDir, "audiocraft-main", "venv", "Scripts", "python.exe"),
                    Path.Combine(baseDir, "audiocraft", "venv", "Scripts", "python.exe"),
                    Path.Combine(baseDir, "musicgen", "venv", "Scripts", "python.exe"),
                ];
                foreach (string p in musicPaths)
                {
                    if (File.Exists(p)) return p;
                }
                string? pythonPath = FindInPath("python.exe");
                if (pythonPath is not null) return pythonPath;
                break;
        }
        return "";
    }

    static string? FindInPath(string exeName)
    {
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar is null) return null;
        foreach (string dir in pathVar.Split(Path.PathSeparator))
        {
            string full = Path.Combine(dir.Trim(), exeName);
            if (File.Exists(full)) return full;
        }
        return null;
    }

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

    public void Dispose() => m_timer.Stop();
}
