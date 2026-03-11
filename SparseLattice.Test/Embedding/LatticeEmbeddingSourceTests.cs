using SparseLattice.Embedding;
using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Test.Embedding;

[TestClass]
public sealed class LatticeEmbeddingSourceTests
{
    // -----------------------------------------------------------------------
    // Unit tests — no GGUF file needed, use DirectHashEmbeddingSource for comparison
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_EmbedSparse_ProducesSparseVectorWithCorrectDimensions()
    {
        // Use the existing DirectHashEmbeddingSource as baseline comparison
        DirectHashEmbeddingSource hashSource = new(dimensions: 64);
        SparseVector hashResult = hashSource.EmbedSparse("public int Add(int a, int b) => a + b;");

        Assert.IsTrue(hashResult.NonzeroCount > 0, "Hash embedding should have nonzero entries.");
        Assert.AreEqual(64, hashResult.TotalDimensions);
    }

    [TestMethod]
    public void Unit_EmbedSparse_EmptyText_ReturnsSentinel()
    {
        DirectHashEmbeddingSource source = new(dimensions: 32);
        SparseVector result = source.EmbedSparse("");

        Assert.IsTrue(result.NonzeroCount > 0, "Empty text should return a non-empty sentinel vector.");
        Assert.AreEqual(32, result.TotalDimensions);
    }

    [TestMethod]
    public void Unit_EmbedSparse_SameInputProducesSameOutput()
    {
        DirectHashEmbeddingSource source = new(dimensions: 128);
        string sample = "public bool IsEven(int n) => n % 2 == 0;";

        SparseVector first = source.EmbedSparse(sample);
        SparseVector second = source.EmbedSparse(sample);

        Assert.AreEqual(first, second, "Deterministic source should produce identical vectors for same input.");
    }

    [TestMethod]
    public void Unit_EmbedSparse_DifferentInputsProduceDifferentVectors()
    {
        DirectHashEmbeddingSource source = new(dimensions: 128);

        SparseVector vecA = source.EmbedSparse("public int Add(int a, int b) => a + b;");
        SparseVector vecB = source.EmbedSparse("public string Reverse(string s) => new string(s.Reverse().ToArray());");

        Assert.AreNotEqual(vecA, vecB, "Different inputs should produce different vectors.");
    }

    [TestMethod]
    public void Unit_EmbedSparseBatch_ProducesCorrectCount()
    {
        DirectHashEmbeddingSource source = new(dimensions: 64);
        List<string> texts =
        [
            "public int Add(int a, int b) => a + b;",
            "public bool IsEven(int n) => n % 2 == 0;",
            "public double Sqrt(double x) => Math.Sqrt(x);",
        ];

        SparseVector[] results = source.EmbedSparseBatch(texts);

        Assert.AreEqual(3, results.Length);
        foreach (SparseVector vector in results)
        {
            Assert.IsTrue(vector.NonzeroCount > 0);
            Assert.AreEqual(64, vector.TotalDimensions);
        }
    }

    [TestMethod]
    public void Unit_SparseVectorEntriesAreSortedAscending()
    {
        DirectHashEmbeddingSource source = new(dimensions: 256);
        SparseVector result = source.EmbedSparse("a longer sample with many tokens to ensure multiple dimensions are hit across the vector");

        ReadOnlySpan<SparseEntry> entries = result.Entries;
        for (int i = 1; i < entries.Length; i++)
            Assert.IsTrue(entries[i].Dimension > entries[i - 1].Dimension,
                $"Entry {i} dimension {entries[i].Dimension} must be > entry {i - 1} dimension {entries[i - 1].Dimension}.");
    }

    [TestMethod]
    public void Unit_SparseVectorNoZeroEntries()
    {
        DirectHashEmbeddingSource source = new(dimensions: 64);
        SparseVector result = source.EmbedSparse("public void Log(string msg) => Console.WriteLine(msg);");

        foreach (SparseEntry entry in result.Entries)
            Assert.AreNotEqual(0L, entry.Value, $"Dimension {entry.Dimension} has zero value — violates SparseVector invariant.");
    }

