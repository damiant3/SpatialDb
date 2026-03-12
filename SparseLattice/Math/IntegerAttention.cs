///////////////////////////////////////////////
namespace SparseLattice.Math;

/// <summary>
/// Integer multi-head self-attention.
/// QKV projections, RoPE rotation, scaled dot-product scores with fixed-point
/// softmax, and weighted value summation — all via Int128 accumulators.
/// </summary>
public static class IntegerAttention
{
    /// <summary>
    /// Precomputed rotary position embedding tables in fixed-point.
    /// </summary>
    public sealed class IntegerRoPECache
    {
        public readonly long[] Cos;
        public readonly long[] Sin;
        public readonly int MaxSeqLen;
        public readonly int HalfDim;
        public readonly int FracBits;

        public IntegerRoPECache(int maxSeqLen, int headDim, float freqBase, int fracBits = IntegerTranscendentals.DefaultFracBits)
        {
            MaxSeqLen = maxSeqLen;
            HalfDim = headDim / 2;
            FracBits = fracBits;
            Cos = new long[maxSeqLen * HalfDim];
            Sin = new long[maxSeqLen * HalfDim];

            for (int t = 0; t < maxSeqLen; t++)
            {
                for (int i = 0; i < HalfDim; i++)
                {
                    double theta = System.Math.Pow(freqBase, -2.0 * i / headDim);
                    double angle = t * theta;
                    Cos[t * HalfDim + i] = IntegerTranscendentals.FixedFromDouble(System.Math.Cos(angle), fracBits);
                    Sin[t * HalfDim + i] = IntegerTranscendentals.FixedFromDouble(System.Math.Sin(angle), fracBits);
                }
            }
        }
    }

    /// <summary>Applies RoPE rotation in-place to Q or K vectors [seqLen × embd].</summary>
    public static void ApplyRoPE(long[] x, int seqLen, int embd, int nHeads, IntegerRoPECache cache)
    {
        int headDim = embd / nHeads;
        int halfDim = headDim / 2;
        int fracBits = cache.FracBits;

        for (int t = 0; t < seqLen; t++)
        {
            int cacheRow = t * halfDim;
            for (int h = 0; h < nHeads; h++)
            {
                int headBase = t * embd + h * headDim;
                for (int i = 0; i < halfDim; i++)
                {
                    long cos = cache.Cos[cacheRow + i];
                    long sin = cache.Sin[cacheRow + i];
                    long x0 = x[headBase + i];
                    long x1 = x[headBase + i + halfDim];
                    x[headBase + i]           = (long)(((Int128)x0 * cos - (Int128)x1 * sin) >> fracBits);
                    x[headBase + i + halfDim] = (long)(((Int128)x0 * sin + (Int128)x1 * cos) >> fracBits);
                }
            }
        }
    }

    /// <summary>
    /// Multi-head self-attention in integer arithmetic.
    /// Scores are computed at fixed-point scale, softmax applied per row,
    /// then V is weighted-summed back to the activation scale.
    /// </summary>
    public static long[] MultiHeadAttention(
        long[] q, long[] k, long[] v,
        int seqLen, int embd, int nHeads,
        int scaleExponent,
        int fracBits = IntegerTranscendentals.DefaultFracBits)
    {
        int headDim = embd / nHeads;
        long[] output = new long[seqLen * embd];
        long[] scores = new long[seqLen * seqLen];

        // rawDot at 2×|scaleExponent| bits → shift to fracBits, then ÷ sqrt(headDim)
        int absScale = System.Math.Abs(scaleExponent);
        int scoreShift = 2 * absScale - fracBits;
        long sqrtHeadDim = IntegerLayerNorm.ISqrt64(headDim);
        if (sqrtHeadDim == 0) sqrtHeadDim = 1;

        for (int h = 0; h < nHeads; h++)
        {
            int headOffset = h * headDim;

            for (int t = 0; t < seqLen; t++)
            {
                int qBase = t * embd + headOffset;
                for (int s = 0; s < seqLen; s++)
                {
                    Int128 rawDot = IntegerMatMul.DotInt128(q, qBase, k, s * embd + headOffset, headDim);
                    scores[t * seqLen + s] = (long)(rawDot >> scoreShift) / sqrtHeadDim;
                }
            }

            for (int t = 0; t < seqLen; t++)
                IntegerTranscendentals.FixedSoftmax(scores, t * seqLen, seqLen, fracBits);

            // scores at fracBits scale × v at scaleExponent → shift by fracBits
            for (int t = 0; t < seqLen; t++)
            {
                int outBase = t * embd + headOffset;
                int scoreBase = t * seqLen;
                for (int s = 0; s < seqLen; s++)
                {
                    long w = scores[scoreBase + s];
                    int vBase = s * embd + headOffset;
                    for (int d = 0; d < headDim; d++)
                        output[outBase + d] += (long)(((Int128)w * v[vBase + d]) >> fracBits);
                }
            }
        }

        return output;
    }

