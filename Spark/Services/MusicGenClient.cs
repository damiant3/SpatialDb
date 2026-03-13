using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
///////////////////////////////////////////////
namespace Spark.Services;

/// <summary>
/// Settings for a MusicGen generation run.
/// </summary>
sealed record MusicGenSettings
{
    public string Prompt { get; init; } = "";
    public double Duration { get; init; } = 10.0;
    public double Temperature { get; init; } = 1.0;
    public double TopK { get; init; } = 250;
    public double TopP { get; init; } = 0.0;
    public double CfgCoefficient { get; init; } = 3.0;
}

/// <summary>
/// Result of a music generation call.
/// </summary>
sealed record MusicGenResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// HTTP client for MusicGen running via Gradio on localhost:7860.
/// MusicGen (Meta's audiocraft) generates music from text prompts.
/// Start with: python -m audiocraft.demos.musicgen_app --listen 0.0.0.0 --server_port 7860
/// </summary>
sealed class MusicGenClient : IDisposable
{
    readonly HttpClient m_http;
    readonly string m_baseUrl;

    public MusicGenClient(string baseUrl = "http://localhost:7860")
    {
        m_baseUrl = baseUrl.TrimEnd('/');
        m_http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    /// <summary>Returns true if MusicGen Gradio API is reachable.</summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            using HttpResponseMessage resp = await m_http.GetAsync($"{m_baseUrl}/info");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Generate music from a text prompt. Returns the path to the saved WAV file.
    /// Calls the Gradio /api/predict endpoint.
    /// </summary>
    public async Task<MusicGenResult> GenerateAsync(
        MusicGenSettings settings, string outputDir, string fileName,
        Action<string>? onStatus = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);
        string outputPath = Path.Combine(outputDir, fileName);

        try
        {
            // Gradio API expects: {"data": [prompt, duration, topk, topp, temperature, cfg_coeff]}
            var payload = new JsonObject
            {
                ["data"] = new JsonArray
                {
                    settings.Prompt,
                    null,                        // melody (null = no conditioning audio)
                    "facebook/musicgen-medium",  // model
                    settings.Duration,
                    settings.TopK,
                    settings.TopP,
                    settings.Temperature,
                    settings.CfgCoefficient,
                }
            };

            onStatus?.Invoke($"Sending prompt to MusicGen ({settings.Duration}s)…");

            using HttpRequestMessage req = new(HttpMethod.Post, $"{m_baseUrl}/api/predict")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };

            using HttpResponseMessage resp = await m_http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            string body = await resp.Content.ReadAsStringAsync(ct);
            JsonNode? result = JsonNode.Parse(body);

            // Gradio returns: {"data": [{"name": "/tmp/xxx.wav", "data": null, "is_file": true}]}
            // Or for newer versions: {"data": ["/path/to/file.wav"]}
            JsonNode? dataNode = result?["data"]?[0];

            string? remotePath = null;
            if (dataNode is JsonObject obj)
                remotePath = obj["name"]?.GetValue<string>();
            else if (dataNode is JsonValue val)
                remotePath = val.GetValue<string>();

            if (remotePath is null)
                return new MusicGenResult { Success = false, Error = "No audio file in response" };

            // Download the generated file from Gradio's file server
            onStatus?.Invoke("Downloading generated audio…");
            string fileUrl = remotePath.StartsWith("http")
                ? remotePath
                : $"{m_baseUrl}/file={remotePath}";

            using HttpResponseMessage fileResp = await m_http.GetAsync(fileUrl, ct);
            fileResp.EnsureSuccessStatusCode();

            await using FileStream fs = File.Create(outputPath);
            await fileResp.Content.CopyToAsync(fs, ct);

            onStatus?.Invoke("✓ Generation complete");
            return new MusicGenResult { Success = true, FilePath = outputPath };
        }
        catch (OperationCanceledException)
        {
            return new MusicGenResult { Success = false, Error = "Cancelled" };
        }
        catch (Exception ex)
        {
            return new MusicGenResult { Success = false, Error = ex.Message };
        }
    }

    public void Dispose() => m_http.Dispose();
}
