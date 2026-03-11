//////////////////////////////
namespace SparseLattice.Math;

public sealed class QuantizationOptions
{
    public float ZeroThreshold { get; init; } = 0.01f;
    public long GlobalScale { get; init; } = long.MaxValue;

    /// <summary>
    /// When set, retains only the <c>SparsityBudget</c> dimensions with the largest
    /// absolute values after threshold filtering. Useful for controlling nnz density
    /// when the model produces many weak but non-zero activations.
    /// Null means no cap — all dimensions that survive the threshold are kept.
    /// </summary>
    public int? SparsityBudget { get; init; }

    public static QuantizationOptions Default { get; } = new();
}

public static class EmbeddingAdapter
{
    public static SparseVector Quantize(float[] embedding, QuantizationOptions? options = null)
    {
        QuantizationOptions effectiveOptions = options ?? QuantizationOptions.Default;

        // Two-pass: first count, then fill, skipping both float-zero and quantized-zero.
        int nonzeroCount = 0;
        for (int i = 0; i < embedding.Length; i++)
        {
            if (MathF.Abs(embedding[i]) < effectiveOptions.ZeroThreshold) continue;
            if ((long)(embedding[i] * (double)effectiveOptions.GlobalScale) == 0L) continue;
            nonzeroCount++;
        }

        SparseEntry[] entries = new SparseEntry[nonzeroCount];
        int writeIndex = 0;
        for (int i = 0; i < embedding.Length; i++)
        {
            if (MathF.Abs(embedding[i]) < effectiveOptions.ZeroThreshold) continue;
            long quantized = (long)(embedding[i] * (double)effectiveOptions.GlobalScale);
            if (quantized == 0L) continue;
            entries[writeIndex++] = new SparseEntry((ushort)i, quantized);
        }

        if (effectiveOptions.SparsityBudget.HasValue && entries.Length > effectiveOptions.SparsityBudget.Value)
            entries = ApplySparsityBudget(entries, effectiveOptions.SparsityBudget.Value);

        return new SparseVector(entries, embedding.Length);
    }

    public static LongVectorN QuantizeDense(float[] embedding, QuantizationOptions? options = null)
    {
        QuantizationOptions effectiveOptions = options ?? QuantizationOptions.Default;
        long[] components = new long[embedding.Length];
        for (int i = 0; i < embedding.Length; i++)
        {
            if (MathF.Abs(embedding[i]) < effectiveOptions.ZeroThreshold)
                continue;
            components[i] = (long)(embedding[i] * (double)effectiveOptions.GlobalScale);
        }
        return new LongVectorN(components);
    }

    public static float[] Dequantize(LongVectorN vector, QuantizationOptions? options = null)
    {
        QuantizationOptions effectiveOptions = options ?? QuantizationOptions.Default;
        float[] result = new float[vector.Dimensions];
        for (int i = 0; i < vector.Dimensions; i++)
            result[i] = (float)(vector[i] / (double)effectiveOptions.GlobalScale);
        return result;
    }

    public static float[] Dequantize(SparseVector vector, QuantizationOptions? options = null)
    {
        QuantizationOptions effectiveOptions = options ?? QuantizationOptions.Default;
        float[] result = new float[vector.TotalDimensions];
        foreach (SparseEntry entry in vector.Entries)
            result[entry.Dimension] = (float)(entry.Value / (double)effectiveOptions.GlobalScale);
        return result;
    }

    /// <summary>
    /// Retains only the <paramref name="budget"/> entries with the largest absolute values,
    /// then re-sorts them ascending by dimension to satisfy <see cref="SparseVector"/> invariants.
    /// </summary>
    private static SparseEntry[] ApplySparsityBudget(SparseEntry[] entries, int budget)
    {
        // Use unchecked abs via unsigned cast to avoid overflow on long.MinValue
        static ulong AbsUnsigned(long v) => v < 0 ? (ulong)(-(v + 1)) + 1UL : (ulong)v;
        System.Array.Sort(entries, (a, b) => AbsUnsigned(b.Value).CompareTo(AbsUnsigned(a.Value)));
        SparseEntry[] trimmed = new SparseEntry[budget];
        System.Array.Copy(entries, trimmed, budget);
        System.Array.Sort(trimmed, (a, b) => a.Dimension.CompareTo(b.Dimension));
        return trimmed;
    }

    /// <summary>
    /// Public entry point for out-of-class callers (e.g. LatticeEmbeddingSource pooling).
    /// Delegates to <see cref="ApplySparsityBudget"/>.
    /// </summary>
    internal static SparseEntry[] TrimToBudget(SparseEntry[] entries, int budget)
        => ApplySparsityBudget(entries, budget);
}
