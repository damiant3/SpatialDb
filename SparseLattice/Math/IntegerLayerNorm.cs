using System.Runtime.CompilerServices;
///////////////////////////////////////////////
namespace SparseLattice.Math;

/// <summary>
/// LayerNorm in exact integer arithmetic.
/// Uses Int128 accumulation for mean/variance and <see cref="ISqrt128"/> for
/// the inverse standard deviation. The only approximation source is the ISqrt
/// floor (±1 ULP) and right-shift truncation — both deterministic and bounded.
/// </summary>
public static class IntegerLayerNorm
{
    /// <summary>
    /// Applies LayerNorm in-place to each row of <paramref name="x"/>
    /// [seqLen × embd]. Weight and bias are [embd] at the same scale.
    /// </summary>
    public static void ApplyInPlace(long[] x, int seqLen, int embd,
        long[] weight, long[] bias, int scaleExponent)
    {
        for (int t = 0; t < seqLen; t++)
            NormalizeRow(x, t * embd, embd, weight, bias, scaleExponent);
    }

    /// <summary>Normalizes a single row in-place.</summary>
    public static void NormalizeRow(long[] x, int rowBase, int embd,
        long[] weight, long[] bias, int scaleExponent)
    {
        Int128 sum = 0;
        for (int d = 0; d < embd; d++)
            sum += x[rowBase + d];

        long mean = (long)(sum / embd);

        Int128 sumSq = 0;
        for (int d = 0; d < embd; d++)
        {
            long delta = x[rowBase + d] - mean;
            sumSq += (Int128)delta * delta;
        }

        Int128 variance128 = sumSq / embd;

        if (variance128 <= 0)
        {
            for (int d = 0; d < embd; d++)
                x[rowBase + d] = bias[d];
            return;
        }

        long isqrtVariance = ISqrt128(variance128);
        if (isqrtVariance == 0) isqrtVariance = 1;

        // (x-mean) at scale S, weight at scale S, isqrt at scale S
        // → (delta * weight) / isqrt produces result at scale S
        for (int d = 0; d < embd; d++)
        {
            long delta = x[rowBase + d] - mean;
            Int128 product = (Int128)delta * weight[d];
            x[rowBase + d] = (long)(product / isqrtVariance) + bias[d];
        }
    }

    /// <summary>
    /// RMS LayerNorm in-place: <c>x[d] = x[d] * weight[d] / rms(x)</c>.
    /// No mean subtraction, no bias. Used by Gemma/LLaMA architectures.
    /// </summary>
    public static void RmsNormInPlace(long[] x, int seqLen, int embd,
        long[] weight, int scaleExponent)
    {
        for (int t = 0; t < seqLen; t++)
            RmsNormRow(x, t * embd, embd, weight, scaleExponent);
    }

    /// <summary>RMS-normalizes a single row in-place.</summary>
    public static void RmsNormRow(long[] x, int rowBase, int embd,
        long[] weight, int scaleExponent)
    {
        Int128 sumSq = 0;
        for (int d = 0; d < embd; d++)
            sumSq += (Int128)x[rowBase + d] * x[rowBase + d];

        Int128 meanSq = sumSq / embd;

        if (meanSq <= 0)
            return;

        long rms = ISqrt128(meanSq);
        if (rms == 0) rms = 1;

        // x[d] at scale S, weight at scale S, rms at scale S
        // → (x * weight) / rms at scale S
        for (int d = 0; d < embd; d++)
        {
            Int128 product = (Int128)x[rowBase + d] * weight[d];
            x[rowBase + d] = (long)(product / rms);
        }
    }

    /// <summary>
    /// Floor integer square root for Int128 via Newton's method.
    /// Result satisfies <c>result² ≤ value &lt; (result+1)²</c>.
    /// </summary>
    public static long ISqrt128(Int128 value)
    {
        if (value <= 0) return 0;
        if (value <= long.MaxValue) return ISqrt64((long)value);

        int bits = 0;
        {
            Int128 tmp = value;
            while (tmp > 0) { tmp >>= 1; bits++; }
        }
        Int128 x = (Int128)1 << ((bits + 1) / 2);

        for (int i = 0; i < 128; i++)
        {
            Int128 xNext = (x + value / x) >> 1;
            if (xNext >= x) break;
            x = xNext;
        }

        while (x * x > value) x--;
        while ((x + 1) * (x + 1) <= value) x++;

        return (long)x;
    }

    /// <summary>
    /// Floor integer square root for long. Uses double as initial guess,
    /// refines via Newton for values above 2^52, and validates via Int128
    /// to avoid long overflow on x² near the top of the range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ISqrt64(long value)
    {
        if (value <= 0) return 0;
        if (value == 1) return 1;

        long x = (long)System.Math.Sqrt((double)value);
        if (x <= 0) x = 1;

        if (value > (1L << 52))
        {
            x = (x + value / x) >> 1;
            if (x > 0) x = (x + value / x) >> 1;
        }

        // Int128 comparisons avoid long overflow when x ≈ 3×10⁹
        while (x > 0 && (Int128)x * x > value) x--;
        while ((Int128)(x + 1) * (x + 1) <= value) x++;

        return x;
    }
}
