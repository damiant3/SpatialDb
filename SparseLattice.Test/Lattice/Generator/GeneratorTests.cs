using SparseLattice.Lattice;
using SparseLattice.Lattice.Generator;
using SparseLattice.Math;
///////////////////////////////////////////////////
namespace SparseLattice.Test.Lattice.Generator;

[TestClass]
public sealed class SparseLatticeCodeGeneratorTests
{
    [TestMethod]
    public void Unit_Generator_ProducesCompilableSource_DefaultDescriptor()
    {
        PartitionerDescriptor descriptor = new()
        {
            Dimensions = 10,
            MaxNonzeros = 4,
            ClassName = "TestPartitioner10",
            Namespace = "SparseLattice.Test.Generated",
        };

        string source = SparseLatticeCodeGenerator.GeneratePartitionerSource(descriptor);
        Assert.IsFalse(string.IsNullOrWhiteSpace(source));
        StringAssert.Contains(source, "TestPartitioner10");
        StringAssert.Contains(source, "IOptimizedPartitioner");
        StringAssert.Contains(source, "PartitionInPlace");
    }

    [TestMethod]
    public void Unit_Generator_ProducesCorrectDimensionsProperty()
    {
        PartitionerDescriptor descriptor = new() { Dimensions = 768, ClassName = "P768", Namespace = "SparseLattice.Test.Generated" };
        string source = SparseLatticeCodeGenerator.GeneratePartitionerSource(descriptor);
        StringAssert.Contains(source, "public int Dimensions => 768;");
    }

    [TestMethod]
    public void Unit_Generator_RejectsZeroDimensions()
    {
        PartitionerDescriptor descriptor = new() { Dimensions = 0 };
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            SparseLatticeCodeGenerator.GeneratePartitionerSource(descriptor));
    }

    [TestMethod]
    public void Unit_Generator_RejectsZeroMaxNonzeros()
    {
        PartitionerDescriptor descriptor = new() { Dimensions = 10, MaxNonzeros = 0 };
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            SparseLatticeCodeGenerator.GeneratePartitionerSource(descriptor));
    }

    [TestMethod]
    public void Unit_Generator_FullyQualifiedClassName_Correct()
    {
        PartitionerDescriptor descriptor = new()
        {
            Dimensions = 32,
            ClassName = "FastPartitioner32",
            Namespace = "My.Ns",
        };
        string fqn = SparseLatticeCodeGenerator.FullyQualifiedClassName(descriptor);
        Assert.AreEqual("My.Ns.FastPartitioner32", fqn);
    }
}

[TestClass]
public sealed class GeneratedPartitionerLoaderTests
{
    [TestMethod]
    public void Unit_Loader_CompilesAndLoads_SmallDimensions()
    {
        PartitionerDescriptor descriptor = new()
        {
            Dimensions = 8,
            MaxNonzeros = 4,
            ClassName = "Partitioner8",
            Namespace = "SparseLattice.Test.Generated",
        };

        using GeneratedPartitionerLoader loader = GeneratedPartitionerLoader.Compile(descriptor);
        Assert.IsNotNull(loader.Partitioner);
        Assert.AreEqual(8, loader.Partitioner.Dimensions);
    }

    [TestMethod]
    public void Unit_Loader_CompilesAndLoads_LargeDimensions()
    {
        PartitionerDescriptor descriptor = new()
        {
            Dimensions = 768,
            MaxNonzeros = 32,
            ClassName = "Partitioner768",
            Namespace = "SparseLattice.Test.Generated",
        };

        using GeneratedPartitionerLoader loader = GeneratedPartitionerLoader.Compile(descriptor);
        Assert.AreEqual(768, loader.Partitioner.Dimensions);
    }

