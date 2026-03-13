using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
///////////////////////////////////////////////
namespace Spark.Services;

sealed class ServiceEndpoint
{
    [JsonPropertyName("baseUrl")]        public string BaseUrl { get; set; } = "";
    [JsonPropertyName("autoStart")]      public bool AutoStart { get; set; }
    [JsonPropertyName("executablePath")] public string ExecutablePath { get; set; } = "";
    [JsonPropertyName("downloadUrl")]    public string DownloadUrl { get; set; } = "";
    [JsonPropertyName("startArguments")] public string StartArguments { get; set; } = "";
}

sealed class ServiceEndpointConfig
{
    static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonPropertyName("aiBaseDir")]
    public string AiBaseDir { get; set; } = @"D:\AI";

    [JsonPropertyName("ollama")]
    public ServiceEndpoint Ollama { get; set; } = new()
    {
        BaseUrl = "http://localhost:11434",
        AutoStart = true,
        DownloadUrl = "https://ollama.com/download/windows",
    };

    [JsonPropertyName("stableDiffusion")]
    public ServiceEndpoint StableDiffusion { get; set; } = new()
    {
        BaseUrl = "http://127.0.0.1:7860",
        AutoStart = true,
        DownloadUrl = "https://github.com/lllyasviel/stable-diffusion-webui-forge/releases",
    };

    [JsonPropertyName("musicGen")]
    public ServiceEndpoint MusicGen { get; set; } = new()
    {
        BaseUrl = "http://localhost:7861",
        AutoStart = false,
        DownloadUrl = "https://github.com/facebookresearch/audiocraft",
        StartArguments = "demos/musicgen_app.py --server_port 7861",
    };

    [JsonPropertyName("probeIntervalSeconds")]
    public int ProbeIntervalSeconds { get; set; } = 10;

    [JsonPropertyName("probeTimeoutMs")]
    public int ProbeTimeoutMs { get; set; } = 3000;

    static string GlobalConfigDir
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spark");

    static string GlobalConfigPath
        => Path.Combine(GlobalConfigDir, "spark_services.json");

    public static ServiceEndpointConfig Load(string? projectDir = null)
    {
        if (projectDir is not null)
        {
            ServiceEndpointConfig? local = TryLoad(Path.Combine(projectDir, "spark_services.json"));
            if (local is not null)
            {
                local.AutoDetectPaths();
                local.Save();
                return local;
            }
        }
        ServiceEndpointConfig? global = TryLoad(GlobalConfigPath);
        if (global is not null)
        {
            global.AutoDetectPaths();
            global.Save();
            return global;
        }

        ServiceEndpointConfig defaults = new();
        defaults.AutoDetectPaths();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(GlobalConfigDir);
            File.WriteAllText(GlobalConfigPath, JsonSerializer.Serialize(this, s_jsonOpts));
        }
        catch { /* non-fatal */ }
    }

    public void AutoDetectPaths()
    {
        string baseDir = AiBaseDir;
        if (!Directory.Exists(baseDir)) return;

        // Stable Diffusion Forge
        if (string.IsNullOrWhiteSpace(StableDiffusion.ExecutablePath))
        {
            string[] forgeCandidates =
            [
                Path.Combine(baseDir, "DiffusionForge", "run.bat"),
                Path.Combine(baseDir, "DiffusionForge", "webui", "webui.bat"),
                Path.Combine(baseDir, "stable-diffusion-webui-forge", "run.bat"),
                Path.Combine(baseDir, "stable-diffusion-webui-forge", "webui", "webui.bat"),
                Path.Combine(baseDir, "stable-diffusion-webui", "webui.bat"),
            ];
            foreach (string p in forgeCandidates)
            {
                if (File.Exists(p)) { StableDiffusion.ExecutablePath = p; break; }
            }
        }

        // MusicGen (python)
        if (string.IsNullOrWhiteSpace(MusicGen.ExecutablePath))
        {
            string[] pythonCandidates =
            [
                Path.Combine(baseDir, "audiocraft-main", "venv", "Scripts", "python.exe"),
                Path.Combine(baseDir, "audiocraft", "venv", "Scripts", "python.exe"),
                Path.Combine(baseDir, "musicgen", "venv", "Scripts", "python.exe"),
            ];
            foreach (string p in pythonCandidates)
            {
                if (File.Exists(p)) { MusicGen.ExecutablePath = p; break; }
            }
        }

        // Fix stale MusicGen launch args: the old "-m audiocraft.demos.musicgen_app" module
        // path doesn't exist — demos/ is a top-level folder, not a Python package.
        if (MusicGen.StartArguments.Contains("audiocraft.demos.musicgen_app"))
            MusicGen.StartArguments = MusicGen.StartArguments
                .Replace("-m audiocraft.demos.musicgen_app", "demos/musicgen_app.py");
    }

    static ServiceEndpointConfig? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ServiceEndpointConfig>(File.ReadAllText(path), s_jsonOpts);
        }
        catch { return null; }
    }
}
