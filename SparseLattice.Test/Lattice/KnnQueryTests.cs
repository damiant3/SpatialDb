using System.Numerics;
using SparseLattice.Lattice;
using SparseLattice.Math;
//////////////////////////////////////////////////
namespace SparseLattice.Test.Lattice;

/// <summary>
/// Tests for the priority-queue KNN implementation (L2 and L1),
/// pruning correctness, and recall@K against brute-force ground truth.
/// </summary>
[TestClass]
public sealed class KnnQueryTests
{
    [TestMethod]
    public void Unit_QueryKNearestL2_ReturnsEmptyForKZero()
    {
        EmbeddingLattice<int> lattice = BuildLattice(BuildLinearItems(10, dims: 5), leafThreshold: 2);
        lattice.Freeze();
        SparseVector center = new([new(0, 500L)], 5);
        List<SparseOccupant<int>> results = lattice.QueryKNearestL2(center, 0);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Unit_QueryKNearestL1_ReturnsEmptyForKZero()
    {
        EmbeddingLattice<int> lattice = BuildLattice(BuildLinearItems(10, dims: 5), leafThreshold: 2);
        lattice.Freeze();
        SparseVector center = new([new(0, 500L)], 5);
        List<SparseOccupant<int>> results = lattice.QueryKNearestL1(center, 0);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Unit_QueryKNearestL1_ReturnsCorrectOrder()
    {
        // items at L1 distances 10, 30, 50, 70, 90 from center 500
        SparseOccupant<int>[] items =
        [
            new(new([new(0, 490L)], 5), 0),  // L1=10
            new(new([new(0, 470L)], 5), 1),  // L1=30
            new(new([new(0, 450L)], 5), 2),  // L1=50
            new(new([new(0, 430L)], 5), 3),  // L1=70
            new(new([new(0, 410L)], 5), 4),  // L1=90
        ];
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 2);
        lattice.Freeze();

        SparseVector center = new([new(0, 500L)], 5);
        List<SparseOccupant<int>> results = lattice.QueryKNearestL1(center, 3);

        Assert.AreEqual(3, results.Count);
        Assert.AreEqual(0, results[0].Payload, "Nearest should be payload 0 (L1=10)");
        Assert.AreEqual(1, results[1].Payload, "Second should be payload 1 (L1=30)");
        Assert.AreEqual(2, results[2].Payload, "Third should be payload 2 (L1=50)");
    }

    [TestMethod]
    public void Unit_QueryKNearestL1_MatchesBruteForce()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(60, dims: 8, seed: 42);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 4);
        lattice.Freeze();

        SparseVector center = new([new(0, 500L), new(3, 300L)], 8);

        List<SparseOccupant<int>> latticeResults = lattice.QueryKNearestL1(center, 5);
        List<SparseOccupant<int>> bruteForce = RecallEvaluator.BruteForceKNearestL1(center, items, 5);

