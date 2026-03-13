using System.Runtime.CompilerServices;
///////////////////////////////////////////////
namespace SparseLattice.Math;

// Real-valued interpretation: Data[i] × 2^ScaleExponent
// Scale exponents compose algebraically — no float rounding occurs.
public readonly struct ScaledTensor
{
    public readonly long[] Data;
    public readonly int ScaleExponent;

    public ScaledTensor(long[] data, int scaleExponent)
    {
        Data = data;
        ScaleExponent = scaleExponent;
    }

    public int Length => Data.Length;

    public ScaledTensor RightShift(int shiftBits)
    {
        if (shiftBits <= 0) return this;
        long[] shifted = new long[Data.Length];
        for (int i = 0; i < Data.Length; i++)
            shifted[i] = Data[i] >> shiftBits;
        return new ScaledTensor(shifted, ScaleExponent + shiftBits);
    }

    public double DequantizeElement(int index)
        => Data[index] * System.Math.Pow(2.0, ScaleExponent);
}

// GGUF weight layout: column-major W[col * nIn + k], activations row-major A[row * nIn + k].
// All accumulation via Int128 for exact arithmetic.
public static class IntegerMatMul
{
    public static long[] MatMul(
        long[] a, int rowsA, int colsA,
        long[] w, int colsB,
        int resultShift = 0)
    {
        long[] c = new long[rowsA * colsB];

        // Parallelize when there's enough work to justify thread overhead.
        // Threshold: at least 256 columns and inner dimension ≥ 256.
        if (colsB >= 256 && colsA >= 256)
        {
            Parallel.For(0, colsB, col =>
            {
                int wBase = col * colsA;
                for (int row = 0; row < rowsA; row++)
                {
                    Int128 acc = DotInt128(a, row * colsA, w, wBase, colsA);
                    c[row * colsB + col] = resultShift > 0
                        ? (long)(acc >> resultShift)
                        : (long)acc;
                }
            });
        }
        else
        {
            for (int row = 0; row < rowsA; row++)
            {
                int aBase = row * colsA;
                for (int col = 0; col < colsB; col++)
                {
                    Int128 acc = DotInt128(a, aBase, w, col * colsA, colsA);
                    c[row * colsB + col] = resultShift > 0
                        ? (long)(acc >> resultShift)
                        : (long)acc;
                }
            }
        }

        return c;
    }

    /// <summary>
    /// <see cref="ScaledTensor"/> overload — result exponent is <c>eA + eW + resultShift</c>.
    /// </summary>
    public static ScaledTensor MatMul(
        ScaledTensor activations, int rowsA, int colsA,
        ScaledTensor weights, int colsB,
        int resultShift = 0)
    {
        long[] result = MatMul(activations.Data, rowsA, colsA, weights.Data, colsB, resultShift);
        int resultExponent = activations.ScaleExponent + weights.ScaleExponent + resultShift;
        return new ScaledTensor(result, resultExponent);
    }

    /// <summary>
    /// Like <see cref="MatMul"/> but weights are row-major: <c>W[row * colsB + col]</c>.
    /// </summary>
    public static long[] MatMulTransposed(
        long[] a, int rowsA, int colsA,
        long[] wT, int colsB,
        int resultShift = 0)
    {
        long[] c = new long[rowsA * colsB];

        for (int row = 0; row < rowsA; row++)
        {
            int aBase = row * colsA;
            for (int col = 0; col < colsB; col++)
            {
                Int128 acc = DotInt128(a, aBase, wT, col * colsA, colsA);
                c[row * colsB + col] = resultShift > 0
                    ? (long)(acc >> resultShift)
                    : (long)acc;
            }
        }

        return c;
    }

    /// <summary>
    /// Mixed-precision matmul: int64 activations × float32 weights.
    /// Weights are quantized to int64 on-the-fly during each dot product,
    /// avoiding the need to store the full weight matrix as int64 (saves 4× memory).
    /// </summary>
    public static long[] MatMul(
        long[] a, int rowsA, int colsA,
        float[] wFloat, int colsB,
        int scaleBits,
        int resultShift = 0)
    {
        long[] c = new long[rowsA * colsB];
        double scale = 1L << scaleBits;

        if (colsB >= 256 && colsA >= 256)
        {
            Parallel.For(0, colsB, col =>
            {
                int wBase = col * colsA;
                for (int row = 0; row < rowsA; row++)
                {
                    Int128 acc = DotInt128Mixed(a, row * colsA, wFloat, wBase, colsA, scale);
                    c[row * colsB + col] = resultShift > 0
                        ? (long)(acc >> resultShift)
                        : (long)acc;
                }
            });
        }
        else
        {
            for (int row = 0; row < rowsA; row++)
            {
                int aBase = row * colsA;
                for (int col = 0; col < colsB; col++)
                {
                    Int128 acc = DotInt128Mixed(a, aBase, wFloat, col * colsA, colsA, scale);
                    c[row * colsB + col] = resultShift > 0
                        ? (long)(acc >> resultShift)
                        : (long)acc;
                }
            }
        }

        return c;
    }

