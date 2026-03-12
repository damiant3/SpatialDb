using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

public sealed partial class IntegerCausalSource
{
    private IntegerCausalSource(
        string modelName,
        BpeTokenizer tokenizer,
        Half[] tokenEmbeddingsHalf,
        long[] outputNormW,
        IntegerGemmaSource.GemmaLayerWeights[] layers,
        int nEmbd, int nHeads, int nKvHeads, int headDim, int nFf,
        int vocabSize,
        float ropeFreqBase, float ropeFreqBaseGlobal,
        int slidingWindow, int globalLayerInterval,
        float normEps,
        int scaleBits)
    {
        ModelName = modelName;
        m_tokenizer = tokenizer;
        m_tokenEmbeddingsHalf = tokenEmbeddingsHalf;
        m_outputNormW = outputNormW;
        m_layers = layers;
        m_nEmbd = nEmbd;
        m_nHeads = nHeads;
        m_nKvHeads = nKvHeads;
        m_headDim = headDim;
        m_qDim = nHeads * headDim;
        m_kvDim = nKvHeads * headDim;
        m_nFf = nFf;
        m_vocabSize = vocabSize;
        m_ropeFreqBase = ropeFreqBase;
        m_ropeFreqBaseGlobal = ropeFreqBaseGlobal;
        m_slidingWindow = slidingWindow;
        m_globalLayerInterval = globalLayerInterval;
        m_normEps = normEps;
        m_scaleBits = scaleBits;
    }

