using System.Numerics;
using SparseLattice.Math;
/////////////////////////////////////////////
namespace SparseLattice.Test.Math;

[TestClass]
public sealed class SparseVectorTests
{
    [TestMethod]
    public void Unit_SparseVector_Construction_Valid()
    {
        SparseEntry[] entries = [new(2, 100L), new(5, -200L)];
        SparseVector vector = new(entries, 768);
        Assert.AreEqual(2, vector.NonzeroCount);
        Assert.AreEqual(768, vector.TotalDimensions);
    }

    [TestMethod]
    public void Unit_SparseVector_Construction_RejectsUnsorted()
    {
        SparseEntry[] entries = [new(5, 100L), new(2, -200L)];
        Assert.ThrowsException<ArgumentException>(() => new SparseVector(entries, 768));
    }

    [TestMethod]
    public void Unit_SparseVector_Construction_RejectsDuplicateDimension()
    {
        SparseEntry[] entries = [new(2, 100L), new(2, -200L)];
        Assert.ThrowsException<ArgumentException>(() => new SparseVector(entries, 768));
    }

    [TestMethod]
    public void Unit_SparseVector_Construction_RejectsZeroValue()
    {
        SparseEntry[] entries = [new(2, 0L)];
        Assert.ThrowsException<ArgumentException>(() => new SparseVector(entries, 768));
    }

