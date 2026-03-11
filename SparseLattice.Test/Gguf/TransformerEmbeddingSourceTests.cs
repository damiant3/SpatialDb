using SparseLattice.Gguf;
//////////////////////////////////////////////////////////
namespace SparseLattice.Test.Gguf;

[TestClass]
public sealed class TransformerEmbeddingSourceTests
{
    // -----------------------------------------------------------------------
    // Helpers — minimal synthetic BERT model
    // -----------------------------------------------------------------------

    // Builds a 1-layer, 2-head, 4-dim BERT model with identity/zero weights.
    // Vocabulary: 8 tokens. BOS=0, EOS=1, UNK=2, plus "a","b","c","d","e".
    // With these settings:
    //   token embeddings = identity rows (token i has 1.0 at dim i%4, else 0)
    //   all projection weights = identity (4x4)
    //   all FFN weights = zero (so FFN sub-layer is a no-op after residual)
    //   all LayerNorm weight=1, bias=0 (pass-through after normalisation)
    //
    // The test only checks that EmbedAsync returns a vector of the right shape
    // and is L2-normalised (||v||₂ ≈ 1.0). Exact numerical values are not
    // asserted because LayerNorm over identity inputs involves non-trivial math.
    private static TransformerEmbeddingSource BuildMinimalModel()
    {
        const int nEmbd  = 4;
        const int nHeads = 2;
        const int nFf    = 4;
        const int vocabSize = 8;

        // Token embeddings: [vocabSize, nEmbd] flat row-major
        // Row i = e_i (standard basis, cycling for i >= nEmbd)
        float[] tokenEmbd = new float[vocabSize * nEmbd];
        for (int i = 0; i < vocabSize; i++)
            tokenEmbd[i * nEmbd + (i % nEmbd)] = 1.0f;

        // Token type embedding: all zeros (type 0 = no contribution)
        float[] tokenType = new float[nEmbd];

        // LayerNorm: weight=1, bias=0
        float[] ones  = Ones(nEmbd);
        float[] zeros = new float[nEmbd];

        // Identity matrices for all projections
        // QKV: [nEmbd, 3*nEmbd] — first nEmbd cols = I, rest zero
        float[] qkv = IdentityPad(nEmbd, 3 * nEmbd);
        // attn_output: [nEmbd, nEmbd] = I
        float[] attnOut = Identity(nEmbd);
        // FFN: all zero weights so FFN is a residual pass-through
        float[] ffnUp   = new float[nEmbd * nFf];
        float[] ffnGate = new float[nEmbd * nFf];
        float[] ffnDown = new float[nFf * nEmbd];

        TransformerEmbeddingSource.LayerWeights layer = new()
        {
            AttnQkv    = qkv,
            AttnOutput = attnOut,
            AttnNormW  = ones,
            AttnNormB  = zeros,
            FfnUp      = ffnUp,
            FfnGate    = ffnGate,
            FfnDown    = ffnDown,
            LayerNormW = ones,
            LayerNormB = zeros,
        };

        string[] vocabTokens = ["[CLS]", "[SEP]", "[UNK]", "a", "b", "c", "d", "e"];
        WordPieceTokenizer tokenizer = new(vocabTokens,
            bosTokenId: 0, eosTokenId: 1, unkTokenId: 2);

        return new TransformerEmbeddingSource(
            modelName:          "synthetic-mini",
            tokenizer:          tokenizer,
            tokenEmbeddings:    tokenEmbd,
            tokenTypeEmbedding: tokenType,
            embdNormW:          ones,
            embdNormB:          zeros,
            layers:             [layer],
            nEmbd:              nEmbd,
            nHeads:             nHeads,
            nFf:                nFf,
            ropeFreqBase:       10000f,
            layerNormEps:       1e-12f);
    }

    private static float[] Identity(int n)
    {
        float[] m = new float[n * n];
        for (int i = 0; i < n; i++)
            m[i * n + i] = 1.0f;
        return m;
    }

    // Identity in the first n columns, zeros in the remaining cols.
    private static float[] IdentityPad(int n, int totalCols)
    {
        float[] m = new float[n * totalCols];
        for (int i = 0; i < n; i++)
            m[i * totalCols + i] = 1.0f;
        return m;
    }

    private static float[] Ones(int n)
    {
        float[] a = new float[n];
        for (int i = 0; i < n; i++) a[i] = 1.0f;
        return a;
    }

    // -----------------------------------------------------------------------
    // Unit tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Unit_EmbedAsync_ReturnsVectorOfCorrectLength()
    {
        using TransformerEmbeddingSource source = BuildMinimalModel();

        float[] result = await source.EmbedAsync("a");

        Assert.AreEqual(4, result.Length, "Embedding length should match n_embd=4");
    }

