using System.Runtime.CompilerServices;
///////////////////////////////////////////////
namespace SparseLattice.Math;

/// <summary>
/// Fixed-point transcendental functions for integer transformer inference.
/// All values are scaled by <c>2^fracBits</c>. Intermediate arithmetic uses
/// Int128 to prevent overflow. Results are deterministic and bit-identical.
/// </summary>
public static class IntegerTranscendentals
{
    /// <summary>Default fractional bits for fixed-point representation.</summary>
    public const int DefaultFracBits = 30;

    /// <summary>
    /// Fixed-point <c>exp(x)</c> via range reduction (x = k·ln2 + r) and
    /// 14-term Taylor series on the remainder. Saturates at long.MaxValue / 0
    /// for extreme inputs.
    /// </summary>
    public static long FixedExp(long x, int fracBits = DefaultFracBits)
    {
        long ln2 = FixedFromDouble(0.6931471805599453, fracBits);
        if (ln2 == 0) ln2 = 1;

        long k = x >= 0 ? x / ln2 : -((-x + ln2 - 1) / ln2);
        long r = x - k * ln2;

        if (k > 62) return long.MaxValue;
        if (k < -(fracBits + 10)) return 0;

        long one = 1L << fracBits;
        long term = one;
        long sum = one;

        for (int n = 1; n <= 14; n++)
        {
            term = (long)((Int128)term * r / ((Int128)n << fracBits));
            sum += term;
            if (term == 0 && n > 4) break;
        }

        if (k >= 0)
        {
            if (k >= 63 || sum > (long.MaxValue >> (int)k))
                return long.MaxValue;
            return sum << (int)k;
        }
        else
        {
            int shift = (int)(-k);
            if (shift >= 63) return 0;
            return sum >> shift;
        }
    }

    /// <summary>Fixed-point <c>sigmoid(x) = 1 / (1 + exp(-x))</c>.</summary>
    public static long FixedSigmoid(long x, int fracBits = DefaultFracBits)
    {
        long one = 1L << fracBits;

        long threshold = 20L * one;
        if (x > threshold) return one;
        if (x < -threshold) return 0;

        long expNegX = FixedExp(-x, fracBits);
        long denom = one + expNegX;
        if (denom == 0) return one;

        // one² via Int128 to avoid overflow (one is 2^30, one² is 2^60)
        return (long)((Int128)one * one / denom);
    }

    /// <summary>Fixed-point <c>SiLU(x) = x × sigmoid(x)</c>.</summary>
    public static long FixedSiLU(long x, int fracBits = DefaultFracBits)
    {
        long sig = FixedSigmoid(x, fracBits);
        return (long)((Int128)x * sig >> fracBits);
    }

    /// <summary>
    /// In-place softmax over <paramref name="scores"/>[rowBase .. rowBase+len).
    /// Output is fixed-point probabilities summing to <c>2^fracBits</c>.
    /// </summary>
    public static void FixedSoftmax(long[] scores, int rowBase, int len, int fracBits = DefaultFracBits)
    {
        long one = 1L << fracBits;

        long max = scores[rowBase];
        for (int i = 1; i < len; i++)
            if (scores[rowBase + i] > max) max = scores[rowBase + i];

        Int128 sum = 0;
        for (int i = 0; i < len; i++)
        {
            long e = FixedExp(scores[rowBase + i] - max, fracBits);
            scores[rowBase + i] = e;
            sum += e;
        }

        if (sum == 0)
        {
            long uniform = one / len;
            for (int i = 0; i < len; i++)
                scores[rowBase + i] = uniform;
            return;
        }

        for (int i = 0; i < len; i++)
            scores[rowBase + i] = (long)((Int128)scores[rowBase + i] * one / sum);
    }

    /// <summary>Converts a double to fixed-point.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long FixedFromDouble(double value, int fracBits = DefaultFracBits)
        => (long)(value * (1L << fracBits));

    /// <summary>Converts fixed-point back to double (for testing).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double FixedToDouble(long value, int fracBits = DefaultFracBits)
        => value / (double)(1L << fracBits);
}