    [TestMethod]
    public void Unit_SparseVector_Construction_RejectsDimensionOutOfRange()
    {
        SparseEntry[] entries = [new(768, 100L)];
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new SparseVector(entries, 768));
    }

    [TestMethod]
    public void Unit_SparseVector_Construction_RejectsZeroDimensions()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new SparseVector([], 0));
    }

    [TestMethod]
    public void Unit_SparseVector_Construction_EmptyEntriesValid()
    {
        SparseVector vector = new([], 10);
        Assert.AreEqual(0, vector.NonzeroCount);
        Assert.AreEqual(10, vector.TotalDimensions);
    }

    [TestMethod]
    public void Unit_SparseVector_ValueAt_ReturnsCorrectOrZero()
    {
        SparseEntry[] entries = [new(2, 100L), new(5, -200L)];
        SparseVector vector = new(entries, 10);
        Assert.AreEqual(100L, vector.ValueAt(2));
        Assert.AreEqual(-200L, vector.ValueAt(5));
        Assert.AreEqual(0L, vector.ValueAt(0));
        Assert.AreEqual(0L, vector.ValueAt(3));
        Assert.AreEqual(0L, vector.ValueAt(9));
    }

    [TestMethod]
    public void Unit_SparseVector_FromDense_RetainsNonzeros()
    {
        long[] dense = [0L, 0L, 100L, 0L, 0L, -200L, 0L];
        SparseVector vector = SparseVector.FromDense(dense);
        Assert.AreEqual(2, vector.NonzeroCount);
        Assert.AreEqual(7, vector.TotalDimensions);
        Assert.AreEqual(100L, vector.ValueAt(2));
        Assert.AreEqual(-200L, vector.ValueAt(5));
    }

    [TestMethod]
    public void Invariant_SparseVector_FromDense_ComponentsAreSortedAndUnique()
    {
        long[] dense = [0L, 50L, 0L, 30L, 0L, 0L, 10L, 0L, 0L, 0L];
        SparseVector vector = SparseVector.FromDense(dense);
        ReadOnlySpan<SparseEntry> entries = vector.Entries;
        for (int i = 1; i < entries.Length; i++)
            Assert.IsTrue(entries[i].Dimension > entries[i - 1].Dimension,
                $"Entry {i} dimension {entries[i].Dimension} should be > entry {i - 1} dimension {entries[i - 1].Dimension}");
    }

    [TestMethod]
    public void Unit_SparseVector_ToDense_RoundTrip()
    {
        long[] dense = [0L, 100L, 0L, -200L, 0L, 50L];
        SparseVector sparse = SparseVector.FromDense(dense);
        long[] reconstructed = sparse.ToDense();
        CollectionAssert.AreEqual(dense, reconstructed);
    }

    [TestMethod]
    public void Unit_SparseVector_DistanceSquaredL2_BothSparse()
    {
        SparseEntry[] entriesA = [new(0, 10L), new(2, 30L)];
        SparseEntry[] entriesB = [new(0, 7L), new(1, 20L), new(2, 25L)];
        SparseVector a = new(entriesA, 5);
        SparseVector b = new(entriesB, 5);

        // diff: dim0=(10-7)=3, dim1=(0-20)=-20, dim2=(30-25)=5
        // dist^2 = 9 + 400 + 25 = 434
        BigInteger expected = 434;
        Assert.AreEqual(expected, a.DistanceSquaredL2(b));
    }

    [TestMethod]
    public void Unit_SparseVector_DistanceSquaredL2_MatchesBruteForceDense()
    {
        long[] denseA = [10L, 0L, 30L, 0L, 5L];
        long[] denseB = [7L, 20L, 25L, 0L, 0L];
        SparseVector sparseA = SparseVector.FromDense(denseA);
        SparseVector sparseB = SparseVector.FromDense(denseB);

        BigInteger sparseResult = sparseA.DistanceSquaredL2(sparseB);

        BigInteger bruteForce = 0;
        for (int i = 0; i < denseA.Length; i++)
        {
            long diff = denseA[i] - denseB[i];
            bruteForce += (BigInteger)diff * diff;
        }

        Assert.AreEqual(bruteForce, sparseResult);
    }

    [TestMethod]
    public void Unit_SparseVector_DistanceL1_BothSparse()
    {
        SparseEntry[] entriesA = [new(0, 10L), new(2, 30L)];
        SparseEntry[] entriesB = [new(0, 7L), new(1, 20L), new(2, 25L)];
        SparseVector a = new(entriesA, 5);
        SparseVector b = new(entriesB, 5);

        // L1: |10-7| + |0-20| + |30-25| = 3 + 20 + 5 = 28
        BigInteger expected = 28;
        Assert.AreEqual(expected, a.DistanceL1(b));
    }

    [TestMethod]
    public void Unit_SparseVector_DistanceSquaredL2_ToLongVectorN()
    {
        long[] denseA = [10L, 0L, 30L, 0L, 5L];
        long[] denseB = [7L, 20L, 25L, 0L, 0L];
        SparseVector sparseA = SparseVector.FromDense(denseA);
        LongVectorN vectorB = new(denseB);

        BigInteger sparseResult = sparseA.DistanceSquaredL2(vectorB);

        BigInteger bruteForce = 0;
        for (int i = 0; i < denseA.Length; i++)
        {
            long diff = denseA[i] - denseB[i];
            bruteForce += (BigInteger)diff * diff;
        }

        Assert.AreEqual(bruteForce, sparseResult);
    }

    [TestMethod]
    public void Unit_SparseVector_Equality()
    {
        SparseEntry[] entries = [new(2, 100L), new(5, -200L)];
        SparseVector a = new(entries, 768);
        SparseVector b = new([new(2, 100L), new(5, -200L)], 768);
        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
    }

    [TestMethod]
    public void Unit_SparseVector_Inequality_DifferentValues()
    {
        SparseVector a = new([new(2, 100L)], 10);
        SparseVector b = new([new(2, 200L)], 10);
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void Unit_SparseVector_Inequality_DifferentDimensions()
    {
        SparseVector a = new([new(2, 100L)], 10);
        SparseVector b = new([new(2, 100L)], 20);
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void Unit_SparseVector_ToLongVectorN()
    {
        SparseVector sparse = SparseVector.FromDense([0L, 100L, 0L, -200L]);
        LongVectorN dense = sparse.ToLongVectorN();
        Assert.AreEqual(4, dense.Dimensions);
        Assert.AreEqual(0L, dense[0]);
        Assert.AreEqual(100L, dense[1]);
        Assert.AreEqual(0L, dense[2]);
        Assert.AreEqual(-200L, dense[3]);
    }

    [TestMethod]
    public void Unit_SparseVector_DistanceToSelf_IsZero()
    {
        SparseVector vector = new([new(3, 42L), new(7, -99L)], 10);
        Assert.AreEqual(BigInteger.Zero, vector.DistanceSquaredL2(vector));
        Assert.AreEqual(BigInteger.Zero, vector.DistanceL1(vector));
    }
}
