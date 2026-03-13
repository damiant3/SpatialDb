using SparseLattice.Gguf;
using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Embedding;

public sealed class LatticeEmbeddingSource : IEmbeddingSource, IDisposable
{
    readonly WordPieceTokenizer m_tokenizer;
    readonly SparseVector[] m_tokenVectors;
    readonly int m_embeddingDimensions;
    readonly long m_scale;
    readonly int m_outputSparsityBudget;
    bool m_disposed;

    public string ModelName { get; }
    public int Dimensions => m_embeddingDimensions;

    public static LatticeEmbeddingSource Load(
        string ggufPath,
        QuantizationOptions? quantizationOptions = null,
        Action<int, int, string>? onProgress = null)
    {
        using GgufReader reader = GgufReader.Open(ggufPath);
        return LoadFromReader(reader, quantizationOptions, onProgress);
    }

    public static LatticeEmbeddingSource LoadFromModelDir(
        string modelName,
        string modelDir,
        QuantizationOptions? quantizationOptions = null,
        Action<int, int, string>? onProgress = null)
    {
        string? ggufPath = OllamaModelLocator.LocateGguf(modelName, modelDir);
        if (ggufPath is null)
            throw new FileNotFoundException(
                $"No GGUF blob found for model '{modelName}' in '{modelDir}'.");
        return Load(ggufPath, quantizationOptions, onProgress);
    }

    LatticeEmbeddingSource(
        string modelName,
        WordPieceTokenizer tokenizer,
        SparseVector[] tokenVectors,
        int embeddingDimensions,
        long scale,
        int outputSparsityBudget)
    {
        ModelName = modelName;
        m_tokenizer = tokenizer;
        m_tokenVectors = tokenVectors;
        m_embeddingDimensions = embeddingDimensions;
        m_scale = scale;
        m_outputSparsityBudget = outputSparsityBudget;
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ct.ThrowIfCancellationRequested();
        SparseVector sparse = EmbedSparse(text);
        float[] dense = new float[m_embeddingDimensions];
        foreach (SparseEntry entry in sparse.Entries)
            dense[entry.Dimension] = (float)(entry.Value / (double)m_scale);
        return Task.FromResult(dense);
    }

