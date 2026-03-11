using SparseLattice.Embedding;
using SparseLattice.Gguf;
using SparseLattice.Lattice;
using SparseLattice.Math;
using SystemMath = System.Math;
///////////////////////////////////////////////
namespace SparseLattice.Test.Embedding;

/// <summary>
/// Quality tests against the full solution source corpus (~1000 real C# lines).
///
/// Q1 — Embedding fidelity:
///   Mean cosine similarity between LatticeEmbeddingSource (token lookup, no transformer)
///   and TransformerEmbeddingSource (full forward pass) on the same text.
///   This answers: "are the fast embeddings semantically equivalent to the expensive ones?"
///
/// Q2 — Lattice recall@10 at scale (n=100, n=500):
///   Brute-force L2 KNN vs lattice KNN, same embedding source for both.
///   This answers: "does the KNN tree find the right neighbours at real corpus sizes?"
///
/// Q3 — End-to-end semantic grouping:
///   Given known-similar pairs (same function family), are they closer in the lattice
///   than known-dissimilar pairs?
///   This answers: "do the embeddings actually encode meaning?"
///
/// All tests are [TestCategory("Integration")] and skip gracefully when prerequisites
/// (GGUF file, Ollama) are not present.
/// </summary>
[TestClass]
public sealed class CorpusQualityTests
{
    private const int    k_topK             = 10;
    private const float  k_cosineThreshold  = 0.60f;  // LatticeEmbed vs TransformerEmbed: minimum acceptable mean cosine
    private const double k_recallThreshold  = 0.70;   // lattice recall@10 vs brute-force
    private const double k_separationRatio  = 1.10;   // similar-pair distance must be this much smaller than dissimilar-pair distance

    // -----------------------------------------------------------------------
    // Q1: Embedding fidelity — LatticeEmbeddingSource vs TransformerEmbeddingSource
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_Fidelity_LatticeVsTransformer_MeanCosine()
    {
        string? gguf = ResolveGguf();
        if (gguf is null) { Assert.Inconclusive("GGUF not found."); return; }

        string[] corpus = LoadCorpus(200);

        using TransformerEmbeddingSource transformer = TransformerEmbeddingSource.Load(gguf);
        using LatticeEmbeddingSource      lattice    = LatticeEmbeddingSource.Load(gguf);

        Console.WriteLine($"[Q1] Fidelity: LatticeEmbeddingSource vs TransformerEmbeddingSource");
        Console.WriteLine($"[Q1] Corpus: {corpus.Length} lines  |  SparsityBudget: {lattice.Dimensions / 4} / {lattice.Dimensions}  (25%)");
        Console.WriteLine();

        float totalCosine = 0f;
        float minCosine   = float.MaxValue;
        float maxCosine   = float.MinValue;

        // Track per-decile to see distribution
        float[] decileSums   = new float[10];
        int[]   decileCounts = new int[10];

        for (int i = 0; i < corpus.Length; i++)
        {
            float[] transformerVec = await transformer.EmbedAsync(corpus[i]).ConfigureAwait(false);
            float[] latticeVec     = await lattice.EmbedAsync(corpus[i]).ConfigureAwait(false);

            float cosine = CosineSimilarity(transformerVec, latticeVec);
            totalCosine += cosine;
            if (cosine < minCosine) minCosine = cosine;
            if (cosine > maxCosine) maxCosine = cosine;

            int decile = SystemMath.Min(9, (int)(cosine * 10));
            decileSums[decile]   += cosine;
            decileCounts[decile] += 1;
        }

        float meanCosine = totalCosine / corpus.Length;

        Console.WriteLine($"[Q1] {"Metric",-30} {"Value",10}");
        Console.WriteLine($"[Q1] {new string('-', 42)}");
        Console.WriteLine($"[Q1] {"Mean cosine",-30} {meanCosine,10:F4}");
        Console.WriteLine($"[Q1] {"Min cosine",-30} {minCosine,10:F4}");
        Console.WriteLine($"[Q1] {"Max cosine",-30} {maxCosine,10:F4}");
        Console.WriteLine($"[Q1] {"Threshold",-30} {k_cosineThreshold,10:F4}");
        Console.WriteLine();
        Console.WriteLine($"[Q1] Distribution:");
        for (int d = 9; d >= 0; d--)
        {
            if (decileCounts[d] == 0) continue;
            float lo = d / 10f, hi = (d + 1) / 10f;
            Console.WriteLine($"[Q1]   cosine [{lo:F1},{hi:F1})  count={decileCounts[d],4}  mean={decileSums[d]/decileCounts[d]:F4}");
        }
        Console.WriteLine();
        Console.WriteLine($"[Q1] VERDICT: mean cosine = {meanCosine:F4}  (need ≥ {k_cosineThreshold:F2})  →  {(meanCosine >= k_cosineThreshold ? "PASS ✓" : "FAIL ✗")}");

        Assert.IsTrue(meanCosine >= k_cosineThreshold,
            $"Mean cosine between LatticeEmbeddingSource and TransformerEmbeddingSource is {meanCosine:F4}, " +
            $"below threshold {k_cosineThreshold}. The token-lookup embeddings diverge too much from the full forward pass.");
    }

