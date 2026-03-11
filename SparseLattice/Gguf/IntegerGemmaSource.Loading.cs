using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

public sealed partial class IntegerGemmaSource
{
    internal sealed class GemmaLayerWeights
    {
        public required long[] AttnNormW      { get; init; }
        public required long[] AttnQ          { get; init; }
        public required long[] AttnK          { get; init; }
        public required long[] AttnV          { get; init; }
        public required long[] AttnQNormW     { get; init; }
        public required long[] AttnKNormW     { get; init; }
        public required long[] AttnOutput     { get; init; }
        public required long[] PostAttnNormW  { get; init; }
        public required long[] FfnNormW       { get; init; }
        public required long[] FfnGate        { get; init; }
        public required long[] FfnUp          { get; init; }
        public required long[] FfnDown        { get; init; }
        public required long[] PostFfwNormW   { get; init; }
    }

    private IntegerGemmaSource(
        string modelName,
        BpeTokenizer tokenizer,
        long[] tokenEmbeddings,
        long[] outputNormW,
        GemmaLayerWeights[] layers,
        int nEmbd, int nHeads, int nKvHeads, int headDim, int nFf,
        float ropeFreqBase, float normEps,
        int scaleBits)
    {
        ModelName = modelName;
        m_tokenizer = tokenizer;
        m_tokenEmbeddings = tokenEmbeddings;
        m_outputNormW = outputNormW;
        m_layers = layers;
        m_nEmbd = nEmbd;
        m_nHeads = nHeads;
        m_nKvHeads = nKvHeads;
        m_headDim = headDim;
        m_kvDim = nKvHeads * headDim;
        m_nFf = nFf;
        m_ropeFreqBase = ropeFreqBase;
        m_normEps = normEps;
        m_scaleBits = scaleBits;
    }

    public static IntegerGemmaSource Load(string ggufPath, int scaleBits = 30,
        Action<int, int, string>? onProgress = null)
    {
        using GgufReader reader = GgufReader.Open(ggufPath);

        string arch = reader.Architecture;
        if (!arch.StartsWith("gemma", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                $"Architecture '{arch}' is not supported by IntegerGemmaSource.");

        return LoadFromReader(reader, scaleBits, onProgress);
    }

    public static IntegerGemmaSource LoadFromModelDir(string modelName, string modelDir,
        int scaleBits = 30, Action<int, int, string>? onProgress = null)
    {
        string? ggufPath = OllamaModelLocator.LocateGguf(modelName, modelDir);
        if (ggufPath is null)
            throw new FileNotFoundException(
                $"No GGUF blob found for model '{modelName}' in '{modelDir}'.");
        return Load(ggufPath, scaleBits, onProgress);
    }

    private static IntegerGemmaSource LoadFromReader(GgufReader reader, int scaleBits,
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

        int totalSteps = 2 + nLayers * 13;
        int step = 0;
        void Report(string name) { step++; onProgress?.Invoke(step, totalSteps, name); }

        long[] tokenEmbd = Q(reader.ReadTensorF32("token_embd.weight"), scaleBits);
        Report("token_embd.weight");

        long[] outputNormW = Q(reader.ReadTensorF32("output_norm.weight"), scaleBits);
        Report("output_norm.weight");

        GemmaLayerWeights[] layers = new GemmaLayerWeights[nLayers];
        for (int i = 0; i < nLayers; i++)
        {
            string pfx = $"blk.{i}";

            long[] attnNormW     = Q(reader.ReadTensorF32($"{pfx}.attn_norm.weight"), scaleBits);
            Report($"blk.{i} attn_norm");
            long[] attnQ         = Q(reader.ReadTensorF32($"{pfx}.attn_q.weight"), scaleBits);
            Report($"blk.{i} attn_q");
            long[] attnK         = Q(reader.ReadTensorF32($"{pfx}.attn_k.weight"), scaleBits);
            Report($"blk.{i} attn_k");
            long[] attnV         = Q(reader.ReadTensorF32($"{pfx}.attn_v.weight"), scaleBits);
            Report($"blk.{i} attn_v");
            long[] attnQNormW    = Q(reader.ReadTensorF32($"{pfx}.attn_q_norm.weight"), scaleBits);
            Report($"blk.{i} attn_q_norm");
            long[] attnKNormW    = Q(reader.ReadTensorF32($"{pfx}.attn_k_norm.weight"), scaleBits);
            Report($"blk.{i} attn_k_norm");
            long[] attnOutput    = Q(reader.ReadTensorF32($"{pfx}.attn_output.weight"), scaleBits);
            Report($"blk.{i} attn_output");
            long[] postAttnNormW = Q(reader.ReadTensorF32($"{pfx}.post_attention_norm.weight"), scaleBits);
            Report($"blk.{i} post_attn_norm");
            long[] ffnNormW      = Q(reader.ReadTensorF32($"{pfx}.ffn_norm.weight"), scaleBits);
            Report($"blk.{i} ffn_norm");
            long[] ffnGate       = Q(reader.ReadTensorF32($"{pfx}.ffn_gate.weight"), scaleBits);
            Report($"blk.{i} ffn_gate");
            long[] ffnUp         = Q(reader.ReadTensorF32($"{pfx}.ffn_up.weight"), scaleBits);
            Report($"blk.{i} ffn_up");
            long[] ffnDown       = Q(reader.ReadTensorF32($"{pfx}.ffn_down.weight"), scaleBits);
            Report($"blk.{i} ffn_down");
            long[] postFfwNormW  = Q(reader.ReadTensorF32($"{pfx}.post_ffw_norm.weight"), scaleBits);
            Report($"blk.{i} post_ffw_norm ({i + 1}/{nLayers})");

            layers[i] = new GemmaLayerWeights
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

        return new IntegerGemmaSource(
            modelName: reader.ModelName,
            tokenizer: tokenizer,
            tokenEmbeddings: tokenEmbd,
            outputNormW: outputNormW,
            layers: layers,
            nEmbd: nEmbd,
            nHeads: nHeads,
            nKvHeads: nKvHeads,
            headDim: headDim,
            nFf: nFf,
            ropeFreqBase: ropeFreqBase,
            normEps: normEps,
            scaleBits: scaleBits);
    }

    private static long[] Q(float[] source, int scaleBits)
        => IntegerMatMul.QuantizeFromFloat(source, scaleBits).Data;

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
