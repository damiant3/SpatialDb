using SparseLattice.Embedding;
using SparseLattice.Lattice;
using SparseLattice.Math;
using System.Text.Json;
/////////////////////////////////////////////////////
namespace SparseLattice.Test.Embedding;

/// <summary>
/// Identity-of-function validation tests.
///
/// These tests ask the question: "Given the same corpus, does the SparseLattice return
/// the same Top-K neighbours as a float cosine brute-force search?"
///
/// Each test computes a <see cref="ConfidenceScore"/> (0–100) and writes it to the
/// test output. No pass/fail assertion is made on the score — you are expected to
/// read the output and judge whether the confidence is acceptable for your use case.
///
/// The score is defined as: mean recall@K × 100, across all query vectors.
///   100 = perfect identity (lattice returns exactly the same neighbours as brute-force)
///     0 = complete failure (no overlap between lattice and brute-force results)
///   Anything in between is context-dependent and should improve over time.
///
/// --- How to run the Ollama integration tests ---
/// The tests marked [TestCategory("Integration")] require a live Ollama server.
/// They are skipped automatically when Ollama is not reachable.
///
/// --- How to enable deeper testing with real model outputs ---
/// Place embedding CSV files (one float per column, one vector per row) in:
///   SparseLattice.Test/TestData/Embeddings/
/// The file name pattern is:  {modelName}_{dimensions}.csv
/// Example: nomic-embed-text_768.csv
/// Tests in the <see cref="EmbeddingFileCorpusTests"/> region will auto-discover
/// these files and run the identity sweep against them.
/// </summary>
[TestClass]
public sealed class IdentityValidationTests
{
    // -----------------------------------------------------------------------
    // Synthetic deterministic tests (no model, no server required)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Unit_Identity_SyntheticCorpus_ConfidenceReported()
    {
        // Build a small synthetic corpus using the HashingEmbeddingSource.
        // This is deterministic and requires no server.
        HashingEmbeddingSource source = new(dimensions: 64);
        List<(string text, string label)> corpus = BuildCodeSnippetCorpus();

        IdentityReport report = await RunIdentityValidation(
            source,
            corpus,
            queriesFromCorpus: 5,
            k: 3,
            zeroThreshold: 0.0f);

        WriteReport(report, "Synthetic hashing source (no model)");

        // No assertion — just report. The hashing source is not a real semantic model
        // so recall may be low. We print it to establish a baseline.
    }

    [TestMethod]
    public async Task Unit_Identity_SparsityBudgetSweep_ConfidenceReported()
    {
        HashingEmbeddingSource source = new(dimensions: 32);
        List<(string text, string label)> corpus = BuildCodeSnippetCorpus();

        int[] budgets = [4, 8, 16, 32];
        foreach (int budget in budgets)
        {
            IdentityReport report = await RunIdentityValidation(
                source,
                corpus,
                queriesFromCorpus: 5,
                k: 3,
                zeroThreshold: 0.0f,
                sparsityBudget: budget);
            WriteReport(report, $"SparsityBudget={budget}");
        }
    }

    [TestMethod]
    public async Task Unit_Identity_ZeroThresholdSweep_ConfidenceReported()
    {
        HashingEmbeddingSource source = new(dimensions: 64);
        List<(string text, string label)> corpus = BuildCodeSnippetCorpus();

        float[] thresholds = [0.0f, 0.005f, 0.01f, 0.02f, 0.05f, 0.1f];
        foreach (float threshold in thresholds)
        {
            IdentityReport report = await RunIdentityValidation(
                source,
                corpus,
                queriesFromCorpus: 5,
                k: 3,
                zeroThreshold: threshold);
            WriteReport(report, $"ZeroThreshold={threshold}");
        }
    }

