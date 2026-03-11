using SparseLattice.Gguf;
using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Embedding;

/// <summary>
/// Embedding source that uses the GGUF token embedding table indexed in the lattice
/// to produce <see cref="SparseVector"/> embeddings without running the transformer
/// forward pass.
///
/// On construction the token embedding table (<c>token_embd.weight</c>) is loaded from
/// the GGUF file, each row is quantized to a <see cref="SparseVector"/>, and the full
/// vocabulary is stored in a direct-lookup array. At embed time, the WordPiece tokenizer
/// splits text into token IDs, the corresponding pre-quantized vectors are looked up
/// (O(1) per token), mean-pooled in the integer domain, and L2-normalized.
///
/// Cost per embed: tokenization + O(T × nnz) integer arithmetic for pooling.
/// No matrix multiplications, no attention, no floats at inference time.
/// </summary>
public sealed class LatticeEmbeddingSource : IEmbeddingSource, IDisposable
{
    private readonly WordPieceTokenizer m_tokenizer;
    private readonly SparseVector[] m_tokenVectors;
    private readonly int m_embeddingDimensions;
    private readonly long m_scale;
    private bool m_disposed;

    public string ModelName { get; }
    public int Dimensions => m_embeddingDimensions;

    // -----------------------------------------------------------------------
    // Construction — from GGUF path
    // -----------------------------------------------------------------------

    /// <summary>
    /// Loads the token embedding table from a GGUF file and quantizes each vocabulary
    /// row into a <see cref="SparseVector"/> for direct integer-domain lookup at embed time.
    /// </summary>
    /// <param name="ggufPath">Path to the GGUF model file.</param>
    /// <param name="quantizationOptions">
    /// Controls threshold and scale for quantization. Defaults to a scale of
    /// <c>1_000_000</c> (not <c>long.MaxValue</c>) to keep pooled sums within
    /// <c>long</c> range when summing across tokens.
    /// </param>
    /// <param name="onProgress">Optional progress callback: (step, total, name).</param>
    public static LatticeEmbeddingSource Load(
        string ggufPath,
        QuantizationOptions? quantizationOptions = null,
        Action<int, int, string>? onProgress = null)
    {
        using GgufReader reader = GgufReader.Open(ggufPath);
        return LoadFromReader(reader, quantizationOptions, onProgress);
    }

    /// <summary>
    /// Resolves a model via <see cref="OllamaModelLocator"/> and loads it.
    /// </summary>
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

    private LatticeEmbeddingSource(
        string modelName,
        WordPieceTokenizer tokenizer,
        SparseVector[] tokenVectors,
        int embeddingDimensions,
        long scale)
    {
        ModelName = modelName;
        m_tokenizer = tokenizer;
        m_tokenVectors = tokenVectors;
        m_embeddingDimensions = embeddingDimensions;
        m_scale = scale;
    }

    // -----------------------------------------------------------------------
    // IEmbeddingSource — float[] interface for pipeline compatibility
    // -----------------------------------------------------------------------

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

    // -----------------------------------------------------------------------
    // Core: text → SparseVector, integer domain, no floats
    // -----------------------------------------------------------------------

    /// <summary>
    /// Embeds text directly into a <see cref="SparseVector"/> using token embedding
    /// lookup, integer mean-pooling, and L2 normalization in the integer domain.
    /// No floating-point arithmetic on the hot path.
    /// </summary>
    public SparseVector EmbedSparse(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new SparseVector([new SparseEntry(0, 1L)], m_embeddingDimensions);

        int[] tokenIds = m_tokenizer.Encode(text, addSpecialTokens: true);
        if (tokenIds.Length == 0)
            return new SparseVector([new SparseEntry(0, 1L)], m_embeddingDimensions);

        return PoolTokenVectors(tokenIds);
    }

    /// <summary>Batch version for efficiency.</summary>
    public SparseVector[] EmbedSparseBatch(IReadOnlyList<string> texts)
    {
        SparseVector[] results = new SparseVector[texts.Count];
        for (int i = 0; i < texts.Count; i++)
            results[i] = EmbedSparse(texts[i]);
        return results;
    }

    public void Dispose() => m_disposed = true;

    // -----------------------------------------------------------------------
    // Integer mean-pool across token vectors
    // -----------------------------------------------------------------------

    private SparseVector PoolTokenVectors(int[] tokenIds)
    {
        // Accumulate into a dense long[] then convert to sparse.
        // For 768 dims this is 6 KB on the stack — acceptable.
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

        // Integer mean: divide by token count
        for (int d = 0; d < m_embeddingDimensions; d++)
            accumulator[d] /= validTokenCount;

        // L2 normalize in integer domain: scale so ||v||₂ ≈ m_scale
        // Compute sum of squares, then scale each component.
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

        // Build sparse vector (skip zeros)
        int nnz = 0;
        for (int d = 0; d < m_embeddingDimensions; d++)
            if (accumulator[d] != 0L)
                nnz++;

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
        return new SparseVector(entries, m_embeddingDimensions);
    }

    // -----------------------------------------------------------------------
    // Load: GGUF → quantized token embedding table
    // -----------------------------------------------------------------------

    private static LatticeEmbeddingSource LoadFromReader(
        GgufReader reader,
        QuantizationOptions? quantizationOptions,
        Action<int, int, string>? onProgress)
    {
        int embeddingDimensions = reader.EmbeddingLength;
        int vocabSize = reader.Tokens.Count;

        // Use a moderate scale that won't overflow when summing across ~50 tokens.
        // long.MaxValue / (768 dims × 512 tokens) ≈ 2.3e13, so 1e9 is safe.
        QuantizationOptions effectiveOptions = quantizationOptions ?? new QuantizationOptions
        {
            ZeroThreshold = 0.005f,
            GlobalScale = 1_000_000_000L,
        };

        long scale = effectiveOptions.GlobalScale;

        int totalSteps = 3;
        int step = 0;
        void Report(string name) { step++; onProgress?.Invoke(step, totalSteps, name); }

        // Step 1: read token embedding table + token type embedding + input LayerNorm
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

        // Step 2: quantize each vocabulary row into a SparseVector
        // Apply: token_embd + token_type → LayerNorm → quantize
        // This is the same first step the full transformer does, but we do it once
        // per vocab entry at load time (not per inference call).
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
            scale);
    }

    // -----------------------------------------------------------------------
    // Minimal LayerNorm for a single row (used once per vocab entry at load)
    // -----------------------------------------------------------------------

    private static void ApplyLayerNormSingle(float[] row, float[] weight, float[] bias)
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

    private static void L2NormalizeSingle(float[] row)
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

    private static float[] CreateDefaultWeight(int dimensions)
    {
        float[] weight = new float[dimensions];
        for (int i = 0; i < dimensions; i++)
            weight[i] = 1.0f;
        return weight;
    }
}
