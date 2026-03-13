//////////////////////////////
namespace SparseLattice.Math;

public sealed class QuantizationOptions
{
    public float ZeroThreshold { get; init; } = 0.01f;
    public long GlobalScale { get; init; } = long.MaxValue;
    public int? SparsityBudget { get; init; }

    public static QuantizationOptions Default { get; } = new();
}

public static class EmbeddingAdapter
{
    public static SparseVector Quantize(float[] embedding, QuantizationOptions? options = null)
    {
        QuantizationOptions effectiveOptions = options ?? QuantizationOptions.Default;

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

    static SparseEntry[] ApplySparsityBudget(SparseEntry[] entries, int budget)
    {
        static ulong AbsUnsigned(long v) => v < 0 ? (ulong)(-(v + 1)) + 1UL : (ulong)v;
        Array.Sort(entries, (a, b) => AbsUnsigned(b.Value).CompareTo(AbsUnsigned(a.Value)));
        SparseEntry[] trimmed = new SparseEntry[budget];
        Array.Copy(entries, trimmed, budget);
        Array.Sort(trimmed, (a, b) => a.Dimension.CompareTo(b.Dimension));
        return trimmed;
    }

    internal static SparseEntry[] TrimToBudget(SparseEntry[] entries, int budget)
        => ApplySparsityBudget(entries, budget);
}