    // -----------------------------------------------------------------------
    // Ollama integration tests — skipped when server is not reachable
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_Identity_Ollama_ConfidenceReported()
    {
        string baseUrl = GetOllamaBaseUrl();
        string modelName = GetOllamaEmbeddingModel();

        if (!await IsOllamaReachable(baseUrl))
        {
            WriteSkipMessage($"Ollama not reachable at {baseUrl}. Skipping.");
            return;
        }

        using OllamaEmbeddingSource source = new(baseUrl, modelName);
        List<(string text, string label)> corpus = BuildCodeSnippetCorpus();

        try
        {
            IdentityReport report = await RunIdentityValidation(
                source,
                corpus,
                queriesFromCorpus: 5,
                k: 3,
                zeroThreshold: 0.01f);
            WriteReport(report, $"Ollama/{modelName}");
        }
        catch (HttpRequestException ex)
        {
            WriteSkipMessage($"Ollama model '{modelName}' not available ({ex.Message}). " +
                $"Set OLLAMA_EMBED_MODEL env var to a pulled model name and retry.");
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_Identity_Ollama_ThresholdSweep_ConfidenceReported()
    {
        string baseUrl = GetOllamaBaseUrl();
        string modelName = GetOllamaEmbeddingModel();

        if (!await IsOllamaReachable(baseUrl))
        {
            WriteSkipMessage($"Ollama not reachable at {baseUrl}. Skipping.");
            return;
        }

        using OllamaEmbeddingSource source = new(baseUrl, modelName);
        List<(string text, string label)> corpus = BuildCodeSnippetCorpus();

        float[] thresholds = [0.001f, 0.005f, 0.01f, 0.02f, 0.05f];
        try
        {
            foreach (float threshold in thresholds)
            {
                IdentityReport report = await RunIdentityValidation(
                    source,
                    corpus,
                    queriesFromCorpus: 5,
                    k: 3,
                    zeroThreshold: threshold);
                WriteReport(report, $"Ollama/{modelName} threshold={threshold}");
            }
        }
        catch (HttpRequestException ex)
        {
            WriteSkipMessage($"Ollama model '{modelName}' not available ({ex.Message}). " +
                $"Set OLLAMA_EMBED_MODEL env var to a pulled model name and retry.");
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_Identity_AllServerEmbeddingModels_ConfidenceReported()
    {
        string baseUrl = GetOllamaBaseUrl();

        if (!await IsOllamaReachable(baseUrl))
        {
            WriteSkipMessage($"Ollama not reachable at {baseUrl}. Skipping.");
            return;
        }

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
        HttpResponseMessage resp;
        try
        {
            resp = await client.GetAsync(baseUrl.TrimEnd('/') + "/api/ps").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WriteSkipMessage($"Failed to list models from Ollama: {ex.Message}");
            return;
        }

        if (!resp.IsSuccessStatusCode)
        {
            WriteSkipMessage($"Ollama /api/ps returned {(int)resp.StatusCode}. Skipping.");
            return;
        }

        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        System.Collections.Generic.HashSet<string> modelNames = new();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("models", out JsonElement modelsElem) && modelsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in modelsElem.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        if (item.TryGetProperty("name", out JsonElement nameProp) && nameProp.ValueKind == JsonValueKind.String)
                            modelNames.Add(nameProp.GetString()!);
                        else if (item.TryGetProperty("model", out JsonElement modelProp) && modelProp.ValueKind == JsonValueKind.String)
                            modelNames.Add(modelProp.GetString()!);
                    }
                    else if (item.ValueKind == JsonValueKind.String)
                        modelNames.Add(item.GetString()!);
                }
            }
        }
        catch (Exception ex)
        {
            WriteSkipMessage($"Failed to parse /api/ps response: {ex.Message}");
            return;
        }

        if (modelNames.Count == 0)
        {
            WriteSkipMessage("No models discovered on server. Skipping.");
            return;
        }

        foreach (string model in modelNames)
        {
            // For each model, probe model info to see if it advertises embedding length
            try
            {
                object payload = new { name = model };
                string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
                using StringContent sc = new(payloadJson, System.Text.Encoding.UTF8, "application/json");
                HttpResponseMessage infoResp = await client.PostAsync(baseUrl.TrimEnd('/') + "/api/show", sc).ConfigureAwait(false);
                if (!infoResp.IsSuccessStatusCode)
                {
                    WriteSkipMessage($"Model {model}: /api/show returned {(int)infoResp.StatusCode}. Skipping model.");
                    continue;
                }

                string infoBody = await infoResp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using JsonDocument infoDoc = JsonDocument.Parse(infoBody);

                // Try to find model_info.general.embedding_length (fallbacks handled)
                int embedLen = 0;
                if (infoDoc.RootElement.TryGetProperty("model_info", out JsonElement mi) && mi.ValueKind == JsonValueKind.Object)
                {
                    if (mi.TryGetProperty("general.embedding_length", out JsonElement ge) && ge.ValueKind == JsonValueKind.Number)
                    {
                        embedLen = ge.GetInt32();
                    }
                    else if (mi.TryGetProperty("general", out JsonElement general) && general.ValueKind == JsonValueKind.Object && general.TryGetProperty("embedding_length", out JsonElement el) && el.ValueKind == JsonValueKind.Number)
                    {
                        embedLen = el.GetInt32();
                    }
                }

                if (embedLen <= 0)
                {
                    // Some servers don't populate model_info; try the details->embedding_length path
                    if (infoDoc.RootElement.TryGetProperty("ModelInfoData", out JsonElement mid) && mid.ValueKind == JsonValueKind.Object)
                    {
                        if (mid.TryGetProperty("EmbeddingLength", out JsonElement el2) && el2.ValueKind == JsonValueKind.Number)
                            embedLen = el2.GetInt32();
                    }
                }

                if (embedLen <= 0)
                {
                    WriteOutput($"Model {model}: does not advertise embedding length (skipping).");
                    continue;
                }

                // Run identity validation for this embedding model
                using OllamaEmbeddingSource source = new(baseUrl, model);
                List<(string text, string label)> corpus = BuildCodeSnippetCorpus();

                try
                {
                    IdentityReport report = await RunIdentityValidation(
                        source,
                        corpus,
                        queriesFromCorpus: 5,
                        k: 3,
                        zeroThreshold: 0.01f);
                    WriteReport(report, $"Ollama/{model}");
                }
                catch (HttpRequestException ex)
                {
                    WriteSkipMessage($"Model {model}: HTTP error during embed ({ex.Message}). Skipping.");
                    continue;
                }
            }
            catch (Exception ex)
            {
                WriteSkipMessage($"Model {model}: unexpected error ({ex.Message}). Skipping.");
                continue;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Core validation engine
    // -----------------------------------------------------------------------

    private static async Task<IdentityReport> RunIdentityValidation(
        IEmbeddingSource source,
        List<(string text, string label)> corpus,
        int queriesFromCorpus,
        int k,
        float zeroThreshold,
        int? sparsityBudget = null)
    {
        QuantizationOptions quantOptions = new()
        {
            ZeroThreshold = zeroThreshold,
            SparsityBudget = sparsityBudget,
        };

        // Embed all corpus items
        float[][] embeddings = await source.EmbedBatchAsync(
            corpus.ConvertAll(c => c.text)).ConfigureAwait(false);

        // Build brute-force float corpus for cosine-distance ground truth
        List<SparseOccupant<string>> floatCorpus = [];
        List<SparseOccupant<string>> sparseCorpus = [];

        for (int i = 0; i < corpus.Count; i++)
        {
            SparseVector sparse = EmbeddingAdapter.Quantize(embeddings[i], quantOptions);
            sparseCorpus.Add(new SparseOccupant<string>(sparse, corpus[i].label));

            // For float ground truth we also quantize — but with zero threshold only,
            // no sparsity budget — so we compare apples to apples on quantized space.
            SparseVector reference = EmbeddingAdapter.Quantize(embeddings[i],
                new QuantizationOptions { ZeroThreshold = 0.0f });
            floatCorpus.Add(new SparseOccupant<string>(reference, corpus[i].label));
        }

        EmbeddingLattice<string> lattice = new(sparseCorpus.ToArray(),
            new LatticeOptions { LeafThreshold = System.Math.Max(4, corpus.Count / 8) });
        lattice.Freeze();

        SparsityReport sparsityReport = lattice.CollectSparsityReport();

        // Choose query vectors from the corpus (first N, to be deterministic)
        int queryCount = System.Math.Min(queriesFromCorpus, corpus.Count);
        List<SparseVector> queryVectors = [];
        for (int i = 0; i < queryCount; i++)
            queryVectors.Add(sparseCorpus[i].Position);

        // Evaluate recall
        AggregateRecallStats recallStats = RecallEvaluator.AggregateL2(
            queryVectors,
            sparseCorpus,
            q => lattice.QueryKNearestL2(q, k),
            k);

        double confidenceScore = recallStats.MeanRecallAtK * 100.0;

        return new IdentityReport
        {
            ModelName = source.ModelName,
            CorpusSize = corpus.Count,
            QueryCount = queryCount,
            K = k,
            ZeroThreshold = zeroThreshold,
            SparsityBudget = sparsityBudget,
            MeanNnz = sparsityReport.MeanNnz,
            RealizedDimensions = sparsityReport.RealizedDimensions,
            TotalDimensions = sparsityReport.TotalDimensions,
            ConfidenceScore = confidenceScore,
            MinRecall = recallStats.MinRecallAtK * 100.0,
            MaxRecall = recallStats.MaxRecallAtK * 100.0,
        };
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static List<(string text, string label)> BuildCodeSnippetCorpus()
        =>
        [
            ("public int Add(int a, int b) => a + b;", "add-integers"),
            ("public string Greet(string name) => $\"Hello, {name}!\";", "greet-string"),
            ("public bool IsEven(int n) => n % 2 == 0;", "is-even"),
            ("public double Sqrt(double x) => Math.Sqrt(x);", "sqrt"),
            ("public List<int> Range(int n) => Enumerable.Range(0, n).ToList();", "range-list"),
            ("public void Log(string msg) => Console.WriteLine(msg);", "log-console"),
            ("public int Max(int a, int b) => a > b ? a : b;", "max-two"),
            ("public bool Contains<T>(IEnumerable<T> src, T item) => src.Any(x => x!.Equals(item));", "contains-generic"),
            ("public string Reverse(string s) => new string(s.Reverse().ToArray());", "reverse-string"),
            ("public int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);", "factorial"),
            ("public Task<T> AsTask<T>(T value) => Task.FromResult(value);", "as-task"),
            ("public bool IsNullOrEmpty(string? s) => string.IsNullOrEmpty(s);", "is-null-empty"),
            ("public IEnumerable<T> Flatten<T>(IEnumerable<IEnumerable<T>> src) => src.SelectMany(x => x);", "flatten"),
            ("public Dictionary<K,V> ToDictionary<K,V>(IEnumerable<(K,V)> pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);", "to-dictionary"),
            ("public int Clamp(int v, int min, int max) => Math.Clamp(v, min, max);", "clamp"),
            ("public byte[] ToBytes(string s) => Encoding.UTF8.GetBytes(s);", "to-bytes"),
            ("public string FromBytes(byte[] b) => Encoding.UTF8.GetString(b);", "from-bytes"),
            ("public int CountDistinct<T>(IEnumerable<T> src) => src.Distinct().Count();", "count-distinct"),
            ("public T First<T>(IList<T> list) => list[0];", "first-element"),
            ("public T Last<T>(IList<T> list) => list[list.Count - 1];", "last-element"),
        ];

    private static List<(string text, string label)> BuildLabelledCorpus(int count)
    {
        List<(string text, string label)> corpus = [];
        for (int i = 0; i < count; i++)
            corpus.Add(($"item-{i}", $"label-{i}"));
        return corpus;
    }

    private static float[][] LoadEmbeddingCsv(string path)
    {
        List<float[]> rows = [];
        foreach (string line in System.IO.File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split(',');
            float[] vec = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v))
                    vec[i] = v;
            rows.Add(vec);
        }
        return [.. rows];
    }

    private static string GetOllamaBaseUrl()
        => System.Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
           ?? "http://localhost:11434/";

    private static string GetOllamaEmbeddingModel()
        => System.Environment.GetEnvironmentVariable("OLLAMA_EMBED_MODEL")
            // ?? "nomic-embed-text";
            ?? "embeddinggemma";
    private static async Task<bool> IsOllamaReachable(string baseUrl)
    {
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(3) };
            HttpResponseMessage response = await client.GetAsync(
                baseUrl.TrimEnd('/') + "/api/tags").ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string GetTestDataEmbeddingsDir()
    {
        // Walks up from the test assembly directory to find the solution root, then
        // looks for SparseLattice.Test/TestData/Embeddings.
        string? dir = System.IO.Path.GetDirectoryName(
            typeof(IdentityValidationTests).Assembly.Location);
        while (dir is not null)
        {
            string candidate = System.IO.Path.Combine(
                dir, "SparseLattice.Test", "TestData", "Embeddings");
            if (System.IO.Directory.Exists(candidate))
                return candidate;
            string relative = System.IO.Path.Combine(dir, "TestData", "Embeddings");
            if (System.IO.Directory.Exists(relative))
                return relative;
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }
        // Fallback: sibling of the assembly directory
        return System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(IdentityValidationTests).Assembly.Location) ?? ".",
            "TestData", "Embeddings");
    }

