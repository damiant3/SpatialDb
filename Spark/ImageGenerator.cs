using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
///////////////////////////////////////////////
namespace Spark;

sealed class ImageGeneratorSettings
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

    // Human-readable tag used as the subdirectory name under Concept\
    public string SettingsTag =>
        $"{Width}x{Height}_s{Steps}_cfg{CfgScale}_{Sampler.Replace("++ ", "pp").Replace(" ", "-")}_{Scheduler}_seed{Seed}";
}

sealed class GenerateResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public long ActualSeed { get; init; }
    public string? Error { get; init; }
}

sealed class ImageGenerator : IDisposable
{
    readonly HttpClient m_http;
    readonly string m_apiBase;

    public ImageGenerator(string baseUrl = "http://127.0.0.1:7860")
    {
        m_apiBase = baseUrl.TrimEnd('/') + "/sdapi/v1";
        m_http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    // Returns true if the A1111/Forge API endpoint is reachable.
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            HttpResponseMessage resp = await m_http.GetAsync($"{m_apiBase}/options");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // Returns the checkpoint title currently loaded in Forge.
    public async Task<string> GetCurrentCheckpointAsync()
    {
        try
        {
            string json = await m_http.GetStringAsync($"{m_apiBase}/options");
            return JsonNode.Parse(json)?["sd_model_checkpoint"]?.GetValue<string>() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Generates one image via the A1111/Forge REST API (/sdapi/v1/txt2img).
    /// Saves the PNG to Concept\{settingsTag}\{promptFilename}.png.
    /// Returns the file path on success, null on failure.
    /// </summary>
    public async Task<GenerateResult> GenerateAsync(
        ArtPrompt prompt,
        ImageGeneratorSettings settings,
        string outputDir,
        Action<string>? onStatus = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);
        string settingsDir = Path.Combine(outputDir, settings.SettingsTag);
        Directory.CreateDirectory(settingsDir);

        string outputPath = Path.Combine(settingsDir, prompt.Filename + ".png");
        if (File.Exists(outputPath))
        {
            onStatus?.Invoke($"[{prompt.Number:D2}] Cached — {prompt.Title}");
            return new GenerateResult { Success = true, FilePath = outputPath };
        }

        onStatus?.Invoke($"[{prompt.Number:D2}] Generating: {prompt.Title}…");

        JsonObject payload = new()
        {
            ["prompt"] = prompt.FullText,
            ["negative_prompt"] = settings.NegativePrompt,
            ["width"] = settings.Width,
            ["height"] = settings.Height,
            ["steps"] = settings.Steps,
            ["cfg_scale"] = settings.CfgScale,
            ["sampler_name"] = settings.Sampler,
            ["scheduler"] = settings.Scheduler,
            ["seed"] = settings.Seed,
            ["batch_size"] = 1,
            ["n_iter"] = 1,
            ["save_images"] = false,       // we handle saving ourselves
            ["send_images"] = true,        // return base64 in response
        };

        try
        {
            StringContent body = new(payload.ToJsonString(), Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await m_http.PostAsync($"{m_apiBase}/txt2img", body, ct);

            string responseJson = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                string snippet = responseJson.Length > 300 ? responseJson[..300] : responseJson;
                onStatus?.Invoke($"[{prompt.Number:D2}] HTTP {(int)resp.StatusCode}: {snippet}");
                return new GenerateResult { Success = false, Error = snippet };
            }

            JsonNode? root = JsonNode.Parse(responseJson);

            // images[] is an array of base64-encoded PNG strings.
            string? b64 = root?["images"]?[0]?.GetValue<string>();
            if (string.IsNullOrEmpty(b64))
            {
                onStatus?.Invoke($"[{prompt.Number:D2}] Response had no images.");
                return new GenerateResult { Success = false, Error = "no images in response" };
            }

            byte[] pngBytes = Convert.FromBase64String(b64);
            await File.WriteAllBytesAsync(outputPath, pngBytes, ct);

            // Extract the actual seed that was used (for caching / reproducibility).
            long actualSeed = -1;
            try
            {
                string? infoJson = root?["info"]?.GetValue<string>();
                if (infoJson is not null)
                    actualSeed = JsonNode.Parse(infoJson)?["seed"]?.GetValue<long>() ?? -1;
            }
            catch { /* non-fatal */ }

            onStatus?.Invoke($"[{prompt.Number:D2}] ✓ {prompt.Title}  ({pngBytes.Length / 1024}KB, seed {actualSeed})");
            return new GenerateResult { Success = true, FilePath = outputPath, ActualSeed = actualSeed };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onStatus?.Invoke($"[{prompt.Number:D2}] Error: {ex.Message}");
            return new GenerateResult { Success = false, Error = ex.Message };
        }
    }

    public void Dispose() => m_http.Dispose();
}
