using SparseLattice.Embedding;
using SparseLattice.Gguf;
using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

/// <summary>
/// CPU-only <see cref="IEmbeddingSource"/> that runs a BERT-family encoder forward
/// pass entirely in integer arithmetic. No floating-point computation on any hot path.
///
/// <para>
/// This is the E4-5 integration of the integer math kernel stack:
/// <see cref="IntegerMatMul"/>, <see cref="IntegerLayerNorm"/>,
/// <see cref="IntegerAttention"/>, <see cref="IntegerFFN"/>,
/// and <see cref="IntegerTranscendentals"/>.
/// </para>
///
/// <para>
/// Weight quantization happens once at load time (float → long at 2^30 scale).
/// All subsequent computation is exact integer arithmetic with Int128 accumulators.
/// The only sources of approximation are:
/// <list type="bullet">
/// <item>Initial quantization of weights (30-bit fixed-point, ~9 decimal digits)</item>
/// <item>Integer square root in LayerNorm (floor, ±1 ULP)</item>
/// <item>Taylor series in softmax/SiLU (14 terms, ~9 digits)</item>
/// <item>Right-shift truncation between layers (deterministic floor)</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class IntegerTransformerSource : IEmbeddingSource, IDisposable
{
    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------

    private readonly WordPieceTokenizer m_tokenizer;
    private readonly IntegerLayerWeights[] m_layers;

    // Embedding tables (quantized)
    private readonly long[] m_tokenEmbeddings;    // [vocab_size * n_embd]
    private readonly long[] m_tokenTypeEmbedding; // [n_embd]

    // Input embedding LayerNorm
    private readonly long[] m_embdNormW;
    private readonly long[] m_embdNormB;

    private readonly int m_nEmbd;
    private readonly int m_nHeads;
    private readonly int m_nFf;
    private readonly float m_ropeFreqBase;
    private readonly int m_scaleBits;

    private IntegerAttention.IntegerRoPECache? m_ropeCache;
    private const int RopeMaxSeqLen = 512;

    private bool m_disposed;

    // -----------------------------------------------------------------------
    // Public properties
    // -----------------------------------------------------------------------

    public string ModelName { get; }
    public int Dimensions => m_nEmbd;
    public int ScaleBits => m_scaleBits;

    // -----------------------------------------------------------------------
    // IEmbeddingSource
    // -----------------------------------------------------------------------

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

    // -----------------------------------------------------------------------
    // Forward pass — returns float[] for IEmbeddingSource compatibility
    // -----------------------------------------------------------------------

    /// <summary>
    /// Runs the full integer forward pass and dequantizes the result to <c>float[]</c>.
    /// This is the <see cref="IEmbeddingSource"/> path.
    /// </summary>
    public float[] ForwardFloat(string text)
    {
        long[] intResult = Forward(text);

        // Dequantize to float and L2-normalize
        double scale = System.Math.Pow(2.0, -m_scaleBits);
        float[] result = new float[m_nEmbd];
        for (int d = 0; d < m_nEmbd; d++)
            result[d] = (float)(intResult[d] * scale);

        L2Normalize(result);
        return result;
    }

    /// <summary>
    /// Runs the full integer forward pass. Returns raw integer embedding
    /// at the working scale (not normalized — caller decides).
    /// </summary>
    public long[] Forward(string text)
    {
        int[] tokenIds = m_tokenizer.Encode(text, addSpecialTokens: true);
        int seqLen = tokenIds.Length;
        int embd = m_nEmbd;

        // 1. Build embeddings: token + token_type
        long[] x = BuildEmbeddings(tokenIds, seqLen);

        // 2. Input LayerNorm
        IntegerLayerNorm.ApplyInPlace(x, seqLen, embd, m_embdNormW, m_embdNormB, -m_scaleBits);

        // 3. Transformer layers
        IntegerAttention.IntegerRoPECache ropeCache = GetRoPECache();
        for (int layerIdx = 0; layerIdx < m_layers.Length; layerIdx++)
            ApplyTransformerBlock(x, seqLen, m_layers[layerIdx], ropeCache);

        // 4. Mean pool
        long[] pooled = MeanPool(x, seqLen);

        return pooled;
    }
}