    // -----------------------------------------------------------------------
    // Q2a: Recall@10 at n=100
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_Recall10_LatticeVsBrute_N100()
    {
        string? gguf = ResolveGguf();
        if (gguf is null) { Assert.Inconclusive("GGUF not found."); return; }

        RunRecallTest(gguf, corpusSize: 100, label: "N=100");
    }

    // -----------------------------------------------------------------------
    // Q2b: Recall@10 at n=500
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_Recall10_LatticeVsBrute_N500()
    {
        string? gguf = ResolveGguf();
        if (gguf is null) { Assert.Inconclusive("GGUF not found."); return; }

        RunRecallTest(gguf, corpusSize: 500, label: "N=500");
    }

    // -----------------------------------------------------------------------
    // Q3: Semantic separation — similar pairs closer than dissimilar pairs
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_SemanticSeparation_SimilarPairsCloserThanDissimilar()
    {
        string? gguf = ResolveGguf();
        if (gguf is null) { Assert.Inconclusive("GGUF not found."); return; }

        using LatticeEmbeddingSource src = LatticeEmbeddingSource.Load(gguf);

        // Pairs expected to be semantically similar (same function family)
        (string a, string b)[] similarPairs =
        [
            ("public int Add(int a, int b) => a + b;",
             "public int Sum(int x, int y) => x + y;"),
            ("public bool IsEven(int n) => n % 2 == 0;",
             "public bool IsOdd(int n) => n % 2 != 0;"),
            ("private void Log(string msg) => Console.WriteLine(msg);",
             "public void Print(string text) => Console.Write(text);"),
            ("public int Max(int a, int b) => a > b ? a : b;",
             "public int Min(int a, int b) => a < b ? a : b;"),
            ("public string ToUpper(string s) => s.ToUpperInvariant();",
             "public string ToLower(string s) => s.ToLowerInvariant();"),
        ];

        // Pairs expected to be semantically dissimilar (different domains)
        (string a, string b)[] dissimilarPairs =
        [
            ("public int Add(int a, int b) => a + b;",
             "private readonly List<SparseOccupant<string>> m_nodes = new();"),
            ("public bool IsEven(int n) => n % 2 == 0;",
             "public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)"),
            ("private void Log(string msg) => Console.WriteLine(msg);",
             "SparseEntry[] entries = new SparseEntry[nonzeroCount];"),
            ("public int Max(int a, int b) => a > b ? a : b;",
             "for (int d = 0; d < m_embeddingDimensions; d++) accumulator[d] /= validTokenCount;"),
            ("public string ToUpper(string s) => s.ToUpperInvariant();",
             "double sumSquared = 0.0; for (int d = 0; d < dims; d++) { long val = acc[d]; sumSquared += (double)val * val; }"),
        ];

        System.Numerics.BigInteger similarTotal    = 0;
        System.Numerics.BigInteger dissimilarTotal = 0;
        int wins = 0;

        Console.WriteLine($"[Q3] Semantic separation test  (SparsityBudget={src.Dimensions/4}/{src.Dimensions})");
        Console.WriteLine($"[Q3] {"Similar pair",-60} {"dist",14}");
        Console.WriteLine($"[Q3] {new string('-', 76)}");

        for (int i = 0; i < similarPairs.Length; i++)
        {
            SparseVector va = src.EmbedSparse(similarPairs[i].a);
            SparseVector vb = src.EmbedSparse(similarPairs[i].b);
            SparseVector vc = src.EmbedSparse(dissimilarPairs[i].a);
            SparseVector vd = src.EmbedSparse(dissimilarPairs[i].b);

            System.Numerics.BigInteger simDist    = va.DistanceSquaredL2(vb);
            System.Numerics.BigInteger disSimDist = vc.DistanceSquaredL2(vd);
            similarTotal    += simDist;
            dissimilarTotal += disSimDist;

            bool win = simDist < disSimDist;
            if (win) wins++;
            Console.WriteLine($"[Q3]  sim:    {similarPairs[i].a[..SystemMath.Min(55, similarPairs[i].a.Length)],-55}  d²={simDist,20}  {(win ? "✓" : "✗")}");
            Console.WriteLine($"[Q3]  dissim: {dissimilarPairs[i].b[..SystemMath.Min(55, dissimilarPairs[i].b.Length)],-55}  d²={disSimDist,20}");
            Console.WriteLine();
        }

        double meanSim    = (double)similarTotal    / similarPairs.Length;
        double meanDisSim = (double)dissimilarTotal / dissimilarPairs.Length;
        double ratio      = meanSim > 0 ? meanDisSim / meanSim : 0;

        Console.WriteLine($"[Q3] Mean similar dist²    : {meanSim:E3}");
        Console.WriteLine($"[Q3] Mean dissimilar dist² : {meanDisSim:E3}");
        Console.WriteLine($"[Q3] Separation ratio      : {ratio:F2}×  (dissim/sim, need ≥ {k_separationRatio:F2})");
        Console.WriteLine($"[Q3] Pair wins             : {wins}/{similarPairs.Length}");
        Console.WriteLine($"[Q3] VERDICT: ratio={ratio:F2}  →  {(ratio >= k_separationRatio ? "PASS ✓" : "FAIL ✗")}");

        Assert.IsTrue(ratio >= k_separationRatio,
            $"Dissimilar pairs (ratio={ratio:F2}) are not sufficiently further apart than similar pairs. " +
            $"Threshold: {k_separationRatio:F2}×. The embeddings may not encode semantic meaning.");
    }

