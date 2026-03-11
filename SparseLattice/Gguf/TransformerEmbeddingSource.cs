using System.Numerics;
using SparseLattice.Embedding;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using SystemMath = System.Math;
//////////////////////////////////////////////////////////////
namespace SparseLattice.Gguf;

/// <summary>
/// CPU-only <see cref="IEmbeddingSource"/> that runs a BERT-family encoder forward
/// pass directly from a GGUF file. No external process or NuGet dependencies beyond
/// <c>System.Numerics</c>.
/// </summary>
/// <remarks>
/// <para>
/// All quantized weight tensors are dequantized to <c>float[]</c> once on load.
/// For nomic-embed-text (768 dimensions, 12 layers) this is approximately 1 GB
/// resident memory — acceptable for a dev/test workload.
/// </para>
/// <para>
/// Supported architectures: <c>nomic-bert</c> (primary), <c>bert</c> (secondary).
/// Causal architectures (llama, gemma) are not supported and will throw on load.
/// </para>
/// </remarks>
public sealed class TransformerEmbeddingSource : IEmbeddingSource, IDisposable
{
    // -----------------------------------------------------------------------
    // Loaded model weights
    // -----------------------------------------------------------------------

    internal sealed class LayerWeights
    {
        // Fused QKV projection: shape [n_embd, 3*n_embd]
        public required float[] AttnQkv     { get; init; }
        // Attention output projection: shape [n_embd, n_embd]
        public required float[] AttnOutput  { get; init; }
        // Post-attention LayerNorm
        public required float[] AttnNormW   { get; init; }
        public required float[] AttnNormB   { get; init; }
        // FFN gated projection (SwiGLU): value branch [n_embd, n_ff]
        public required float[] FfnUp       { get; init; }
        // FFN gated projection: gate branch [n_embd, n_ff]
        public required float[] FfnGate     { get; init; }
        // FFN down projection: [n_ff, n_embd]
        public required float[] FfnDown     { get; init; }
        // Post-FFN LayerNorm
        public required float[] LayerNormW  { get; init; }
        public required float[] LayerNormB  { get; init; }
    }

    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------

    private readonly WordPieceTokenizer m_tokenizer;
    private readonly LayerWeights[]     m_layers;

    // Embedding tables
    private readonly float[]    m_tokenEmbeddings;    // [vocab_size * n_embd]
    private readonly float[]    m_tokenTypeEmbedding; // [n_embd] — row 0 of token_types.weight

    // Input embedding LayerNorm (token_embd_norm)
    private readonly float[]    m_embdNormW;
    private readonly float[]    m_embdNormB;

    private readonly int        m_nEmbd;
    private readonly int        m_nHeads;
    private readonly int        m_nFf;
    private readonly float      m_ropeFreqBase;
    private readonly float      m_layerNormEps;  // unused?

    private bool m_disposed;

    // -----------------------------------------------------------------------
    // Public properties
    // -----------------------------------------------------------------------

    public string ModelName  { get; }
    public int    Dimensions => m_nEmbd;

    // -----------------------------------------------------------------------
    // Construction — from GGUF path
    // -----------------------------------------------------------------------