    public static IntegerCausalSource Load(string ggufPath, int scaleBits = 30,
        Action<int, int, string>? onProgress = null)
    {
        using GgufReader reader = GgufReader.Open(ggufPath);

        string arch = reader.Architecture;
        if (!arch.StartsWith("gemma", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                $"Architecture '{arch}' is not supported by IntegerCausalSource.");

        return LoadFromReader(reader, scaleBits, onProgress);
    }

    public static IntegerCausalSource LoadFromOllama(string modelName, string ollamaRoot,
        string? tag = null, int scaleBits = 30, Action<int, int, string>? onProgress = null)
    {
        string? ggufPath = OllamaModelLocator.LocateGgufOllama(modelName, ollamaRoot, tag);
        if (ggufPath is null)
            throw new FileNotFoundException(
                $"No GGUF blob found for model '{modelName}' in '{ollamaRoot}'.");
        return Load(ggufPath, scaleBits, onProgress);
    }

    public static IntegerCausalSource LoadFromModelDir(string modelName, string modelDir,
        int scaleBits = 30, Action<int, int, string>? onProgress = null)
    {
        string? ggufPath = OllamaModelLocator.LocateGguf(modelName, modelDir);
        if (ggufPath is null)
            throw new FileNotFoundException(
                $"No GGUF blob found for model '{modelName}' in '{modelDir}'.");
        return Load(ggufPath, scaleBits, onProgress);
    }

    private static IntegerCausalSource LoadFromReader(GgufReader reader, int scaleBits,
        Action<int, int, string>? onProgress)
    {
        string arch = reader.Architecture;
        int nEmbd = reader.EmbeddingLength;
        int nHeads = reader.HeadCount;
        int nLayers = reader.LayerCount;
        int nFf = reader.FeedForwardLength;
        int nKvHeads = GetInt(reader, $"{arch}.attention.head_count_kv", nHeads);
        int headDim = GetInt(reader, $"{arch}.attention.key_length", nEmbd / nHeads);
        float ropeFreqBase = GetFloat(reader, $"{arch}.rope.freq_base", 10000f);
        float normEps = GetFloat(reader, $"{arch}.attention.layer_norm_rms_epsilon", 1e-6f);
        int vocabSize = reader.Tokens.Count;

        // Gemma3 sliding window attention: local layers use sliding window + low-freq RoPE,
        // global layers (every Nth) use full attention + high-freq RoPE.
        int slidingWindow = GetInt(reader, $"{arch}.attention.sliding_window", 0);
        // Gemma3 convention: global layers every 6th layer, freq_base 1M for global.
        // These are architectural constants not stored in the GGUF metadata.
        int globalLayerInterval = (slidingWindow > 0) ? 6 : 0;
        float ropeFreqBaseGlobal = (slidingWindow > 0) ? 1_000_000f : ropeFreqBase;

        int totalSteps = 2 + nLayers * 13;
        int step = 0;
        void Report(string name) { step++; onProgress?.Invoke(step, totalSteps, name); }

        Half[] tokenEmbdHalf = ReadAsHalf(reader, "token_embd.weight");
        Report("token_embd.weight");

        long[] outputNormW = Q(reader.ReadTensorF32("output_norm.weight"), scaleBits);
        Report("output_norm.weight");

        IntegerGemmaSource.GemmaLayerWeights[] layers =
            new IntegerGemmaSource.GemmaLayerWeights[nLayers];
        for (int i = 0; i < nLayers; i++)
        {
            string pfx = $"blk.{i}";

            // Norm weights: small vectors, keep as int64 for RmsNorm arithmetic.
            long[] attnNormW     = ReadAndQuantize(reader, $"{pfx}.attn_norm.weight", scaleBits);
            Report($"blk.{i} attn_norm");

            // Projection weights: stored as Half (2 bytes/element).
            // Quantized to int64 on-the-fly during MatMul.
            Half[] attnQ         = ReadAsHalf(reader, $"{pfx}.attn_q.weight");
            Report($"blk.{i} attn_q");
            Half[] attnK         = ReadAsHalf(reader, $"{pfx}.attn_k.weight");
            Report($"blk.{i} attn_k");
            Half[] attnV         = ReadAsHalf(reader, $"{pfx}.attn_v.weight");
            Report($"blk.{i} attn_v");

            long[] attnQNormW = reader.HasTensor($"{pfx}.attn_q_norm.weight")
                ? ReadAndQuantize(reader, $"{pfx}.attn_q_norm.weight", scaleBits)
                : MakeOnesWeight(nHeads * headDim, scaleBits);
            Report($"blk.{i} attn_q_norm");

            long[] attnKNormW = reader.HasTensor($"{pfx}.attn_k_norm.weight")
                ? ReadAndQuantize(reader, $"{pfx}.attn_k_norm.weight", scaleBits)
                : MakeOnesWeight(nKvHeads * headDim, scaleBits);
            Report($"blk.{i} attn_k_norm");

            Half[] attnOutput    = ReadAsHalf(reader, $"{pfx}.attn_output.weight");
            Report($"blk.{i} attn_output");

            long[] postAttnNormW = reader.HasTensor($"{pfx}.post_attention_norm.weight")
                ? ReadAndQuantize(reader, $"{pfx}.post_attention_norm.weight", scaleBits)
                : MakeOnesWeight(nEmbd, scaleBits);
            Report($"blk.{i} post_attn_norm");

            long[] ffnNormW      = ReadAndQuantize(reader, $"{pfx}.ffn_norm.weight", scaleBits);
            Report($"blk.{i} ffn_norm");
            Half[] ffnGate       = ReadAsHalf(reader, $"{pfx}.ffn_gate.weight");
            Report($"blk.{i} ffn_gate");
            Half[] ffnUp         = ReadAsHalf(reader, $"{pfx}.ffn_up.weight");
            Report($"blk.{i} ffn_up");
            Half[] ffnDown       = ReadAsHalf(reader, $"{pfx}.ffn_down.weight");
            Report($"blk.{i} ffn_down");

            long[] postFfwNormW = reader.HasTensor($"{pfx}.post_ffw_norm.weight")
                ? ReadAndQuantize(reader, $"{pfx}.post_ffw_norm.weight", scaleBits)
                : MakeOnesWeight(nEmbd, scaleBits);
            Report($"blk.{i} post_ffw_norm ({i + 1}/{nLayers})");

            layers[i] = new IntegerGemmaSource.GemmaLayerWeights
            {
                AttnNormW     = attnNormW,
                AttnQ         = attnQ,
                AttnK         = attnK,
                AttnV         = attnV,
                AttnQNormW    = attnQNormW,
                AttnKNormW    = attnKNormW,
                AttnOutput    = attnOutput,
                PostAttnNormW = postAttnNormW,
                FfnNormW      = ffnNormW,
                FfnGate       = ffnGate,
                FfnUp         = ffnUp,
                FfnDown       = ffnDown,
                PostFfwNormW  = postFfwNormW,
            };
        }

        BpeTokenizer tokenizer = BpeTokenizer.FromGguf(reader);

        return new IntegerCausalSource(
            modelName: reader.ModelName,
            tokenizer: tokenizer,
            tokenEmbeddingsHalf: tokenEmbdHalf,
            outputNormW: outputNormW,
            layers: layers,
            nEmbd: nEmbd,
            nHeads: nHeads,
            nKvHeads: nKvHeads,
            headDim: headDim,
            nFf: nFf,
            vocabSize: vocabSize,
            ropeFreqBase: ropeFreqBase,
            ropeFreqBaseGlobal: ropeFreqBaseGlobal,
            slidingWindow: slidingWindow,
            globalLayerInterval: globalLayerInterval,
            normEps: normEps,
            scaleBits: scaleBits);
    }

    private static long[] Q(float[] source, int scaleBits)
        => IntegerMatMul.QuantizeFromFloat(source, scaleBits).Data;

    /// <summary>
    /// Reads a tensor from the GGUF reader, quantizes to int64, and aggressively
    /// releases the float intermediate to reduce GC pressure during loading.
    /// </summary>
    private static long[] ReadAndQuantize(GgufReader reader, string name, int scaleBits)
    {
        float[] floats = reader.ReadTensorF32(name);
        double scale = 1L << scaleBits;
        long[] data = new long[floats.Length];
        for (int i = 0; i < floats.Length; i++)
            data[i] = (long)(floats[i] * scale);
        return data;
    }

    /// <summary>
    /// Reads a tensor as float32 and compresses to Half[] for compact storage.
    /// Saves 2× memory vs float[] and 4× vs long[].
    /// </summary>
    private static Half[] ReadAsHalf(GgufReader reader, string name)
    {
        float[] floats = reader.ReadTensorF32(name);
        Half[] halves = new Half[floats.Length];
        for (int i = 0; i < floats.Length; i++)
            halves[i] = (Half)floats[i];
        return halves;
    }

    private static long[] MakeOnesWeight(int length, int scaleBits)
    {
        long one = 1L << scaleBits;
        long[] result = new long[length];
        Array.Fill(result, one);
        return result;
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

    private static int GetInt(GgufReader reader, string key, int defaultValue)
    {
        if (!reader.Metadata.TryGetValue(key, out GgufValue? v)) return defaultValue;
        return v.Type switch
        {
            GgufValueType.Int32 => v.AsInt32(),
            GgufValueType.UInt32 => (int)v.AsUInt32(),
            _ => defaultValue,
        };
    }
}
