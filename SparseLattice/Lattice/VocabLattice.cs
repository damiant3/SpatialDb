using SparseLattice.Math;
///////////////////////////////////
namespace SparseLattice.Lattice;

/// <summary>
/// Wraps an <see cref="EmbeddingLattice{TPayload}"/> built from the model's output
/// embedding table. Each occupant's payload is the token ID, and the vector is the
/// quantized output embedding for that token.
/// Instead of computing a full [hidden × vocab] matmul for logits, callers query
/// KNN to find the top-K candidate tokens and score only those.
/// </summary>
public sealed class VocabLattice
{
    private readonly EmbeddingLattice<int> m_lattice;

    public int VocabSize { get; }
    public int Dimensions { get; }
    public int K { get; }

    /// <summary>
    /// Builds a <see cref="VocabLattice"/> from a flat output embedding table.
    /// </summary>
    /// <param name="outputEmbeddings">
    /// Row-major float array [vocabSize × dims] — the output (unembedding) weight matrix.
    /// </param>
    /// <param name="vocabSize">Number of tokens in the vocabulary.</param>
    /// <param name="dims">Embedding dimension per token.</param>
    /// <param name="k">Default K for KNN queries.</param>
    /// <param name="quantizationOptions">Options for float→sparse quantization.</param>
    public VocabLattice(float[] outputEmbeddings, int vocabSize, int dims,
        int k = 32, QuantizationOptions? quantizationOptions = null)
    {
        VocabSize = vocabSize;
        Dimensions = dims;
        K = k;

        QuantizationOptions options = quantizationOptions ?? new QuantizationOptions
        {
            ZeroThreshold = 0.001f,
            GlobalScale = 1_000_000_000L,
            SparsityBudget = null,
        };

        SparseOccupant<int>[] occupants = new SparseOccupant<int>[vocabSize];
        for (int tokenId = 0; tokenId < vocabSize; tokenId++)
        {
            float[] row = new float[dims];
            Array.Copy(outputEmbeddings, tokenId * dims, row, 0, dims);
            SparseVector sv = EmbeddingAdapter.Quantize(row, options);
            occupants[tokenId] = new SparseOccupant<int>(sv, tokenId);
        }

        m_lattice = new EmbeddingLattice<int>(occupants);
        m_lattice.Freeze();
    }

    /// <summary>
    /// Finds the K nearest vocabulary tokens to the given hidden state.
    /// Returns token IDs sorted by ascending L2 distance.
    /// </summary>
    /// <param name="hiddenState">Dense float hidden state [dims].</param>
    /// <param name="k">Number of candidates to return. Uses default K if null.</param>
    /// <param name="quantizationOptions">Options for quantizing the query vector.</param>
    public int[] QueryTopK(float[] hiddenState, int? k = null, QuantizationOptions? quantizationOptions = null)
    {
        QuantizationOptions options = quantizationOptions ?? new QuantizationOptions
        {
            ZeroThreshold = 0.001f,
            GlobalScale = 1_000_000_000L,
            SparsityBudget = null,
        };

        SparseVector query = EmbeddingAdapter.Quantize(hiddenState, options);
        int effectiveK = k ?? K;
        List<SparseOccupant<int>> results = m_lattice.QueryKNearestL2(query, effectiveK);
        int[] tokenIds = new int[results.Count];
        for (int i = 0; i < results.Count; i++)
            tokenIds[i] = results[i].Payload;
        return tokenIds;
    }

    /// <summary>
    /// Scores a set of candidate token IDs against the hidden state using exact
    /// dot products. Returns (tokenId, score) pairs sorted descending by score.
    /// </summary>
    /// <param name="hiddenState">Dense float hidden state [dims].</param>
    /// <param name="candidateTokenIds">Token IDs to score.</param>
    /// <param name="outputEmbeddings">Flat [vocabSize × dims] output embedding table.</param>
    public static (int TokenId, float Score)[] ScoreCandidates(
        float[] hiddenState, int[] candidateTokenIds, float[] outputEmbeddings, int dims)
    {
        (int TokenId, float Score)[] scored = new (int, float)[candidateTokenIds.Length];
        for (int i = 0; i < candidateTokenIds.Length; i++)
        {
            int tokenId = candidateTokenIds[i];
            int rowBase = tokenId * dims;
            float dot = 0f;
            for (int d = 0; d < dims; d++)
                dot += hiddenState[d] * outputEmbeddings[rowBase + d];
            scored[i] = (tokenId, dot);
        }

        Array.Sort(scored, (a, b) => b.Score.CompareTo(a.Score));
        return scored;
    }

    /// <summary>
    /// Full brute-force logits for validation: dot product of hidden state against
    /// every vocabulary token embedding. Returns token ID of the argmax.
    /// </summary>
    public static int ArgmaxBruteForce(float[] hiddenState, float[] outputEmbeddings, int vocabSize, int dims)
    {
        int bestId = 0;
        float bestScore = float.NegativeInfinity;

        int maxVocab = outputEmbeddings.Length / dims;
        if (vocabSize > maxVocab)
            vocabSize = maxVocab;

        for (int tokenId = 0; tokenId < vocabSize; tokenId++)
        {
            int rowBase = tokenId * dims;
            float dot = 0f;
            for (int d = 0; d < dims; d++)
                dot += hiddenState[d] * outputEmbeddings[rowBase + d];
            if (dot > bestScore)
            {
                bestScore = dot;
                bestId = tokenId;
            }
        }
        return bestId;
    }

    /// <summary>
    /// Half-precision overload: avoids expanding the full embedding table to float[].
    /// Each Half is promoted to float on-the-fly for the dot product.
    /// </summary>
    public static int ArgmaxBruteForce(float[] hiddenState, Half[] outputEmbeddings, int vocabSize, int dims)
    {
        int bestId = 0;
        float bestScore = float.NegativeInfinity;

        // Clamp vocabSize to actual embedding table size to avoid OOB
        int maxVocab = outputEmbeddings.Length / dims;
        if (vocabSize > maxVocab)
            vocabSize = maxVocab;

        for (int tokenId = 0; tokenId < vocabSize; tokenId++)
        {
            int rowBase = tokenId * dims;
            float dot = 0f;
            for (int d = 0; d < dims; d++)
                dot += hiddenState[d] * (float)outputEmbeddings[rowBase + d];
            if (dot > bestScore)
            {
                bestScore = dot;
                bestId = tokenId;
            }
        }
        return bestId;
    }
}
