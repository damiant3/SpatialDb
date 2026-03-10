using SparseLattice.Math;
////////////////////////////////////////////////
namespace SparseLattice.Test.Math;

[TestClass]
public sealed class EmbeddingAdapterSparsityBudgetTests
{
    [TestMethod]
    public void Unit_Quantize_SparsityBudget_CapsDimensions()
    {
        // 5 non-zero dims, budget = 3 → only top-3 by abs value survive
        float[] embedding = [0.0f, 0.5f, 0.2f, 0.9f, 0.0f, 0.3f, 0.0f, 0.1f];
        QuantizationOptions options = new() { ZeroThreshold = 0.0f, SparsityBudget = 3 };

        SparseVector result = EmbeddingAdapter.Quantize(embedding, options);

        Assert.AreEqual(3, result.NonzeroCount,
            "SparsityBudget=3 must yield exactly 3 nonzero entries.");
    }

    [TestMethod]
    public void Unit_Quantize_SparsityBudget_KeepsLargestAbsoluteValues()
    {
        // Values: dim1=0.1, dim2=0.9, dim3=0.5 — top-2 by abs should be dim2 and dim3
        float[] embedding = [0.0f, 0.1f, 0.9f, 0.5f];
        QuantizationOptions options = new() { ZeroThreshold = 0.0f, SparsityBudget = 2 };

        SparseVector result = EmbeddingAdapter.Quantize(embedding, options);

        Assert.AreEqual(2, result.NonzeroCount);
        Assert.AreNotEqual(0L, result.ValueAt(2), "dim2 (0.9) should survive budget trim.");
        Assert.AreNotEqual(0L, result.ValueAt(3), "dim3 (0.5) should survive budget trim.");
        Assert.AreEqual(0L, result.ValueAt(1), "dim1 (0.1) should be trimmed.");
    }

    [TestMethod]
    public void Unit_Quantize_SparsityBudget_NegativeValuesCountByAbsolute()
    {
        // -0.8 has larger abs than +0.3; budget=1 should keep -0.8
        float[] embedding = [0.3f, -0.8f];
        QuantizationOptions options = new() { ZeroThreshold = 0.0f, SparsityBudget = 1 };

        SparseVector result = EmbeddingAdapter.Quantize(embedding, options);

        Assert.AreEqual(1, result.NonzeroCount);
        Assert.AreNotEqual(0L, result.ValueAt(1), "dim1 (-0.8) should survive budget.");
        Assert.AreEqual(0L, result.ValueAt(0), "dim0 (0.3) should be trimmed.");
    }

    [TestMethod]
    public void Unit_Quantize_SparsityBudget_LargerThanNnz_NoTrimming()
    {
        float[] embedding = [0.0f, 0.5f, 0.0f, 0.3f];
        QuantizationOptions options = new() { ZeroThreshold = 0.0f, SparsityBudget = 10 };

        SparseVector result = EmbeddingAdapter.Quantize(embedding, options);
        Assert.AreEqual(2, result.NonzeroCount,
            "Budget larger than actual nnz should leave all entries intact.");
    }

    [TestMethod]
    public void Unit_Quantize_SparsityBudget_Null_NoChange()
    {
        float[] embedding = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f];
        QuantizationOptions withBudget = new() { ZeroThreshold = 0.0f, SparsityBudget = null };
        QuantizationOptions withoutBudget = new() { ZeroThreshold = 0.0f };

        SparseVector a = EmbeddingAdapter.Quantize(embedding, withBudget);
        SparseVector b = EmbeddingAdapter.Quantize(embedding, withoutBudget);

        Assert.AreEqual(b.NonzeroCount, a.NonzeroCount,
            "Null SparsityBudget must produce same result as no budget.");
    }

    [TestMethod]
    public void Invariant_Quantize_SparsityBudget_ResultSortedByDimension()
    {
        float[] embedding = [0.4f, 0.1f, 0.9f, 0.2f, 0.7f, 0.3f];
        QuantizationOptions options = new() { ZeroThreshold = 0.0f, SparsityBudget = 3 };

        SparseVector result = EmbeddingAdapter.Quantize(embedding, options);

        ReadOnlySpan<SparseEntry> entries = result.Entries;
        for (int i = 1; i < entries.Length; i++)
            Assert.IsTrue(entries[i].Dimension > entries[i - 1].Dimension,
                $"Entries not sorted at index {i}: dim {entries[i - 1].Dimension} >= {entries[i].Dimension}");
    }

    [TestMethod]
    public void Invariant_Quantize_SparsityBudget_ThresholdAppliedBeforeBudget()
    {
        // threshold kills dim0 (0.005f), leaving 4; budget=2 then trims to 2
        float[] embedding = [0.005f, 0.5f, 0.9f, 0.2f, 0.3f];
        QuantizationOptions options = new() { ZeroThreshold = 0.01f, SparsityBudget = 2 };

        SparseVector result = EmbeddingAdapter.Quantize(embedding, options);

        Assert.AreEqual(2, result.NonzeroCount);
        Assert.AreEqual(0L, result.ValueAt(0), "dim0 killed by threshold, not by budget.");
    }
}
