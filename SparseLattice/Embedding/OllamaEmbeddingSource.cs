using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
/////////////////////////////////////////
namespace SparseLattice.Embedding;

/// <summary>
/// Embedding source that calls the Ollama <c>api/embed</c> endpoint over HTTP.
/// Adapted from LocalVisualStudioCopilot's OllamaService — dependencies on
/// Newtonsoft.Json and VSIX APIs have been replaced with System.Text.Json.
/// </summary>
public sealed class OllamaEmbeddingSource : IEmbeddingSource, IDisposable
{
    private readonly HttpClient m_client;
    private readonly bool m_ownsClient;
    private readonly string m_baseUrl;
    private int m_dimensions;
    private bool m_disposed;

    /// <param name="baseUrl">Ollama server base URL, e.g. "http://localhost:11434/".</param>
    /// <param name="modelName">Model name as registered in Ollama, e.g. "nomic-embed-text".</param>
    /// <param name="httpClient">Optional shared <see cref="HttpClient"/>. If null, one is created and owned by this instance.</param>
    public OllamaEmbeddingSource(string baseUrl, string modelName, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Base URL must not be empty.", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name must not be empty.", nameof(modelName));

        m_baseUrl = baseUrl.TrimEnd('/') + "/";
        ModelName = modelName;

        if (httpClient is null)
        {
            m_client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            m_ownsClient = true;
        }
        else
        {
            m_client = httpClient;
            m_ownsClient = false;
        }
    }

    public string ModelName { get; }

    /// <summary>
    /// Dimension count of vectors produced by this model.
    /// Zero until the first successful call to <see cref="EmbedAsync"/>.
    /// </summary>
    public int Dimensions => m_dimensions;

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        string payload = JsonSerializer.Serialize(new { model = ModelName, input = text });
        using StringContent content = new(payload, Encoding.UTF8, "application/json");
        using HttpRequestMessage request = new(HttpMethod.Post, m_baseUrl + "api/embed")
        {
            Content = content
        };

        HttpResponseMessage response = await m_client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        float[] vector = ParseEmbeddingResponse(body);

        if (m_dimensions == 0 && vector.Length > 0)
            m_dimensions = vector.Length;

        return vector;
    }

    /// <inheritdoc/>
    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        float[][] results = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
            results[i] = await EmbedAsync(texts[i], ct).ConfigureAwait(false);
        return results;
    }

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        if (m_ownsClient)
            m_client.Dispose();
    }

    /// <summary>
    /// Parses the Ollama embed response. Supports both the <c>embeddings</c> array
    /// format (current API) and the legacy <c>embedding</c> flat-array format.
    /// </summary>
    public static float[] ParseEmbeddingResponse(string json)
    {
        JsonNode? root = JsonNode.Parse(json);
        if (root is null)
            throw new InvalidOperationException("Empty or null JSON in embed response.");

        // Current Ollama format: { "embeddings": [[...]] }
        JsonNode? embeddingsNode = root["embeddings"];
        if (embeddingsNode is JsonArray outerArr && outerArr.Count > 0 && outerArr[0] is JsonArray innerArr)
            return FloatsFromJsonArray(innerArr);

        // Legacy format: { "embedding": [...] }
        JsonNode? embeddingNode = root["embedding"];
        if (embeddingNode is JsonArray flatArr)
            return FloatsFromJsonArray(flatArr);

        throw new InvalidOperationException(
            $"Unrecognised Ollama embed response shape. Body: {json[..System.Math.Min(200, json.Length)]}");
    }

    private static float[] FloatsFromJsonArray(JsonArray arr)
    {
        float[] result = new float[arr.Count];
        for (int i = 0; i < arr.Count; i++)
            result[i] = arr[i]!.GetValue<float>();
        return result;
    }
}
