using SparseLattice.Math;
/////////////////////////////////////////////
namespace SparseLattice.Test.Math;

[TestClass]
public sealed class EmbeddingAdapterTests
{
    [TestMethod]
    public void Unit_Quantize_KillsNoiseUnderThreshold()
    {
        float[] embedding = [0.0f, 0.005f, -0.005f, 0.5f, -0.8f, 0.001f];
        QuantizationOptions options = new() { ZeroThreshold = 0.01f };
        SparseVector result = EmbeddingAdapter.Quantize(embedding, options);

        Assert.AreEqual(2, result.NonzeroCount);
        Assert.AreNotEqual(0L, result.ValueAt(3));
        Assert.AreNotEqual(0L, result.ValueAt(4));
        Assert.AreEqual(0L, result.ValueAt(0));
        Assert.AreEqual(0L, result.ValueAt(1));
        Assert.AreEqual(0L, result.ValueAt(2));
        Assert.AreEqual(0L, result.ValueAt(5));
    }

    [TestMethod]
    public void Unit_Quantize_AllZeros_ProducesEmptySparse()
    {
        float[] embedding = [0.0f, 0.005f, -0.005f];
        SparseVector result = EmbeddingAdapter.Quantize(embedding);
        Assert.AreEqual(0, result.NonzeroCount);
        Assert.AreEqual(3, result.TotalDimensions);
    }

    [TestMethod]
    public void Unit_Quantize_SignPreserved()
    {
        float[] embedding = [0.0f, 0.5f, -0.5f];
        SparseVector result = EmbeddingAdapter.Quantize(embedding);
        Assert.IsTrue(result.ValueAt(1) > 0L);
        Assert.IsTrue(result.ValueAt(2) < 0L);
    }

    [TestMethod]
    public void Unit_QuantizeDense_AllDimensionsPresent()
    {
        float[] embedding = [0.0f, 0.5f, -0.5f];
        LongVectorN result = EmbeddingAdapter.QuantizeDense(embedding);
        Assert.AreEqual(3, result.Dimensions);
        Assert.AreEqual(0L, result[0]);
        Assert.IsTrue(result[1] > 0L);
        Assert.IsTrue(result[2] < 0L);
    }

    [TestMethod]
    public void Unit_Dequantize_RoundTrip_Reproducible()
    {
        float[] original = [0.0f, 0.5f, -0.7f, 0.0f, 0.3f];
        QuantizationOptions options = new() { ZeroThreshold = 0.01f };

        SparseVector quantized = EmbeddingAdapter.Quantize(original, options);
        float[] dequantized = EmbeddingAdapter.Dequantize(quantized, options);
        SparseVector requantized = EmbeddingAdapter.Quantize(dequantized, options);

        Assert.AreEqual(quantized.NonzeroCount, requantized.NonzeroCount);
        for (int i = 0; i < original.Length; i++)
            Assert.AreEqual(quantized.ValueAt((ushort)i), requantized.ValueAt((ushort)i));
    }

    [TestMethod]
    public void Invariant_Quantize_NoOverflow()
    {
        float[] extremes = [1.0f, -1.0f, 0.9999f, -0.9999f];
        QuantizationOptions options = new() { ZeroThreshold = 0.0f, GlobalScale = long.MaxValue };

        SparseVector result = EmbeddingAdapter.Quantize(extremes, options);
        foreach (SparseEntry entry in result.Entries)
        {
            Assert.IsTrue(entry.Value >= long.MinValue);
            Assert.IsTrue(entry.Value <= long.MaxValue);
        }
    }

    [TestMethod]
    public void Unit_Dequantize_LongVectorN()
    {
        float[] original = [0.0f, 0.5f, -0.7f];
        QuantizationOptions options = new() { ZeroThreshold = 0.01f };
        LongVectorN quantized = EmbeddingAdapter.QuantizeDense(original, options);
        float[] dequantized = EmbeddingAdapter.Dequantize(quantized, options);

        for (int i = 0; i < original.Length; i++)
        {
            float delta = MathF.Abs(original[i] - dequantized[i]);
            Assert.IsTrue(delta < 0.02f, $"Dimension {i}: delta {delta} too large");
        }
    }
}
