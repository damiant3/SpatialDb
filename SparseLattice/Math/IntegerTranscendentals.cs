using System.Runtime.CompilerServices;
///////////////////////////////////////////////
namespace SparseLattice.Math;

/// <summary>
/// Fixed-point transcendental functions for integer transformer inference.
///
/// <para>
/// All functions operate in a fixed-point representation where values are
/// scaled by <c>2^FracBits</c>. For example, with <c>FracBits=30</c>,
/// the integer value <c>1073741824</c> represents real value <c>1.0</c>.
/// </para>
///
/// <para>
/// The implementations use Taylor series truncated to enough terms for
/// the error to be below 1 ULP at the working precision. All intermediate
/// arithmetic uses <see cref="Int128"/> to prevent overflow. The results
/// are deterministic and bit-identical across runs.
/// </para>
/// </summary>
public static class IntegerTranscendentals
{
    /// <summary>Default number of fractional bits for fixed-point representation.</summary>
    public const int DefaultFracBits = 30;

    // -----------------------------------------------------------------------
    // exp(x) in fixed-point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Computes <c>exp(x)</c> in fixed-point with <paramref name="fracBits"/> fractional bits.
    ///
    /// <para>
    /// Input <paramref name="x"/> is a fixed-point value: real value = x / 2^fracBits.
    /// Output is also fixed-point at the same scale.
    /// </para>
    ///
    /// <para>
    /// Uses range reduction + Taylor series:
    /// 1. Decompose x = k * ln(2) + r  where k = floor(x / ln(2)), |r| ≤ ln(2)/2
    /// 2. exp(x) = 2^k * exp(r)
    /// 3. exp(r) via Taylor: 1 + r + r²/2! + r³/3! + ... (12 terms for 30-bit precision)
    /// 4. 2^k is an exact left-shift
    /// </para>
    ///
    /// <para>
    /// For the softmax use case, inputs are scores shifted by max (so all ≤ 0).
    /// exp of a negative number is in (0, 1], which maps to [0, 2^fracBits] — fits in long.
    /// </para>
    /// </summary>
    public static long FixedExp(long x, int fracBits = DefaultFracBits)
    {
        // ln(2) in fixed-point
        // ln(2) ≈ 0.6931471805599453
        long ln2 = FixedFromDouble(0.6931471805599453, fracBits);
        if (ln2 == 0) ln2 = 1; // safety for very small fracBits

        // Range reduction: x = k * ln2 + r
        // k = floor(x / ln2) — note x may be negative
        long k = x >= 0 ? x / ln2 : -((-x + ln2 - 1) / ln2);
        long r = x - k * ln2;

        // Clamp k to prevent astronomical shifts
        // exp(x) for x > 40 overflows any fixed-point representation
        // exp(x) for x < -40 is effectively 0
        if (k > 62) return long.MaxValue; // overflow — saturate
        if (k < -(fracBits + 10)) return 0; // underflow — effectively zero

        // Taylor series for exp(r) where |r| ≤ ln(2) ≈ 0.693
        // At 30-bit precision, we need ~12 terms for < 1 ULP error.
        // exp(r) = sum_{n=0}^{N} r^n / n!
        //
        // In fixed-point: start with term = 1.0 (= 2^fracBits), accumulate sum.
        // Each step: term = term * r / (n * 2^fracBits)
        // Using Int128 for the multiply to avoid overflow.
        long one = 1L << fracBits;
        long term = one; // r^0 / 0! = 1.0
        long sum = one;  // running sum

        for (int n = 1; n <= 14; n++)
        {
            // term = term * r / (n * one)
            // = (term * r) >> fracBits / n
            // To preserve precision: (term * r / n) >> fracBits
            term = (long)((Int128)term * r / ((Int128)n << fracBits));
            sum += term;
            // Early exit when term is negligible
            if (term == 0 && n > 4) break;
        }

        // Apply 2^k: shift the result
        if (k >= 0)
        {
            // Check for overflow before shifting
            if (k >= 63 || sum > (long.MaxValue >> (int)k))
                return long.MaxValue; // saturate
            return sum << (int)k;
        }
        else
        {
            int shift = (int)(-k);
            if (shift >= 63) return 0;
            return sum >> shift;
        }
    }