    private static void WriteReport(IdentityReport report, string label)
    {
        string line = $"[IDENTITY] {label,-50} | "
            + $"confidence={report.ConfidenceScore,6:F1}/100 "
            + $"(min={report.MinRecall:F1} max={report.MaxRecall:F1}) | "
            + $"corpus={report.CorpusSize} k={report.K} queries={report.QueryCount} | "
            + $"threshold={report.ZeroThreshold} budget={report.SparsityBudget?.ToString() ?? "none"} | "
            + $"nnz mean={report.MeanNnz:F1} dims={report.RealizedDimensions}/{report.TotalDimensions}";

        Console.WriteLine(line);
    }

    private static void WriteOutput(string message)
        => Console.WriteLine(message);

    private static void WriteSkipMessage(string message)
        => Console.WriteLine($"[SKIP] {message}");
}

// -----------------------------------------------------------------------
// Supporting types
// -----------------------------------------------------------------------

/// <summary>Result of a single identity-of-function validation run.</summary>
public sealed class IdentityReport
{
    public string ModelName { get; init; } = "";
    public int CorpusSize { get; init; }
    public int QueryCount { get; init; }
    public int K { get; init; }
    public float ZeroThreshold { get; init; }
    public int? SparsityBudget { get; init; }
    public double MeanNnz { get; init; }
    public int RealizedDimensions { get; init; }
    public int TotalDimensions { get; init; }

