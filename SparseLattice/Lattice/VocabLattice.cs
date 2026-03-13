using SparseLattice.Math;
///////////////////////////////////
namespace SparseLattice.Lattice;

// Token IDs stored as payloads; vectors are quantized output embeddings.
// KNN replaces the full [hidden × vocab] matmul for logit computation.
public sealed class VocabLattice
{
    readonly EmbeddingLattice<int> m_lattice;

    public int VocabSize { get; }
    public int Dimensions { get; }
    public int K { get; }

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

    // Half→float promotion on-the-fly to avoid expanding the full table
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
