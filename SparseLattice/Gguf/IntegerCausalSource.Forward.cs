using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

public sealed partial class IntegerCausalSource
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

        long sqrtEmbd = IntegerLayerNorm.ISqrt64(embd);
        for (int i = 0; i < x.Length; i++)
            x[i] = (long)((Int128)x[i] * sqrtEmbd);

        return x;
    }

    private void ApplyCausalBlock(long[] x, int seqLen, IntegerGemmaSource.GemmaLayerWeights layer,
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

        long[] attnOut = IntegerAttention.CausalGroupedQueryAttention(
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

    private void ApplyRoPEGqa(long[] q, long[] k, int seqLen,
        IntegerAttention.IntegerRoPECache cache)
    {
        IntegerAttention.ApplyRoPE(q, seqLen, m_nEmbd, m_nHeads, cache);
        IntegerAttention.ApplyRoPE(k, seqLen, m_kvDim, m_nKvHeads, cache);
    }

    private static void RmsNormPerHead(long[] x, int seqLen, int nHeads, int headDim,
        long[] weight, int scaleExponent)
    {
        int totalDim = nHeads * headDim;
        for (int t = 0; t < seqLen; t++)
            for (int h = 0; h < nHeads; h++)
                IntegerLayerNorm.RmsNormRow(x, t * totalDim + h * headDim, headDim, weight, scaleExponent);
    }

    private IntegerAttention.IntegerRoPECache GetRoPECache()
    {
        if (m_ropeCache is not null) return m_ropeCache;
        m_ropeCache = new IntegerAttention.IntegerRoPECache(
            RopeMaxSeqLen, m_headDim, m_ropeFreqBase);
        return m_ropeCache;
    }
}