    [TestMethod]
    public void Integration_GeneratedPartitioner_PartitionInPlace_MatchesGenericResult()
    {
        PartitionerDescriptor descriptor = new()
        {
            Dimensions = 10,
            MaxNonzeros = 4,
            ClassName = "PartitionerEquivalence10",
            Namespace = "SparseLattice.Test.Generated",
        };

        using GeneratedPartitionerLoader loader = GeneratedPartitionerLoader.Compile(descriptor);
        IOptimizedPartitioner generated = loader.Partitioner;

        SparseOccupant<string>[] MakeItems()
        {
            return
            [
                new(new([new(2, 900L)], 10), "a"),
                new(new([new(2, 100L)], 10), "b"),
                new(new([new(2, 500L)], 10), "c"),
                new(new([new(2, -300L)], 10), "d"),
                new(new([new(2, 700L)], 10), "e"),
                new(new([new(2, 200L)], 10), "f"),
            ];
        }

        SparseOccupant<string>[] forGenerated = MakeItems();
        SparseOccupant<string>[] forGeneric = MakeItems();

        const ushort splitDim = 2;
        const long pivot = 400L;

        int generatedBoundary = generated.PartitionInPlace(forGenerated.AsSpan(), splitDim, pivot);
        int genericBoundary = GenericPartitionInPlace(forGeneric.AsSpan(), splitDim, pivot);

        Assert.AreEqual(genericBoundary, generatedBoundary,
            $"Boundary mismatch: generated={generatedBoundary}, generic={genericBoundary}");

        // Both halves must contain identical payload sets (order within halves may differ)
        List<string> generatedBelow = PayloadsInSpan(forGenerated.AsSpan()[..generatedBoundary]);
        List<string> genericBelow = PayloadsInSpan(forGeneric.AsSpan()[..genericBoundary]);
        generatedBelow.Sort();
        genericBelow.Sort();
        CollectionAssert.AreEqual(genericBelow, generatedBelow, "Below-pivot sets differ");

        List<string> generatedAbove = PayloadsInSpan(forGenerated.AsSpan()[generatedBoundary..]);
        List<string> genericAbove = PayloadsInSpan(forGeneric.AsSpan()[genericBoundary..]);
        generatedAbove.Sort();
        genericAbove.Sort();
        CollectionAssert.AreEqual(genericAbove, generatedAbove, "Above-pivot sets differ");
    }

    [TestMethod]
    public void Integration_GeneratedPartitioner_EmptySpan_ReturnsBoundaryZero()
    {
        PartitionerDescriptor descriptor = new()
        {
            Dimensions = 5,
            ClassName = "PartitionerEmptySpan",
            Namespace = "SparseLattice.Test.Generated",
        };

        using GeneratedPartitionerLoader loader = GeneratedPartitionerLoader.Compile(descriptor);
        int boundary = loader.Partitioner.PartitionInPlace(Span<SparseOccupant<string>>.Empty, 0, 100L);
        Assert.AreEqual(0, boundary);
    }

    [TestMethod]
    public void Integration_GeneratedPartitioner_AllBelow_BoundaryEqualsLength()
    {
        PartitionerDescriptor descriptor = new()
        {
            Dimensions = 5,
            ClassName = "PartitionerAllBelow",
            Namespace = "SparseLattice.Test.Generated",
        };

        using GeneratedPartitionerLoader loader = GeneratedPartitionerLoader.Compile(descriptor);

        SparseOccupant<string>[] items =
        [
            new(new([new(0, 10L)], 5), "x"),
            new(new([new(0, 20L)], 5), "y"),
            new(new([new(0, 30L)], 5), "z"),
        ];

        int boundary = loader.Partitioner.PartitionInPlace(items.AsSpan(), 0, 1000L);
        Assert.AreEqual(3, boundary, "All items are below 1000, boundary should equal Length");
    }