    [TestMethod]
    public async Task Unit_EmbedAsync_ResultIsL2Normalized()
    {
        using TransformerEmbeddingSource source = BuildMinimalModel();

        float[] result = await source.EmbedAsync("a b");

        float norm = 0f;
        foreach (float v in result) norm += v * v;
        Assert.AreEqual(1.0f, MathF.Sqrt(norm), 1e-4f, "Result must be L2-normalized");
    }

    [TestMethod]
    public async Task Unit_EmbedAsync_EmptyText_ReturnsNormalizedVector()
    {
        using TransformerEmbeddingSource source = BuildMinimalModel();

        // Even with empty text the model still has BOS+SEP; mean pool over zero
        // content tokens falls back to the CLS/SEP embeddings.
        float[] result = await source.EmbedAsync("");

        Assert.AreEqual(4, result.Length);
        float norm = 0f;
        foreach (float v in result) norm += v * v;
        Assert.AreEqual(1.0f, MathF.Sqrt(norm), 1e-4f, "Empty input must still yield a normalized vector");
    }

    [TestMethod]
    public void Unit_ModelName_MatchesConstructorValue()
    {
        using TransformerEmbeddingSource source = BuildMinimalModel();
        Assert.AreEqual("synthetic-mini", source.ModelName);
    }

    [TestMethod]
    public void Unit_Dimensions_MatchesNEmbd()
    {
        using TransformerEmbeddingSource source = BuildMinimalModel();
        Assert.AreEqual(4, source.Dimensions);
    }

    [TestMethod]
    public async Task Unit_EmbedBatchAsync_ReturnsOneResultPerInput()
    {
        using TransformerEmbeddingSource source = BuildMinimalModel();

        float[][] results = await source.EmbedBatchAsync(["a", "b c", "d"]);

        Assert.AreEqual(3, results.Length);
        foreach (float[] v in results)
            Assert.AreEqual(4, v.Length);
    }

    [TestMethod]
    public async Task Unit_EmbedAsync_DifferentInputs_ProduceDifferentVectors()
    {
        using TransformerEmbeddingSource source = BuildMinimalModel();

        float[] va = await source.EmbedAsync("a");
        float[] vb = await source.EmbedAsync("b");

        // Two different tokens should produce different embeddings
        float cosine = Cosine(va, vb);
        Assert.IsTrue(cosine < 0.9999f,
            $"Embeddings for 'a' and 'b' should not be identical (cosine={cosine:F6})");
    }

    [TestMethod]
    public async Task Unit_EmbedAsync_SameInputTwice_ProducesSameVector()
    {
        using TransformerEmbeddingSource source = BuildMinimalModel();

        float[] v1 = await source.EmbedAsync("a b");
        float[] v2 = await source.EmbedAsync("a b");

        for (int i = 0; i < v1.Length; i++)
            Assert.AreEqual(v1[i], v2[i], 1e-7f, $"dim {i} not deterministic");
    }

    // -----------------------------------------------------------------------
    // Unit tests — WordPieceTokenizer
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_WordPiece_Encode_SimpleWord_ReturnsTokenIds()
    {
        string[] vocab = ["[CLS]", "[SEP]", "[UNK]", "hello", "world", "##ly"];
        WordPieceTokenizer tokenizer = new(vocab, bosTokenId: 0, eosTokenId: 1, unkTokenId: 2);

        int[] ids = tokenizer.Encode("hello world", addSpecialTokens: true);

        Assert.AreEqual(0, ids[0],  "First token must be [CLS]");
        Assert.AreEqual(1, ids[^1], "Last token must be [SEP]");
        Assert.AreEqual(4, ids.Length, "CLS + hello + world + SEP");
        Assert.AreEqual(3, ids[1], "'hello' should map to id 3");
        Assert.AreEqual(4, ids[2], "'world' should map to id 4");
    }

    [TestMethod]
    public void Unit_WordPiece_Encode_UnknownWord_ReturnsUnk()
    {
        string[] vocab = ["[CLS]", "[SEP]", "[UNK]", "known"];
        WordPieceTokenizer tokenizer = new(vocab, bosTokenId: 0, eosTokenId: 1, unkTokenId: 2);

        int[] ids = tokenizer.Encode("unknown", addSpecialTokens: false);

        CollectionAssert.AreEqual(new int[] { 2 }, ids, "Unknown word should produce [UNK]");
    }

    [TestMethod]
    public void Unit_WordPiece_Encode_SubwordSplit_UsesContinuationPrefix()
    {
        // "playing" = "play" + "##ing"
        string[] vocab = ["[CLS]", "[SEP]", "[UNK]", "play", "##ing"];
        WordPieceTokenizer tokenizer = new(vocab, bosTokenId: 0, eosTokenId: 1, unkTokenId: 2);

        int[] ids = tokenizer.Encode("playing", addSpecialTokens: false);

        CollectionAssert.AreEqual(new int[] { 3, 4 }, ids);
    }

