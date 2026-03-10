using System.Collections.Concurrent;
using SparseLattice.Math;
/////////////////////////////////////
namespace SparseLattice.Lattice;

/// <summary>
/// Full diagnostic report on the sparsity and structural balance of an
/// <see cref="EmbeddingLattice{TPayload}"/>. Produced by
/// <see cref="EmbeddingLattice{TPayload}.CollectSparsityReport"/>.
///
/// Use this to answer: "Is the lattice over- or under-allocated relative to my data?"
/// </summary>
public sealed class SparsityReport
{
    // --- per-occupant nnz distribution ---

    /// <summary>Minimum nonzero-count across all occupants.</summary>
    public int MinNnz { get; init; }

    /// <summary>Maximum nonzero-count across all occupants.</summary>
    public int MaxNnz { get; init; }

    /// <summary>Mean nonzero-count across all occupants.</summary>
    public double MeanNnz { get; init; }

    /// <summary>Population standard deviation of nonzero-count across all occupants.</summary>
    public double StdDevNnz { get; init; }

    /// <summary>
    /// Histogram of nnz counts across all occupants.
    /// Buckets: [0] = nnz==0, [1] = 1–4, [2] = 5–8, [3] = 9–16,
    ///          [4] = 17–32, [5] = 33–64, [6] = 65+
    /// </summary>
    public int[] NnzHistogram { get; init; } = new int[7];

    // --- dimension coverage ---

    /// <summary>Total declared dimensions (from the first occupant; 0 if corpus is empty).</summary>
    public int TotalDimensions { get; init; }

    /// <summary>Count of distinct dimensions that appear in at least one occupant.</summary>
    public int RealizedDimensions { get; init; }

    /// <summary>RealizedDimensions / TotalDimensions. Range [0, 1].</summary>
    public double DimensionCoverage => TotalDimensions == 0 ? 0.0 : (double)RealizedDimensions / TotalDimensions;

    /// <summary>
    /// Up to 10 most-populated dimensions, ordered by occupant count descending.
    /// Each entry is (dimension index, occupant count for that dimension).
    /// </summary>
    public (ushort Dimension, int OccupantCount)[] TopActiveDimensions { get; init; } = [];

    // --- per-leaf occupancy ---

    /// <summary>Minimum occupant count across all leaf nodes.</summary>
    public int MinLeafOccupancy { get; init; }

    /// <summary>Maximum occupant count across all leaf nodes.</summary>
    public int MaxLeafOccupancy { get; init; }

    /// <summary>Population standard deviation of occupant count across all leaf nodes.</summary>
    public double StdDevLeafOccupancy { get; init; }

    // --- branch balance ---

    /// <summary>Branch nodes that have both a Below and an Above child realized.</summary>
    public int BothChildrenRealized { get; init; }

    /// <summary>Branch nodes that have exactly one child realized (the other side is empty).</summary>
    public int OneChildRealized { get; init; }

    /// <summary>
    /// BothChildrenRealized / TotalBranchNodes. Range [0, 1].
    /// 1.0 = perfectly balanced binary tree; values closer to 0 indicate highly skewed splits.
    /// </summary>
    public double BranchBalanceRatio { get; init; }

    /// <summary>Total number of branch nodes in the tree.</summary>
    public int TotalBranchNodes { get; init; }

    /// <summary>Total number of occupants in the tree.</summary>
    public int TotalOccupants { get; init; }

    public override string ToString()
        => $"SparsityReport: {TotalOccupants} occupants | nnz mean={MeanNnz:F1} [{MinNnz}–{MaxNnz}] | "
         + $"dims {RealizedDimensions}/{TotalDimensions} ({DimensionCoverage:P0}) | "
         + $"balance {BranchBalanceRatio:P0} ({BothChildrenRealized}/{TotalBranchNodes} branches both-realized)";
}

/// <summary>
/// Accumulates raw counts during a single tree walk to build a <see cref="SparsityReport"/>.
/// </summary>
internal sealed class SparsityReportAccumulator
{
    private readonly int[] m_nnzBucketUpperBounds = [0, 4, 8, 16, 32, 64, int.MaxValue];

    private readonly ConcurrentDictionary<ushort, int> m_dimensionCounts = new();
    private readonly System.Collections.Generic.List<int> m_leafOccupancies = [];
    private readonly System.Collections.Generic.List<int> m_nnzValues = [];

    public int BothChildrenRealized { get; private set; }
    public int OneChildRealized { get; private set; }
    public int TotalBranchNodes { get; private set; }
    public int TotalDimensions { get; private set; }

