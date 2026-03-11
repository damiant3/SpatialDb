///////////////////////////////////////////////
namespace SparseLattice.Math;

/// <summary>
/// Integer multi-head self-attention and feed-forward network.
///
/// <para>
/// These implement the two sub-layers of a transformer block in exact integer
/// arithmetic, matching the structure in <c>TransformerEmbeddingSource.ApplyTransformerBlock</c>:
/// </para>
///
/// <list type="number">
/// <item>QKV projection → split → RoPE → attention scores → softmax → output projection</item>
/// <item>SiLU-gated FFN: gate and up projections, element-wise SiLU gate, down projection</item>
/// </list>
///
/// <para>
/// All matmuls use <see cref="IntegerMatMul"/> with <see cref="Int128"/> accumulators.
/// Softmax and SiLU use <see cref="IntegerTranscendentals"/> fixed-point Taylor series.
/// RoPE sin/cos tables are precomputed at load time as fixed-point <c>long</c> values.
/// </para>
/// </summary>
public static class IntegerAttention
{
    // -----------------------------------------------------------------------
    // RoPE: precomputed sin/cos tables in integer domain
    // -----------------------------------------------------------------------

    /// <summary>
    /// Precomputed rotary position embedding tables.
    /// Cos[t * halfDim + i] and Sin[t * halfDim + i] are the cos/sin values
    /// for position t, rotation index i, in fixed-point with <see cref="FracBits"/> fractional bits.
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

    /// <summary>
    /// Applies RoPE in-place to Q or K vectors.
    /// <paramref name="x"/> is [seqLen × embd], organized as [seqLen × nHeads × headDim].
    /// The rotation operates on pairs (x[..i], x[..i+halfDim]) using precomputed cos/sin.
    /// </summary>
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
                    // Rotation: x0' = x0*cos - x1*sin, x1' = x0*sin + x1*cos
                    // Use Int128 for the products, shift back by fracBits
                    x[headBase + i]           = (long)(((Int128)x0 * cos - (Int128)x1 * sin) >> fracBits);
                    x[headBase + i + halfDim] = (long)(((Int128)x0 * sin + (Int128)x1 * cos) >> fracBits);
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Multi-head self-attention
    // -----------------------------------------------------------------------

