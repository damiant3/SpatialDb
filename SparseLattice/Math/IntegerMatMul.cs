using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
///////////////////////////////////////////////
namespace SparseLattice.Math;

/// <summary>
/// A tensor value paired with a power-of-two scale exponent.
/// The real-valued interpretation is <c>RawValue × 2^ScaleExponent</c>.
/// All arithmetic on <see cref="ScaledTensor"/> preserves exact results by tracking
/// scale exponents algebraically; no floating-point rounding ever occurs.
/// </summary>
public readonly struct ScaledTensor
{
    /// <summary>Row-major int64 data. Shape is tracked by the caller.</summary>
    public readonly long[] Data;

    /// <summary>
    /// Power-of-two exponent that relates <see cref="Data"/> to the true real value.
    /// A negative exponent means the integer values are scaled UP (multiplied by 2^|e|
    /// relative to the original floats), so they carry more fractional precision.
    /// After a matmul of two tensors with exponents eA and eB, the result exponent
    /// is <c>eA + eB</c>.
    /// </summary>
    public readonly int ScaleExponent;

    public ScaledTensor(long[] data, int scaleExponent)
    {
        Data = data;
        ScaleExponent = scaleExponent;
    }

    /// <summary>Total elements in <see cref="Data"/>.</summary>
    public int Length => Data.Length;

    /// <summary>
    /// Right-shift all values by <paramref name="shiftBits"/> (arithmetic shift,
    /// rounds toward negative infinity). Returns a new <see cref="ScaledTensor"/>
    /// with adjusted exponent. This is the primary mechanism for keeping values
    /// in <c>long</c> range between layers.
    /// </summary>
    public ScaledTensor RightShift(int shiftBits)
    {
        if (shiftBits <= 0) return this;
        long[] shifted = new long[Data.Length];
        for (int i = 0; i < Data.Length; i++)
            shifted[i] = Data[i] >> shiftBits;
        return new ScaledTensor(shifted, ScaleExponent + shiftBits);
    }

    /// <summary>
    /// Dequantizes a single element back to <c>double</c> for test comparison.
    /// Not used on any hot path.
    /// </summary>
    public double DequantizeElement(int index)
        => Data[index] * System.Math.Pow(2.0, ScaleExponent);
}

/// <summary>
/// Exact integer matrix multiplication using <see cref="Int128"/> accumulators.
///
/// <para>
/// Every product of two <c>long</c> values fits exactly in <see cref="Int128"/>.
/// A dot product over <c>n</c> dimensions accumulates <c>n</c> such products.
/// For n ≤ 65536, the sum fits in 128 bits without overflow when inputs are
/// bounded by ±2^30 (our quantization scale).
/// </para>
///
/// <para>
/// After accumulation the result is right-shifted back to <c>long</c> range.
/// The shift amount is deterministic and exact: unlike float truncation, the
/// bits below the shift point are simply discarded (floor toward negative infinity),
/// and the remaining bits are perfectly preserved.
/// </para>
///
/// <para>
/// GGUF weight layout: weights are stored column-major, matching
/// <see cref="TransformerEmbeddingSource.MatMulGguf"/>: <c>W[col * nIn + k]</c>.
/// Activations are row-major: <c>A[row * nIn + k]</c>.
/// </para>
/// </summary>
public static class IntegerMatMul
{
    // -----------------------------------------------------------------------
    // Core: dense × dense, Int128 accumulator
    // -----------------------------------------------------------------------

    /// <summary>
    /// Multiplies activation matrix A [rowsA × colsA] by weight matrix W [colsA × colsB]
    /// stored column-major (GGUF layout: W[col * colsA + k]), producing C [rowsA × colsB].
    ///
    /// <para>
    /// Each element <c>C[row, col] = Σ_k A[row, k] × W[col * colsA + k]</c>,
    /// accumulated in <see cref="Int128"/> for exact results.
    /// The 128-bit accumulator is then right-shifted by <paramref name="resultShift"/>
    /// to produce a <c>long</c> output.
    /// </para>
    /// </summary>
    /// <param name="a">Row-major activations [rowsA × colsA].</param>
    /// <param name="rowsA">Number of rows (sequence length).</param>
    /// <param name="colsA">Number of columns in A = inner dimension.</param>
    /// <param name="w">Column-major weights [colsB × colsA] (GGUF layout).</param>
    /// <param name="colsB">Number of output columns.</param>
    /// <param name="resultShift">
    /// Right-shift applied to each Int128 accumulator before storing as long.
    /// Typically equals the sum of the input scale exponents minus the desired
    /// output scale exponent. Zero means no shift (for testing or when ranges allow).
    /// </param>
    /// <returns>Row-major result [rowsA × colsB].</returns>
    public static long[] MatMul(
        long[] a, int rowsA, int colsA,
        long[] w, int colsB,
        int resultShift = 0)
    {
        long[] c = new long[rowsA * colsB];

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

        return c;
    }

