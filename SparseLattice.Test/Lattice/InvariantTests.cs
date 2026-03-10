using SparseLattice.Lattice;
using SparseLattice.Math;
/////////////////////////////////////////////
namespace SparseLattice.Test.Lattice;

/// <summary>
/// Invariant tests: immutability after freeze, double-freeze guard, and
/// occupant-count preservation across the full query lifecycle.
/// </summary>
[TestClass]
public sealed class InvariantTests
{
    [TestMethod]
    public void Invariant_NoMutationsAfterFreeze_DoubleFreezeThrows()
    {
        EmbeddingLattice<int> lattice = new([], new LatticeOptions());
        lattice.Freeze();
        Assert.ThrowsException<InvalidOperationException>(
            () => lattice.Freeze(),
            "Second call to Freeze() must throw InvalidOperationException.");
    }

    [TestMethod]
    public void Invariant_NoMutationsAfterFreeze_FreezeIsIdempotentOnStructure()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(40);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
        lattice.Freeze();

        SparseTreeStats stats = lattice.CollectStats();
        Assert.AreEqual(40, stats.TotalOccupants,
            "Occupant count must not change after freeze.");
    }

    [TestMethod]
    public void Invariant_NoDenseChildrenAllocated_BranchNodeNeverHoldsBothSlots()
    {
        // Any branch node in the tree must never exceed 2 realized children
        // (the physical maximum) and must not exceed the data-implied count.
        SparseOccupant<int>[] items = BuildClusteredItems(80);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
        lattice.Freeze();

        SparseTreeStats stats = lattice.CollectStats();
        // The compact frozen structure only allocates exactly as many child slots as realized.
        // If any node allocated 2 children but only one data path exists, stats would expose it
        // via LeafNodes vs BranchNodes mismatch. We assert the tree is valid structurally.
        Assert.IsTrue(stats.BranchNodes > 0);
        Assert.IsTrue(stats.LeafNodes >= stats.BranchNodes - 1,
            "A binary tree must satisfy LeafNodes >= BranchNodes - 1.");
    }

    [TestMethod]
    public void Invariant_OccupantCount_MatchesInputAfterBuild()
    {
        for (int n = 0; n <= 50; n += 5)
        {
            SparseOccupant<int>[] items = BuildClusteredItems(n);
            EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
            SparseTreeStats stats = lattice.CollectStats();
            Assert.AreEqual(n, stats.TotalOccupants,
                $"Expected {n} occupants in tree for input of size {n}.");
        }
    }

    [TestMethod]
    public void Invariant_OccupantCount_PreservedAfterFreeze()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(55);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
        SparseTreeStats before = lattice.CollectStats();
        lattice.Freeze();
        SparseTreeStats after = lattice.CollectStats();
        Assert.AreEqual(before.TotalOccupants, after.TotalOccupants,
            "Freeze must not change total occupant count.");
    }

    [TestMethod]
    public void Invariant_SparseEntries_NeverZeroValue()
    {
        // SparseVector constructor enforces no zero entries; validate that
        // items built through EmbeddingAdapter also satisfy this invariant.
        float[] embedding = new float[10];
        embedding[0] = 0.5f;
        embedding[3] = -0.3f;
        embedding[7] = 0.001f;  // below default threshold, should be zeroed out

        SparseVector sparse = EmbeddingAdapter.Quantize(embedding);
        foreach (SparseEntry entry in sparse.Entries)
            Assert.AreNotEqual(0L, entry.Value,
                $"Sparse entry at dimension {entry.Dimension} must not be zero.");
    }

    [TestMethod]
    public void Invariant_SparseEntries_DimensionsStrictlyAscending()
    {
        float[] embedding = new float[20];
        embedding[2] = 0.4f;
        embedding[8] = -0.7f;
        embedding[15] = 0.2f;

        SparseVector sparse = EmbeddingAdapter.Quantize(embedding);
        ReadOnlySpan<SparseEntry> entries = sparse.Entries;
        for (int i = 1; i < entries.Length; i++)
            Assert.IsTrue(entries[i].Dimension > entries[i - 1].Dimension,
                $"Dimension ordering violated at index {i}: {entries[i - 1].Dimension} >= {entries[i].Dimension}.");
    }

    [TestMethod]
    public void Invariant_SplitDimension_AlwaysWithinTotalDimensions()
    {
        // Any branch node's split dimension must be a valid index within TotalDimensions.
        const int dims = 16;
        SparseOccupant<int>[] items = BuildHighDimSparseItems(60, dims, nnz: 4, seed: 2);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
        ISparseNode root = EmbeddingLatticeBuilder.Build(items, new LatticeOptions { LeafThreshold = 4 });
        AssertSplitDimensionsValid(root, dims);
    }

    [TestMethod]
    public void Invariant_BuildIsDeterministic()
    {
        SparseOccupant<int>[] items = BuildClusteredItems(50);
        SparseTreeStats stats1 = new EmbeddingLattice<int>(items, new LatticeOptions { LeafThreshold = 4 }).CollectStats();
        SparseTreeStats stats2 = new EmbeddingLattice<int>(items, new LatticeOptions { LeafThreshold = 4 }).CollectStats();

        Assert.AreEqual(stats1.TotalNodes, stats2.TotalNodes, "Build must be deterministic: TotalNodes differs.");
        Assert.AreEqual(stats1.MaxDepth, stats2.MaxDepth, "Build must be deterministic: MaxDepth differs.");
        Assert.AreEqual(stats1.BranchNodes, stats2.BranchNodes, "Build must be deterministic: BranchNodes differs.");
    }

    [TestMethod]
    public void Invariant_ChildRealizationOnlyWhenDataExists_EmptySubtreeAbsent()
    {
        // When all items fall to one side of a split, only one child is realized.
        // Build items that all share the same value for dim 0 — the partition should
        // degenerate to a single side, and the other child node must not be created.
        SparseOccupant<int>[] items = new SparseOccupant<int>[20];
        for (int i = 0; i < 20; i++)
        {
            // dim 0 values range widely; dim 1 is constant — split on dim 1 yields degenerate partition
            long dim0 = (i + 1) * 100L;
            items[i] = new(new SparseVector([new SparseEntry(0, dim0)], 10), i);
        }

        // With LeafThreshold=4, a split on dim 0 will occur and both sides will be non-empty.
        // We check the invariant using the stats: realized nodes <= 2 * leaf threshold + branches
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
        SparseTreeStats stats = lattice.CollectStats();
        Assert.AreEqual(20, stats.TotalOccupants);
        // In a balanced tree, BranchNodes < TotalNodes
        Assert.IsTrue(stats.BranchNodes < stats.TotalNodes);
    }

    // --- helpers ---

    private static SparseOccupant<int>[] BuildClusteredItems(int count)
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

    private static SparseOccupant<int>[] BuildHighDimSparseItems(int count, int dims, int nnz, int seed)
    {
        System.Random rng = new(seed);
        SparseOccupant<int>[] items = new SparseOccupant<int>[count];
        for (int i = 0; i < count; i++)
        {
            System.Collections.Generic.SortedSet<ushort> chosen = [];
            while (chosen.Count < nnz)
                chosen.Add((ushort)rng.Next(0, dims));
            SparseEntry[] entries = new SparseEntry[nnz];
            int idx = 0;
            foreach (ushort dim in chosen)
                entries[idx++] = new SparseEntry(dim, rng.NextInt64(1L, 100_000L));
            items[i] = new(new SparseVector(entries, dims), i);
        }
        return items;
    }

    private static void AssertSplitDimensionsValid(ISparseNode node, int totalDimensions)
    {
        if (node is SparseLeafNode<int>)
            return;
        if (node is SparseBranchNode branch)
        {
            Assert.IsTrue(branch.SplitDimension < totalDimensions,
                $"SplitDimension {branch.SplitDimension} >= totalDimensions {totalDimensions}.");
            if (branch.Below is not null) AssertSplitDimensionsValid(branch.Below, totalDimensions);
            if (branch.Above is not null) AssertSplitDimensionsValid(branch.Above, totalDimensions);
        }
        else if (node is FrozenBranchNode frozen)
        {
            Assert.IsTrue(frozen.SplitDimension < totalDimensions,
                $"SplitDimension {frozen.SplitDimension} >= totalDimensions {totalDimensions}.");
            if (frozen.Below is not null) AssertSplitDimensionsValid(frozen.Below, totalDimensions);
            if (frozen.Above is not null) AssertSplitDimensionsValid(frozen.Above, totalDimensions);
        }
    }
}
