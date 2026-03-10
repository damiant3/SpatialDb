using SparseLattice.Embedding;
using SparseLattice.Lattice;
using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Test.Embedding;

[TestClass]
public sealed class LatticeIndexBuilderTests
{
    [TestMethod]
    public async Task Unit_BuildAsync_EmptyCorpus_ReturnsFrozenEmptyLattice()
    {
        LatticeIndexBuilder<string> builder = new(new FixedDimSource(4));
        EmbeddingLattice<string> lattice = await builder.BuildAsync([]);

        Assert.IsTrue(lattice.IsFrozen);
        SparseTreeStats stats = lattice.CollectStats();
        Assert.AreEqual(0, stats.TotalOccupants);
    }

    [TestMethod]
    public async Task Unit_BuildAsync_SingleItem_ReturnsFrozenSingleOccupant()
    {
        LatticeIndexBuilder<string> builder = new(new FixedDimSource(4));
        EmbeddingLattice<string> lattice = await builder.BuildAsync(
            [("hello", "payload-a")]);

        Assert.IsTrue(lattice.IsFrozen);
        Assert.AreEqual(1, lattice.CollectStats().TotalOccupants);
    }

    [TestMethod]
    public async Task Unit_BuildAsync_CorpusOccupantCountPreserved()
    {
        LatticeIndexBuilder<int> builder = new(new FixedDimSource(8));
        List<(string text, int payload)> corpus = [];
        for (int i = 0; i < 20; i++)
            corpus.Add(($"item {i}", i));

        EmbeddingLattice<int> lattice = await builder.BuildAsync(corpus);
        Assert.AreEqual(20, lattice.CollectStats().TotalOccupants);
    }

    [TestMethod]
    public async Task Unit_BuildAsync_LatticeIsFrozenAfterBuild()
    {
        LatticeIndexBuilder<string> builder = new(new FixedDimSource(4));
        EmbeddingLattice<string> lattice = await builder.BuildAsync([("test", "x")]);
        Assert.IsTrue(lattice.IsFrozen, "Lattice returned by BuildAsync must always be frozen.");
    }

    [TestMethod]
    public async Task Unit_BuildAsync_QuantizationOptionsApplied()
    {
        // threshold=0.5 with FixedDimSource that returns [0.1f, 0.6f, 0.3f, 0.8f]
        // → only dims 1 and 3 survive threshold
        QuantizationOptions options = new() { ZeroThreshold = 0.5f };
        LatticeIndexBuilder<string> builder = new(
            new FixedDimSource(4, new[] { 0.1f, 0.6f, 0.3f, 0.8f }),
            options);

        EmbeddingLattice<string> lattice = await builder.BuildAsync([("text", "payload")]);
        SparsityReport report = lattice.CollectSparsityReport();
        Assert.AreEqual(2, report.MeanNnz,
            "Only dims where abs(v) >= 0.5 should be kept.");
    }

    [TestMethod]
    public async Task Integration_BuildAsync_QueryFindsNearestNeighbor()
    {
        // Two items with very different embeddings — each should be its own nearest neighbour.
        float[] nearVec = [0.9f, 0.1f, 0.0f, 0.0f];
        float[] farVec = [0.0f, 0.0f, 0.9f, 0.8f];

        SequentialSource source = new([nearVec, farVec]);
        LatticeIndexBuilder<string> builder = new(source, new QuantizationOptions { ZeroThreshold = 0.0f });

        EmbeddingLattice<string> lattice = await builder.BuildAsync(
            [("near-text", "near"), ("far-text", "far")]);

        // Query with a vector very close to nearVec
        SparseVector queryNear = EmbeddingAdapter.Quantize(nearVec, new QuantizationOptions { ZeroThreshold = 0.0f });
        List<SparseOccupant<string>> results = lattice.QueryKNearestL2(queryNear, 1);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("near", results[0].Payload);
    }

    [TestMethod]
    public async Task Unit_BuildAsync_CancellationToken_Propagates()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        LatticeIndexBuilder<string> builder = new(new SlowSource());
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => builder.BuildAsync([("text", "x")], ct: cts.Token));
    }

    // --- mock sources ---

    /// <summary>Returns a fixed float[] of the given dimension for every input text.</summary>
    private sealed class FixedDimSource(int dims, float[]? vector = null) : IEmbeddingSource
    {
        private readonly float[] m_vector = vector ?? BuildUniform(dims, 0.5f);

        public string ModelName => "fixed-test-source";
        public int Dimensions => m_vector.Length;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
            => Task.FromResult((float[])m_vector.Clone());

        public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            float[][] results = new float[texts.Count][];
            for (int i = 0; i < texts.Count; i++)
                results[i] = await EmbedAsync(texts[i], ct).ConfigureAwait(false);
            return results;
        }

        private static float[] BuildUniform(int dims, float value)
        {
            float[] v = new float[dims];
            for (int i = 0; i < dims; i++) v[i] = value;
            return v;
        }
    }

    /// <summary>Returns vectors from a pre-defined list in order.</summary>
    private sealed class SequentialSource(float[][] vectors) : IEmbeddingSource
    {
        private int m_index;

        public string ModelName => "sequential-test-source";
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

    /// <summary>Source that honours cancellation to test token propagation.</summary>
    private sealed class SlowSource : IEmbeddingSource
    {
        public string ModelName => "slow-test-source";
        public int Dimensions => 4;

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return [];
        }

        public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            float[][] results = new float[texts.Count][];
            for (int i = 0; i < texts.Count; i++)
                results[i] = await EmbedAsync(texts[i], ct).ConfigureAwait(false);
            return results;
        }
    }
}
