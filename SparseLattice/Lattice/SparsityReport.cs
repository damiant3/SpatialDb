using System.Collections.Concurrent;
using SparseLattice.Math;
/////////////////////////////////////
namespace SparseLattice.Lattice;

public sealed class SparsityReport
{
    public int MinNnz { get; init; }
    public int MaxNnz { get; init; }
    public double MeanNnz { get; init; }
    public double StdDevNnz { get; init; }

    // Buckets: [0]=0, [1]=1–4, [2]=5–8, [3]=9–16, [4]=17–32, [5]=33–64, [6]=65+
    public int[] NnzHistogram { get; init; } = new int[7];

    public int TotalDimensions { get; init; }
    public int RealizedDimensions { get; init; }
    public double DimensionCoverage => TotalDimensions == 0 ? 0.0 : (double)RealizedDimensions / TotalDimensions;
    public (ushort Dimension, int OccupantCount)[] TopActiveDimensions { get; init; } = [];

    public int MinLeafOccupancy { get; init; }
    public int MaxLeafOccupancy { get; init; }
    public double StdDevLeafOccupancy { get; init; }

    // 1.0 = perfectly balanced; closer to 0 = highly skewed splits
    public int BothChildrenRealized { get; init; }
    public int OneChildRealized { get; init; }
    public double BranchBalanceRatio { get; init; }
    public int TotalBranchNodes { get; init; }
    public int TotalOccupants { get; init; }

    public override string ToString()
        => $"SparsityReport: {TotalOccupants} occupants | nnz mean={MeanNnz:F1} [{MinNnz}–{MaxNnz}] | "
         + $"dims {RealizedDimensions}/{TotalDimensions} ({DimensionCoverage:P0}) | "
         + $"balance {BranchBalanceRatio:P0} ({BothChildrenRealized}/{TotalBranchNodes} branches both-realized)";
}

internal sealed class SparsityReportAccumulator
{
    readonly int[] m_nnzBucketUpperBounds = [0, 4, 8, 16, 32, 64, int.MaxValue];
    readonly ConcurrentDictionary<ushort, int> m_dimensionCounts = new();
    readonly System.Collections.Generic.List<int> m_leafOccupancies = [];
    readonly System.Collections.Generic.List<int> m_nnzValues = [];

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

    int NnzBucket(int nnz)
    {
        for (int i = 0; i < m_nnzBucketUpperBounds.Length; i++)
            if (nnz <= m_nnzBucketUpperBounds[i]) return i;
        return m_nnzBucketUpperBounds.Length - 1;
    }
}
