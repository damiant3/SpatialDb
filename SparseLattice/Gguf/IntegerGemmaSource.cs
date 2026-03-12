using SparseLattice.Embedding;
using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

/// <summary>
/// CPU-only <see cref="IEmbeddingSource"/> running a Gemma3-family encoder forward pass
/// entirely in integer arithmetic. Supports GQA, RMS LayerNorm, separate Q/K/V,
/// and Q/K norms — the architectural differences from nomic-bert.
/// </summary>
public sealed partial class IntegerGemmaSource : IEmbeddingSource, IDisposable
{
    private readonly BpeTokenizer m_tokenizer;
    private readonly GemmaLayerWeights[] m_layers;
    private readonly long[] m_tokenEmbeddings;
    private readonly long[] m_outputNormW;
    private readonly int m_nEmbd;
    private readonly int m_nHeads;
    private readonly int m_nKvHeads;
    private readonly int m_headDim;
    private readonly int m_qDim;    // nHeads * headDim (may differ from nEmbd)
    private readonly int m_kvDim;   // nKvHeads * headDim
    private readonly int m_nFf;
    private readonly float m_ropeFreqBase;
    private readonly float m_normEps;
    private readonly int m_scaleBits;
    private IntegerAttention.IntegerRoPECache? m_ropeCache;
    private bool m_disposed;

    private const int RopeMaxSeqLen = 2048;

    public string ModelName { get; }
    public int Dimensions => m_nEmbd;
    public int ScaleBits => m_scaleBits;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ForwardFloat(text));
    }

    public async Task<float[][]> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        float[][] results = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
            results[i] = await EmbedAsync(texts[i], ct).ConfigureAwait(false);
        return results;
    }

    public void Dispose() => m_disposed = true;

    public float[] ForwardFloat(string text)
    {
        long[] intResult = Forward(text);
        double scale = System.Math.Pow(2.0, -m_scaleBits);
        float[] result = new float[m_nEmbd];
        for (int d = 0; d < m_nEmbd; d++)
            result[d] = (float)(intResult[d] * scale);

        L2Normalize(result);
        return result;
    }

    public long[] Forward(string text)
    {
        int[] tokenIds = m_tokenizer.Encode(text, addSpecialTokens: true);
        int seqLen = tokenIds.Length;

        long[] x = BuildEmbeddings(tokenIds, seqLen);

        IntegerAttention.IntegerRoPECache ropeCache = GetRoPECache();
        for (int layerIdx = 0; layerIdx < m_layers.Length; layerIdx++)
            ApplyGemmaBlock(x, seqLen, m_layers[layerIdx], ropeCache);

        IntegerLayerNorm.RmsNormInPlace(x, seqLen, m_nEmbd, m_outputNormW, -m_scaleBits);

        return MeanPool(x, seqLen);
    }
}