    public async Task<float[][]> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        float[][] results = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
            results[i] = await EmbedAsync(texts[i], ct).ConfigureAwait(false);
        return results;
    }

    public SparseVector EmbedSparse(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new SparseVector([new SparseEntry(0, 1L)], m_embeddingDimensions);

        int[] tokenIds = m_tokenizer.Encode(text, addSpecialTokens: true);
        if (tokenIds.Length == 0)
            return new SparseVector([new SparseEntry(0, 1L)], m_embeddingDimensions);

        return PoolTokenVectors(tokenIds);
    }

    public SparseVector[] EmbedSparseBatch(IReadOnlyList<string> texts)
    {
        SparseVector[] results = new SparseVector[texts.Count];
        for (int i = 0; i < texts.Count; i++)
            results[i] = EmbedSparse(texts[i]);
        return results;
    }

    public void Dispose() => m_disposed = true;

    SparseVector PoolTokenVectors(int[] tokenIds)
    {
        long[] accumulator = System.Buffers.ArrayPool<long>.Shared.Rent(m_embeddingDimensions);
        accumulator.AsSpan(0, m_embeddingDimensions).Clear();

        int validTokenCount = 0;
        foreach (int tokenId in tokenIds)
        {
            if (tokenId < 0 || tokenId >= m_tokenVectors.Length)
                continue;

            SparseVector tokenVector = m_tokenVectors[tokenId];
            foreach (SparseEntry entry in tokenVector.Entries)
                accumulator[entry.Dimension] += entry.Value;

            validTokenCount++;
        }

        if (validTokenCount == 0)
        {
            System.Buffers.ArrayPool<long>.Shared.Return(accumulator);
            return new SparseVector([new SparseEntry(0, 1L)], m_embeddingDimensions);
        }

        // Integer mean
        for (int d = 0; d < m_embeddingDimensions; d++)
            accumulator[d] /= validTokenCount;

        // L2 normalize in integer domain
        double sumSquared = 0.0;
        for (int d = 0; d < m_embeddingDimensions; d++)
        {
            long val = accumulator[d];
            sumSquared += (double)val * val;
        }

        if (sumSquared > 0)
        {
            double norm = System.Math.Sqrt(sumSquared);
            double scaleFactor = m_scale / norm;
            for (int d = 0; d < m_embeddingDimensions; d++)
                accumulator[d] = (long)(accumulator[d] * scaleFactor);
        }

        // Build sparse entries (skip zeros)
        int nnz = 0;
        for (int d = 0; d < m_embeddingDimensions; d++)
            if (accumulator[d] != 0L) nnz++;

        if (nnz == 0)
        {
            System.Buffers.ArrayPool<long>.Shared.Return(accumulator);
            return new SparseVector([new SparseEntry(0, 1L)], m_embeddingDimensions);
        }

        SparseEntry[] entries = new SparseEntry[nnz];
        int writeIndex = 0;
        for (int d = 0; d < m_embeddingDimensions; d++)
            if (accumulator[d] != 0L)
                entries[writeIndex++] = new SparseEntry((ushort)d, accumulator[d]);

        System.Buffers.ArrayPool<long>.Shared.Return(accumulator);

        // Apply output budget: trim pooled vector to top-N dims by absolute value.
        // This enforces the same sparsity on multi-token phrases as on single token rows.
        if (m_outputSparsityBudget > 0 && entries.Length > m_outputSparsityBudget)
            entries = EmbeddingAdapter.TrimToBudget(entries, m_outputSparsityBudget);

        return new SparseVector(entries, m_embeddingDimensions);
    }

    static LatticeEmbeddingSource LoadFromReader(
        GgufReader reader,
        QuantizationOptions? quantizationOptions,
        Action<int, int, string>? onProgress)
    {
        int embeddingDimensions = reader.EmbeddingLength;
        int vocabSize = reader.Tokens.Count;

        // Use a moderate scale that won't overflow when summing across ~50 tokens.
        // long.MaxValue / (768 dims × 512 tokens) ≈ 2.3e13, so 1e9 is safe.
        // After L2 normalization every component sits near ±1/√d (≈ ±0.036 for d=768),
        // so an absolute ZeroThreshold kills nothing useful. Instead, keep the top 25%
        // of dimensions by absolute value per token — this gives ~75% sparsity while
        // retaining enough discriminative signal to meet recall@10 ≥ 0.70.
        // SparsityBudget is applied by EmbeddingAdapter.Quantize after threshold filtering.
        QuantizationOptions effectiveOptions = quantizationOptions ?? new QuantizationOptions
        {
            ZeroThreshold  = 0f,
            GlobalScale    = 1_000_000_000L,
            SparsityBudget = System.Math.Max(1, embeddingDimensions / 4),  // top 25% ≈ 192/768
        };

        long scale = effectiveOptions.GlobalScale;

        int totalSteps = 3;
        int step = 0;
        void Report(string name) { step++; onProgress?.Invoke(step, totalSteps, name); }

        float[] tokenEmbeddings = reader.ReadTensorF32("token_embd.weight");
        Report("token_embd.weight");

        float[] tokenTypes    = reader.HasTensor("token_types.weight")
            ? reader.ReadTensorF32("token_types.weight")
            : new float[embeddingDimensions];  // Default to all zeros if missing
        float[] tokenTypeRow0 = new float[embeddingDimensions];
        Array.Copy(tokenTypes, 0, tokenTypeRow0, 0, embeddingDimensions);
        Report("token_types.weight");

        float[] embdNormW = reader.HasTensor("token_embd_norm.weight")
            ? reader.ReadTensorF32("token_embd_norm.weight")
            : CreateDefaultWeight(embeddingDimensions);
        float[] embdNormB = reader.HasTensor("token_embd_norm.bias")
            ? reader.ReadTensorF32("token_embd_norm.bias")
            : new float[embeddingDimensions];
        Report("token_embd_norm");

        // token_embd + token_type → LayerNorm → quantize per vocab entry at load time
        SparseVector[] tokenVectors = new SparseVector[vocabSize];
        float[] rowBuffer = new float[embeddingDimensions];

        for (int tokenId = 0; tokenId < vocabSize; tokenId++)
        {
            int srcBase = tokenId * embeddingDimensions;
            if (srcBase + embeddingDimensions > tokenEmbeddings.Length)
            {
                tokenVectors[tokenId] = new SparseVector([new SparseEntry(0, 1L)], embeddingDimensions);
                continue;
            }

            // Sum token embedding + token type embedding
            for (int d = 0; d < embeddingDimensions; d++)
                rowBuffer[d] = tokenEmbeddings[srcBase + d] + tokenTypeRow0[d];

            // Apply input LayerNorm (same as the model does before transformer layers)
            ApplyLayerNormSingle(rowBuffer, embdNormW, embdNormB);

            // L2 normalize the float vector before quantizing
            L2NormalizeSingle(rowBuffer);

            // Quantize to SparseVector
            tokenVectors[tokenId] = EmbeddingAdapter.Quantize(rowBuffer, effectiveOptions);
        }
        Report("quantize vocab");

        WordPieceTokenizer tokenizer = WordPieceTokenizer.FromGguf(reader);

        return new LatticeEmbeddingSource(
            reader.ModelName,
            tokenizer,
            tokenVectors,
            embeddingDimensions,
            scale,
            effectiveOptions.SparsityBudget ?? 0);
    }

    static void ApplyLayerNormSingle(float[] row, float[] weight, float[] bias)
    {
        int embeddingDimensions = row.Length;
        const float eps = 1e-12f;

        float mean = 0f;
        for (int d = 0; d < embeddingDimensions; d++)
            mean += row[d];
        mean /= embeddingDimensions;

        float variance = 0f;
        for (int d = 0; d < embeddingDimensions; d++)
        {
            float delta = row[d] - mean;
            variance += delta * delta;
        }
        variance /= embeddingDimensions;

        float invStd = 1.0f / MathF.Sqrt(variance + eps);

        for (int d = 0; d < embeddingDimensions; d++)
            row[d] = (row[d] - mean) * invStd * weight[d] + bias[d];
    }

    static void L2NormalizeSingle(float[] row)
    {
        float norm = 0f;
        for (int i = 0; i < row.Length; i++)
            norm += row[i] * row[i];
        norm = MathF.Sqrt(norm);
        if (norm < 1e-10f) return;
        float invNorm = 1.0f / norm;
        for (int i = 0; i < row.Length; i++)
            row[i] *= invNorm;
    }

    static float[] CreateDefaultWeight(int dimensions)
    {
        float[] weight = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
            weight[i] = 1.0f;
        return weight;
    }
}
