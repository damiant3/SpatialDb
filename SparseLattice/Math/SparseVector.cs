using System.Numerics;
///////////////////////////////
namespace SparseLattice.Math;

public readonly struct SparseEntry(ushort dimension, long value)
{
    public readonly ushort Dimension = dimension;
    public readonly long Value = value;

    public override string ToString() => $"[{Dimension}]={Value}";
}

public readonly struct SparseVector : IEquatable<SparseVector>
{
    readonly SparseEntry[] m_entries;

    public int NonzeroCount => m_entries.Length;
    public int TotalDimensions { get; }
    public ReadOnlySpan<SparseEntry> Entries => m_entries.AsSpan();

    public SparseVector(SparseEntry[] entries, int totalDimensions)
    {
        if (totalDimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalDimensions));
        ValidateEntriesOrThrow(entries, totalDimensions);
        m_entries = entries;
        TotalDimensions = totalDimensions;
    }

    public long ValueAt(ushort dimension)
    {
        foreach (SparseEntry entry in m_entries)
        {
            if (entry.Dimension == dimension) return entry.Value;
            if (entry.Dimension > dimension) break;
        }
        return 0L;
    }

    /// <summary>
    /// Binary-search lookup — O(log nnz) vs the linear O(nnz) of <see cref="ValueAt"/>.
    /// Hot path: called once per branch node during every KNN traversal.
    /// </summary>
    public long ValueAtFast(ushort dimension)
    {
        ReadOnlySpan<SparseEntry> entries = m_entries.AsSpan();
        int lo = 0, hi = entries.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            ushort d = entries[mid].Dimension;
            if (d == dimension) return entries[mid].Value;
            if (d < dimension) lo = mid + 1;
            else hi = mid - 1;
        }
        return 0L;
    }

    public static SparseVector FromDense(long[] dense)
    {
        int nonzeroCount = 0;
        for (int i = 0; i < dense.Length; i++)
            if (dense[i] != 0L)
                nonzeroCount++;

        SparseEntry[] entries = new SparseEntry[nonzeroCount];
        int writeIndex = 0;
        for (int i = 0; i < dense.Length; i++)
            if (dense[i] != 0L)
                entries[writeIndex++] = new SparseEntry((ushort)i, dense[i]);

        return new SparseVector(entries, dense.Length);
    }

    public long[] ToDense()
    {
        long[] dense = new long[TotalDimensions];
        foreach (SparseEntry entry in m_entries)
            dense[entry.Dimension] = entry.Value;
        return dense;
    }

    public LongVectorN ToLongVectorN() => new(ToDense());

    public BigInteger DistanceSquaredL2(SparseVector other)
    {
        BigInteger sum = 0;
        int i = 0;
        int j = 0;
        ReadOnlySpan<SparseEntry> left = m_entries.AsSpan();
        ReadOnlySpan<SparseEntry> right = other.m_entries.AsSpan();

        while (i < left.Length && j < right.Length)
        {
            if (left[i].Dimension == right[j].Dimension)
            {
                long diff = left[i].Value - right[j].Value;
                sum += (BigInteger)diff * diff;
                i++;
                j++;
            }
            else if (left[i].Dimension < right[j].Dimension)
            {
                // right has zero for this dimension
                sum += (BigInteger)left[i].Value * left[i].Value;
                i++;
            }
            else
            {
                // left has zero for this dimension
                sum += (BigInteger)right[j].Value * right[j].Value;
                j++;
            }
        }

        while (i < left.Length)
        {
            sum += (BigInteger)left[i].Value * left[i].Value;
            i++;
        }

        while (j < right.Length)
        {
            sum += (BigInteger)right[j].Value * right[j].Value;
            j++;
        }

        return sum;
    }

    /// <summary>
    /// L2 distance squared using <c>ulong</c> accumulation — no <see cref="BigInteger"/>
    /// allocation. Safe for quantized embeddings where each <c>long</c> component is bounded
    /// by <c>EmbeddingAdapter</c> scale (~±2^30), so each squared diff fits in a <c>long</c>
    /// and the sum over ~20-30 NNZ fits in a <c>ulong</c>.
    /// Returns <c>ulong.MaxValue</c> on overflow (treated as "very far").
    /// </summary>
    public ulong DistanceSquaredL2Fast(SparseVector other)
    {
        ulong sum = 0;
        int i = 0, j = 0;
        ReadOnlySpan<SparseEntry> left  = m_entries.AsSpan();
        ReadOnlySpan<SparseEntry> right = other.m_entries.AsSpan();

        while (i < left.Length && j < right.Length)
        {
            if (left[i].Dimension == right[j].Dimension)
            {
                long diff = left[i].Value - right[j].Value;
                sum += (ulong)(diff * diff);
                i++; j++;
            }
            else if (left[i].Dimension < right[j].Dimension)
            {
                long v = left[i].Value;
                sum += (ulong)(v * v);
                i++;
            }
            else
            {
                long v = right[j].Value;
                sum += (ulong)(v * v);
                j++;
            }
        }
        while (i < left.Length) { long v = left[i].Value;  sum += (ulong)(v * v); i++; }
        while (j < right.Length){ long v = right[j].Value; sum += (ulong)(v * v); j++; }
        return sum;
    }

    public BigInteger DistanceL1(SparseVector other)
    {
        BigInteger sum = 0;
        int i = 0;
        int j = 0;
        ReadOnlySpan<SparseEntry> left = m_entries.AsSpan();
        ReadOnlySpan<SparseEntry> right = other.m_entries.AsSpan();

        while (i < left.Length && j < right.Length)
        {
            if (left[i].Dimension == right[j].Dimension)
            {
                sum += BigInteger.Abs(left[i].Value - right[j].Value);
                i++;
                j++;
            }
            else if (left[i].Dimension < right[j].Dimension)
            {
                sum += BigInteger.Abs(left[i].Value);
                i++;
            }
            else
            {
                sum += BigInteger.Abs(right[j].Value);
                j++;
            }
        }

        while (i < left.Length)
        {
            sum += BigInteger.Abs(left[i].Value);
            i++;
        }

        while (j < right.Length)
        {
            sum += BigInteger.Abs(right[j].Value);
            j++;
        }

        return sum;
    }

    /// <summary>
    /// L1 (Manhattan) distance using <c>ulong</c> — no <see cref="BigInteger"/> allocation.
    /// </summary>
    public ulong DistanceL1Fast(SparseVector other)
    {
        ulong sum = 0;
        int i = 0, j = 0;
        ReadOnlySpan<SparseEntry> left  = m_entries.AsSpan();
        ReadOnlySpan<SparseEntry> right = other.m_entries.AsSpan();

        while (i < left.Length && j < right.Length)
        {
            if (left[i].Dimension == right[j].Dimension)
            {
                long diff = left[i].Value - right[j].Value;
                sum += (ulong)(diff < 0 ? -diff : diff);
                i++; j++;
            }
            else if (left[i].Dimension < right[j].Dimension)
            {
                long v = left[i].Value;
                sum += (ulong)(v < 0 ? -v : v);
                i++;
            }
            else
            {
                long v = right[j].Value;
                sum += (ulong)(v < 0 ? -v : v);
                j++;
            }
        }
        while (i < left.Length) { long v = left[i].Value;  sum += (ulong)(v < 0 ? -v : v); i++; }
        while (j < right.Length){ long v = right[j].Value; sum += (ulong)(v < 0 ? -v : v); j++; }
        return sum;
    }

    public BigInteger DistanceSquaredL2(LongVectorN other)
    {
        BigInteger sum = 0;
        int sparseIdx = 0;
        ReadOnlySpan<SparseEntry> entries = m_entries.AsSpan();

        for (int dim = 0; dim < other.Dimensions; dim++)
        {
            long sparseVal = 0L;
            if (sparseIdx < entries.Length && entries[sparseIdx].Dimension == dim)
            {
                sparseVal = entries[sparseIdx].Value;
                sparseIdx++;
            }
            long diff = sparseVal - other[dim];
            sum += (BigInteger)diff * diff;
        }

        return sum;
    }

    public bool Equals(SparseVector other)
    {
        if (TotalDimensions != other.TotalDimensions) return false;
        if (m_entries.Length != other.m_entries.Length) return false;
        for (int i = 0; i < m_entries.Length; i++)
        {
            if (m_entries[i].Dimension != other.m_entries[i].Dimension) return false;
            if (m_entries[i].Value != other.m_entries[i].Value) return false;
        }
        return true;
    }

    public override bool Equals(object? obj)
        => obj is SparseVector other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(TotalDimensions);
        foreach (SparseEntry entry in m_entries)
        {
            hash.Add(entry.Dimension);
            hash.Add(entry.Value);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(SparseVector a, SparseVector b) => a.Equals(b);
    public static bool operator !=(SparseVector a, SparseVector b) => !a.Equals(b);

    public override string ToString()
        => $"Sparse({NonzeroCount}/{TotalDimensions})";

    static void ValidateEntriesOrThrow(SparseEntry[] entries, int totalDimensions)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Dimension >= totalDimensions)
                throw new ArgumentOutOfRangeException(
                    nameof(entries),
                    $"Entry dimension {entries[i].Dimension} exceeds total dimensions {totalDimensions}.");
            if (entries[i].Value == 0L)
                throw new ArgumentException(
                    $"Sparse entry at index {i} has value zero; zero entries must be omitted.",
                    nameof(entries));
            if (i > 0 && entries[i].Dimension <= entries[i - 1].Dimension)
                throw new ArgumentException(
                    $"Entries must be sorted ascending by dimension with no duplicates. " +
                    $"Dimension {entries[i].Dimension} at index {i} violates ordering.",
                    nameof(entries));
        }
    }
}