    [TestMethod]
    public void Unit_WordPiece_Decode_RemovesContinuationPrefix()
    {
        string[] vocab = ["[CLS]", "[SEP]", "[UNK]", "play", "##ing"];
        WordPieceTokenizer tokenizer = new(vocab, bosTokenId: 0, eosTokenId: 1, unkTokenId: 2);

        int[] ids = tokenizer.Encode("playing", addSpecialTokens: true);
        string decoded = tokenizer.Decode(ids);

        Assert.AreEqual("playing", decoded);
    }

    [TestMethod]
    public void Unit_WordPiece_Encode_SpecialTokensOff_NoBosOrEos()
    {
        string[] vocab = ["[CLS]", "[SEP]", "[UNK]", "hi"];
        WordPieceTokenizer tokenizer = new(vocab, bosTokenId: 0, eosTokenId: 1, unkTokenId: 2);

        int[] ids = tokenizer.Encode("hi", addSpecialTokens: false);

        CollectionAssert.DoesNotContain(ids, 0, "BOS must not appear");
        CollectionAssert.DoesNotContain(ids, 1, "EOS must not appear");
    }

    // -----------------------------------------------------------------------
    // Integration tests — real nomic-embed-text GGUF
    // -----------------------------------------------------------------------

    private static string? FindNomicGguf()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate))
                return OllamaModelLocator.LocateGguf("nomic-embed-text", candidate);
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_NomicEmbedText_EmbedAsync_Returns768DimNormalizedVector()
    {
        string? ggufPath = FindNomicGguf();
        if (ggufPath is null)
        {
            Assert.Inconclusive("nomic-embed-text GGUF not found — skipping integration test.");
            return;
        }

        using TransformerEmbeddingSource source = TransformerEmbeddingSource.Load(ggufPath);

        Assert.AreEqual(768, source.Dimensions, "nomic-embed-text must report 768 dimensions");

        float[] result = await source.EmbedAsync("public static void Main");

        Assert.AreEqual(768, result.Length, "Embedding length must be 768");

        float norm = 0f;
        foreach (float v in result) norm += v * v;
        Assert.AreEqual(1.0f, MathF.Sqrt(norm), 1e-3f, "Result must be L2-normalized");

        bool hasNaN = result.Any(float.IsNaN);
        Assert.IsFalse(hasNaN, "Embedding must not contain NaN values");
        Console.WriteLine($"[E4] Embedding sample: [{string.Join(", ", result.Take(5).Select(v => v.ToString("F4")))}]");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_NomicEmbedText_LoadFromModelDir_Works()
    {
        string? dir = AppContext.BaseDirectory;
        string? embeddingsDir = null;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate)) { embeddingsDir = candidate; break; }
            dir = Path.GetDirectoryName(dir);
        }
        if (embeddingsDir is null || OllamaModelLocator.LocateGguf("nomic-embed-text", embeddingsDir) is null)
        {
            Assert.Inconclusive("nomic-embed-text GGUF not found — skipping integration test.");
            return;
        }

        using TransformerEmbeddingSource source =
            TransformerEmbeddingSource.LoadFromModelDir("nomic-embed-text", embeddingsDir);

        float[] result = await source.EmbedAsync("hello world");
        Assert.AreEqual(768, result.Length);
    }

    // -----------------------------------------------------------------------
    // Diagnostic tests
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_NomicEmbedText_DiagnoseForwardPass_PrintsIntermediates()
    {
        string? ggufPath = FindNomicGguf();
        if (ggufPath is null)
        {
            Assert.Inconclusive("nomic-embed-text GGUF not found.");
            return;
        }

        using TransformerEmbeddingSource source = TransformerEmbeddingSource.Load(ggufPath);

        // Test with simple text
        float[] result = await source.EmbedAsync("the cat sat on the mat");
        Console.WriteLine($"[CS simple] final[:4] = [{string.Join(", ", result.Take(4).Select(v => $"{v:F8}"))}]");

        // Test with complex code string
        string codeText = "public int Add(int a, int b) => a + b;";
        float[] codeResult = await source.EmbedAsync(codeText);
        Console.WriteLine($"[CS code]   final[:4] = [{string.Join(", ", codeResult.Take(4).Select(v => $"{v:F8}"))}]");

        // Print token IDs for the code text
        WordPieceTokenizer tokenizer = WordPieceTokenizer.FromGguf(SparseLattice.Gguf.GgufReader.Open(ggufPath));
        int[] ids = tokenizer.Encode(codeText);
        Console.WriteLine($"[CS code] Token IDs ({ids.Length}): [{string.Join(", ", ids)}]");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static float Cosine(float[] a, float[] b)
    {
        float dot = 0f, na = 0f, nb = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na  += a[i] * a[i];
            nb  += b[i] * b[i];
        }
        return dot / (MathF.Sqrt(na) * MathF.Sqrt(nb) + 1e-10f);
    }
}