    /// <summary>
    /// Mixed-precision matmul: int64 activations × Half weights.
    /// Weights stored as Half (2 bytes each) for 4× memory savings vs int64.
    /// </summary>
    public static long[] MatMul(
        long[] a, int rowsA, int colsA,
        Half[] wHalf, int colsB,
        int scaleBits,
        int resultShift = 0)
    {
        long[] c = new long[rowsA * colsB];
        double scale = 1L << scaleBits;

        if (colsB >= 256 && colsA >= 256)
        {
            Parallel.For(0, colsB, col =>
            {
                int wBase = col * colsA;
                for (int row = 0; row < rowsA; row++)
                {
                    Int128 acc = DotInt128Mixed(a, row * colsA, wHalf, wBase, colsA, scale);
                    c[row * colsB + col] = resultShift > 0
                        ? (long)(acc >> resultShift)
                        : (long)acc;
                }
            });
        }
        else
        {
            for (int row = 0; row < rowsA; row++)
            {
                int aBase = row * colsA;
                for (int col = 0; col < colsB; col++)
                {
                    Int128 acc = DotInt128Mixed(a, aBase, wHalf, col * colsA, colsA, scale);
                    c[row * colsB + col] = resultShift > 0
                        ? (long)(acc >> resultShift)
                        : (long)acc;
                }
            }
        }

        return c;
    }

    // 4× unrolled Int128 dot — each long×long is a single x64 mul into rdx:rax
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Int128 DotInt128(long[] a, int aOffset, long[] b, int bOffset, int length)
    {
        Int128 acc = 0;
        int i = 0;
        int limit4 = length - 3;
        while (i < limit4)
        {
            acc += (Int128)a[aOffset + i]     * b[bOffset + i];
            acc += (Int128)a[aOffset + i + 1] * b[bOffset + i + 1];
            acc += (Int128)a[aOffset + i + 2] * b[bOffset + i + 2];
            acc += (Int128)a[aOffset + i + 3] * b[bOffset + i + 3];
            i += 4;
        }
        while (i < length)
        {
            acc += (Int128)a[aOffset + i] * b[bOffset + i];
            i++;
        }

        return acc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Int128 DotInt128Mixed(long[] a, int aOffset, float[] b, int bOffset, int length, double scale)
    {
        Int128 acc = 0;
        int i = 0;
        int limit4 = length - 3;
        while (i < limit4)
        {
            acc += (Int128)a[aOffset + i]     * (long)(b[bOffset + i]     * scale);
            acc += (Int128)a[aOffset + i + 1] * (long)(b[bOffset + i + 1] * scale);
            acc += (Int128)a[aOffset + i + 2] * (long)(b[bOffset + i + 2] * scale);
            acc += (Int128)a[aOffset + i + 3] * (long)(b[bOffset + i + 3] * scale);
            i += 4;
        }
        while (i < length)
        {
            acc += (Int128)a[aOffset + i] * (long)(b[bOffset + i] * scale);
            i++;
        }

        return acc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Int128 DotInt128Mixed(long[] a, int aOffset, Half[] b, int bOffset, int length, double scale)
    {
        Int128 acc = 0;
        int i = 0;
        int limit4 = length - 3;
        while (i < limit4)
        {
            acc += (Int128)a[aOffset + i]     * (long)((float)b[bOffset + i]     * scale);
            acc += (Int128)a[aOffset + i + 1] * (long)((float)b[bOffset + i + 1] * scale);
            acc += (Int128)a[aOffset + i + 2] * (long)((float)b[bOffset + i + 2] * scale);
            acc += (Int128)a[aOffset + i + 3] * (long)((float)b[bOffset + i + 3] * scale);
            i += 4;
        }
        while (i < length)
        {
            acc += (Int128)a[aOffset + i] * (long)((float)b[bOffset + i] * scale);
            i++;
        }

        return acc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long DotLong(long[] a, int aOffset, long[] b, int bOffset, int length, int shift = 0)
    {
        Int128 acc = DotInt128(a, aOffset, b, bOffset, length);
        return shift > 0 ? (long)(acc >> shift) : (long)acc;
    }

    public static void AddInPlace(long[] dst, long[] src, int count)
    {
        for (int i = 0; i < count; i++)
            dst[i] += src[i];
    }

    public static void RightShiftInPlace(long[] data, int count, int shift)
    {
        if (shift <= 0) return;
        for (int i = 0; i < count; i++)
            data[i] >>= shift;
    }

    public static ScaledTensor QuantizeFromFloat(float[] source, int scaleBits = 30)
    {
        double scale = 1L << scaleBits;
        long[] data = new long[source.Length];
        for (int i = 0; i < source.Length; i++)
            data[i] = (long)(source[i] * scale);
        return new ScaledTensor(data, -scaleBits);
    }

    public static float[] DequantizeToFloat(ScaledTensor tensor)
    {
        double scale = System.Math.Pow(2.0, tensor.ScaleExponent);
        float[] result = new float[tensor.Data.Length];
        for (int i = 0; i < tensor.Data.Length; i++)
            result[i] = (float)(tensor.Data[i] * scale);
        return result;
    }
}