    /// <summary>Splits fused QKV [seqLen × 3·embd] into separate Q, K, V.</summary>
    public static void SplitQkv(long[] qkv, long[] q, long[] k, long[] v, int seqLen, int embd)
    {
        for (int t = 0; t < seqLen; t++)
        {
            int srcBase = t * 3 * embd;
            int dstBase = t * embd;
            System.Array.Copy(qkv, srcBase, q, dstBase, embd);
            System.Array.Copy(qkv, srcBase + embd, k, dstBase, embd);
            System.Array.Copy(qkv, srcBase + 2 * embd, v, dstBase, embd);
        }
    }

    /// <summary>
    /// GQA multi-head attention: Q has <paramref name="nHeads"/> heads,
    /// K/V have <paramref name="nKvHeads"/> heads. Each KV head serves
    /// <c>nHeads/nKvHeads</c> query heads.
    /// </summary>
    public static long[] GroupedQueryAttention(
        long[] q, long[] k, long[] v,
        int seqLen, int qEmbd, int kvDim,
        int nHeads, int nKvHeads,
        int scaleExponent,
        int fracBits = IntegerTranscendentals.DefaultFracBits)
    {
        int headDim = qEmbd / nHeads;
        int kvHeadDim = kvDim / nKvHeads;
        int headsPerKv = nHeads / nKvHeads;
        long[] output = new long[seqLen * qEmbd];
        long[] scores = new long[seqLen * seqLen];

        int absScale = System.Math.Abs(scaleExponent);
        int scoreShift = 2 * absScale - fracBits;
        long sqrtHeadDim = IntegerLayerNorm.ISqrt64(headDim);
        if (sqrtHeadDim == 0) sqrtHeadDim = 1;

        // Parallelize across heads when there are enough to benefit.
        // Each head writes to a non-overlapping slice of the output array.
        if (nHeads >= 4)
        {
            Parallel.For(0, nHeads, h =>
            {
                NonCausalHeadAttention(q, k, v, output, seqLen, qEmbd, kvDim,
                    h, headDim, kvHeadDim, headsPerKv, scoreShift, sqrtHeadDim, fracBits);
            });
        }
        else
        {
            for (int h = 0; h < nHeads; h++)
            {
                NonCausalHeadAttention(q, k, v, output, seqLen, qEmbd, kvDim,
                    h, headDim, kvHeadDim, headsPerKv, scoreShift, sqrtHeadDim, fracBits);
            }
        }

        return output;
    }

    private static void NonCausalHeadAttention(
        long[] q, long[] k, long[] v, long[] output,
        int seqLen, int qEmbd, int kvDim,
        int h, int headDim, int kvHeadDim, int headsPerKv,
        int scoreShift, long sqrtHeadDim, int fracBits)
    {
        int kvHead = h / headsPerKv;
        int qOffset = h * headDim;
        int kvOffset = kvHead * kvHeadDim;

        long[] scores = new long[seqLen * seqLen];

        for (int t = 0; t < seqLen; t++)
        {
            int qBase = t * qEmbd + qOffset;
            for (int s = 0; s < seqLen; s++)
            {
                Int128 rawDot = IntegerMatMul.DotInt128(q, qBase, k, s * kvDim + kvOffset, headDim);
                scores[t * seqLen + s] = (long)(rawDot >> scoreShift) / sqrtHeadDim;
            }
        }

        for (int t = 0; t < seqLen; t++)
            IntegerTranscendentals.FixedSoftmax(scores, t * seqLen, seqLen, fracBits);

        for (int t = 0; t < seqLen; t++)
        {
            int outBase = t * qEmbd + qOffset;
            int scoreBase = t * seqLen;
            for (int s = 0; s < seqLen; s++)
            {
                long w = scores[scoreBase + s];
                int vBase = s * kvDim + kvOffset;
                for (int d = 0; d < headDim; d++)
                    output[outBase + d] += (long)(((Int128)w * v[vBase + d]) >> fracBits);
            }
        }
    }

