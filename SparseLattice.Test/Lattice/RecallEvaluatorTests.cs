using System.Numerics;
using SparseLattice.Lattice;
using SparseLattice.Math;
////////////////////////////////////////////////
namespace SparseLattice.Test.Lattice;

/// <summary>
/// Tests for <see cref="RecallEvaluator"/>: brute-force helpers, recall@K computation,
/// and aggregate recall over multiple queries.
/// </summary>
[TestClass]
public sealed class RecallEvaluatorTests
{
    [TestMethod]
    public void Unit_BruteForceKNearestL2_ReturnsCorrectOrder()
    {
        SparseOccupant<int>[] corpus =
        [
            new(new([new(0, 100L)], 5), 0),  // dist^2 from 500 = 160000
            new(new([new(0, 300L)], 5), 1),  // dist^2 from 500 = 40000
            new(new([new(0, 500L)], 5), 2),  // dist^2 from 500 = 0
            new(new([new(0, 700L)], 5), 3),  // dist^2 from 500 = 40000
            new(new([new(0, 900L)], 5), 4),  // dist^2 from 500 = 160000
        ];
        SparseVector query = new([new(0, 500L)], 5);

        List<SparseOccupant<int>> nearest = RecallEvaluator.BruteForceKNearestL2(query, corpus, 3);

        Assert.AreEqual(3, nearest.Count);
        Assert.AreEqual(2, nearest[0].Payload, "Nearest should be the exact match at 500.");
    }

    [TestMethod]
    public void Unit_BruteForceKNearestL1_ReturnsCorrectOrder()
    {
        SparseOccupant<int>[] corpus =
        [
            new(new([new(0, 100L), new(1, 100L)], 5), 0),  // L1 from (500,500) = 800
            new(new([new(0, 400L), new(1, 400L)], 5), 1),  // L1 from (500,500) = 200
            new(new([new(0, 500L), new(1, 500L)], 5), 2),  // L1 from (500,500) = 0
        ];
        SparseVector query = new([new(0, 500L), new(1, 500L)], 5);

        List<SparseOccupant<int>> nearest = RecallEvaluator.BruteForceKNearestL1(query, corpus, 2);

        Assert.AreEqual(2, nearest.Count);
        Assert.AreEqual(2, nearest[0].Payload, "Nearest L1 should be exact match at (500,500).");
        Assert.AreEqual(1, nearest[1].Payload);
    }

    [TestMethod]
    public void Unit_EvaluateQuery_PerfectRecall()
    {
        SparseOccupant<int>[] groundTruth =
        [
            new(new([new(0, 10L)], 5), 0),
            new(new([new(0, 20L)], 5), 1),
            new(new([new(0, 30L)], 5), 2),
        ];
        // Candidates = same set
        RecallResult result = RecallEvaluator.EvaluateQuery<int>(groundTruth, groundTruth, k: 3);
        Assert.AreEqual(1.0, result.RecallAtK);
        Assert.AreEqual(3, result.TruePositives);
    }

    [TestMethod]
    public void Unit_EvaluateQuery_ZeroRecall()
    {
        SparseOccupant<int>[] groundTruth =
        [
            new(new([new(0, 10L)], 5), 0),
            new(new([new(0, 20L)], 5), 1),
        ];
        SparseOccupant<int>[] candidates =
        [
            new(new([new(0, 99L)], 5), 99),
            new(new([new(0, 88L)], 5), 88),
        ];
        RecallResult result = RecallEvaluator.EvaluateQuery<int>(groundTruth, candidates, k: 2);
        Assert.AreEqual(0, result.TruePositives);
        Assert.AreEqual(0.0, result.RecallAtK);
    }

    [TestMethod]
    public void Unit_EvaluateQuery_PartialRecall()
    {
        SparseOccupant<int>[] groundTruth =
        [
            new(new([new(0, 10L)], 5), 0),
            new(new([new(0, 20L)], 5), 1),
            new(new([new(0, 30L)], 5), 2),
            new(new([new(0, 40L)], 5), 3),
        ];
        SparseOccupant<int>[] candidates =
        [
            new(new([new(0, 10L)], 5), 0),  // match
            new(new([new(0, 20L)], 5), 1),  // match
            new(new([new(0, 99L)], 5), 99), // miss
            new(new([new(0, 88L)], 5), 88), // miss
        ];
        RecallResult result = RecallEvaluator.EvaluateQuery<int>(groundTruth, candidates, k: 4);
        Assert.AreEqual(2, result.TruePositives);
        Assert.AreEqual(0.5, result.RecallAtK, 0.001);
    }

