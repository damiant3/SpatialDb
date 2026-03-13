using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Common.Core.Net;
///////////////////////////////////////////////
namespace Spark;

sealed record ImageGeneratorSettings
{
    public int Width { get; init; } = 1344;
    public int Height { get; init; } = 768;
    public int Steps { get; init; } = 20;
    public double CfgScale { get; init; } = 7.0;
    public string Sampler { get; init; } = "DPM++ 2M SDE";
    public string Scheduler { get; init; } = "karras";
    public long Seed { get; init; } = -1;
    public string NegativePrompt { get; init; } =
        "watermark, text, signature, logo, deformed, bad anatomy, disfigured, " +
        "mutated, extra limbs, missing limbs, poorly drawn face, poorly drawn hands, " +
        "low quality, worst quality, blurry, out of focus";

    public string SettingsTag =>
        $"{Width}x{Height}_s{Steps}_cfg{CfgScale}_{Sampler.Replace("++ ", "pp").Replace(" ", "-")}_{Scheduler}_seed{Seed}";
}

static class RefinePresets
{
    sealed record PresetData(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("width")] int? Width = null,
        [property: JsonPropertyName("height")] int? Height = null,
        [property: JsonPropertyName("steps")] int? Steps = null,
        [property: JsonPropertyName("cfgScale")] double? CfgScale = null,
        [property: JsonPropertyName("promptSuffix")] string PromptSuffix = "",
        [property: JsonPropertyName("negativeSuffix")] string NegativeSuffix = "");

    static PresetData[]? s_presets;

    static PresetData[] LoadAll()
    {
        if (s_presets is not null) return s_presets;
        string path = Path.Combine(AppContext.BaseDirectory, "refine_presets.json");
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path);
                s_presets = JsonSerializer.Deserialize<PresetData[]>(json) ?? [];
                return s_presets;
            }
            catch { /* fall through */ }
        }
        s_presets = [new PresetData("none")];
        return s_presets;
    }

    public static void Reload() { s_presets = null; }

    public static string[] Names => LoadAll().Select(p => p.Name).ToArray();

    public static (ImageGeneratorSettings settings, string promptSuffix, string negativeSuffix) Apply(
        string preset, ImageGeneratorSettings baseSettings)
    {
        PresetData? data = LoadAll().FirstOrDefault(p => p.Name == preset);
        if (data is null || data.Name == "none")
            return (baseSettings, "", "");

        ImageGeneratorSettings s = baseSettings;
        if (data.Width.HasValue)    s = s with { Width = data.Width.Value };
        if (data.Height.HasValue)   s = s with { Height = data.Height.Value };
        if (data.Steps.HasValue)    s = s with { Steps = data.Steps.Value };
        if (data.CfgScale.HasValue) s = s with { CfgScale = data.CfgScale.Value };

        return (s, data.PromptSuffix, data.NegativeSuffix);
    }
}

// SDXL trained resolution buckets — these produce best results because the
// model was actually trained on these aspect ratios at 1024px base.
static class SdxlResolutions
{
    public static readonly (int w, int h, string label)[] Buckets =
    [
        (1024, 1024, "1:1 Square"),
        (1152,  896, "9:7 Landscape"),
        (1216,  832, "3:2 Landscape"),
        (1344,  768, "16:9 Landscape"),
        (1536,  640, "21:9 Ultra-wide"),
        ( 896, 1152, "7:9 Portrait"),
        ( 832, 1216, "2:3 Portrait"),
        ( 768, 1344, "9:16 Portrait"),
        ( 640, 1536, "9:21 Tall"),
    ];

    // Scale a bucket up/down while preserving its aspect ratio and staying
    // at reasonable pixel counts for SDXL (max ~2 megapixels).
    public static (int w, int h) ScaleBucket(int baseW, int baseH, double factor)
    {
        int w = ((int)(baseW * factor) / 64) * 64;
        int h = ((int)(baseH * factor) / 64) * 64;
        return (Math.Max(512, w), Math.Max(512, h));
    }