    // -----------------------------------------------------------------------
    // Integration tests — require GGUF file (skipped when not available)
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_LatticeEmbeddingSource_LoadAndEmbed()
    {
        string? ggufPath = ResolveGgufPath();
        if (ggufPath is null)
        {
            Console.WriteLine("[SKIP] GGUF file not found — skipping LatticeEmbeddingSource integration test.");
            return;
        }

        using LatticeEmbeddingSource source = LatticeEmbeddingSource.Load(ggufPath);

        Assert.IsTrue(source.Dimensions > 0, "Dimensions should be positive after load.");
        Assert.IsFalse(string.IsNullOrEmpty(source.ModelName), "ModelName should be set.");

        SparseVector result = source.EmbedSparse("public int Add(int a, int b) => a + b;");
        Assert.IsTrue(result.NonzeroCount > 0, "Embedding should have nonzero entries.");
        Assert.AreEqual(source.Dimensions, result.TotalDimensions);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_LatticeEmbeddingSource_DeterministicOutput()
    {
        string? ggufPath = ResolveGgufPath();
        if (ggufPath is null)
        {
            Console.WriteLine("[SKIP] GGUF file not found.");
            return;
        }

        using LatticeEmbeddingSource source = LatticeEmbeddingSource.Load(ggufPath);
        string sample = "public bool IsEven(int n) => n % 2 == 0;";

        SparseVector first = source.EmbedSparse(sample);
        SparseVector second = source.EmbedSparse(sample);

        Assert.AreEqual(first, second, "Same input must produce identical SparseVector.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_LatticeEmbeddingSource_DifferentInputsDiffer()
    {
        string? ggufPath = ResolveGgufPath();
        if (ggufPath is null)
        {
            Console.WriteLine("[SKIP] GGUF file not found.");
            return;
        }

        using LatticeEmbeddingSource source = LatticeEmbeddingSource.Load(ggufPath);

        SparseVector vecA = source.EmbedSparse("public int Add(int a, int b) => a + b;");
        SparseVector vecB = source.EmbedSparse("public string Reverse(string s) => new string(s.Reverse().ToArray());");

        Assert.AreNotEqual(vecA, vecB, "Semantically different inputs should produce different vectors.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_LatticeEmbeddingSource_FloatInterface()
    {
        string? ggufPath = ResolveGgufPath();
        if (ggufPath is null)
        {
            Console.WriteLine("[SKIP] GGUF file not found.");
            return;
        }

        using LatticeEmbeddingSource source = LatticeEmbeddingSource.Load(ggufPath);
        float[] result = await source.EmbedAsync("public int Add(int a, int b) => a + b;");

        Assert.AreEqual(source.Dimensions, result.Length);
        double norm = System.Math.Sqrt(result.Sum(v => (double)v * v));
        Assert.IsTrue(norm > 0.5, $"L2 norm should be close to 1.0, got {norm:F4}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_LatticeEmbeddingSource_BatchProducesCorrectCount()
    {
        string? ggufPath = ResolveGgufPath();
        if (ggufPath is null)
        {
            Console.WriteLine("[SKIP] GGUF file not found.");
            return;
        }

        using LatticeEmbeddingSource source = LatticeEmbeddingSource.Load(ggufPath);
        List<string> texts =
        [
            "public int Add(int a, int b) => a + b;",
            "public bool IsEven(int n) => n % 2 == 0;",
            "public double Sqrt(double x) => Math.Sqrt(x);",
        ];

        SparseVector[] results = source.EmbedSparseBatch(texts);
        Assert.AreEqual(3, results.Length);
        foreach (SparseVector vector in results)
            Assert.IsTrue(vector.NonzeroCount > 0);
    }

    // -----------------------------------------------------------------------
    // Helper — resolve GGUF path for integration tests
    // -----------------------------------------------------------------------

    private static string? ResolveGgufPath()
    {
        string? path = Environment.GetEnvironmentVariable("EMBEDDING_GGUF_PATH");
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return path;

        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate))
            {
                string[] blobs = Directory.GetFiles(candidate, "sha256-*", SearchOption.TopDirectoryOnly);
                if (blobs.Length > 0)
                    return blobs.OrderByDescending(f => new FileInfo(f).Length).First();
            }
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