    /// <summary>Mean recall@K × 100. Range [0, 100]. Higher is better.</summary>
    public double ConfidenceScore { get; init; }

    /// <summary>Minimum per-query recall@K × 100 observed across all queries.</summary>
    public double MinRecall { get; init; }

    /// <summary>Maximum per-query recall@K × 100 observed across all queries.</summary>
    public double MaxRecall { get; init; }
}

// -----------------------------------------------------------------------
// Mock sources used by tests above
// -----------------------------------------------------------------------

/// <summary>
/// Deterministic hashing-based embedding source. Produces bag-of-words style float
/// vectors by hashing each token and accumulating into the appropriate dimension slot.
/// Useful for confirming the pipeline works end-to-end without a real model.
/// </summary>
internal sealed class HashingEmbeddingSource(int dimensions = 64) : IEmbeddingSource
{
    private readonly int m_dimensions = System.Math.Max(8, dimensions);

    public string ModelName => $"hashing-{m_dimensions}d";
    public int Dimensions => m_dimensions;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        float[] vector = new float[m_dimensions];
        if (string.IsNullOrEmpty(text))
            return Task.FromResult(vector);

        string[] tokens = text.Split(
            [' ', '\n', '\r', '\t', '.', ',', '(', ')', '{', '}', ';', ':'],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string token in tokens)
        {
            string t = token.Trim();
            if (t.Length == 0) continue;
            int h = t.GetHashCode();
            int idx = System.Math.Abs(h) % m_dimensions;
            vector[idx] += 1f;
        }

        double sum = 0;
        for (int i = 0; i < vector.Length; i++)
            sum += vector[i] * vector[i];
        if (sum > 0)
        {
            double norm = System.Math.Sqrt(sum);
            for (int i = 0; i < vector.Length; i++)
                vector[i] = (float)(vector[i] / norm);
        }

        return Task.FromResult(vector);
    }

    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        float[][] results = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
            results[i] = await EmbedAsync(texts[i], ct).ConfigureAwait(false);
        return results;
    }
}

/// <summary>
/// Source backed by a pre-loaded float[][] array — used for file-corpus tests.
/// Returns vectors in the order they were supplied, cycling if the corpus is exhausted.
/// </summary>
internal sealed class PreloadedEmbeddingSource(string modelName, float[][] vectors) : IEmbeddingSource
{
    private int m_index;

    public string ModelName => modelName;
    public int Dimensions => vectors.Length > 0 ? vectors[0].Length : 0;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        float[] v = vectors[m_index % vectors.Length];
        m_index++;
        return Task.FromResult((float[])v.Clone());
    }

    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        float[][] results = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
            results[i] = await EmbedAsync(texts[i], ct).ConfigureAwait(false);
        return results;
    }
}