    /// <summary>
    /// Computes multi-head self-attention in integer arithmetic.
    ///
    /// <para>
    /// Q, K, V are [seqLen × embd]. Scores use <see cref="IntegerMatMul.DotInt128"/>
    /// for exact dot products. Softmax uses <see cref="IntegerTranscendentals.FixedSoftmax"/>.
    /// The weighted sum of V uses Int128 products shifted back to the working scale.
    /// </para>
    /// </summary>
    /// <param name="q">Query vectors [seqLen × embd].</param>
    /// <param name="k">Key vectors [seqLen × embd].</param>
    /// <param name="v">Value vectors [seqLen × embd].</param>
    /// <param name="seqLen">Number of tokens.</param>
    /// <param name="embd">Embedding dimension.</param>
    /// <param name="nHeads">Number of attention heads.</param>
    /// <param name="scaleExponent">Scale exponent of Q, K, V activations.</param>
    /// <param name="fracBits">Fixed-point precision for softmax.</param>
    /// <returns>Attention output [seqLen × embd] at the same scale as input.</returns>
    public static long[] MultiHeadAttention(
        long[] q, long[] k, long[] v,
        int seqLen, int embd, int nHeads,
        int scaleExponent,
        int fracBits = IntegerTranscendentals.DefaultFracBits)
    {
        int headDim = embd / nHeads;
        long[] output = new long[seqLen * embd];
        long[] scores = new long[seqLen * seqLen]; // reuse across heads

        // Scale factor: 1/sqrt(headDim) — absorbed into the score computation.
        // dot(q,k) produces values at scale 2*scaleExponent (product of two scaled values).
        // We need scores in fixed-point for softmax (at fracBits scale).
        // Target: score = dot(q,k) / sqrt(headDim) in fixed-point.
        //
        // Strategy: compute raw Int128 dot, then shift to fracBits scale:
        //   rawDot is at scale 2*|scaleExponent| (e.g. 2*30 = 60 bits)
        //   We want scores at fracBits (30 bits)
        //   So shift right by (2*|scaleExponent| - fracBits) = 30 for default
        //   Then divide by isqrt(headDim) for the 1/sqrt(headDim) scaling
        int absScale = System.Math.Abs(scaleExponent);
        int scoreShift = 2 * absScale - fracBits;
        long sqrtHeadDim = IntegerLayerNorm.ISqrt64(headDim);
        if (sqrtHeadDim == 0) sqrtHeadDim = 1;

        for (int h = 0; h < nHeads; h++)
        {
            int headOffset = h * headDim;

            // Compute attention scores
            for (int t = 0; t < seqLen; t++)
            {
                int qBase = t * embd + headOffset;
                for (int s = 0; s < seqLen; s++)
                {
                    Int128 rawDot = IntegerMatMul.DotInt128(q, qBase, k, s * embd + headOffset, headDim);
                    // Shift to fracBits scale and divide by sqrt(headDim)
                    long score = (long)(rawDot >> scoreShift) / sqrtHeadDim;
                    scores[t * seqLen + s] = score;
                }
            }

            // Softmax each row
            for (int t = 0; t < seqLen; t++)
                IntegerTranscendentals.FixedSoftmax(scores, t * seqLen, seqLen, fracBits);

            // Weighted sum: output[t,h] = sum_s scores[t,s] * v[s,h]
            // scores are at fracBits scale (probabilities), v is at scaleExponent scale.
            // product is at (fracBits + |scaleExponent|) bits → shift right by fracBits
            // to get output at scaleExponent scale.
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

    // -----------------------------------------------------------------------
    // QKV split
    // -----------------------------------------------------------------------

    /// <summary>
    /// Splits fused QKV tensor [seqLen × 3*embd] into separate Q, K, V [seqLen × embd].
    /// </summary>
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
}

/// <summary>
/// Integer SiLU-gated feed-forward network (E4-4).
///
/// <para>
/// Matches the nomic-bert FFN structure:
/// <code>
/// gate = matmul(x, W_gate)
/// up   = matmul(x, W_up)
/// y    = silu(gate) * up      // element-wise
/// down = matmul(y, W_down)
/// </code>
/// </para>
/// </summary>
public static class IntegerFFN
{
    /// <summary>
    /// Applies the SiLU-gated FFN in integer arithmetic.
    /// </summary>
    /// <param name="x">Input activations [seqLen × embd].</param>
    /// <param name="seqLen">Sequence length.</param>
    /// <param name="embd">Input/output embedding dimension.</param>
    /// <param name="wGate">Gate weight [embd × nFf] column-major.</param>
    /// <param name="wUp">Up weight [embd × nFf] column-major.</param>
    /// <param name="wDown">Down weight [nFf × embd] column-major.</param>
    /// <param name="nFf">FFN intermediate dimension.</param>
    /// <param name="matmulShift">Right-shift applied after each matmul to stay in long range.</param>
    /// <param name="fracBits">Fixed-point precision for SiLU computation.</param>
    /// <returns>FFN output [seqLen × embd] at the shifted scale.</returns>
    public static long[] Apply(
        long[] x, int seqLen, int embd,
        long[] wGate, long[] wUp, long[] wDown,
        int nFf,
        int matmulShift,
        int fracBits = IntegerTranscendentals.DefaultFracBits)
    {
        // gate = matmul(x, W_gate) → [seqLen × nFf]
        long[] gate = IntegerMatMul.MatMul(x, seqLen, embd, wGate, nFf, matmulShift);

        // up = matmul(x, W_up) → [seqLen × nFf]
        long[] up = IntegerMatMul.MatMul(x, seqLen, embd, wUp, nFf, matmulShift);

        // y[i] = silu(gate[i]) * up[i]
        // silu(gate) is at the same scale as gate. Multiply by up → double scale.
        // Shift right by fracBits to get back to single scale.
        int total = seqLen * nFf;
        for (int i = 0; i < total; i++)
        {
            long siluGate = IntegerTranscendentals.FixedSiLU(gate[i], fracBits);
            up[i] = (long)(((Int128)siluGate * up[i]) >> fracBits);
        }

        // down = matmul(y, W_down) → [seqLen × embd]
        long[] down = IntegerMatMul.MatMul(up, seqLen, nFf, wDown, embd, matmulShift);

        return down;
    }
}
