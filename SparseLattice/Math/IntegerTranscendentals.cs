using System.Runtime.CompilerServices;
///////////////////////////////////////////////
namespace SparseLattice.Math;

// All values scaled by 2^fracBits. Int128 intermediates prevent overflow.
// Results are deterministic and bit-identical across platforms.
public static class IntegerTranscendentals
{
    public const int DefaultFracBits = 30;

    // exp(x) via range reduction (x = k·ln2 + r) and 14-term Taylor series.
    // Saturates at long.MaxValue / 0 for extreme inputs.
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

    public static long FixedSiLU(long x, int fracBits = DefaultFracBits)
    {
        long sig = FixedSigmoid(x, fracBits);
        return (long)((Int128)x * sig >> fracBits);
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long FixedFromDouble(double value, int fracBits = DefaultFracBits)
        => (long)(value * (1L << fracBits));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double FixedToDouble(long value, int fracBits = DefaultFracBits)
        => value / (double)(1L << fracBits);
}