    // -----------------------------------------------------------------------
    // Shared recall runner
    // -----------------------------------------------------------------------

    private static void RunRecallTest(string gguf, int corpusSize, string label)
    {
        string[] allLines = LoadCorpus(corpusSize);
        int n = SystemMath.Min(corpusSize, allLines.Length);

        using LatticeEmbeddingSource src = LatticeEmbeddingSource.Load(gguf);

        var occupants = new SparseOccupant<string>[n];
        for (int i = 0; i < n; i++)
            occupants[i] = new SparseOccupant<string>(src.EmbedSparse(allLines[i]), allLines[i][..SystemMath.Min(30, allLines[i].Length)]);

        EmbeddingLattice<string> lattice = new(
            occupants,
            new LatticeOptions { LeafThreshold = SystemMath.Max(8, n / 32) });
        lattice.Freeze();

        // Use every 5th item as a query (diverse sample, not just the first few)
        int queryCount = SystemMath.Min(50, n / 5);
        int step       = n / queryCount;

        int    truePositives = 0;
        int    totalExpected = queryCount * k_topK;
        double minRecall = 1.0, maxRecall = 0.0;

        // Per-bucket recall distribution
        int[] recallBuckets = new int[11]; // [0.0,0.1), ..., [0.9,1.0], [1.0]

        for (int qi = 0; qi < queryCount; qi++)
        {
            int idx = (qi * step) % n;
            SparseVector query = occupants[idx].Position;

            var latticeResults = lattice.QueryKNearestL2(query, k_topK);
            var bruteResults   = RecallEvaluator.BruteForceKNearestL2(query, occupants, k_topK);

            RecallResult r = RecallEvaluator.EvaluateQuery(bruteResults, latticeResults, k_topK);
            truePositives += r.TruePositives;
            if (r.RecallAtK < minRecall) minRecall = r.RecallAtK;
            if (r.RecallAtK > maxRecall) maxRecall = r.RecallAtK;
            recallBuckets[SystemMath.Min(10, (int)(r.RecallAtK * 10))]++;
        }

        double meanRecall = (double)truePositives / totalExpected;

        Console.WriteLine($"[Q2] Recall@{k_topK} — {label}");
        Console.WriteLine($"[Q2] Corpus: {n}  Queries: {queryCount}  SparsityBudget: {src.Dimensions/4}/{src.Dimensions}  LeafThreshold: {SystemMath.Max(8, n/32)}");
        Console.WriteLine();
        Console.WriteLine($"[Q2] {"Metric",-30} {"Value",10}");
        Console.WriteLine($"[Q2] {new string('-', 42)}");
        Console.WriteLine($"[Q2] {"Mean recall@10",-30} {meanRecall,10:F4}");
        Console.WriteLine($"[Q2] {"Min recall@10",-30} {minRecall,10:F4}");
        Console.WriteLine($"[Q2] {"Max recall@10",-30} {maxRecall,10:F4}");
        Console.WriteLine($"[Q2] {"Threshold",-30} {k_recallThreshold,10:F2}");
        Console.WriteLine();
        Console.WriteLine($"[Q2] Recall distribution ({queryCount} queries):");
        for (int b = 10; b >= 0; b--)
        {
            if (recallBuckets[b] == 0) continue;
            float lo = b / 10f;
            string bar = new('#', recallBuckets[b] * 40 / queryCount);
            Console.WriteLine($"[Q2]   recall {(b == 10 ? "1.0  " : $"[{lo:F1},{lo+0.1f:F1})")}  {recallBuckets[b],4}  {bar}");
        }
        Console.WriteLine();
        Console.WriteLine($"[Q2] VERDICT: recall@10 = {meanRecall:F4}  (need ≥ {k_recallThreshold:F2})  →  {(meanRecall >= k_recallThreshold ? "PASS ✓" : "FAIL ✗")}");

        Assert.IsTrue(meanRecall >= k_recallThreshold,
            $"Lattice recall@{k_topK} at {label} = {meanRecall:F4}, below threshold {k_recallThreshold}. " +
            "The KNN tree is failing to find ground-truth neighbours at this corpus size.");
    }

