//////////////////////////////
namespace SparseLattice.Math;

public sealed class QuantizationOptions
{
    public float ZeroThreshold { get; init; } = 0.01f;
    public long GlobalScale { get; init; } = long.MaxValue;

    public static QuantizationOptions Default { get; } = new();
}

public static class EmbeddingAdapter
{
    public static SparseVector Quantize(float[] embedding, QuantizationOptions? options = null)
    {
        QuantizationOptions effectiveOptions = options ?? QuantizationOptions.Default;

        int nonzeroCount = 0;
        for (int i = 0; i < embedding.Length; i++)
            if (MathF.Abs(embedding[i]) >= effectiveOptions.ZeroThreshold)
                nonzeroCount++;

        SparseEntry[] entries = new SparseEntry[nonzeroCount];
        int writeIndex = 0;
        for (int i = 0; i < embedding.Length; i++)
        {
            if (MathF.Abs(embedding[i]) < effectiveOptions.ZeroThreshold)
                continue;
            long quantized = (long)(embedding[i] * (double)effectiveOptions.GlobalScale);
            entries[writeIndex++] = new SparseEntry((ushort)i, quantized);
        }

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
}
