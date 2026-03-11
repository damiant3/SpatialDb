using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

public sealed partial class IntegerGemmaSource
{
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

            Array.Copy(m_tokenEmbeddings, srcBase, x, dstBase, embd);
        }

        // Gemma scaling: embeddings are multiplied by sqrt(embd)
        long sqrtEmbd = IntegerLayerNorm.ISqrt64(embd);
        for (int i = 0; i < x.Length; i++)
            x[i] = (long)((Int128)x[i] * sqrtEmbd);

        return x;
    }

    private void ApplyGemmaBlock(long[] x, int seqLen, GemmaLayerWeights layer,
        IntegerAttention.IntegerRoPECache ropeCache)
    {
        int embd = m_nEmbd;
        int total = seqLen * embd;

        long[] residual = new long[total];
        Array.Copy(x, residual, total);

        IntegerLayerNorm.RmsNormInPlace(x, seqLen, embd, layer.AttnNormW, -m_scaleBits);

        long[] q = IntegerMatMul.MatMul(x, seqLen, embd, layer.AttnQ, embd, m_scaleBits);
        long[] k = IntegerMatMul.MatMul(x, seqLen, embd, layer.AttnK, m_kvDim, m_scaleBits);
        long[] v = IntegerMatMul.MatMul(x, seqLen, embd, layer.AttnV, m_kvDim, m_scaleBits);

        RmsNormPerHead(q, seqLen, m_nHeads, m_headDim, layer.AttnQNormW, -m_scaleBits);
        RmsNormPerHead(k, seqLen, m_nKvHeads, m_headDim, layer.AttnKNormW, -m_scaleBits);

        ApplyRoPEGqa(q, k, seqLen, ropeCache);

        long[] attnOut = IntegerAttention.GroupedQueryAttention(
            q, k, v, seqLen, embd, m_kvDim, m_nHeads, m_nKvHeads, -m_scaleBits);

        long[] projected = IntegerMatMul.MatMul(attnOut, seqLen, embd, layer.AttnOutput, embd, m_scaleBits);

        IntegerLayerNorm.RmsNormInPlace(projected, seqLen, embd, layer.PostAttnNormW, -m_scaleBits);

        IntegerMatMul.AddInPlace(projected, residual, total);

        Array.Copy(projected, residual, total);

        IntegerLayerNorm.RmsNormInPlace(projected, seqLen, embd, layer.FfnNormW, -m_scaleBits);

        long[] ffnOut = IntegerFFN.Apply(
            projected, seqLen, embd,
            layer.FfnGate, layer.FfnUp, layer.FfnDown,
            m_nFf, m_scaleBits);

        IntegerLayerNorm.RmsNormInPlace(ffnOut, seqLen, embd, layer.PostFfwNormW, -m_scaleBits);

        IntegerMatMul.AddInPlace(ffnOut, residual, total);
        Array.Copy(ffnOut, x, total);
    }

    /// <summary>
    /// Applies RoPE to Q [seqLen × embd] with nHeads and K [seqLen × kvDim] with nKvHeads.
    /// </summary>
    private void ApplyRoPEGqa(long[] q, long[] k, int seqLen,
        IntegerAttention.IntegerRoPECache cache)
    {
        IntegerAttention.ApplyRoPE(q, seqLen, m_nEmbd, m_nHeads, cache);
        IntegerAttention.ApplyRoPE(k, seqLen, m_kvDim, m_nKvHeads, cache);
    }

    /// <summary>Applies RMS norm per attention head.</summary>
    private static void RmsNormPerHead(long[] x, int seqLen, int nHeads, int headDim,
        long[] weight, int scaleExponent)
    {
        int totalDim = nHeads * headDim;
        for (int t = 0; t < seqLen; t++)
            for (int h = 0; h < nHeads; h++)
                IntegerLayerNorm.RmsNormRow(x, t * totalDim + h * headDim, headDim, weight, scaleExponent);
    }

    private long[] MeanPool(long[] x, int seqLen)
    {
        int embd = m_nEmbd;
        long[] pooled = new long[embd];
        if (seqLen == 0) return pooled;

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

    private IntegerAttention.IntegerRoPECache GetRoPECache()
    {
        if (m_ropeCache is not null) return m_ropeCache;
        m_ropeCache = new IntegerAttention.IntegerRoPECache(
            RopeMaxSeqLen, m_headDim, m_ropeFreqBase);
        return m_ropeCache;
    }
}
