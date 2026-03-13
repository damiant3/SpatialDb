using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Common.Core.Net;
///////////////////////////////////////////////
namespace Spark.Services;

sealed class OllamaClient : HttpServiceClient
{
    public OllamaClient(ServiceUri<OllamaApi> endpoint)
        : base(endpoint.Value, TimeSpan.FromMinutes(5)) { }

    public async Task<bool> IsAvailableAsync()
    {
        ProbeResult result = await ProbeAsync("api/tags");
        return result.IsAvailable;
    }

    public async Task<List<string>> ListModelsAsync()
    {
        if (!await IsReachableAsync()) return [];
        try
        {
            string json = await GetStringAsync("api/tags");
            JsonArray? models = JsonNode.Parse(json)?["models"]?.AsArray();
            if (models is null) return [];
            return models
                .Select(m => m?["name"]?.GetValue<string>() ?? "")
                .Where(n => n.Length > 0)
                .ToList();
        }
        catch { return []; }
    }

    public async Task<string> GenerateAsync(
        string model, string prompt, string? system = null,
        Action<string>? onToken = null, CancellationToken ct = default)
    {
        JsonObject payload = new()
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = true,
        };
        if (system is not null)
            payload["system"] = system;

        using Stream stream = await PostStreamAsync("api/generate", payload.ToJsonString(), ct);
        using StreamReader reader = new(stream);
        StringBuilder sb = new();

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            try
            {
                JsonNode? node = JsonNode.Parse(line);
                string? token = node?["response"]?.GetValue<string>();
                if (token is not null)
                {
                    sb.Append(token);
                    onToken?.Invoke(token);
                }
                if (node?["done"]?.GetValue<bool>() == true) break;
            }
            catch { /* skip malformed lines */ }
        }

        return sb.ToString();
    }
}
