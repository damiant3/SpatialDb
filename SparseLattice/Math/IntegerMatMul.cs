using System.Runtime.CompilerServices;
using System.Threading.Tasks;
///////////////////////////////////////////////
namespace SparseLattice.Math;

/// <summary>
/// A tensor value paired with a power-of-two scale exponent.
/// The real-valued interpretation is <c>Data[i] × 2^ScaleExponent</c>.
/// Scale exponents compose algebraically through operations — no float rounding occurs.
/// </summary>
public readonly struct ScaledTensor
{
    /// <summary>Row-major int64 data. Shape is tracked by the caller.</summary>
    public readonly long[] Data;

    /// <summary>
    /// Power-of-two exponent relating <see cref="Data"/> to the true real value.
    /// Negative means values are scaled UP (carry fractional precision).
    /// After matmul of tensors with exponents eA and eB, result exponent is <c>eA + eB</c>.
    /// </summary>
    public readonly int ScaleExponent;

    public ScaledTensor(long[] data, int scaleExponent)
    {
        Data = data;
        ScaleExponent = scaleExponent;
    }

    public int Length => Data.Length;

    /// <summary>
    /// Arithmetic right-shift all values, adjusting exponent.
    /// Primary mechanism for keeping values in <c>long</c> range between layers.
    /// </summary>
    public ScaledTensor RightShift(int shiftBits)
    {
        if (shiftBits <= 0) return this;
        long[] shifted = new long[Data.Length];
        for (int i = 0; i < Data.Length; i++)
            shifted[i] = Data[i] >> shiftBits;
        return new ScaledTensor(shifted, ScaleExponent + shiftBits);
    }

    /// <summary>Dequantizes a single element to <c>double</c> for test comparison.</summary>
    public double DequantizeElement(int index)
        => Data[index] * System.Math.Pow(2.0, ScaleExponent);
}

/// <summary>
/// Exact integer matrix multiplication using <see cref="Int128"/> accumulators.
/// GGUF weight layout: column-major <c>W[col * nIn + k]</c>.
/// Activations: row-major <c>A[row * nIn + k]</c>.
/// </summary>
public static class IntegerMatMul
{
    /// <summary>
    /// Multiplies row-major A [rowsA × colsA] by column-major W [colsB × colsA],
    /// producing row-major C [rowsA × colsB] with Int128 accumulation.
    /// Parallelizes across output columns for large matrices.
    /// </summary>
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

    /// <summary>
    /// Exact dot product via Int128 accumulation with 4× unroll.
    /// Each long×long product is a single x64 <c>mul</c> into rdx:rax.
    /// </summary>
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

    /// <summary>
    /// Mixed dot product: int64 activations × float weights quantized on-the-fly.
    /// 4× unroll for throughput.
    /// </summary>
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

    /// <summary>
    /// Mixed dot product: int64 activations × Half weights quantized on-the-fly.
    /// </summary>
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

    /// <summary>Dot product returning <c>long</c> with optional right-shift.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long DotLong(long[] a, int aOffset, long[] b, int bOffset, int length, int shift = 0)
    {
        Int128 acc = DotInt128(a, aOffset, b, bOffset, length);
        return shift > 0 ? (long)(acc >> shift) : (long)acc;
    }

    /// <summary>In-place element-wise addition for residual connections.</summary>
    public static void AddInPlace(long[] dst, long[] src, int count)
    {
        for (int i = 0; i < count; i++)
            dst[i] += src[i];
    }

    /// <summary>In-place arithmetic right-shift (floor toward negative infinity).</summary>
    public static void RightShiftInPlace(long[] data, int count, int shift)
    {
        if (shift <= 0) return;
        for (int i = 0; i < count; i++)
            data[i] >>= shift;
    }

    /// <summary>
    /// Quantizes float32 to int64 at <c>2^scaleBits</c> scale.
    /// Result has <c>ScaleExponent = -scaleBits</c>.
    /// </summary>
    public static ScaledTensor QuantizeFromFloat(float[] source, int scaleBits = 30)
    {
        double scale = 1L << scaleBits;
        long[] data = new long[source.Length];
        for (int i = 0; i < source.Length; i++)
            data[i] = (long)(source[i] * scale);
        return new ScaledTensor(data, -scaleBits);
    }

    /// <summary>Dequantizes to float32 for test comparison. Not for hot paths.</summary>
    public static float[] DequantizeToFloat(ScaledTensor tensor)
    {
        double scale = System.Math.Pow(2.0, tensor.ScaleExponent);
        float[] result = new float[tensor.Data.Length];
        for (int i = 0; i < tensor.Data.Length; i++)
            result[i] = (float)(tensor.Data[i] * scale);
        return result;
    }
}
