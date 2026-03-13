using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using Common.Core.Net;
///////////////////////////////////////////////
namespace Spark.Services;

sealed record MusicGenSettings
{
    public string Prompt { get; init; } = "";
    public double Duration { get; init; } = 10.0;
    public double Temperature { get; init; } = 1.0;
    public double TopK { get; init; } = 250;
    public double TopP { get; init; } = 0.0;
    public double CfgCoefficient { get; init; } = 3.0;
}

sealed record MusicGenResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public string? Error { get; init; }
}

sealed class MusicGenClient : HttpServiceClient
{
    public MusicGenClient(ServiceUri<MusicGenApi> endpoint)
        : base(endpoint.Value, TimeSpan.FromMinutes(10)) { }

    public async Task<bool> IsAvailableAsync()
    {
        // Newer Gradio (4.x+) uses /gradio_api/info; older uses /info
        ProbeResult result = await ProbeAsync("gradio_api/info");
        if (!result.IsAvailable)
            result = await ProbeAsync("info");
        return result.IsAvailable;
    }

    public async Task<MusicGenResult> GenerateAsync(
        MusicGenSettings settings, string outputDir, string fileName,
        Action<string>? onStatus = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);
        string outputPath = Path.Combine(outputDir, fileName);

        try
        {
            // New Gradio 4.x+ API: POST to submit, then SSE stream for result.
            // /predict_full params: model, model_path, decoder, text, melody,
            //                       duration, topk, topp, temperature, cfg_coef
            JsonObject payload = new()
            {
                ["data"] = new JsonArray
                {
                    "facebook/musicgen-medium",
                    "",
                    "Default",
                    settings.Prompt,
                    null,
                    settings.Duration,
                    settings.TopK,
                    settings.TopP,
                    settings.Temperature,
                    settings.CfgCoefficient,
                }
            };

            onStatus?.Invoke($"Sending prompt to MusicGen ({settings.Duration}s)…");

            // Step 1: Submit the job
            using HttpResponseMessage submitResp = await PostJsonAsync(
                "gradio_api/call/predict_full", payload.ToJsonString(), ct);
            submitResp.EnsureSuccessStatusCode();

            string submitBody = await submitResp.Content.ReadAsStringAsync(ct);
            string? eventId = JsonNode.Parse(submitBody)?["event_id"]?.GetValue<string>();
            if (eventId is null)
                return new MusicGenResult { Success = false, Error = "No event_id in submit response" };

            // Step 2: Poll the SSE stream for completion
            onStatus?.Invoke("Generating audio (this may take a minute)…");
            string? resultData = await PollGradioResultAsync(
                $"gradio_api/call/predict_full/{eventId}", onStatus, ct);

            if (resultData is null)
                return new MusicGenResult { Success = false, Error = "Generation failed — no result from server" };

            // Parse the result — expect array with audio file info
            JsonNode? resultNode = JsonNode.Parse(resultData);
            JsonNode? firstResult = resultNode?[0];

            string? remotePath = null;
            if (firstResult is JsonObject obj)
                remotePath = obj["url"]?.GetValue<string>()
                    ?? obj["name"]?.GetValue<string>();
            else if (firstResult is JsonValue val)
                remotePath = val.GetValue<string>();

            if (remotePath is null)
                return new MusicGenResult { Success = false, Error = "No audio file path in response" };

            onStatus?.Invoke("Downloading generated audio…");
            Uri fileUri = remotePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new Uri(remotePath)
                : Endpoint($"file={remotePath}");

            await DownloadToFileAsync(fileUri, outputPath, ct);

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

    /// <summary>
    /// Reads the Gradio SSE stream, waiting for <c>event: complete</c>.
    /// Returns the data payload or null on error/timeout.
    /// </summary>
    async Task<string?> PollGradioResultAsync(
        string path, Action<string>? onStatus, CancellationToken ct)
    {
        using HttpResponseMessage resp = await GetAsync(path, ct);
        resp.EnsureSuccessStatusCode();

        using StreamReader reader = new(await resp.Content.ReadAsStreamAsync(ct));

        string? currentEvent = null;
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            if (line.StartsWith("event: "))
            {
                currentEvent = line["event: ".Length..].Trim();
            }
            else if (line.StartsWith("data: ") && currentEvent is not null)
            {
                string data = line["data: ".Length..];
                switch (currentEvent)
                {
                    case "complete":
                        return data;
                    case "error":
                        return null;
                    case "heartbeat":
                        break;
                }
                currentEvent = null;
            }
        }

        return null;
    }
}
