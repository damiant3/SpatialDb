using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

public sealed partial class IntegerCausalSource
{
    private long[] BuildEmbeddings(int[] tokenIds, int seqLen)
    {
        int embd = m_nEmbd;
        long[] x = new long[seqLen * embd];
        double quantScale = System.Math.Pow(2.0, m_scaleBits);

        for (int t = 0; t < seqLen; t++)
        {
            int tokenId = tokenIds[t];
            int srcBase = tokenId * embd;
            int dstBase = t * embd;

            if (tokenId < 0 || srcBase + embd > m_tokenEmbeddingsHalf.Length)
                srcBase = m_tokenizer.UnkTokenId * embd;

            for (int d = 0; d < embd; d++)
                x[dstBase + d] = (long)((float)m_tokenEmbeddingsHalf[srcBase + d] * quantScale);
        }

        long sqrtEmbd = IntegerLayerNorm.ISqrt64(embd);
        for (int i = 0; i < x.Length; i++)
            x[i] = (long)((Int128)x[i] * sqrtEmbd);

        return x;
    }

    /// <summary>
    /// Returns true if this layer should use global (full) attention.
    /// In Gemma3, every 6th layer (indices 5, 11, 17, ...) is global.
    /// Layers not on that interval use local (sliding window) attention.
    /// </summary>
    private bool IsGlobalLayer(int layerIdx)
    {
        if (m_globalLayerInterval <= 0) return true; // no sliding window → all global
        // Gemma3 convention: layer indices (5, 11, 17, 23, ...) = (interval-1) mod interval
        return ((layerIdx + 1) % m_globalLayerInterval) == 0;
    }

    private void ApplyCausalBlock(long[] x, int seqLen, IntegerGemmaSource.GemmaLayerWeights layer,
        IntegerAttention.IntegerRoPECache ropeCache, bool isGlobal)
    {
        int embd = m_nEmbd;
        int qDim = m_qDim;   // nHeads * headDim — may differ from embd
        int total = seqLen * embd;

        long[] residual = new long[total];
        Array.Copy(x, residual, total);

        IntegerLayerNorm.RmsNormInPlace(x, seqLen, embd, layer.AttnNormW, -m_scaleBits);

        // Q, K, V projections are independent — parallelize for large models.
        // Q output dim is qDim (nHeads*headDim), not embd.
        long[] q = null!, k = null!, v = null!;
        if (embd >= 1024)
        {
            Parallel.Invoke(
                () => q = IntegerMatMul.MatMul(x, seqLen, embd, layer.AttnQ, qDim, m_scaleBits, m_scaleBits),
                () => k = IntegerMatMul.MatMul(x, seqLen, embd, layer.AttnK, m_kvDim, m_scaleBits, m_scaleBits),
                () => v = IntegerMatMul.MatMul(x, seqLen, embd, layer.AttnV, m_kvDim, m_scaleBits, m_scaleBits));
        }
        else
        {
            q = IntegerMatMul.MatMul(x, seqLen, embd, layer.AttnQ, qDim, m_scaleBits, m_scaleBits);
            k = IntegerMatMul.MatMul(x, seqLen, embd, layer.AttnK, m_kvDim, m_scaleBits, m_scaleBits);
            v = IntegerMatMul.MatMul(x, seqLen, embd, layer.AttnV, m_kvDim, m_scaleBits, m_scaleBits);
        }

        RmsNormPerHead(q, seqLen, m_nHeads, m_headDim, layer.AttnQNormW, -m_scaleBits);
        RmsNormPerHead(k, seqLen, m_nKvHeads, m_headDim, layer.AttnKNormW, -m_scaleBits);

        ApplyRoPEGqa(q, k, seqLen, ropeCache);

        // Global layers: full causal attention (attend to all previous positions).
        // Local layers: sliding window causal attention (attend to at most m_slidingWindow positions back).
        long[] attnOut;
        if (isGlobal || m_slidingWindow <= 0)
        {
            attnOut = IntegerAttention.CausalGroupedQueryAttention(
                q, k, v, seqLen, qDim, m_kvDim, m_nHeads, m_nKvHeads, -m_scaleBits);
        }
        else
        {
            attnOut = IntegerAttention.SlidingWindowCausalGQA(
                q, k, v, seqLen, qDim, m_kvDim, m_nHeads, m_nKvHeads, -m_scaleBits,
                m_slidingWindow);
        }

        // Output projection: [seqLen, qDim] → [seqLen, embd]
        long[] projected = IntegerMatMul.MatMul(attnOut, seqLen, qDim, layer.AttnOutput, embd, m_scaleBits, m_scaleBits);

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
        IntegerAttention.ApplyRoPE(q, seqLen, m_qDim, m_nHeads, cache);
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

    private IntegerAttention.IntegerRoPECache GetRoPECacheLocal()
    {
        if (m_ropeCacheLocal is not null) return m_ropeCacheLocal;
        m_ropeCacheLocal = new IntegerAttention.IntegerRoPECache(
            RopeMaxSeqLen, m_headDim, m_ropeFreqBase);
        return m_ropeCacheLocal;
    }

    private IntegerAttention.IntegerRoPECache GetRoPECacheGlobal()
    {
        if (m_ropeCacheGlobal is not null) return m_ropeCacheGlobal;
        m_ropeCacheGlobal = new IntegerAttention.IntegerRoPECache(
            RopeMaxSeqLen, m_headDim, m_ropeFreqBaseGlobal);
        return m_ropeCacheGlobal;
    }
}