    // -----------------------------------------------------------------------
    // Corpus loader — real .cs lines from solution, same logic as GenerateSamples tool
    // -----------------------------------------------------------------------

    private static string[] LoadCorpus(int maxLines)
    {
        // First try the pre-built samples.txt (written by GenerateSamples tool)
        string? samplesFile = FindSamplesFile();
        if (samplesFile is not null)
        {
            string[] lines = File.ReadAllLines(samplesFile)
                .Select(l => l.Trim())
                .Where(l => l.Length >= 10)
                .Take(maxLines)
                .ToArray();
            if (lines.Length >= SystemMath.Min(maxLines, 50))
                return lines;
        }

        // Fall back: harvest directly from .cs files in the solution
        return HarvestFromSolution(maxLines);
    }

    private static string? FindSamplesFile()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 10 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Samples", "samples.txt");
            if (File.Exists(candidate)) return candidate;
            // Also check the Perf project's TestData
            string perf = Path.Combine(dir, "SparseLattice.Perf", "TestData", "Samples", "samples.txt");
            if (File.Exists(perf)) return perf;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string[] HarvestFromSolution(int maxLines)
    {
        string? root = FindSolutionRoot();
        if (root is null) return FallbackSnippets();

        var lines = new List<string>(maxLines + 50);
        var seen  = new HashSet<string>(StringComparer.Ordinal);

        string[] roots =
        [
            Path.Combine(root, "SpatialDbLib"),
            Path.Combine(root, "SparseLattice"),
            Path.Combine(root, "SparseLattice.Test"),
            Path.Combine(root, "SparseLattice.Perf"),
            Path.Combine(root, "SpatialGame"),
        ];

        foreach (string projRoot in roots)
        {
            if (!Directory.Exists(projRoot)) continue;
            foreach (string file in Directory.GetFiles(projRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (lines.Count >= maxLines) break;
                string fp = file.Replace('\\', '/').ToLowerInvariant();
                if (fp.Contains("/bin/") || fp.Contains("/obj/") || fp.Contains("/testdata/")) continue;
                if (fp.Contains("extensionguide") || fp.Contains("guide.cs")) continue;

                foreach (string raw in File.ReadLines(file))
                {
                    if (lines.Count >= maxLines) break;
                    string l = raw.Trim();
                    if (l.Length < 10 || l.Length > 200) continue;
                    if (l.StartsWith("//") || l.StartsWith("/*") || l.StartsWith("*") || l.StartsWith("///")) continue;
                    if (l.StartsWith("using ") || l.StartsWith("namespace ") || l.StartsWith("#")) continue;
                    if (System.Text.RegularExpressions.Regex.IsMatch(l, @"^[{}\[\]();,\s]+$")) continue;
                    if (!IsCodeLine(l)) continue;
                    if (!seen.Add(l)) continue;
                    lines.Add(l);
                }
            }
            if (lines.Count >= maxLines) break;
        }

        return lines.Count >= 10 ? lines.ToArray() : FallbackSnippets();
    }

    private static bool IsCodeLine(string l)
        => l.Contains(';') || l.Contains("=>") || l.Contains("public ") || l.Contains("private ") ||
           l.Contains("return ") || l.Contains("var ") || l.Contains("new ") ||
           l.Contains("static ") || l.Contains("override ") || l.Contains("async ") ||
           l.Contains("await ") || l.Contains("Task<") || l.Contains("if (") ||
           l.Contains("for (") || l.Contains("foreach (") || l.Contains("string ") ||
           l.Contains("int ") || l.Contains("float ") || l.Contains("double ") || l.Contains("void ");

    private static string? FindSolutionRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 10 && dir is not null; depth++)
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
            if (Directory.GetFiles(dir, "*.sln", SearchOption.TopDirectoryOnly).Length > 0) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? ResolveGguf()
    {
        string? path = Environment.GetEnvironmentVariable("EMBEDDING_GGUF_PATH");
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;

        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 10 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate))
            {
                // Prefer the nomic-embed-text blob specifically — it's the model
                // both TransformerEmbeddingSource and LatticeEmbeddingSource are built for.
                string? located = OllamaModelLocator.LocateGguf("nomic-embed-text", candidate);
                if (!string.IsNullOrEmpty(located) && File.Exists(located)) return located;

                // Fallback: smallest sha256-* blob (nomic is ~262 MB, gemma is ~593 MB)
                string[] blobs = Directory.GetFiles(candidate, "sha256-*", SearchOption.TopDirectoryOnly);
                if (blobs.Length > 0)
                    return blobs.OrderBy(f => new FileInfo(f).Length).First();
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string[] FallbackSnippets() =>
    [
        "public int Add(int a, int b) => a + b;",
        "public string Greet(string name) => $\"Hello, {name}!\";",
        "public bool IsEven(int n) => n % 2 == 0;",
        "public double Sqrt(double x) => Math.Sqrt(x);",
        "public void Log(string msg) => Console.WriteLine(msg);",
        "public int Max(int a, int b) => a > b ? a : b;",
        "public string Reverse(string s) => new string(s.Reverse().ToArray());",
        "public int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);",
        "public Task<T> AsTask<T>(T value) => Task.FromResult(value);",
        "public byte[] ToBytes(string s) => Encoding.UTF8.GetBytes(s);",
    ];

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < 1e-10f ? 0f : dot / denom;
    }
}