    // -----------------------------------------------------------------------
    // sigmoid(x) = 1 / (1 + exp(-x))
    // -----------------------------------------------------------------------

    /// <summary>
    /// Computes <c>sigmoid(x) = 1 / (1 + exp(-x))</c> in fixed-point.
    /// Output range: [0, 2^fracBits] representing [0.0, 1.0].
    /// </summary>
    public static long FixedSigmoid(long x, int fracBits = DefaultFracBits)
    {
        long one = 1L << fracBits;

        // For large positive x: sigmoid → 1.0
        // For large negative x: sigmoid → 0.0
        // Threshold: |x| > 20 * one is safely saturated for 30-bit precision
        long threshold = 20L * one;
        if (x > threshold) return one;
        if (x < -threshold) return 0;

        long expNegX = FixedExp(-x, fracBits);
        long denom = one + expNegX;
        if (denom == 0) return one; // guard (shouldn't happen)

        // sigmoid = one * one / denom = one² / denom
        // Use Int128 to avoid overflow: one is 2^30, one² is 2^60
        return (long)((Int128)one * one / denom);
    }

    // -----------------------------------------------------------------------
    // SiLU(x) = x * sigmoid(x)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Computes <c>SiLU(x) = x * sigmoid(x)</c> in fixed-point.
    /// </summary>
    public static long FixedSiLU(long x, int fracBits = DefaultFracBits)
    {
        long sig = FixedSigmoid(x, fracBits);
        // x * sig / one  (sig is in [0, one], so x * sig / one is at the same scale as x)
        return (long)((Int128)x * sig >> fracBits);
    }

    // -----------------------------------------------------------------------
    // Softmax over a row of fixed-point scores
    // -----------------------------------------------------------------------

    /// <summary>
    /// In-place softmax over <paramref name="scores"/>[rowBase .. rowBase+len).
    ///
    /// <para>
    /// After this call, each score is replaced by its softmax probability
    /// in fixed-point: value represents probability × 2^fracBits.
    /// The sum of the row equals 2^fracBits (= 1.0 in fixed-point).
    /// </para>
    ///
    /// <para>
    /// Algorithm (matches float softmax exactly in structure):
    /// 1. Find max score in the row
    /// 2. Subtract max (numerically stable — all values now ≤ 0)
    /// 3. Compute exp(score[i] - max) for each element
    /// 4. Sum all exp values
    /// 5. Divide each exp value by sum → probability
    /// </para>
    /// </summary>
    public static void FixedSoftmax(long[] scores, int rowBase, int len, int fracBits = DefaultFracBits)
    {
        long one = 1L << fracBits;

        // 1. Find max
        long max = scores[rowBase];
        for (int i = 1; i < len; i++)
            if (scores[rowBase + i] > max) max = scores[rowBase + i];

        // 2-3. Subtract max and compute exp
        // Also accumulate sum using Int128 to handle large sequences
        Int128 sum = 0;
        for (int i = 0; i < len; i++)
        {
            long shifted = scores[rowBase + i] - max; // ≤ 0
            long e = FixedExp(shifted, fracBits);
            scores[rowBase + i] = e;
            sum += e;
        }

        // 4-5. Normalize: prob[i] = exp[i] * one / sum
        if (sum == 0)
        {
            // Uniform distribution (shouldn't happen with valid scores)
            long uniform = one / len;
            for (int i = 0; i < len; i++)
                scores[rowBase + i] = uniform;
            return;
        }

        for (int i = 0; i < len; i++)
            scores[rowBase + i] = (long)((Int128)scores[rowBase + i] * one / sum);
    }

    // -----------------------------------------------------------------------
    // Utility
    // -----------------------------------------------------------------------

    /// <summary>Converts a double to fixed-point representation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long FixedFromDouble(double value, int fracBits = DefaultFracBits)
        => (long)(value * (1L << fracBits));

    /// <summary>Converts a fixed-point value back to double (for testing).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double FixedToDouble(long value, int fracBits = DefaultFracBits)
        => value / (double)(1L << fracBits);
}
