using SparseLattice.Gguf;
///////////////////////////////////////////////
namespace SparseLattice.Test.Gguf;

[TestClass]
public sealed class IntegerGemmaSourceTests
{
    private static readonly string s_modelName = "embeddinggemma";

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_GemmaInteger_LoadAndEmbed_Smoke()
    {
        string? gguf = ResolveGgufPath();
        if (gguf is null)
        {
            Assert.Inconclusive("embeddinggemma GGUF not found.");
            return;
        }

        using IntegerGemmaSource src = IntegerGemmaSource.Load(gguf);
        Assert.AreEqual(768, src.Dimensions);
        Assert.AreEqual(30, src.ScaleBits);

        float[] embedding = src.ForwardFloat("hello world");
        Assert.AreEqual(768, embedding.Length);

        float norm = 0f;
        for (int i = 0; i < embedding.Length; i++)
            norm += embedding[i] * embedding[i];
        norm = MathF.Sqrt(norm);

        Console.WriteLine($"[E4-6] Smoke: dim={embedding.Length}, L2 norm={norm:F6}");
        Console.WriteLine($"[E4-6]   first 5: [{string.Join(", ", embedding.Take(5).Select(v => $"{v:F6}"))}]");
        Assert.IsTrue(norm > 0.9f && norm < 1.1f,
            $"L2 norm should be ≈1.0, got {norm:F6}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_GemmaInteger_Deterministic()
    {
        string? gguf = ResolveGgufPath();
        if (gguf is null)
        {
            Assert.Inconclusive("embeddinggemma GGUF not found.");
            return;
        }

        using IntegerGemmaSource src = IntegerGemmaSource.Load(gguf);
        string text = "public int Add(int a, int b) => a + b;";

        float[] ref1 = await src.EmbedAsync(text);
        float[] ref2 = await src.EmbedAsync(text);

        for (int d = 0; d < ref1.Length; d++)
            Assert.AreEqual(ref1[d], ref2[d], $"Dim {d}: not bit-identical across runs.");

        Console.WriteLine($"[E4-6] Determinism: 768-dim bit-identical ✓");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_GemmaInteger_SemanticCoherence()
    {
        string? gguf = ResolveGgufPath();
        if (gguf is null)
        {
            Assert.Inconclusive("embeddinggemma GGUF not found.");
            return;
        }

        using IntegerGemmaSource src = IntegerGemmaSource.Load(gguf);

        (string, string)[] similarPairs =
        [
            ("public int Add(int a, int b) => a + b;", "int sum = x + y;"),
            ("The cat sat on the mat.", "A kitten rested on the rug."),
            ("HTTP request failed with 404.", "Server returned a not-found error."),
        ];

        (string, string)[] dissimilarPairs =
        [
            ("public int Add(int a, int b) => a + b;", "The cat sat on the mat."),
            ("HTTP request failed with 404.", "int sum = x + y;"),
            ("A kitten rested on the rug.", "Server returned a not-found error."),
        ];

        Console.WriteLine("[E4-6] Semantic coherence:");

        float avgSimilar = 0f;
        foreach ((string a, string b) in similarPairs)
        {
            float[] va = await src.EmbedAsync(a);
            float[] vb = await src.EmbedAsync(b);
            float cos = CosineSimilarity(va, vb);
            avgSimilar += cos;
            Console.WriteLine($"[E4-6]   Similar:    cos={cos:F4}  \"{Trunc(a, 40)}\" vs \"{Trunc(b, 40)}\"");
        }
        avgSimilar /= similarPairs.Length;

        float avgDissimilar = 0f;
        foreach ((string a, string b) in dissimilarPairs)
        {
            float[] va = await src.EmbedAsync(a);
            float[] vb = await src.EmbedAsync(b);
            float cos = CosineSimilarity(va, vb);
            avgDissimilar += cos;
            Console.WriteLine($"[E4-6]   Dissimilar: cos={cos:F4}  \"{Trunc(a, 40)}\" vs \"{Trunc(b, 40)}\"");
        }
        avgDissimilar /= dissimilarPairs.Length;

        float separation = avgSimilar - avgDissimilar;
        Console.WriteLine($"[E4-6]   Avg similar:    {avgSimilar:F4}");
        Console.WriteLine($"[E4-6]   Avg dissimilar: {avgDissimilar:F4}");
        Console.WriteLine($"[E4-6]   Separation:     {separation:F4}");

        Assert.IsTrue(separation > 0.05f,
            $"Similar pairs should have higher cosine than dissimilar. " +
            $"Separation={separation:F4}, need > 0.05.");
    }

    private static string? ResolveGgufPath()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate))
                return OllamaModelLocator.LocateGguf(s_modelName, candidate);
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < 1e-10f ? 0f : dot / denom;
    }

    private static string Trunc(string s, int max)
        => s.Length <= max ? s : s[..(max - 3)] + "...";
}