    /// <summary>Loads model weights from a GGUF file at <paramref name="ggufPath"/>.</summary>
    public static TransformerEmbeddingSource Load(string ggufPath,
        Action<int, int, string>? onProgress = null)
    {
        using GgufReader reader = GgufReader.Open(ggufPath);

        string arch = reader.Architecture;
        if (!arch.Equals("nomic-bert", StringComparison.OrdinalIgnoreCase) &&
            !arch.Equals("bert",       StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                $"Architecture '{arch}' is not supported. Only 'nomic-bert' and 'bert' are implemented.");

        return LoadFromReader(reader, onProgress);
    }

    /// <summary>
    /// Resolves a model name via <see cref="OllamaModelLocator"/> in
    /// <paramref name="modelDir"/>, then loads the GGUF.
    /// </summary>
    public static TransformerEmbeddingSource LoadFromModelDir(string modelName, string modelDir,
        Action<int, int, string>? onProgress = null)
    {
        string? ggufPath = OllamaModelLocator.LocateGguf(modelName, modelDir);
        if (ggufPath is null)
            throw new FileNotFoundException(
                $"No GGUF blob found for model '{modelName}' in '{modelDir}'.");
        return Load(ggufPath, onProgress);
    }

    // -----------------------------------------------------------------------
    // Internal constructor — unit tests supply pre-built weights directly
    // -----------------------------------------------------------------------

    internal TransformerEmbeddingSource(
        string              modelName,
        WordPieceTokenizer  tokenizer,
        float[]             tokenEmbeddings,
        float[]             tokenTypeEmbedding,
        float[]             embdNormW,
        float[]             embdNormB,
        LayerWeights[]      layers,
        int                 nEmbd,
        int                 nHeads,
        int                 nFf,
        float               ropeFreqBase,
        float               layerNormEps)
    {
        ModelName            = modelName;
        m_tokenizer          = tokenizer;
        m_tokenEmbeddings    = tokenEmbeddings;
        m_tokenTypeEmbedding = tokenTypeEmbedding;
        m_embdNormW          = embdNormW;
        m_embdNormB          = embdNormB;
        m_layers             = layers;
        m_nEmbd              = nEmbd;
        m_nHeads             = nHeads;
        m_nFf                = nFf;
        m_ropeFreqBase       = ropeFreqBase;
        m_layerNormEps       = layerNormEps;
    }

    // -----------------------------------------------------------------------
    // IEmbeddingSource
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Forward(text));
    }

    /// <inheritdoc/>
    public async Task<float[][]> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        float[][] results = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
            results[i] = await EmbedAsync(texts[i], ct).ConfigureAwait(false);
        return results;
    }

    // -----------------------------------------------------------------------
    // Dispose
    // -----------------------------------------------------------------------

    public void Dispose() => m_disposed = true;

    // -----------------------------------------------------------------------
    // Forward pass
    // -----------------------------------------------------------------------

    private float[] Forward(string text)
    {
        int[] tokenIds = m_tokenizer.Encode(text, addSpecialTokens: true);
        int sequenceLen = tokenIds.Length;

        // x: [T, n_embd] — working hidden state, row-major
        float[] x = BuildEmbeddings(tokenIds, sequenceLen);

        // Input LayerNorm (nomic-bert applies this after the initial embedding sum)
        ApplyLayerNorm(x, sequenceLen, m_embdNormW, m_embdNormB);

        for (int layerIdx = 0; layerIdx < m_layers.Length; layerIdx++)
            ApplyTransformerBlock(x, sequenceLen, layerIdx, m_layers[layerIdx]);

        // Mean pool over non-special tokens (positions 1..T-2, i.e. skip CLS and SEP)
        float[] pooled = MeanPool(x, sequenceLen);

        // L2 normalize
        L2Normalize(pooled);

        return pooled;
    }

    // Build the initial embedding matrix by summing token + token-type embeddings.
    private float[] BuildEmbeddings(int[] tokenIds, int sequenceLen)
    {
        float[] x = new float[sequenceLen * m_nEmbd];

        for (int t = 0; t < sequenceLen; t++)
        {
            int tokenId = tokenIds[t];
            int srcBase = tokenId * m_nEmbd;
            int dstBase = t * m_nEmbd;

            // Token embedding row — bounds-clamp unknown ids to UNK
            if (tokenId < 0 || srcBase + m_nEmbd > m_tokenEmbeddings.Length)
                srcBase = m_tokenizer.UnkTokenId * m_nEmbd;

            for (int d = 0; d < m_nEmbd; d++)
                x[dstBase + d] = m_tokenEmbeddings[srcBase + d] + m_tokenTypeEmbedding[d];
        }

        return x;
    }

    // Applies a single transformer block in-place on x [T, n_embd].
    //
    // nomic-bert (llama.cpp build_bert, NOMIC_BERT path):
    //   1. QKV projected from raw x (NO pre-attention LayerNorm)
    //   2. RoPE on Q and K
    //   3. Attention output + residual (raw x)
    //   4. attn_out_norm applied post-residual   ← attn_output_norm weights
    //   5. FFN (SiLU gate, parallel up/gate) from normed post-attn output
    //   6. FFN output + residual (normed post-attn output)
    //   7. layer_out_norm applied post-residual  ← layer_output_norm weights
    private void ApplyTransformerBlock(float[] x, int seqLen, int layerIdx, LayerWeights layer)
    {
        int embd = m_nEmbd;

        // -- Attention sub-layer --
        // QKV projected directly from raw x — no pre-norm for nomic-bert
        float[] residualAttn = x.ToArray();

        float[] qkv = MatMulGguf(x, seqLen, embd, layer.AttnQkv, 3 * embd);

        float[] q = new float[seqLen * embd];
        float[] k = new float[seqLen * embd];
        float[] v = new float[seqLen * embd];
        SplitQkv(qkv, q, k, v, seqLen, embd);

        ApplyRope(q, seqLen, embd, m_nHeads, m_ropeFreqBase);
        ApplyRope(k, seqLen, embd, m_nHeads, m_ropeFreqBase);

        float[] attnOut   = MultiHeadAttention(q, k, v, seqLen, embd, m_nHeads);
        float[] projected = MatMulGguf(attnOut, seqLen, embd, layer.AttnOutput, embd);

        // Residual add then post-attn norm
        AddInPlace(projected, residualAttn, seqLen * embd);
        ApplyLayerNorm(projected, seqLen, layer.AttnNormW, layer.AttnNormB);

        // -- FFN sub-layer --
        // FFN input is the normed post-attn output; residual is also that same tensor
        float[] residualFfn = projected.ToArray();

        float[] ffnUp   = MatMulGguf(projected, seqLen, embd, layer.FfnUp,   m_nFf);
        float[] ffnGate = MatMulGguf(projected, seqLen, embd, layer.FfnGate, m_nFf);

        for (int i = 0; i < ffnGate.Length; i++)
            ffnUp[i] *= Silu(ffnGate[i]);

        float[] ffnOut = MatMulGguf(ffnUp, seqLen, m_nFf, layer.FfnDown, embd);

        // Residual add then post-FFN norm
        AddInPlace(ffnOut, residualFfn, seqLen * embd);
        ApplyLayerNorm(ffnOut, seqLen, layer.LayerNormW, layer.LayerNormB);

        Array.Copy(ffnOut, x, seqLen * embd);
    }

    // -----------------------------------------------------------------------
    // Attention operations
    // -----------------------------------------------------------------------

    private static void SplitQkv(float[] qkv, float[] q, float[] k, float[] v, int seqLen, int embd)
    {
        for (int t = 0; t < seqLen; t++)
        {
            int srcBase = t * 3 * embd;
            int dstBase = t * embd;
            Array.Copy(qkv, srcBase,          q, dstBase, embd);
            Array.Copy(qkv, srcBase + embd,   k, dstBase, embd);
            Array.Copy(qkv, srcBase + 2*embd, v, dstBase, embd);
        }
    }

    // -----------------------------------------------------------------------
    // RoPE sin/cos cache  (immutable after first use, safe for concurrent reads)
    // -----------------------------------------------------------------------

    private sealed class RopeCache
    {
        // cos[t, i] and sin[t, i] for t in [0, maxSeqLen), i in [0, headDim/2)
        public readonly float[] Cos;
        public readonly float[] Sin;
        public readonly int     MaxSeqLen;
        public readonly int     HalfDim;

        public RopeCache(int maxSeqLen, int headDim, float freqBase)
        {
            MaxSeqLen = maxSeqLen;
            HalfDim   = headDim / 2;
            Cos = new float[maxSeqLen * HalfDim];
            Sin = new float[maxSeqLen * HalfDim];
            for (int t = 0; t < maxSeqLen; t++)
            {
                for (int i = 0; i < HalfDim; i++)
                {
                    float theta = (float)SystemMath.Pow(freqBase, -2.0 * i / headDim);
                    double angle = t * theta;
                    Cos[t * HalfDim + i] = (float)SystemMath.Cos(angle);
                    Sin[t * HalfDim + i] = (float)SystemMath.Sin(angle);
                }
            }
        }
    }

    // Built lazily on first Forward() call; covers any sequence up to 512 tokens.
    private RopeCache? m_ropeCache;
    private const int  RopeMaxSeqLen = 512;

    private RopeCache GetRopeCache()
    {
        if (m_ropeCache is not null) return m_ropeCache;
        int headDim = m_nEmbd / m_nHeads;
        m_ropeCache = new RopeCache(RopeMaxSeqLen, headDim, m_ropeFreqBase);
        return m_ropeCache;
    }

    // Apply RoPE using the pre-computed sin/cos cache.
    private void ApplyRope(float[] x, int seqLen, int embd, int nHeads, float freqBase)
    {
        RopeCache cache   = GetRopeCache();
        int       headDim = embd / nHeads;
        int       halfDim = headDim / 2;

        for (int t = 0; t < seqLen; t++)
        {
            int cacheRow = t * halfDim;
            for (int h = 0; h < nHeads; h++)
            {
                int headBase = t * embd + h * headDim;
                for (int i = 0; i < halfDim; i++)
                {
                    float cos = cache.Cos[cacheRow + i];
                    float sin = cache.Sin[cacheRow + i];
                    float x0  = x[headBase + i];
                    float x1  = x[headBase + i + halfDim];
                    x[headBase + i]         = x0 * cos - x1 * sin;
                    x[headBase + i + halfDim] = x0 * sin + x1 * cos;
                }
            }
        }
    }

    // Multiply activation A [rowsA, nIn] by GGUF weight W stored column-major,
    // producing C [rowsA, nOut].
    // Uses Span<float> + MemoryMarshal to avoid per-iteration Vector<float> allocations.
    private static float[] MatMulGguf(float[] a, int rowsA, int nIn, float[] w, int nOut)
    {
        float[] c      = new float[rowsA * nOut];
        int     vecLen = Vector<float>.Count;

        ReadOnlySpan<float> aSpan = a;
        ReadOnlySpan<float> wSpan = w;

        for (int row = 0; row < rowsA; row++)
        {
            int aBase = row * nIn;
            ReadOnlySpan<float> aRow = aSpan.Slice(aBase, nIn);

            for (int col = 0; col < nOut; col++)
            {
                int   wBase = col * nIn;
                float sum   = 0f;
                int   i     = 0;

                ReadOnlySpan<float> wCol = wSpan.Slice(wBase, nIn);

                // SIMD dot using ReadOnlySpan — no heap allocation per iteration
                ref float aRef = ref MemoryMarshal.GetReference(aRow);
                ref float wRef = ref MemoryMarshal.GetReference(wCol);

                while (i <= nIn - vecLen)
                {
                    sum += Vector.Dot(
                        Unsafe.ReadUnaligned<Vector<float>>(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref aRef, i))),
                        Unsafe.ReadUnaligned<Vector<float>>(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref wRef, i))));
                    i += vecLen;
                }
                while (i < nIn)
                {
                    sum += Unsafe.Add(ref aRef, i) * Unsafe.Add(ref wRef, i);
                    i++;
                }

                c[row * nOut + col] = sum;
            }
        }

        return c;
    }

    // Multi-head scaled dot-product attention.
    // Fixed loop order: innermost loop over headDim is now contiguous on v.
    private static float[] MultiHeadAttention(
        float[] q, float[] k, float[] v,
        int seqLen, int embd, int nHeads)
    {
        int headDim = embd / nHeads;
        float scale = 1.0f / MathF.Sqrt(headDim);
        float[] output = new float[seqLen * embd];
        float[] scores = new float[seqLen * seqLen];   // reuse across heads

        for (int h = 0; h < nHeads; h++)
        {
            int headOffset = h * headDim;

            // scores[t, s] = scale * dot(q[t, h], k[s, h])
            for (int t = 0; t < seqLen; t++)
            {
                int qBase = t * embd + headOffset;
                for (int s = 0; s < seqLen; s++)
                {
                    scores[t * seqLen + s] = DotProduct(q, qBase, k, s * embd + headOffset, headDim) * scale;
                }
            }

            Softmax(scores, seqLen);

            // Output[t, h] = sum_s scores[t,s] * v[s, h]
            // Loop order: t → s → d  so the innermost stride over d is contiguous in both output and v
            for (int t = 0; t < seqLen; t++)
            {
                int outBase   = t * embd + headOffset;
                int scoreBase = t * seqLen;
                for (int s = 0; s < seqLen; s++)
                {
                    float w   = scores[scoreBase + s];
                    int vBase = s * embd + headOffset;
                    for (int d = 0; d < headDim; d++)
                        output[outBase + d] += w * v[vBase + d];
                }
            }
        }

        return output;
    }

    // -----------------------------------------------------------------------
    // Math primitives
    // -----------------------------------------------------------------------

    // LayerNorm in-place over rows of x [T, n_embd].
    private static void ApplyLayerNorm(float[] x, int seqLen, float[] weight, float[] bias)
    {
        int embd = weight.Length;
        const float eps = 1e-12f;

        for (int t = 0; t < seqLen; t++)
        {
            int rowBase = t * embd;

            // Mean
            float mean = 0f;
            for (int d = 0; d < embd; d++)
                mean += x[rowBase + d];
            mean /= embd;

            // Variance
            float variance = 0f;
            for (int d = 0; d < embd; d++)
            {
                float delta = x[rowBase + d] - mean;
                variance += delta * delta;
            }
            variance /= embd;

            float invStd = 1.0f / MathF.Sqrt(variance + eps);

            for (int d = 0; d < embd; d++)
                x[rowBase + d] = (x[rowBase + d] - mean) * invStd * weight[d] + bias[d];
        }
    }

    private static void AddInPlace(float[] dst, float[] src, int count)
    {
        int vecLen = Vector<float>.Count;
        int i      = 0;
        for (; i <= count - vecLen; i += vecLen)
        {
            Vector<float> vd = new(dst, i);
            Vector<float> vs = new(src, i);
            (vd + vs).CopyTo(dst, i);
        }
        for (; i < count; i++)
            dst[i] += src[i];
    }

    private static float DotProduct(float[] a, int aOffset, float[] b, int bOffset, int length)
    {
        float sum   = 0f;
        int vecLen  = Vector<float>.Count;
        int i       = 0;

        for (; i <= length - vecLen; i += vecLen)
        {
            Vector<float> va = new(a, aOffset + i);
            Vector<float> vb = new(b, bOffset + i);
            sum += Vector.Dot(va, vb);
        }
        for (; i < length; i++)
            sum += a[aOffset + i] * b[bOffset + i];
        return sum;
    }

    // Softmax in-place on a [seqLen, seqLen] scores matrix, row by row.
    private static void Softmax(float[] scores, int seqLen)
    {
        for (int t = 0; t < seqLen; t++)
        {
            int rowBase = t * seqLen;
            float maxVal = scores[rowBase];
            for (int s = 1; s < seqLen; s++)
                if (scores[rowBase + s] > maxVal) maxVal = scores[rowBase + s];

            float sum = 0f;
            for (int s = 0; s < seqLen; s++)
            {
                scores[rowBase + s] = MathF.Exp(scores[rowBase + s] - maxVal);
                sum += scores[rowBase + s];
            }
            float invSum = 1.0f / sum;
            for (int s = 0; s < seqLen; s++)
                scores[rowBase + s] *= invSum;
        }
    }

    // SiLU activation: x * sigmoid(x)  — used by nomic-bert FFN (LLM_FFN_SILU).
    private static float Silu(float x) => x / (1.0f + MathF.Exp(-x));

    // GELU activation (tanh approximation) — kept for reference / BERT variants.
    private static float Gelu(float x)
    {
        const float sqrt2OverPi = 0.7978845608f;
        const float c = 0.044715f;
        float inner = sqrt2OverPi * (x + c * x * x * x);
        return 0.5f * x * (1.0f + MathF.Tanh(inner));
    }

    // Mean pool over all token positions 0..T-1 (including CLS and SEP),
    // matching the nomic-embed-text pooling_type=MEAN behaviour in llama.cpp.
    // Falls back to CLS position when sequence is empty.
    private float[] MeanPool(float[] x, int seqLen)
    {
        float[] pooled = new float[m_nEmbd];

        if (seqLen == 0)
            return pooled;

        for (int t = 0; t < seqLen; t++)
        {
            int rowBase = t * m_nEmbd;
            for (int d = 0; d < m_nEmbd; d++)
                pooled[d] += x[rowBase + d];
        }

        float inv = 1.0f / seqLen;
        for (int d = 0; d < m_nEmbd; d++)
            pooled[d] *= inv;

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

    // -----------------------------------------------------------------------
    // Private — load weights from GgufReader
    // -----------------------------------------------------------------------

    private static TransformerEmbeddingSource LoadFromReader(GgufReader reader,
        Action<int, int, string>? onProgress = null)
    {
        string arch        = reader.Architecture;
        int nEmbd          = reader.EmbeddingLength;
        int nHeads         = reader.HeadCount;
        int nLayers        = reader.LayerCount;
        int nFf            = reader.FeedForwardLength;
        float ropeFreqBase = GetFloat(reader, $"{arch}.rope.freq_base", 10000f);
        float layerNormEps = GetFloat(reader, $"{arch}.attention.layer_norm_epsilon", 1e-12f);

        // Total Report() calls: 4 shared + 7 per layer
        int totalSteps = 4 + nLayers * 7;
        int step = 0;

        void Report(string name)
        {
            step++;
            onProgress?.Invoke(step, totalSteps, name);
        }

        float[] tokenEmbd = reader.ReadTensorF32("token_embd.weight");
        Report("token_embd.weight");

        float[] tokenTypes    = reader.ReadTensorF32("token_types.weight");
        float[] tokenTypeRow0 = new float[nEmbd];
        Array.Copy(tokenTypes, 0, tokenTypeRow0, 0, nEmbd);
        Report("token_types.weight");

        float[] embdNormW = reader.ReadTensorF32("token_embd_norm.weight");
        Report("token_embd_norm.weight");

        float[] embdNormB = reader.ReadTensorF32("token_embd_norm.bias");
        Report("token_embd_norm.bias");

        LayerWeights[] layers = new LayerWeights[nLayers];
        for (int i = 0; i < nLayers; i++)
        {
            string pfx = $"blk.{i}";

            float[] attnQkv    = reader.ReadTensorF32($"{pfx}.attn_qkv.weight");
            Report($"blk.{i} attn_qkv");
            float[] attnOutput = reader.ReadTensorF32($"{pfx}.attn_output.weight");
            Report($"blk.{i} attn_output");
            float[] attnNormW  = reader.ReadTensorF32($"{pfx}.attn_output_norm.weight");
            float[] attnNormB  = reader.ReadTensorF32($"{pfx}.attn_output_norm.bias");
            Report($"blk.{i} attn_norm");
            float[] ffnUp      = reader.ReadTensorF32($"{pfx}.ffn_up.weight");
            Report($"blk.{i} ffn_up");
            float[] ffnGate    = reader.ReadTensorF32($"{pfx}.ffn_gate.weight");
            Report($"blk.{i} ffn_gate");
            float[] ffnDown    = reader.ReadTensorF32($"{pfx}.ffn_down.weight");
            Report($"blk.{i} ffn_down");
            float[] layerNormW = reader.ReadTensorF32($"{pfx}.layer_output_norm.weight");
            float[] layerNormB = reader.ReadTensorF32($"{pfx}.layer_output_norm.bias");
            Report($"blk.{i} layer_norm  ({i + 1}/{nLayers})");

            layers[i] = new LayerWeights
            {
                AttnQkv    = attnQkv,
                AttnOutput = attnOutput,
                AttnNormW  = attnNormW,
                AttnNormB  = attnNormB,
                FfnUp      = ffnUp,
                FfnGate    = ffnGate,
                FfnDown    = ffnDown,
                LayerNormW = layerNormW,
                LayerNormB = layerNormB,
            };
        }

        WordPieceTokenizer tokenizer = WordPieceTokenizer.FromGguf(reader);

        return new TransformerEmbeddingSource(
            modelName:           reader.ModelName,
            tokenizer:           tokenizer,
            tokenEmbeddings:     tokenEmbd,
            tokenTypeEmbedding:  tokenTypeRow0,
            embdNormW:           embdNormW,
            embdNormB:           embdNormB,
            layers:              layers,
            nEmbd:               nEmbd,
            nHeads:              nHeads,
            nFf:                 nFf,
            ropeFreqBase:        ropeFreqBase,
            layerNormEps:        layerNormEps);
    }

    private static float GetFloat(GgufReader reader, string key, float defaultValue)
    {
        if (!reader.Metadata.TryGetValue(key, out GgufValue? v)) return defaultValue;
        return v.Type switch
        {
            GgufValueType.Float32 => v.AsFloat32(),
            GgufValueType.Float64 => (float)v.AsFloat64(),
            _ => defaultValue,
        };
    }
}