        RecallResult recall = RecallEvaluator.EvaluateQuery(bruteForce, latticeResults, 5);
        Assert.AreEqual(1.0, recall.RecallAtK,
            $"KNN L1 recall@5 should be 1.0 but got {recall.RecallAtK:P2}");
    }

    [TestMethod]
    public void Unit_QueryKNearestL2_MatchesBruteForce_SmallDataset()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(60, dims: 8, seed: 7);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 4);
        lattice.Freeze();

        SparseVector center = new([new(0, 400L), new(2, 600L)], 8);

        List<SparseOccupant<int>> latticeResults = lattice.QueryKNearestL2(center, 5);
        List<SparseOccupant<int>> bruteForce = RecallEvaluator.BruteForceKNearestL2(center, items, 5);

        RecallResult recall = RecallEvaluator.EvaluateQuery(bruteForce, latticeResults, 5);
        Assert.AreEqual(1.0, recall.RecallAtK,
            $"KNN L2 recall@5 should be 1.0 but got {recall.RecallAtK:P2}");
    }

    [TestMethod]
    public void Unit_QueryKNearestL2_KLargerThanCorpus_ReturnsAll()
    {
        SparseOccupant<int>[] items = BuildLinearItems(5, dims: 5);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 2);
        lattice.Freeze();

        SparseVector center = new([new(0, 300L)], 5);
        List<SparseOccupant<int>> results = lattice.QueryKNearestL2(center, 100);
        Assert.AreEqual(5, results.Count, "Should return all 5 items when K > corpus size");
    }

    [TestMethod]
    public void Unit_QueryKNearestL1_KLargerThanCorpus_ReturnsAll()
    {
        SparseOccupant<int>[] items = BuildLinearItems(5, dims: 5);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 2);
        lattice.Freeze();

        SparseVector center = new([new(0, 300L)], 5);
        List<SparseOccupant<int>> results = lattice.QueryKNearestL1(center, 100);
        Assert.AreEqual(5, results.Count, "Should return all 5 items when K > corpus size");
    }

    [TestMethod]
    public void Integration_QueryKNearestL2_HighDimensionalSparse_MatchesBruteForce()
    {
        // 200 items in 64-dim space, each with ~8 nonzero dims — realistic embedding sparsity
        SparseOccupant<int>[] items = BuildHighDimSparseItems(200, dims: 64, nnz: 8, seed: 13);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 8);
        lattice.Freeze();

        SparseVector center = BuildSparseQuery(dims: 64, nnz: 8, seed: 99);
        List<SparseOccupant<int>> latticeResults = lattice.QueryKNearestL2(center, 10);
        List<SparseOccupant<int>> bruteForce = RecallEvaluator.BruteForceKNearestL2(center, items, 10);

        RecallResult recall = RecallEvaluator.EvaluateQuery(bruteForce, latticeResults, 10);
        Assert.IsTrue(recall.RecallAtK >= 0.9,
            $"High-dim KNN L2 recall@10 should be >= 0.90 but got {recall.RecallAtK:P2}");
    }

    [TestMethod]
    public void Integration_QueryKNearestL1_HighDimensionalSparse_MatchesBruteForce()
    {
        SparseOccupant<int>[] items = BuildHighDimSparseItems(200, dims: 64, nnz: 8, seed: 17);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 8);
        lattice.Freeze();

        SparseVector center = BuildSparseQuery(dims: 64, nnz: 8, seed: 31);
        List<SparseOccupant<int>> latticeResults = lattice.QueryKNearestL1(center, 10);
        List<SparseOccupant<int>> bruteForce = RecallEvaluator.BruteForceKNearestL1(center, items, 10);

        RecallResult recall = RecallEvaluator.EvaluateQuery(bruteForce, latticeResults, 10);
        Assert.IsTrue(recall.RecallAtK >= 0.9,
            $"High-dim KNN L1 recall@10 should be >= 0.90 but got {recall.RecallAtK:P2}");
    }

    [TestMethod]
    public void Integration_QueryL2_RadiusSearch_HighDimensionalSparse_MatchesBruteForce()
    {
        SparseOccupant<int>[] items = BuildHighDimSparseItems(200, dims: 64, nnz: 8, seed: 55);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 8);
        lattice.Freeze();

        SparseVector center = BuildSparseQuery(dims: 64, nnz: 8, seed: 77);

        // Radius chosen to capture roughly the nearest 20 items
        List<SparseOccupant<int>> bruteAll = RecallEvaluator.BruteForceKNearestL2(center, items, 20);
        BigInteger radius = center.DistanceSquaredL2(bruteAll[19].Position);

        List<SparseOccupant<int>> latticeResults = lattice.QueryWithinDistanceL2(center, radius);
        List<int> brutePayloads = PayloadsSorted(bruteAll);

        // All 20 nearest must appear in radius results
        int truePositives = 0;
        System.Collections.Generic.HashSet<int> resultSet = [];
        foreach (SparseOccupant<int> r in latticeResults)
            resultSet.Add(r.Payload);
        foreach (int p in brutePayloads)
            if (resultSet.Contains(p)) truePositives++;

        Assert.AreEqual(20, truePositives,
            $"Radius L2 search missed {20 - truePositives} of the 20 nearest items.");
    }

    [TestMethod]
    public void Integration_RecallEvaluator_AggregateL2_PerfectRecallOnSmallDataset()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(50, dims: 8, seed: 1);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 4);
        lattice.Freeze();

        List<SparseVector> queries =
        [
            new([new(0, 200L), new(1, 300L)], 8),
            new([new(0, 700L), new(2, 500L)], 8),
            new([new(3, 100L)], 8),
        ];

        AggregateRecallStats stats = RecallEvaluator.AggregateL2(
            queries,
            items,
            q => lattice.QueryKNearestL2(q, 5),
            k: 5);

        Assert.AreEqual(3, stats.QueryCount);
        Assert.AreEqual(1.0, stats.MeanRecallAtK,
            $"Expected perfect recall on small clustered dataset, got {stats.MeanRecallAtK:P2}");
    }

    [TestMethod]
    public void Unit_QueryKNearestL2_ResultsSortedAscendingByDistance()
    {
        SparseOccupant<int>[] items = BuildLinearItems(20, dims: 5);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 4);
        lattice.Freeze();

        SparseVector center = new([new(0, 500L)], 5);
        List<SparseOccupant<int>> results = lattice.QueryKNearestL2(center, 10);

        for (int i = 1; i < results.Count; i++)
        {
            BigInteger d0 = center.DistanceSquaredL2(results[i - 1].Position);
            BigInteger d1 = center.DistanceSquaredL2(results[i].Position);
            Assert.IsTrue(d0 <= d1,
                $"Results not sorted at index {i}: d[{i-1}]={d0} > d[{i}]={d1}");
        }
    }

    [TestMethod]
    public void Unit_QueryKNearestL1_ResultsSortedAscendingByDistance()
    {
        SparseOccupant<int>[] items = BuildLinearItems(20, dims: 5);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 4);
        lattice.Freeze();

        SparseVector center = new([new(0, 500L)], 5);
        List<SparseOccupant<int>> results = lattice.QueryKNearestL1(center, 10);

        for (int i = 1; i < results.Count; i++)
        {
            BigInteger d0 = center.DistanceL1(results[i - 1].Position);
            BigInteger d1 = center.DistanceL1(results[i].Position);
            Assert.IsTrue(d0 <= d1,
                $"Results not sorted at index {i}: d[{i-1}]={d0} > d[{i}]={d1}");
        }
    }

    [TestMethod]
    public void Integration_KnnL2_BeforeAndAfterFreeze_SameResults()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(40, dims: 6, seed: 3);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 4);

        SparseVector center = new([new(0, 500L), new(1, 300L)], 6);

        List<SparseOccupant<int>> before = lattice.QueryKNearestL2(center, 5);
        lattice.Freeze();
        List<SparseOccupant<int>> after = lattice.QueryKNearestL2(center, 5);

        CollectionAssert.AreEqual(PayloadsSorted(before), PayloadsSorted(after),
            "KNN L2 results changed across freeze boundary.");
    }

    [TestMethod]
    public void Integration_KnnL1_BeforeAndAfterFreeze_SameResults()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(40, dims: 6, seed: 5);
        EmbeddingLattice<int> lattice = BuildLattice(items, leafThreshold: 4);

        SparseVector center = new([new(0, 400L), new(2, 700L)], 6);

        List<SparseOccupant<int>> before = lattice.QueryKNearestL1(center, 5);
        lattice.Freeze();
        List<SparseOccupant<int>> after = lattice.QueryKNearestL1(center, 5);

        CollectionAssert.AreEqual(PayloadsSorted(before), PayloadsSorted(after),
            "KNN L1 results changed across freeze boundary.");
    }

    // --- helpers ---

    private static EmbeddingLattice<int> BuildLattice(SparseOccupant<int>[] items, int leafThreshold)
        => new(items, new LatticeOptions { LeafThreshold = leafThreshold });

    private static SparseOccupant<int>[] BuildLinearItems(int count, int dims)
    {
        SparseOccupant<int>[] items = new SparseOccupant<int>[count];
        for (int i = 0; i < count; i++)
        {
            long value = (i + 1) * 100L;
            items[i] = new(new([new(0, value)], dims), i);
        }
        return items;
    }

    private static SparseOccupant<int>[] BuildClusteredItems(int count, int dims, int seed)
    {
        System.Random rng = new(seed);
        SparseOccupant<int>[] items = new SparseOccupant<int>[count];
        for (int i = 0; i < count; i++)
        {
            int nnz = System.Math.Min(2, dims);
            SparseEntry[] entries = new SparseEntry[nnz];
            for (int e = 0; e < nnz; e++)
                entries[e] = new SparseEntry((ushort)e, rng.NextInt64(100L, 1000L));
            items[i] = new(new SparseVector(entries, dims), i);
        }
        return items;
    }

    private static SparseOccupant<int>[] BuildHighDimSparseItems(int count, int dims, int nnz, int seed)
    {
        System.Random rng = new(seed);
        SparseOccupant<int>[] items = new SparseOccupant<int>[count];
        for (int i = 0; i < count; i++)
        {
            SparseEntry[] entries = BuildUniqueEntries(rng, dims, nnz);
            items[i] = new(new SparseVector(entries, dims), i);
        }
        return items;
    }

    private static SparseVector BuildSparseQuery(int dims, int nnz, int seed)
    {
        System.Random rng = new(seed);
        SparseEntry[] entries = BuildUniqueEntries(rng, dims, nnz);
        return new SparseVector(entries, dims);
    }

    private static SparseEntry[] BuildUniqueEntries(System.Random rng, int dims, int nnz)
    {
        int actualNnz = System.Math.Min(nnz, dims);
        System.Collections.Generic.SortedSet<ushort> chosen = [];
        while (chosen.Count < actualNnz)
            chosen.Add((ushort)rng.Next(0, dims));
        SparseEntry[] entries = new SparseEntry[actualNnz];
        int idx = 0;
        foreach (ushort dim in chosen)
            entries[idx++] = new SparseEntry(dim, rng.NextInt64(1L, 1_000_000L));
        return entries;
    }

    private static List<int> PayloadsSorted(List<SparseOccupant<int>> occupants)
    {
        List<int> payloads = new(occupants.Count);
        foreach (SparseOccupant<int> o in occupants)
            payloads.Add(o.Payload);
        payloads.Sort();
        return payloads;
    }
}
