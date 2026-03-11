using SparseLattice.Gguf;
///////////////////////////////////////////////
namespace SparseLattice.Test.Gguf;

[TestClass]
public sealed class GgufProbeTests
{
    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_BF16Dequant_ProducesValidWeights()
    {
        string? gguf = LocateModel("embeddinggemma");
        if (gguf is null)
        {
            Assert.Inconclusive("embeddinggemma GGUF not found.");
            return;
        }

        using GgufReader reader = GgufReader.Open(gguf);

        float[] attnNorm = reader.ReadTensorF32("blk.0.attn_norm.weight");
        Assert.IsTrue(attnNorm.All(v => !float.IsNaN(v) && !float.IsInfinity(v)));

        float[] attnQ = reader.ReadTensorF32("blk.0.attn_q.weight");
        Assert.IsTrue(attnQ.All(v => !float.IsNaN(v) && !float.IsInfinity(v)));

        float absMax = attnQ.Max(MathF.Abs);
        Assert.IsTrue(absMax > 0.001f && absMax < 10f,
            $"BF16 weight abs max {absMax} outside expected range.");
    }

    private static string? LocateModel(string modelName)
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate))
                return OllamaModelLocator.LocateGguf(modelName, candidate);
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