    /// <summary>
    /// Causal GQA: identical to <see cref="GroupedQueryAttention"/> but masks
    /// future positions (score[t,s] = -∞ for s &gt; t) before softmax.
    /// </summary>
    public static long[] CausalGroupedQueryAttention(
        long[] q, long[] k, long[] v,
        int seqLen, int qEmbd, int kvDim,
        int nHeads, int nKvHeads,
        int scaleExponent,
        int fracBits = IntegerTranscendentals.DefaultFracBits)
    {
        int headDim = qEmbd / nHeads;
        int kvHeadDim = kvDim / nKvHeads;
        int headsPerKv = nHeads / nKvHeads;
        long[] output = new long[seqLen * qEmbd];

        int absScale = System.Math.Abs(scaleExponent);
        int scoreShift = 2 * absScale - fracBits;
        long sqrtHeadDim = IntegerLayerNorm.ISqrt64(headDim);
        if (sqrtHeadDim == 0) sqrtHeadDim = 1;

        long maskValue = -(1L << (fracBits - 1));

        // Parallelize across heads when there are enough to benefit.
        // Each head writes to a non-overlapping slice of the output array.
        if (nHeads >= 4)
        {
            Parallel.For(0, nHeads, h =>
            {
                CausalHeadAttention(q, k, v, output, seqLen, qEmbd, kvDim,
                    h, headDim, kvHeadDim, headsPerKv, scoreShift, sqrtHeadDim, maskValue, fracBits);
            });
        }
        else
        {
            for (int h = 0; h < nHeads; h++)
            {
                CausalHeadAttention(q, k, v, output, seqLen, qEmbd, kvDim,
                    h, headDim, kvHeadDim, headsPerKv, scoreShift, sqrtHeadDim, maskValue, fracBits);
            }
        }

        return output;
    }

    private static void CausalHeadAttention(
        long[] q, long[] k, long[] v, long[] output,
        int seqLen, int qEmbd, int kvDim,
        int h, int headDim, int kvHeadDim, int headsPerKv,
        int scoreShift, long sqrtHeadDim, long maskValue, int fracBits)
    {
        int kvHead = h / headsPerKv;
        int qOffset = h * headDim;
        int kvOffset = kvHead * kvHeadDim;

        // Thread-local scores buffer — avoids contention on shared array.
        long[] scores = new long[seqLen * seqLen];

        for (int t = 0; t < seqLen; t++)
        {
            int qBase = t * qEmbd + qOffset;
            for (int s = 0; s < seqLen; s++)
            {
                if (s > t)
                {
                    scores[t * seqLen + s] = maskValue;
                    continue;
                }
                Int128 rawDot = IntegerMatMul.DotInt128(q, qBase, k, s * kvDim + kvOffset, headDim);
                scores[t * seqLen + s] = (long)(rawDot >> scoreShift) / sqrtHeadDim;
            }
        }

        for (int t = 0; t < seqLen; t++)
            IntegerTranscendentals.FixedSoftmax(scores, t * seqLen, seqLen, fracBits);

        for (int t = 0; t < seqLen; t++)
        {
            int outBase = t * qEmbd + qOffset;
            int scoreBase = t * seqLen;
            for (int s = 0; s < seqLen; s++)
            {
                long w = scores[scoreBase + s];
                int vBase = s * kvDim + kvOffset;
                for (int d = 0; d < headDim; d++)
                    output[outBase + d] += (long)(((Int128)w * v[vBase + d]) >> fracBits);
            }
        }
    }
}
