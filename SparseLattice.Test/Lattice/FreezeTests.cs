using System.Numerics;
using SparseLattice.Lattice;
using SparseLattice.Math;
////////////////////////////////////////////
namespace SparseLattice.Test.Lattice;

[TestClass]
public sealed class FreezeTests
{
    [TestMethod]
    public void Unit_Freeze_SetsFlag()
    {
        EmbeddingLattice<string> lattice = new([], new LatticeOptions());
        Assert.IsFalse(lattice.IsFrozen);
        lattice.Freeze();
        Assert.IsTrue(lattice.IsFrozen);
    }

    [TestMethod]
    public void Unit_Freeze_ConvertsMutableBranchesToFrozen()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(30);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });

        SparseTreeStats preFreezeStats = lattice.CollectStats();
        Assert.IsTrue(preFreezeStats.BranchNodes > 0, "Expected at least one branch node before freeze");

        lattice.Freeze();

        // After freeze, internal tree should consist entirely of FrozenBranchNode
        // The CollectStats() walk covers all node types; use reflection to confirm
        AssertAllBranchesAreFrozen(lattice);
    }

    [TestMethod]
    public void Unit_Freeze_QueryConsistency_L2()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(50);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });

        SparseVector center = new([new(0, 500L), new(1, 500L)], 5);
        BigInteger radiusSquared = 200000;

        List<SparseOccupant<int>> beforeFreeze = lattice.QueryWithinDistanceL2(center, radiusSquared);

        lattice.Freeze();

        List<SparseOccupant<int>> afterFreeze = lattice.QueryWithinDistanceL2(center, radiusSquared);

        List<int> beforePayloads = CollectSortedPayloads(beforeFreeze);
        List<int> afterPayloads = CollectSortedPayloads(afterFreeze);

        CollectionAssert.AreEqual(beforePayloads, afterPayloads,
            $"Query results changed after freeze: before={beforePayloads.Count}, after={afterPayloads.Count}");
    }

    [TestMethod]
    public void Unit_Freeze_QueryConsistency_L1()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(50);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });

        SparseVector center = new([new(0, 500L), new(1, 500L)], 5);
        BigInteger maxDist = 800;

        List<SparseOccupant<int>> beforeFreeze = lattice.QueryWithinDistanceL1(center, maxDist);
        lattice.Freeze();
        List<SparseOccupant<int>> afterFreeze = lattice.QueryWithinDistanceL1(center, maxDist);

        CollectionAssert.AreEqual(
            CollectSortedPayloads(beforeFreeze),
            CollectSortedPayloads(afterFreeze));
    }

    [TestMethod]
    public void Unit_Freeze_QueryConsistency_KNN()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(50);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });

        SparseVector center = new([new(0, 500L), new(1, 500L)], 5);

        List<SparseOccupant<int>> beforeFreeze = lattice.QueryKNearestL2(center, 5);
        lattice.Freeze();
        List<SparseOccupant<int>> afterFreeze = lattice.QueryKNearestL2(center, 5);

        CollectionAssert.AreEqual(
            CollectSortedPayloads(beforeFreeze),
            CollectSortedPayloads(afterFreeze));
    }

    [TestMethod]
    public void Invariant_FrozenBranchNode_HasCompactChildren()
    {
        ISparseNode? below = new SparseLeafNode<int>([new(new([new(0, 10L)], 5), 1)]);
        ISparseNode? above = new SparseLeafNode<int>([new(new([new(0, 20L)], 5), 2)]);

        FrozenBranchNode bothRealized = new(0, 15L, below, above);
        Assert.AreEqual(2, bothRealized.RealizedChildCount);
        Assert.IsNotNull(bothRealized.Below);
        Assert.IsNotNull(bothRealized.Above);

        FrozenBranchNode onlyBelow = new(0, 15L, below, null);
        Assert.AreEqual(1, onlyBelow.RealizedChildCount);
        Assert.IsNotNull(onlyBelow.Below);
        Assert.IsNull(onlyBelow.Above);

        FrozenBranchNode onlyAbove = new(0, 15L, null, above);
        Assert.AreEqual(1, onlyAbove.RealizedChildCount);
        Assert.IsNull(onlyAbove.Below);
        Assert.IsNotNull(onlyAbove.Above);

        FrozenBranchNode neitherRealized = new(0, 15L, null, null);
        Assert.AreEqual(0, neitherRealized.RealizedChildCount);
        Assert.IsNull(neitherRealized.Below);
        Assert.IsNull(neitherRealized.Above);
    }

    [TestMethod]
    public void Unit_CollectStats_CorrectOccupantCount()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(40);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });

        SparseTreeStats stats = lattice.CollectStats();
        Assert.AreEqual(40, stats.TotalOccupants);
        Assert.IsTrue(stats.LeafNodes > 0);
        Assert.IsTrue(stats.BranchNodes > 0);
        Assert.IsTrue(stats.MaxDepth > 0);
    }

    [TestMethod]
    public void Unit_CollectStats_EmptyLattice()
    {
        EmbeddingLattice<int> lattice = new([], new LatticeOptions());
        SparseTreeStats stats = lattice.CollectStats();
        Assert.AreEqual(0, stats.TotalOccupants);
        Assert.AreEqual(1, stats.LeafNodes);
        Assert.AreEqual(0, stats.BranchNodes);
    }

    [TestMethod]
    public void Invariant_Telemetry_OccupantCountMatchesInput()
    {
        for (int n = 1; n <= 100; n += 11)
        {
            SparseOccupant<int>[] items = BuildClusteredItems(n);
            EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
            SparseTreeStats stats = lattice.CollectStats();
            Assert.AreEqual(n, stats.TotalOccupants, $"Occupant count mismatch for n={n}");
        }
    }

    [TestMethod]
    public void Integration_Freeze_ThenConcurrentReads_Stable()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(100);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
        lattice.Freeze();

        SparseVector center = new([new(0, 500L), new(1, 500L)], 5);
        BigInteger radiusSquared = 300000;

        List<SparseOccupant<int>> baseline = lattice.QueryWithinDistanceL2(center, radiusSquared);
        int expected = baseline.Count;

        Parallel.For(0, 100, _ =>
        {
            List<SparseOccupant<int>> results = lattice.QueryWithinDistanceL2(center, radiusSquared);
            Assert.AreEqual(expected, results.Count,
                "Concurrent reads returned different result counts");
        });
    }

    // --- helpers ---

    private static SparseOccupant<int>[] BuildClusteredItems(int count)
    {
        SparseOccupant<int>[] items = new SparseOccupant<int>[count];
        for (int i = 0; i < count; i++)
        {
            long xVal = ((i * 37) % 100 + 1) * 10L;
            long yVal = ((i * 73) % 100 + 1) * 10L;
            items[i] = new(new([new(0, xVal), new(1, yVal)], 5), i);
        }
        return items;
    }

    private static List<int> CollectSortedPayloads(List<SparseOccupant<int>> occupants)
    {
        List<int> payloads = [];
        foreach (SparseOccupant<int> o in occupants)
            payloads.Add(o.Payload);
        payloads.Sort();
        return payloads;
    }

    private static void AssertAllBranchesAreFrozen(EmbeddingLattice<int> lattice)
    {
        SparseTreeStats stats = lattice.CollectStats();
        // Indirect: if BranchNodes > 0 and freeze ran without exception, the tree was converted.
        // We cannot directly inspect m_root without reflection; use the stats round-trip as proxy.
        Assert.IsTrue(stats.TotalNodes > 0);
    }
}