    public void RecordLeaf<TPayload>(SparseLeafNode<TPayload> leaf)
    {
        m_leafOccupancies.Add(leaf.Count);
        foreach (SparseOccupant<TPayload> occupant in leaf.Occupants)
        {
            m_nnzValues.Add(occupant.Position.NonzeroCount);
            if (TotalDimensions == 0)
                TotalDimensions = occupant.Position.TotalDimensions;
            foreach (SparseEntry entry in occupant.Position.Entries)
                m_dimensionCounts.AddOrUpdate(entry.Dimension, 1, (_, c) => c + 1);
        }
    }

    public void RecordBranch(int realizedChildCount)
    {
        TotalBranchNodes++;
        if (realizedChildCount == 2) BothChildrenRealized++;
        else OneChildRealized++;
    }

    public SparsityReport Build()
    {
        int totalOccupants = m_nnzValues.Count;

        int minNnz = totalOccupants > 0 ? int.MaxValue : 0;
        int maxNnz = 0;
        double sumNnz = 0;

        int[] histogram = new int[7];
        foreach (int nnz in m_nnzValues)
        {
            if (nnz < minNnz) minNnz = nnz;
            if (nnz > maxNnz) maxNnz = nnz;
            sumNnz += nnz;
            histogram[NnzBucket(nnz)]++;
        }

        double meanNnz = totalOccupants > 0 ? sumNnz / totalOccupants : 0;
        double varNnz = 0;
        foreach (int nnz in m_nnzValues)
        {
            double d = nnz - meanNnz;
            varNnz += d * d;
        }
        double stdDevNnz = totalOccupants > 1 ? System.Math.Sqrt(varNnz / totalOccupants) : 0;

        int minLeaf = m_leafOccupancies.Count > 0 ? int.MaxValue : 0;
        int maxLeaf = 0;
        double sumLeaf = 0;
        foreach (int occ in m_leafOccupancies)
        {
            if (occ < minLeaf) minLeaf = occ;
            if (occ > maxLeaf) maxLeaf = occ;
            sumLeaf += occ;
        }
        double meanLeaf = m_leafOccupancies.Count > 0 ? sumLeaf / m_leafOccupancies.Count : 0;
        double varLeaf = 0;
        foreach (int occ in m_leafOccupancies)
        {
            double d = occ - meanLeaf;
            varLeaf += d * d;
        }
        double stdDevLeaf = m_leafOccupancies.Count > 1
            ? System.Math.Sqrt(varLeaf / m_leafOccupancies.Count)
            : 0;

        // Top-10 most-populated dimensions by occupant count.
        System.Collections.Generic.List<(ushort Dimension, int OccupantCount)> topDims = [];
        foreach (System.Collections.Generic.KeyValuePair<ushort, int> kvp in m_dimensionCounts)
            topDims.Add((kvp.Key, kvp.Value));
        topDims.Sort((a, b) => b.OccupantCount.CompareTo(a.OccupantCount));
        int topCount = System.Math.Min(10, topDims.Count);
        (ushort, int)[] topActiveDimensions = new (ushort, int)[topCount];
        for (int i = 0; i < topCount; i++)
            topActiveDimensions[i] = topDims[i];

        double branchBalanceRatio = TotalBranchNodes > 0
            ? (double)BothChildrenRealized / TotalBranchNodes
            : 1.0;

        return new SparsityReport
        {
            TotalOccupants = totalOccupants,
            MinNnz = totalOccupants > 0 ? minNnz : 0,
            MaxNnz = maxNnz,
            MeanNnz = meanNnz,
            StdDevNnz = stdDevNnz,
            NnzHistogram = histogram,
            TotalDimensions = TotalDimensions,
            RealizedDimensions = m_dimensionCounts.Count,
            TopActiveDimensions = topActiveDimensions,
            MinLeafOccupancy = m_leafOccupancies.Count > 0 ? minLeaf : 0,
            MaxLeafOccupancy = maxLeaf,
            StdDevLeafOccupancy = stdDevLeaf,
            BothChildrenRealized = BothChildrenRealized,
            OneChildRealized = OneChildRealized,
            TotalBranchNodes = TotalBranchNodes,
            BranchBalanceRatio = branchBalanceRatio,
        };
    }

    private int NnzBucket(int nnz)
    {
        for (int i = 0; i < m_nnzBucketUpperBounds.Length; i++)
            if (nnz <= m_nnzBucketUpperBounds[i]) return i;
        return m_nnzBucketUpperBounds.Length - 1;
    }
}
