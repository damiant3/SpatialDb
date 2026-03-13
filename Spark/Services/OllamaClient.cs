using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
///////////////////////////////////////////////
namespace Spark.Services;

/// <summary>
/// Thin HTTP client for a local Ollama instance at localhost:11434.
/// Provides model listing, availability check, and streaming text generation.
/// </summary>
sealed class OllamaClient : IDisposable
{
    readonly HttpClient m_http;
    readonly string m_baseUrl;

    public OllamaClient(string baseUrl = "http://localhost:11434")
    {
        m_baseUrl = baseUrl.TrimEnd('/');
        m_http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <summary>Returns true if Ollama is reachable.</summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            using HttpResponseMessage resp = await m_http.GetAsync($"{m_baseUrl}/api/tags");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Lists installed model names.</summary>
    public async Task<List<string>> ListModelsAsync()
    {
        try
        {
            string json = await m_http.GetStringAsync($"{m_baseUrl}/api/tags");
            JsonNode? root = JsonNode.Parse(json);
            JsonArray? models = root?["models"]?.AsArray();
            if (models is null) return [];
            return models
                .Select(m => m?["name"]?.GetValue<string>() ?? "")
                .Where(n => n.Length > 0)
                .ToList();
        }
        catch { return []; }
    }

    /// <summary>
    /// Generates text from a prompt using the specified model.
    /// Streams tokens and calls <paramref name="onToken"/> for each chunk.
    /// Returns the full concatenated response.
    /// </summary>
    public async Task<string> GenerateAsync(
        string model, string prompt, string? system = null,
        Action<string>? onToken = null, CancellationToken ct = default)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = true,
        };
        if (system is not null)
            payload["system"] = system;

        using HttpRequestMessage req = new(HttpMethod.Post, $"{m_baseUrl}/api/generate")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        using HttpResponseMessage resp = await m_http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        StringBuilder sb = new();
        using Stream stream = await resp.Content.ReadAsStreamAsync(ct);
        using StreamReader reader = new(stream);

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
                bool done = node?["done"]?.GetValue<bool>() ?? false;
                if (done) break;
            }
            catch { /* skip malformed lines */ }
        }

        return sb.ToString();
    }

    public void Dispose() => m_http.Dispose();
}
