using System.Net.Http.Json;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SparseLattice.Gguf;
using SparseLattice.Embedding;
using SparseLattice.Lattice;
using SparseLattice.Math;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class EmbeddingPerfBenchmarks
{
    // Small corpus to warm and measure
    readonly string[] m_sampleTexts = new[]
    {
        "public int Add(int a, int b) => a + b;",
        "public string Greet(string name) => $\"Hello, {name}!\";",
        "public bool IsEven(int n) => n % 2 == 0;",
        "public double Sqrt(double x) => Math.Sqrt(x);"
    };

    TransformerEmbeddingSource? m_localSource;
    OllamaEmbeddingSource? m_ollamaSource; // optional, from SparseLattice
    HttpClient? m_ollamaHttpClient;
    string? m_ggufPath;
    string? m_ollamaBaseUrl;

    [Params(1, 8, 32)]
    public int ParallelClients { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Resolve GGUF path: env then OllamaModelLocator
        m_ggufPath = Environment.GetEnvironmentVariable("EMBEDDING_GGUF_PATH");
        if (string.IsNullOrEmpty(m_ggufPath))
        {
            string? testData = GetTestDataEmbeddingsDir();
            if (testData != null)
            {
                // First try manifest -> blob resolution using OllamaModelLocator
                try
                {
                    string? resolved = OllamaModelLocator.LocateGguf("nomic-embed-text", testData);
                    if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                    {
                        m_ggufPath = resolved;
                    }
                }
                catch
                {
                    // fall back to blob file search below
                }

                if (string.IsNullOrEmpty(m_ggufPath))
                {
                    // Prefer raw sha256-* blobs copied from Ollama manifests
                    var shaFiles = Directory.GetFiles(testData, "sha256-*", SearchOption.TopDirectoryOnly);
                    if (shaFiles.Length > 0)
                    {
                        m_ggufPath = shaFiles.OrderByDescending(f => new FileInfo(f).Length).First();
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(m_ggufPath) && File.Exists(m_ggufPath))
        {
            m_localSource = TransformerEmbeddingSource.Load(m_ggufPath);
        }

        m_ollamaBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434/";

        // Optional: use built-in Ollama client if available (OllamaEmbeddingSource)
        try
        {
            m_ollamaSource = new OllamaEmbeddingSource(m_ollamaBaseUrl, "nomic-embed-text");
            m_ollamaHttpClient = new HttpClient { BaseAddress = new Uri(m_ollamaBaseUrl) };
        }
        catch
        {
            // If OllamaEmbeddingSource not usable, we fall back to raw HTTP client usage
            m_ollamaHttpClient = new HttpClient { BaseAddress = new Uri(m_ollamaBaseUrl) };
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        m_localSource?.Dispose();
        m_ollamaSource?.Dispose();
        m_ollamaHttpClient?.Dispose();
    }

    // Simple single-embed latency for local model
    [Benchmark(Baseline = true, Description = "Local Embed (single)")]
    public async Task<float[]> Local_Embed_Single()
    {
        if (m_localSource is null) throw new InvalidOperationException("Local GGUF not found.");
        return await m_localSource.EmbedAsync(m_sampleTexts[0]).ConfigureAwait(false);
    }

    // Simple single-embed latency for Ollama via its HTTP API (if available)
    [Benchmark(Description = "Ollama Embed (HTTP)")]
    public async Task<float[]> Ollama_Embed_Single()
    {
        if (m_ollamaSource is not null)
        {
            return await m_ollamaSource.EmbedAsync(m_sampleTexts[0]).ConfigureAwait(false);
        }

        // Fallback: call Ollama embedding endpoint (model name and endpoint may vary)
        if (m_ollamaHttpClient is null) throw new InvalidOperationException("Ollama client not configured.");
        // The endpoint and payload below may need adapting to your running Ollama API.
        var payload = new { model = "nomic-embed-text", input = m_sampleTexts[0] };
        HttpResponseMessage resp = await m_ollamaHttpClient.PostAsJsonAsync("api/embed", payload).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        // Expect JSON array of floats
        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        float[] vec = JsonSerializer.Deserialize<float[]>(json) ?? Array.Empty<float>();
        return vec;
    }

    // Parallel throughput: issue many concurrent embed requests to local model
    [Benchmark(Description = "Local Embed (parallel)")]
    public async Task Local_Embed_Parallel()
    {
        if (m_localSource is null) throw new InvalidOperationException("Local GGUF not found.");
        var tasks = new List<Task<float[]>>(ParallelClients);
        for (int i = 0; i < ParallelClients; i++)
            tasks.Add(m_localSource.EmbedAsync(m_sampleTexts[i % m_sampleTexts.Length]));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    // Parallel throughput: issue many concurrent embed requests to Ollama
    [Benchmark(Description = "Ollama Embed (parallel)")]
    public async Task Ollama_Embed_Parallel()
    {
        var tasks = new List<Task<float[]>>(ParallelClients);
        for (int i = 0; i < ParallelClients; i++)
        {
            if (m_ollamaSource is not null)
                tasks.Add(m_ollamaSource.EmbedAsync(m_sampleTexts[i % m_sampleTexts.Length]));
            else
                tasks.Add(OllamaEmbedViaHttp(m_ollamaHttpClient!, m_sampleTexts[i % m_sampleTexts.Length]));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    // Optional: measure lattice query throughput (single-threaded)
    [Benchmark(Description = "Lattice Query (KNN)")]
    public void Lattice_Query_Knn()
    {
        if (m_localSource is null) throw new InvalidOperationException("Local GGUF not found.");

        // Build a small corpus (N ~ 2000) and index it once
        const int N = 2000;
        var embeddings = new List<float[]>(N);
        for (int i = 0; i < N; i++)
            embeddings.Add(m_localSource.EmbedAsync(m_sampleTexts[i % m_sampleTexts.Length]).GetAwaiter().GetResult());

        // Quantize and build lattice (lightweight for bench)
        var occupants = embeddings.Select((v, i) =>
            new SparseOccupant<string>(EmbeddingAdapter.Quantize(v, new QuantizationOptions()), $"id{i}")
        ).ToArray();

        var lattice = new EmbeddingLattice<string>(occupants, new LatticeOptions { LeafThreshold = 16 });
        lattice.Freeze();

        // Query many times sequentially
        for (int q = 0; q < 200; q++)
        {
            var center = occupants[q].Position;
            var results = lattice.QueryKNearestL2(center, 5);
            // keep results to avoid optimizing away
            if (results.Count == 0) { /* noop */ }
        }
    }

    // Small helper: call Ollama embed endpoint via HTTP (fallback)
    static async Task<float[]> OllamaEmbedViaHttp(HttpClient client, string text)
    {
        var payload = new { model = "nomic-embed-text", input = text };
        HttpResponseMessage resp = await client.PostAsJsonAsync("api/embed", payload).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<float[]>(json) ?? Array.Empty<float>();
    }

    static string? GetTestDataEmbeddingsDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
