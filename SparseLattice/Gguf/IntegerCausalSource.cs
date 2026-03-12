using SparseLattice.Lattice;
using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

/// <summary>
/// CPU-only causal (autoregressive) transformer source running a Gemma3-family
/// forward pass entirely in integer arithmetic. Produces next-token logits and
/// supports greedy text generation with optional <see cref="VocabLattice"/>
/// acceleration for the output scoring step.
/// </summary>
public sealed partial class IntegerCausalSource : IDisposable
{
    private readonly BpeTokenizer m_tokenizer;
    private readonly IntegerGemmaSource.GemmaLayerWeights[] m_layers;
    private readonly long[] m_tokenEmbeddings;
    private readonly float[] m_tokenEmbeddingsFloat;
    private readonly long[] m_outputNormW;
    private readonly int m_nEmbd;
    private readonly int m_nHeads;
    private readonly int m_nKvHeads;
    private readonly int m_headDim;
    private readonly int m_kvDim;
    private readonly int m_nFf;
    private readonly int m_vocabSize;
    private readonly float m_ropeFreqBase;
    private readonly float m_normEps;
    private readonly int m_scaleBits;
    private IntegerAttention.IntegerRoPECache? m_ropeCache;
    private VocabLattice? m_vocabLattice;
    private bool m_disposed;

    private const int RopeMaxSeqLen = 2048;

    public string ModelName { get; }
    public int Dimensions => m_nEmbd;
    public int VocabSize => m_vocabSize;
    public int ScaleBits => m_scaleBits;
    public BpeTokenizer Tokenizer => m_tokenizer;

    /// <summary>
    /// Gets or lazily builds the <see cref="VocabLattice"/> from the output embedding table.
    /// The lattice enables KNN-accelerated token prediction.
    /// </summary>
    public VocabLattice GetVocabLattice(int k = 64)
    {
        if (m_vocabLattice is not null) return m_vocabLattice;
        m_vocabLattice = new VocabLattice(m_tokenEmbeddingsFloat, m_vocabSize, m_nEmbd, k);
        return m_vocabLattice;
    }

    /// <summary>
    /// Generates text autoregressively from the given prompt using greedy decoding.
    /// Uses lattice-accelerated output scoring when <paramref name="useLattice"/> is true.
    /// </summary>
    /// <param name="prompt">Input text prompt.</param>
    /// <param name="maxNewTokens">Maximum number of tokens to generate.</param>
    /// <param name="useLattice">
    /// When true, uses <see cref="VocabLattice"/> KNN to find top-K candidates
    /// and scores only those, instead of full vocabulary brute force.
    /// </param>
    /// <param name="latticeK">Number of KNN candidates when using lattice acceleration.</param>
    public string Generate(string prompt, int maxNewTokens = 64, bool useLattice = false, int latticeK = 64)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        int[] promptTokens = m_tokenizer.Encode(prompt, addSpecialTokens: true);
        List<int> generated = [.. promptTokens];

        VocabLattice? lattice = useLattice ? GetVocabLattice(latticeK) : null;

        for (int step = 0; step < maxNewTokens; step++)
        {
            float[] lastHidden = ForwardCausalFloat([.. generated]);
            int nextToken = PredictNextToken(lastHidden, lattice, latticeK);

            if (nextToken == m_tokenizer.EosTokenId)
                break;

            generated.Add(nextToken);
        }

        int[] outputTokens = generated.Skip(promptTokens.Length).ToArray();
        return m_tokenizer.Decode(outputTokens);
    }

    /// <summary>
    /// Runs the causal forward pass and returns the last position's hidden state
    /// as a dequantized float vector (pre-logit).
    /// </summary>
    public float[] ForwardCausalFloat(int[] tokenIds)
    {
        long[] lastHidden = ForwardCausal(tokenIds);
        double scale = System.Math.Pow(2.0, -m_scaleBits);
        float[] result = new float[m_nEmbd];
        for (int d = 0; d < m_nEmbd; d++)
            result[d] = (float)(lastHidden[d] * scale);
        return result;
    }

    /// <summary>
    /// Runs the full causal forward pass returning the last position's hidden state
    /// as raw long[] at working scale.
    /// </summary>
    public long[] ForwardCausal(int[] tokenIds)
    {
        int seqLen = tokenIds.Length;
        long[] x = BuildEmbeddings(tokenIds, seqLen);

        IntegerAttention.IntegerRoPECache ropeCache = GetRoPECache();
        for (int layerIdx = 0; layerIdx < m_layers.Length; layerIdx++)
            ApplyCausalBlock(x, seqLen, m_layers[layerIdx], ropeCache);

        IntegerLayerNorm.RmsNormInPlace(x, seqLen, m_nEmbd, m_outputNormW, -m_scaleBits);

        long[] lastPosition = new long[m_nEmbd];
        Array.Copy(x, (seqLen - 1) * m_nEmbd, lastPosition, 0, m_nEmbd);
        return lastPosition;
    }

    private int PredictNextToken(float[] hiddenState, VocabLattice? lattice, int latticeK)
    {
        if (lattice is not null)
        {
            int[] candidates = lattice.QueryTopK(hiddenState, latticeK);
            (int TokenId, float Score)[] scored =
                VocabLattice.ScoreCandidates(hiddenState, candidates, m_tokenEmbeddingsFloat, m_nEmbd);
            return scored.Length > 0 ? scored[0].TokenId : m_tokenizer.EosTokenId;
        }

        return VocabLattice.ArgmaxBruteForce(hiddenState, m_tokenEmbeddingsFloat, m_vocabSize, m_nEmbd);
    }

    public void Dispose() => m_disposed = true;
}
