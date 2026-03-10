using SparseLattice.Lattice;
using SparseLattice.Math;
/////////////////////////////////////////////////
namespace SparseLattice.Test.Lattice;

[TestClass]
public sealed class SparsityReportTests
{
    [TestMethod]
    public void Unit_SparsityReport_EmptyLattice_ZeroEverything()
    {
        EmbeddingLattice<int> lattice = new([], new LatticeOptions());
        SparsityReport report = lattice.CollectSparsityReport();

        Assert.AreEqual(0, report.TotalOccupants);
        Assert.AreEqual(0, report.MinNnz);
        Assert.AreEqual(0, report.MaxNnz);
        Assert.AreEqual(0.0, report.MeanNnz);
        Assert.AreEqual(0, report.TotalDimensions);
        Assert.AreEqual(0, report.RealizedDimensions);
        Assert.AreEqual(0, report.TotalBranchNodes);
    }

    [TestMethod]
    public void Unit_SparsityReport_SingleOccupant_CorrectNnz()
    {
        SparseVector vec = new([new SparseEntry(0, 100L), new SparseEntry(2, 200L)], 5);
        SparseOccupant<string>[] items = [new(vec, "a")];
        EmbeddingLattice<string> lattice = new(items, new LatticeOptions());

        SparsityReport report = lattice.CollectSparsityReport();

        Assert.AreEqual(1, report.TotalOccupants);
        Assert.AreEqual(2, report.MinNnz);
        Assert.AreEqual(2, report.MaxNnz);
        Assert.AreEqual(2.0, report.MeanNnz);
        Assert.AreEqual(5, report.TotalDimensions);
        Assert.AreEqual(2, report.RealizedDimensions, "dims 0 and 2 are used.");
    }