    [TestMethod]
    public void Integration_GeneratedPartitioner_AllAbove_BoundaryEqualsZero()
    {
        PartitionerDescriptor descriptor = new()
        {
            Dimensions = 5,
            ClassName = "PartitionerAllAbove",
            Namespace = "SparseLattice.Test.Generated",
        };

        using GeneratedPartitionerLoader loader = GeneratedPartitionerLoader.Compile(descriptor);

        SparseOccupant<string>[] items =
        [
            new(new([new(0, 500L)], 5), "x"),
            new(new([new(0, 600L)], 5), "y"),
            new(new([new(0, 700L)], 5), "z"),
        ];

        int boundary = loader.Partitioner.PartitionInPlace(items.AsSpan(), 0, 1L);
        Assert.AreEqual(0, boundary, "All items are above 1, boundary should equal 0");
    }

    [TestMethod]
    public void Integration_GeneratedPartitioner_MissingDimension_TreatedAsZero()
    {
        PartitionerDescriptor descriptor = new()
        {
            Dimensions = 5,
            ClassName = "PartitionerMissingDim",
            Namespace = "SparseLattice.Test.Generated",
        };

        using GeneratedPartitionerLoader loader = GeneratedPartitionerLoader.Compile(descriptor);

        // dim 1 is absent in all vectors — should be treated as 0
        SparseOccupant<string>[] items =
        [
            new(new([new(0, 100L)], 5), "has-dim0-only"),
            new(new([new(2, 200L)], 5), "has-dim2-only"),
        ];

        // splitting on dim 1 with pivot 50 — both vectors have 0 for dim 1 ? both >= 0 (pivot is 50 > 0)
        // so all items should be below pivot? No: 0 < 50, so both go to below partition
        int boundary = loader.Partitioner.PartitionInPlace(items.AsSpan(), 1, 50L);
        Assert.AreEqual(2, boundary, "Both items have 0 for dim 1 which is < 50, so all should be below");
    }

    [TestMethod]
    public void Unit_Loader_Dispose_DoesNotThrow()
    {
        PartitionerDescriptor descriptor = new()
        {
            Dimensions = 4,
            ClassName = "PartitionerDispose",
            Namespace = "SparseLattice.Test.Generated",
        };

        GeneratedPartitionerLoader loader = GeneratedPartitionerLoader.Compile(descriptor);
        loader.Dispose();
        loader.Dispose();  // double-dispose should not throw
    }

    [TestMethod]
    public void Unit_Loader_TwoIndependentLoaders_DoNotInterfere()
    {
        PartitionerDescriptor d1 = new() { Dimensions = 3, ClassName = "PIndep3", Namespace = "SparseLattice.Test.Generated" };
        PartitionerDescriptor d2 = new() { Dimensions = 7, ClassName = "PIndep7", Namespace = "SparseLattice.Test.Generated" };

        using GeneratedPartitionerLoader loader1 = GeneratedPartitionerLoader.Compile(d1);
        using GeneratedPartitionerLoader loader2 = GeneratedPartitionerLoader.Compile(d2);

        Assert.AreEqual(3, loader1.Partitioner.Dimensions);
        Assert.AreEqual(7, loader2.Partitioner.Dimensions);
    }

    // --- helpers ---

    private static int GenericPartitionInPlace(Span<SparseOccupant<string>> span, ushort dimension, long pivotValue)
    {
        int left = 0;
        int right = span.Length - 1;
        while (left <= right)
        {
            long leftVal = span[left].Position.ValueAt(dimension);
            if (leftVal < pivotValue) { left++; continue; }
            long rightVal = span[right].Position.ValueAt(dimension);
            if (rightVal >= pivotValue) { right--; continue; }
            (span[left], span[right]) = (span[right], span[left]);
            left++;
            right--;
        }
        return left;
    }

    private static List<string> PayloadsInSpan(ReadOnlySpan<SparseOccupant<string>> span)
    {
        List<string> list = [];
        foreach (SparseOccupant<string> o in span)
            list.Add(o.Payload);
        return list;
    }
}