    /// <summary>
    /// <see cref="ScaledTensor"/> overload. The result's scale exponent is computed
    /// algebraically: <c>eA + eW + resultShift</c>.
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

    // -----------------------------------------------------------------------
    // Core: dense × dense, transposed weight layout (row-major W)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Like <see cref="MatMul"/> but weights are stored row-major: <c>W[row * colsB + col]</c>.
    /// Useful for weight matrices that have already been transposed at load time.
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

    // -----------------------------------------------------------------------
    // Dot product: Int128 accumulator
    // -----------------------------------------------------------------------

    /// <summary>
    /// Computes the exact dot product of two <c>long</c> spans using <see cref="Int128"/>
    /// accumulation. Each <c>long × long</c> product fits exactly in Int128; the sum
    /// of up to 2^63 such products also fits (128 - 63 = 65 bits of headroom for the
    /// count, far more than any realistic dimension).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Int128 DotInt128(long[] a, int aOffset, long[] b, int bOffset, int length)
    {
        Int128 acc = 0;
        int i = 0;

        // Unroll 4× — the compiler can schedule these loads and multiplies across
        // execution ports. Each Int128 multiply is a single x64 `mul` instruction
        // producing 128-bit result in rdx:rax.
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
    /// Dot product returning <c>long</c> with right-shift. Convenience for callers
    /// that don't need the full 128-bit result.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long DotLong(long[] a, int aOffset, long[] b, int bOffset, int length, int shift = 0)
    {
        Int128 acc = DotInt128(a, aOffset, b, bOffset, length);
        return shift > 0 ? (long)(acc >> shift) : (long)acc;
    }

    // -----------------------------------------------------------------------
    // Element-wise operations (used between layers)
    // -----------------------------------------------------------------------

    /// <summary>
    /// In-place element-wise addition: <c>dst[i] += src[i]</c> for residual connections.
    /// Exact — long addition cannot lose precision (may overflow, but we control scale
    /// to prevent this).
    /// </summary>
    public static void AddInPlace(long[] dst, long[] src, int count)
    {
        for (int i = 0; i < count; i++)
            dst[i] += src[i];
    }

    /// <summary>
    /// In-place right-shift (arithmetic): <c>data[i] >>= shift</c>.
    /// Deterministic floor-toward-negative-infinity truncation.
    /// </summary>
    public static void RightShiftInPlace(long[] data, int count, int shift)
    {
        if (shift <= 0) return;
        for (int i = 0; i < count; i++)
            data[i] >>= shift;
    }

    // -----------------------------------------------------------------------
    // Quantization: float[] → long[] at a given scale
    // -----------------------------------------------------------------------

    /// <summary>
    /// Quantizes a float32 array to int64 by multiplying by <c>2^scaleBits</c>.
    /// The resulting <see cref="ScaledTensor"/> has <c>ScaleExponent = -scaleBits</c>
    /// (negative because the integers are scaled UP relative to the original floats).
    ///
    /// <para>
    /// For a weight value of 0.037 with scaleBits=30:
    /// <c>(long)(0.037 × 2^30) = (long)(0.037 × 1073741824) = 39728447</c>.
    /// This preserves ~9 decimal digits of precision per weight (vs float32's ~7).
    /// </para>
    /// </summary>
    /// <param name="source">Source float array.</param>
    /// <param name="scaleBits">Number of fractional bits. Higher = more precision, smaller range.</param>
    public static ScaledTensor QuantizeFromFloat(float[] source, int scaleBits = 30)
    {
        double scale = 1L << scaleBits;
        long[] data = new long[source.Length];
        for (int i = 0; i < source.Length; i++)
            data[i] = (long)(source[i] * scale);
        return new ScaledTensor(data, -scaleBits);
    }

    /// <summary>
    /// Dequantizes a <see cref="ScaledTensor"/> back to float32 for comparison/test.
    /// Not for hot paths — precision is reduced to float32.
    /// </summary>
    public static float[] DequantizeToFloat(ScaledTensor tensor)
    {
        double scale = System.Math.Pow(2.0, tensor.ScaleExponent);
        float[] result = new float[tensor.Data.Length];
        for (int i = 0; i < tensor.Data.Length; i++)
            result[i] = (float)(tensor.Data[i] * scale);
        return result;
    }
}
