using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

// Partial: forward pass internals (embeddings, transformer block, pooling)
public sealed partial class IntegerTransformerSource
{
    // -----------------------------------------------------------------------
    // Build embeddings: token + token_type
    // -----------------------------------------------------------------------

    private long[] BuildEmbeddings(int[] tokenIds, int seqLen)
    {
        int embd = m_nEmbd;
        long[] x = new long[seqLen * embd];

        for (int t = 0; t < seqLen; t++)
        {
            int tokenId = tokenIds[t];
            int srcBase = tokenId * embd;
            int dstBase = t * embd;

            if (tokenId < 0 || srcBase + embd > m_tokenEmbeddings.Length)
                srcBase = m_tokenizer.UnkTokenId * embd;

            for (int d = 0; d < embd; d++)
                x[dstBase + d] = m_tokenEmbeddings[srcBase + d] + m_tokenTypeEmbedding[d];
        }

        return x;
    }

    // -----------------------------------------------------------------------
    // Transformer block — mirrors TransformerEmbeddingSource.ApplyTransformerBlock
    // -----------------------------------------------------------------------

    private void ApplyTransformerBlock(long[] x, int seqLen, IntegerLayerWeights layer,
        IntegerAttention.IntegerRoPECache ropeCache)
    {
        int embd = m_nEmbd;
        int total = seqLen * embd;

        // -- Attention sub-layer --
        long[] residualAttn = new long[total];
        Array.Copy(x, residualAttn, total);

        // QKV projection
        long[] qkv = IntegerMatMul.MatMul(x, seqLen, embd, layer.AttnQkv, 3 * embd, m_scaleBits);

        long[] q = new long[total];
        long[] k = new long[total];
        long[] v = new long[total];
        IntegerAttention.SplitQkv(qkv, q, k, v, seqLen, embd);

        // RoPE
        IntegerAttention.ApplyRoPE(q, seqLen, embd, m_nHeads, ropeCache);
        IntegerAttention.ApplyRoPE(k, seqLen, embd, m_nHeads, ropeCache);

        // Multi-head attention
        long[] attnOut = IntegerAttention.MultiHeadAttention(
            q, k, v, seqLen, embd, m_nHeads, -m_scaleBits);

        // Output projection
        long[] projected = IntegerMatMul.MatMul(attnOut, seqLen, embd, layer.AttnOutput, embd, m_scaleBits);

        // Residual add + post-attn LayerNorm
        IntegerMatMul.AddInPlace(projected, residualAttn, total);
        IntegerLayerNorm.ApplyInPlace(projected, seqLen, embd, layer.AttnNormW, layer.AttnNormB, -m_scaleBits);

        // -- FFN sub-layer --
        long[] residualFfn = new long[total];
        Array.Copy(projected, residualFfn, total);

        long[] ffnOut = IntegerFFN.Apply(
            projected, seqLen, embd,
            layer.FfnGate, layer.FfnUp, layer.FfnDown,
            m_nFf, m_scaleBits);

        // Residual add + post-FFN LayerNorm
        IntegerMatMul.AddInPlace(ffnOut, residualFfn, total);
        IntegerLayerNorm.ApplyInPlace(ffnOut, seqLen, embd, layer.LayerNormW, layer.LayerNormB, -m_scaleBits);

        Array.Copy(ffnOut, x, total);
    }

    // -----------------------------------------------------------------------
    // Mean pool + L2 normalize
    // -----------------------------------------------------------------------

    private long[] MeanPool(long[] x, int seqLen)
    {
        int embd = m_nEmbd;
        long[] pooled = new long[embd];

        if (seqLen == 0) return pooled;

        // Sum all rows using Int128 to avoid overflow
        Int128[] sums = new Int128[embd];
        for (int t = 0; t < seqLen; t++)
        {
            int rowBase = t * embd;
            for (int d = 0; d < embd; d++)
                sums[d] += x[rowBase + d];
        }

        for (int d = 0; d < embd; d++)
            pooled[d] = (long)(sums[d] / seqLen);

        return pooled;
    }

    private static void L2Normalize(float[] v)
    {
        float norm = 0f;
        for (int i = 0; i < v.Length; i++)
            norm += v[i] * v[i];
        norm = MathF.Sqrt(norm);
        if (norm < 1e-10f) return;
        float invNorm = 1.0f / norm;
        for (int i = 0; i < v.Length; i++)
            v[i] *= invNorm;
    }

    // -----------------------------------------------------------------------
    // RoPE cache
    // -----------------------------------------------------------------------

    private IntegerAttention.IntegerRoPECache GetRoPECache()
    {
        if (m_ropeCache is not null) return m_ropeCache;
        int headDim = m_nEmbd / m_nHeads;
        m_ropeCache = new IntegerAttention.IntegerRoPECache(
            RopeMaxSeqLen, headDim, m_ropeFreqBase);
        return m_ropeCache;
    }
}