    [TestMethod]
    public void Unit_EvaluateQuery_KSmallerThanGroundTruth_UsesTopK()
    {
        SparseOccupant<int>[] groundTruth =
        [
            new(new([new(0, 10L)], 5), 0),
            new(new([new(0, 20L)], 5), 1),
            new(new([new(0, 30L)], 5), 2),
            new(new([new(0, 40L)], 5), 3),
        ];
        // Only first 2 of groundTruth matter for k=2
        SparseOccupant<int>[] candidates =
        [
            new(new([new(0, 10L)], 5), 0),
            new(new([new(0, 20L)], 5), 1),
        ];
        RecallResult result = RecallEvaluator.EvaluateQuery<int>(groundTruth, candidates, k: 2);
        Assert.AreEqual(1.0, result.RecallAtK);
    }

    [TestMethod]
    public void Unit_AggregateL2_EmptyQueries_ReturnsZeroStats()
    {
        SparseOccupant<int>[] corpus = [];
        AggregateRecallStats stats = RecallEvaluator.AggregateL2<int>(
            [],
            corpus,
            _ => [],
            k: 5);
        Assert.AreEqual(0, stats.QueryCount);
        Assert.AreEqual(0.0, stats.MeanRecallAtK);
    }

    [TestMethod]
    public void Unit_AggregateL2_SingleQueryPerfect_MeanIsOne()
    {
        SparseOccupant<int>[] corpus = BuildItems(10);
        SparseVector query = new([new(0, 100L)], 5);
        List<SparseOccupant<int>> brute = RecallEvaluator.BruteForceKNearestL2(query, corpus, 3);

        AggregateRecallStats stats = RecallEvaluator.AggregateL2<int>(
            [query],
            corpus,
            _ => brute,
            k: 3);

        Assert.AreEqual(1.0, stats.MeanRecallAtK);
        Assert.AreEqual(1.0, stats.MinRecallAtK);
        Assert.AreEqual(1.0, stats.MaxRecallAtK);
        Assert.AreEqual(1, stats.QueryCount);
    }

    [TestMethod]
    public void Unit_RecallResult_ToString_ContainsKeyMetrics()
    {
        AggregateRecallStats stats = new()
        {
            K = 5,
            QueryCount = 10,
            MeanRecallAtK = 0.80,
            MinRecallAtK = 0.60,
            MaxRecallAtK = 1.00,
        };
        string text = stats.ToString();
        StringAssert.Contains(text, "Recall@5");
        StringAssert.Contains(text, "10 queries");
    }

    [TestMethod]
    public void Integration_EvaluateL2_MatchesManualGroundTruth()
    {
        SparseOccupant<int>[] corpus = BuildItems(20);
        SparseVector query = new([new(0, 300L), new(1, 400L)], 5);

        // Build ground truth manually
        List<(int payload, BigInteger dist)> pairs = [];
        foreach (SparseOccupant<int> item in corpus)
            pairs.Add((item.Payload, query.DistanceSquaredL2(item.Position)));
        pairs.Sort((a, b) => a.dist.CompareTo(b.dist));

        List<SparseOccupant<int>> manualTop5 = [];
        for (int i = 0; i < 5; i++)
            manualTop5.Add(corpus[pairs[i].payload]);

        RecallResult result = RecallEvaluator.EvaluateL2(query, corpus, manualTop5, k: 5);
        Assert.AreEqual(1.0, result.RecallAtK,
            "Manual top-5 must achieve perfect recall against itself.");
    }

    // --- helpers ---

    private static SparseOccupant<int>[] BuildItems(int count)
    {
        SparseOccupant<int>[] items = new SparseOccupant<int>[count];
        for (int i = 0; i < count; i++)
        {
            long xVal = ((i * 37) % 100 + 1) * 10L;
            long yVal = ((i * 73) % 100 + 1) * 10L;
            items[i] = new(new SparseVector([new SparseEntry(0, xVal), new SparseEntry(1, yVal)], 5), i);
        }
        return items;
    }
}