    [TestMethod]
    public void Unit_SparsityReport_DimensionCoverage_Correct()
    {
        // 10 dims total, only dims 0 and 1 populated across all items
        SparseOccupant<int>[] items = BuildItems(20, dims: 10, nnzPerItem: 2);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });

        SparsityReport report = lattice.CollectSparsityReport();

        Assert.AreEqual(10, report.TotalDimensions);
        Assert.IsTrue(report.RealizedDimensions >= 1 && report.RealizedDimensions <= 10);
        Assert.IsTrue(report.DimensionCoverage > 0.0 && report.DimensionCoverage <= 1.0);
    }

    [TestMethod]
    public void Unit_SparsityReport_NnzHistogram_CorrectBuckets()
    {
        // nnz = 3 → bucket [1] (1–4)
        SparseVector vec = new([new SparseEntry(0, 1L), new SparseEntry(1, 2L), new SparseEntry(2, 3L)], 10);
        SparseOccupant<int>[] items = [new(vec, 0)];
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions());

        SparsityReport report = lattice.CollectSparsityReport();

        Assert.AreEqual(7, report.NnzHistogram.Length, "Histogram must have 7 buckets.");
        Assert.AreEqual(1, report.NnzHistogram[1],
            "nnz=3 must land in bucket 1 (1–4).");
    }

    [TestMethod]
    public void Unit_SparsityReport_NnzHistogram_ZeroBucket()
    {
        // A vector with zero nonzero entries — must land in bucket 0
        // We build using a SparseVector with no entries, total dims = 5
        SparseVector emptyVec = SparseVector.FromDense(new long[] { 0L, 0L, 0L, 0L, 0L });
        SparseOccupant<int>[] items = [new(emptyVec, 0)];
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions());

        SparsityReport report = lattice.CollectSparsityReport();
        Assert.AreEqual(1, report.NnzHistogram[0],
            "Zero-nnz vector must land in bucket 0.");
    }

    [TestMethod]
    public void Unit_SparsityReport_BranchBalance_WithData()
    {
        SparseOccupant<int>[] items = BuildItems(40, dims: 5, nnzPerItem: 2);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });

        SparsityReport report = lattice.CollectSparsityReport();

        Assert.AreEqual(report.BothChildrenRealized + report.OneChildRealized, report.TotalBranchNodes,
            "TotalBranchNodes must equal BothChildrenRealized + OneChildRealized.");
        Assert.IsTrue(report.BranchBalanceRatio >= 0.0 && report.BranchBalanceRatio <= 1.0,
            $"BranchBalanceRatio out of range: {report.BranchBalanceRatio}");
    }

    [TestMethod]
    public void Unit_SparsityReport_LeafOccupancy_MinMaxCorrect()
    {
        SparseOccupant<int>[] items = BuildItems(30, dims: 5, nnzPerItem: 2);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });

        SparsityReport report = lattice.CollectSparsityReport();

        Assert.IsTrue(report.MinLeafOccupancy >= 1,
            "Min leaf occupancy must be >= 1 for non-empty corpus.");
        Assert.IsTrue(report.MaxLeafOccupancy <= 30,
            "Max leaf occupancy cannot exceed corpus size.");
        Assert.IsTrue(report.MinLeafOccupancy <= report.MaxLeafOccupancy,
            "Min must not exceed max.");
    }

    [TestMethod]
    public void Unit_SparsityReport_TopActiveDimensions_AtMostTen()
    {
        SparseOccupant<int>[] items = BuildItems(50, dims: 20, nnzPerItem: 5);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });

        SparsityReport report = lattice.CollectSparsityReport();

        Assert.IsTrue(report.TopActiveDimensions.Length <= 10,
            "TopActiveDimensions must contain at most 10 entries.");
    }

    [TestMethod]
    public void Unit_SparsityReport_TopActiveDimensions_SortedDescendingByCount()
    {
        SparseOccupant<int>[] items = BuildItems(50, dims: 20, nnzPerItem: 5);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });

        SparsityReport report = lattice.CollectSparsityReport();

        (ushort Dimension, int OccupantCount)[] top = report.TopActiveDimensions;
        for (int i = 1; i < top.Length; i++)
            Assert.IsTrue(top[i].OccupantCount <= top[i - 1].OccupantCount,
                $"TopActiveDimensions not sorted at index {i}.");
    }

    [TestMethod]
    public void Unit_SparsityReport_ToString_ContainsKeyFields()
    {
        SparseOccupant<int>[] items = BuildItems(10, dims: 5, nnzPerItem: 2);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions());

        SparsityReport report = lattice.CollectSparsityReport();
        string text = report.ToString();

        StringAssert.Contains(text, "SparsityReport:");
        StringAssert.Contains(text, "occupants");
        StringAssert.Contains(text, "dims");
    }

    [TestMethod]
    public void Unit_SparsityReport_PreservedAcrossFreeze()
    {
        SparseOccupant<int>[] items = BuildItems(40, dims: 8, nnzPerItem: 3);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });

        SparsityReport before = lattice.CollectSparsityReport();
        lattice.Freeze();
        SparsityReport after = lattice.CollectSparsityReport();

        Assert.AreEqual(before.TotalOccupants, after.TotalOccupants);
        Assert.AreEqual(before.RealizedDimensions, after.RealizedDimensions);
        Assert.AreEqual(before.TotalBranchNodes, after.TotalBranchNodes);
    }

    // --- helpers ---

    private static SparseOccupant<int>[] BuildItems(int count, int dims, int nnzPerItem)
    {
        System.Random rng = new(42);
        SparseOccupant<int>[] items = new SparseOccupant<int>[count];
        for (int i = 0; i < count; i++)
        {
            int actualNnz = System.Math.Min(nnzPerItem, dims);
            System.Collections.Generic.SortedSet<ushort> chosen = [];
            while (chosen.Count < actualNnz)
                chosen.Add((ushort)rng.Next(0, dims));
            SparseEntry[] entries = new SparseEntry[actualNnz];
            int idx = 0;
            foreach (ushort dim in chosen)
                entries[idx++] = new SparseEntry(dim, rng.NextInt64(1L, 100_000L));
            items[i] = new(new SparseVector(entries, dims), i);
        }
        return items;
    }
}
