using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

// Partial: weight types and GGUF loading
public sealed partial class IntegerTransformerSource
{
    // -----------------------------------------------------------------------
    // Quantized layer weights
    // -----------------------------------------------------------------------

    internal sealed class IntegerLayerWeights
    {
        public required long[] AttnQkv     { get; init; }
        public required long[] AttnOutput  { get; init; }
        public required long[] AttnNormW   { get; init; }
        public required long[] AttnNormB   { get; init; }
        public required long[] FfnUp       { get; init; }
        public required long[] FfnGate     { get; init; }
        public required long[] FfnDown     { get; init; }
        public required long[] LayerNormW  { get; init; }
        public required long[] LayerNormB  { get; init; }
    }

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    private IntegerTransformerSource(
        string modelName,
        WordPieceTokenizer tokenizer,
        long[] tokenEmbeddings,
        long[] tokenTypeEmbedding,
        long[] embdNormW,
        long[] embdNormB,
        IntegerLayerWeights[] layers,
        int nEmbd, int nHeads, int nFf,
        float ropeFreqBase,
        int scaleBits)
    {
        ModelName = modelName;
        m_tokenizer = tokenizer;
        m_tokenEmbeddings = tokenEmbeddings;
        m_tokenTypeEmbedding = tokenTypeEmbedding;
        m_embdNormW = embdNormW;
        m_embdNormB = embdNormB;
        m_layers = layers;
        m_nEmbd = nEmbd;
        m_nHeads = nHeads;
        m_nFf = nFf;
        m_ropeFreqBase = ropeFreqBase;
        m_scaleBits = scaleBits;
    }

    // -----------------------------------------------------------------------
    // Load from GGUF — quantize all weights to long[]
    // -----------------------------------------------------------------------

    /// <summary>Loads model weights from a GGUF file, quantizing to integer.</summary>
    public static IntegerTransformerSource Load(string ggufPath, int scaleBits = 30,
        Action<int, int, string>? onProgress = null)
    {
        using GgufReader reader = GgufReader.Open(ggufPath);

        string arch = reader.Architecture;
        if (!arch.Equals("nomic-bert", StringComparison.OrdinalIgnoreCase) &&
            !arch.Equals("bert", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                $"Architecture '{arch}' is not supported. Only 'nomic-bert' and 'bert' are implemented.");

        return LoadFromReader(reader, scaleBits, onProgress);
    }

    /// <summary>
    /// Resolves a model name via <see cref="OllamaModelLocator"/>, then loads the GGUF.
    /// </summary>
    public static IntegerTransformerSource LoadFromModelDir(string modelName, string modelDir,
        int scaleBits = 30, Action<int, int, string>? onProgress = null)
    {
        string? ggufPath = OllamaModelLocator.LocateGguf(modelName, modelDir);
        if (ggufPath is null)
            throw new FileNotFoundException(
                $"No GGUF blob found for model '{modelName}' in '{modelDir}'.");
        return Load(ggufPath, scaleBits, onProgress);
    }

    private static IntegerTransformerSource LoadFromReader(GgufReader reader, int scaleBits,
        Action<int, int, string>? onProgress)
    {
        string arch = reader.Architecture;
        int nEmbd = reader.EmbeddingLength;
        int nHeads = reader.HeadCount;
        int nLayers = reader.LayerCount;
        int nFf = reader.FeedForwardLength;
        float ropeFreqBase = GetFloat(reader, $"{arch}.rope.freq_base", 10000f);

        int totalSteps = 4 + nLayers * 7;
        int step = 0;
        void Report(string name) { step++; onProgress?.Invoke(step, totalSteps, name); }

        long[] tokenEmbd = Q(reader.ReadTensorF32("token_embd.weight"), scaleBits);
        Report("token_embd.weight");

        float[] tokenTypes = reader.ReadTensorF32("token_types.weight");
        float[] tokenTypeRow0 = new float[nEmbd];
        Array.Copy(tokenTypes, 0, tokenTypeRow0, 0, nEmbd);
        long[] tokenTypeQ = Q(tokenTypeRow0, scaleBits);
        Report("token_types.weight");

        long[] embdNormW = Q(reader.ReadTensorF32("token_embd_norm.weight"), scaleBits);
        Report("token_embd_norm.weight");
        long[] embdNormB = Q(reader.ReadTensorF32("token_embd_norm.bias"), scaleBits);
        Report("token_embd_norm.bias");

        IntegerLayerWeights[] layers = new IntegerLayerWeights[nLayers];
        for (int i = 0; i < nLayers; i++)
        {
            string pfx = $"blk.{i}";

            long[] attnQkv    = Q(reader.ReadTensorF32($"{pfx}.attn_qkv.weight"), scaleBits);
            Report($"blk.{i} attn_qkv");
            long[] attnOutput = Q(reader.ReadTensorF32($"{pfx}.attn_output.weight"), scaleBits);
            Report($"blk.{i} attn_output");
            long[] attnNormW  = Q(reader.ReadTensorF32($"{pfx}.attn_output_norm.weight"), scaleBits);
            long[] attnNormB  = Q(reader.ReadTensorF32($"{pfx}.attn_output_norm.bias"), scaleBits);
            Report($"blk.{i} attn_norm");
            long[] ffnUp      = Q(reader.ReadTensorF32($"{pfx}.ffn_up.weight"), scaleBits);
            Report($"blk.{i} ffn_up");
            long[] ffnGate    = Q(reader.ReadTensorF32($"{pfx}.ffn_gate.weight"), scaleBits);
            Report($"blk.{i} ffn_gate");
            long[] ffnDown    = Q(reader.ReadTensorF32($"{pfx}.ffn_down.weight"), scaleBits);
            Report($"blk.{i} ffn_down");
            long[] layerNormW = Q(reader.ReadTensorF32($"{pfx}.layer_output_norm.weight"), scaleBits);
            long[] layerNormB = Q(reader.ReadTensorF32($"{pfx}.layer_output_norm.bias"), scaleBits);
            Report($"blk.{i} layer_norm ({i + 1}/{nLayers})");

            layers[i] = new IntegerLayerWeights
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

        return new IntegerTransformerSource(
            modelName: reader.ModelName,
            tokenizer: tokenizer,
            tokenEmbeddings: tokenEmbd,
            tokenTypeEmbedding: tokenTypeQ,
            embdNormW: embdNormW,
            embdNormB: embdNormB,
            layers: layers,
            nEmbd: nEmbd,
            nHeads: nHeads,
            nFf: nFf,
            ropeFreqBase: ropeFreqBase,
            scaleBits: scaleBits);
    }

    /// <summary>Quantize float[] to long[] at the given scale.</summary>
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
}