    public static (int w, int h) FindClosestBucket(int width, int height)
    {
        double targetRatio = (double)width / height;
        double bestDiff = double.MaxValue;
        (int, int) best = (1344, 768);
        foreach ((int bw, int bh, _) in Buckets)
        {
            double diff = Math.Abs((double)bw / bh - targetRatio);
            if (diff < bestDiff) { bestDiff = diff; best = (bw, bh); }
        }
        return best;
    }
}

sealed class LoraInfo
{
    public string Name { get; init; } = "";
    public string Alias { get; init; } = "";
    public string Path { get; init; } = "";
    public string PromptTag(double weight = 0.8) => $"<lora:{Name}:{weight:F1}>";
}

sealed class GenerateResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public long ActualSeed { get; init; }
    public string? Error { get; init; }
}

sealed class ImageGenerator : HttpServiceClient
{
    readonly Uri m_apiBase;
    List<LoraInfo>? m_cachedLoras;

    public ImageGenerator(ServiceUri<StableDiffusionApi> endpoint)
        : base(endpoint.Value, TimeSpan.FromMinutes(10))
    {
        m_apiBase = new Uri(endpoint.Value, "sdapi/v1/");
    }

    Uri Api(string path) => new(m_apiBase, path);

    public async Task<bool> IsAvailableAsync()
    {
        if (!await IsReachableAsync()) return false;
        try
        {
            using HttpResponseMessage resp = await GetAsync(Api("options"));
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string> GetCurrentCheckpointAsync()
    {
        if (!await IsReachableAsync()) return "";
        try
        {
            using HttpResponseMessage resp = await GetAsync(Api("options"));
            if (!resp.IsSuccessStatusCode) return "";
            string json = await resp.Content.ReadAsStringAsync();
            return JsonNode.Parse(json)?["sd_model_checkpoint"]?.GetValue<string>() ?? "";
        }
        catch { return ""; }
    }

    public async Task<List<LoraInfo>> GetAvailableLoras(bool forceRefresh = false)
    {
        if (m_cachedLoras is not null && !forceRefresh)
            return m_cachedLoras;
        if (!await IsReachableAsync()) return m_cachedLoras ?? [];
        try
        {
            using HttpResponseMessage resp = await GetAsync(Api("loras"));
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync();
            JsonArray? arr = JsonNode.Parse(json)?.AsArray();
            m_cachedLoras = [];
            if (arr is null) return m_cachedLoras;
            foreach (JsonNode? item in arr)
            {
                if (item is null) continue;
                m_cachedLoras.Add(new LoraInfo
                {
                    Name = item["name"]?.GetValue<string>() ?? "",
                    Alias = item["alias"]?.GetValue<string>() ?? "",
                    Path = item["path"]?.GetValue<string>() ?? "",
                });
            }
            return m_cachedLoras;
        }
        catch { return m_cachedLoras ?? []; }
    }

    public async Task RefreshLoras()
    {
        if (!await IsReachableAsync()) return;
        try
        {
            using HttpResponseMessage _ = await PostJsonAsync("sdapi/v1/refresh-loras", "{}");
            m_cachedLoras = null;
        }
        catch { /* non-fatal */ }
    }

    public async Task<bool> DownloadLoraAsync(string url, string fileName,
        Action<string>? onStatus = null, CancellationToken ct = default)
    {
        try
        {
            onStatus?.Invoke($"Downloading LoRA: {fileName}…");
            string loraDir = Path.Combine(@"D:\AI\DiffusionForge\webui\models\Lora");
            Directory.CreateDirectory(loraDir);
            string dest = Path.Combine(loraDir, fileName);
            if (File.Exists(dest))
            {
                onStatus?.Invoke($"LoRA already exists: {fileName}");
                return true;
            }

            string tempDest = dest + ".downloading";
            await DownloadToFileAsync(new Uri(url), tempDest, ct);
            long fileSize = new FileInfo(tempDest).Length;
            File.Move(tempDest, dest);
            onStatus?.Invoke($"Downloaded LoRA: {fileName} ({fileSize / 1024 / 1024}MB)");

            await Task.Delay(500, ct);
            await RefreshLoras();
            return true;
        }
        catch (Exception ex)
        {
            onStatus?.Invoke($"LoRA download failed: {ex.Message}");
            return false;
        }
    }

    public async Task<GenerateResult> GenerateAsync(
        ArtPrompt prompt,
        ImageGeneratorSettings settings,
        string outputDir,
        int runIndex = 0,
        string refinePreset = "none",
        string? promptOverride = null,
        string? loraTag = null,
        string? promptAugment = null,
        Action<string>? onStatus = null,
        CancellationToken ct = default)
    {
        (ImageGeneratorSettings refined, string promptSuffix, string negativeSuffix) =
            RefinePresets.Apply(refinePreset, settings);

        string settingsDir = Path.Combine(outputDir, refined.SettingsTag);
        Directory.CreateDirectory(settingsDir);

        string suffix = runIndex > 0 ? $"_r{runIndex:D2}" : "";
        string fileName = prompt.Filename + suffix + ".png";
        string outputPath = Path.Combine(settingsDir, fileName);

        if (File.Exists(outputPath))
        {
            onStatus?.Invoke($"[{prompt.Number:D2}] Cached — {prompt.Title} (run {runIndex})");
            return new GenerateResult { Success = true, FilePath = outputPath };
        }

        onStatus?.Invoke($"[{prompt.Number:D2}] Generating: {prompt.Title} (run {runIndex}, {refinePreset})…");

        string basePrompt = promptOverride ?? prompt.FullText;
        if (!string.IsNullOrWhiteSpace(promptAugment))
            basePrompt += ", " + promptAugment.Trim();
        if (!string.IsNullOrWhiteSpace(loraTag))
            basePrompt += " " + loraTag;
        string finalPrompt = basePrompt + promptSuffix;
        string finalNegative = refined.NegativePrompt + negativeSuffix;

        JsonObject payload = new()
        {
            ["prompt"] = finalPrompt,
            ["negative_prompt"] = finalNegative,
            ["width"] = refined.Width,
            ["height"] = refined.Height,
            ["steps"] = refined.Steps,
            ["cfg_scale"] = refined.CfgScale,
            ["sampler_name"] = refined.Sampler,
            ["scheduler"] = refined.Scheduler,
            ["seed"] = refined.Seed,
            ["batch_size"] = 1,
            ["n_iter"] = 1,
            ["save_images"] = false,
            ["send_images"] = true,
        };

        try
        {
            using HttpResponseMessage resp = await PostJsonAsync(
                "sdapi/v1/txt2img", payload.ToJsonString(), ct);
            string responseJson = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                string snippet = responseJson.Length > 300 ? responseJson[..300] : responseJson;
                onStatus?.Invoke($"[{prompt.Number:D2}] HTTP {(int)resp.StatusCode}: {snippet}");
                return new GenerateResult { Success = false, Error = snippet };
            }

            JsonNode? root = JsonNode.Parse(responseJson);
            string? b64 = root?["images"]?[0]?.GetValue<string>();
            if (string.IsNullOrEmpty(b64))
            {
                onStatus?.Invoke($"[{prompt.Number:D2}] Response had no images.");
                return new GenerateResult { Success = false, Error = "no images in response" };
            }

            byte[] pngBytes = Convert.FromBase64String(b64);
            await File.WriteAllBytesAsync(outputPath, pngBytes, ct);

            long actualSeed = -1;
            try
            {
                string? infoJson = root?["info"]?.GetValue<string>();
                if (infoJson is not null)
                    actualSeed = JsonNode.Parse(infoJson)?["seed"]?.GetValue<long>() ?? -1;
            }
            catch { /* seed extraction is non-fatal */ }

            onStatus?.Invoke($"[{prompt.Number:D2}] ✓ {prompt.Title} run {runIndex}  ({pngBytes.Length / 1024}KB, seed {actualSeed})");
            return new GenerateResult { Success = true, FilePath = outputPath, ActualSeed = actualSeed };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onStatus?.Invoke($"[{prompt.Number:D2}] Error: {ex.Message}");
            return new GenerateResult { Success = false, Error = ex.Message };
        }
    }
}
