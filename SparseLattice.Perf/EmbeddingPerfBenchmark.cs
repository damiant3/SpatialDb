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
    readonly string[] m_sampleTexts = new[]
    {
        "public int Add(int a, int b) => a + b;",
        "public string Greet(string name) => $\"Hello, {name}!\";",
        "public bool IsEven(int n) => n % 2 == 0;",
        "public double Sqrt(double x) => Math.Sqrt(x);"
    };

    TransformerEmbeddingSource? m_localSource;
    LatticeEmbeddingSource? m_latticeSource;
    OllamaEmbeddingSource? m_ollamaSource;
    HttpClient? m_ollamaHttpClient;
    string? m_ggufPath;
    string? m_ollamaBaseUrl;

    [Params(1, 8, 32)]
    public int ParallelClients { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        m_ggufPath = Environment.GetEnvironmentVariable("EMBEDDING_GGUF_PATH");
        if (string.IsNullOrEmpty(m_ggufPath))
        {
            string? testData = GetTestDataEmbeddingsDir();
            if (testData != null)
            {
                try
                {
                    string? resolved = OllamaModelLocator.LocateGguf("nomic-embed-text", testData);
                    if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                        m_ggufPath = resolved;
                }
                catch { }

                if (string.IsNullOrEmpty(m_ggufPath))
                {
                    var shaFiles = Directory.GetFiles(testData, "sha256-*", SearchOption.TopDirectoryOnly);
                    if (shaFiles.Length > 0)
                        m_ggufPath = shaFiles.OrderByDescending(f => new FileInfo(f).Length).First();
                }
            }
        }

        if (!string.IsNullOrEmpty(m_ggufPath) && File.Exists(m_ggufPath))
        {
            m_localSource   = TransformerEmbeddingSource.Load(m_ggufPath);
            m_latticeSource = LatticeEmbeddingSource.Load(m_ggufPath);
        }

        m_ollamaBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434/";
        try
        {
            m_ollamaSource    = new OllamaEmbeddingSource(m_ollamaBaseUrl, "nomic-embed-text");
            m_ollamaHttpClient = new HttpClient { BaseAddress = new Uri(m_ollamaBaseUrl) };
        }
        catch
        {
            m_ollamaHttpClient = new HttpClient { BaseAddress = new Uri(m_ollamaBaseUrl) };
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        m_localSource?.Dispose();
        m_latticeSource?.Dispose();
        m_ollamaSource?.Dispose();
        m_ollamaHttpClient?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Local Embed (single)")]
    public async Task<float[]> Local_Embed_Single()
    {
        if (m_localSource is null) throw new InvalidOperationException("Local GGUF not found.");
        return await m_localSource.EmbedAsync(m_sampleTexts[0]).ConfigureAwait(false);
    }

    [Benchmark(Description = "Ollama Embed (HTTP)")]
    public async Task<float[]> Ollama_Embed_Single()
    {
        if (m_ollamaSource is not null)
            return await m_ollamaSource.EmbedAsync(m_sampleTexts[0]).ConfigureAwait(false);

        if (m_ollamaHttpClient is null) throw new InvalidOperationException("Ollama client not configured.");
        var payload = new { model = "nomic-embed-text", input = m_sampleTexts[0] };
        HttpResponseMessage resp = await m_ollamaHttpClient.PostAsJsonAsync("api/embed", payload).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<float[]>(json) ?? Array.Empty<float>();
    }

    [Benchmark(Description = "Local Embed (parallel)")]
    public async Task Local_Embed_Parallel()
    {
        if (m_localSource is null) throw new InvalidOperationException("Local GGUF not found.");
        var tasks = new List<Task<float[]>>(ParallelClients);
        for (int i = 0; i < ParallelClients; i++)
            tasks.Add(m_localSource.EmbedAsync(m_sampleTexts[i % m_sampleTexts.Length]));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

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

    [Benchmark(Description = "Lattice Query (KNN)")]
    public void Lattice_Query_Knn()
    {
        if (m_localSource is null) throw new InvalidOperationException("Local GGUF not found.");

        const int N = 2000;
        var embeddings = new List<float[]>(N);
        for (int i = 0; i < N; i++)
            embeddings.Add(m_localSource.EmbedAsync(m_sampleTexts[i % m_sampleTexts.Length]).GetAwaiter().GetResult());

        var occupants = embeddings.Select((v, i) =>
            new SparseOccupant<string>(EmbeddingAdapter.Quantize(v, new QuantizationOptions()), $"id{i}")
        ).ToArray();

        var lattice = new EmbeddingLattice<string>(occupants, new LatticeOptions { LeafThreshold = 16 });
        lattice.Freeze();

        for (int q = 0; q < 200; q++)
        {
            var center  = occupants[q].Position;
            var results = lattice.QueryKNearestL2(center, 5);
            if (results.Count == 0) { }
        }
    }

    [Benchmark(Description = "Lattice Embed (single, SparseVector)")]
    public SparseVector Lattice_Embed_Single_Sparse()
    {
        if (m_latticeSource is null) throw new InvalidOperationException("Lattice source not loaded.");
        return m_latticeSource.EmbedSparse(m_sampleTexts[0]);
    }

    [Benchmark(Description = "Lattice Embed (single, float[])")]
    public async Task<float[]> Lattice_Embed_Single_Float()
    {
        if (m_latticeSource is null) throw new InvalidOperationException("Lattice source not loaded.");
        return await m_latticeSource.EmbedAsync(m_sampleTexts[0]).ConfigureAwait(false);
    }

    [Benchmark(Description = "Lattice Embed (parallel, SparseVector)")]
    public void Lattice_Embed_Parallel_Sparse()
    {
        if (m_latticeSource is null) throw new InvalidOperationException("Lattice source not loaded.");
        SparseVector[] results = m_latticeSource.EmbedSparseBatch(m_sampleTexts);
        if (results.Length == 0) { }
    }

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
        // BenchmarkDotNet spawns a worker process in a temp dir — walk up from
        // multiple candidate roots to find TestData/Embeddings wherever it lives.
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Path.GetDirectoryName(typeof(EmbeddingPerfBenchmarks).Assembly.Location),
        };

        foreach (string? root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            string? found = WalkUpForEmbeddings(root);
            if (found != null) return found;
        }
        return null;
    }

    static string? WalkUpForEmbeddings(string startDir)
    {
        string? dir = startDir;
        for (int depth = 0; depth < 12 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate)) return candidate;

            string perfCandidate = Path.Combine(dir, "SparseLattice.Perf", "TestData", "Embeddings");
            if (Directory.Exists(perfCandidate)) return perfCandidate;

            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
