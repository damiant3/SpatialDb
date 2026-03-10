using System.Numerics;
using SparseLattice.Math;
/////////////////////////////////////////////
namespace SparseLattice.Test.Math;

[TestClass]
public sealed class LongVectorNTests
{
    [TestMethod]
    public void Unit_LongVectorN_Construction_FromDimensions()
    {
        LongVectorN vector = new(3);
        Assert.AreEqual(3, vector.Dimensions);
        Assert.AreEqual(0L, vector[0]);
        Assert.AreEqual(0L, vector[1]);
        Assert.AreEqual(0L, vector[2]);
    }

    [TestMethod]
    public void Unit_LongVectorN_Construction_FromArray()
    {
        long[] components = [10L, 20L, 30L];
        LongVectorN vector = new(components);
        Assert.AreEqual(3, vector.Dimensions);
        Assert.AreEqual(10L, vector[0]);
        Assert.AreEqual(20L, vector[1]);
        Assert.AreEqual(30L, vector[2]);
    }

    [TestMethod]
    public void Unit_LongVectorN_Construction_RejectsEmpty()
    {
        Assert.ThrowsException<ArgumentException>(() => new LongVectorN([]));
    }

    [TestMethod]
    public void Unit_LongVectorN_Construction_RejectsZeroDimensions()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new LongVectorN(0));
    }

    [TestMethod]
    public void Unit_LongVectorN_Indexer_ThrowsOnOutOfRange()
    {
        LongVectorN vector = new([1L, 2L, 3L]);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => vector[-1]);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => vector[3]);
    }

    [TestMethod]
    public void Unit_LongVectorN_Subtract_Correct()
    {
        LongVectorN a = new([10L, 20L, 30L]);
        LongVectorN b = new([3L, 7L, 11L]);
        LongVectorN result = a - b;
        Assert.AreEqual(7L, result[0]);
        Assert.AreEqual(13L, result[1]);
        Assert.AreEqual(19L, result[2]);
    }

    [TestMethod]
    public void Unit_LongVectorN_Add_Correct()
    {
        LongVectorN a = new([1L, 2L, 3L]);
        LongVectorN b = new([4L, 5L, 6L]);
        LongVectorN result = a + b;
        Assert.AreEqual(5L, result[0]);
        Assert.AreEqual(7L, result[1]);
        Assert.AreEqual(9L, result[2]);
    }

    [TestMethod]
    public void Unit_LongVectorN_Multiply_Correct()
    {
        LongVectorN a = new([2L, 3L, 4L]);
        LongVectorN result = a * 5;
        Assert.AreEqual(10L, result[0]);
        Assert.AreEqual(15L, result[1]);
        Assert.AreEqual(20L, result[2]);
    }

    [TestMethod]
    public void Unit_LongVectorN_Divide_Correct()
    {
        LongVectorN a = new([20L, 30L, 40L]);
        LongVectorN result = a / 10;
        Assert.AreEqual(2L, result[0]);
        Assert.AreEqual(3L, result[1]);
        Assert.AreEqual(4L, result[2]);
    }

    [TestMethod]
    public void Unit_LongVectorN_MagnitudeSquared_OverflowSafe()
    {
        LongVectorN vector = new([long.MaxValue, long.MaxValue]);
        BigInteger expected = (BigInteger)long.MaxValue * long.MaxValue * 2;
        Assert.AreEqual(expected, vector.MagnitudeSquaredBig);
    }

    [TestMethod]
    public void Unit_LongVectorN_SumAbs_Correct()
    {
        LongVectorN vector = new([-5L, 3L, -7L]);
        Assert.AreEqual((BigInteger)15, vector.SumAbsBig);
    }

    [TestMethod]
    public void Unit_LongVectorN_DotProduct_OverflowSafe()
    {
        LongVectorN a = new([long.MaxValue, 1L]);
        LongVectorN b = new([2L, long.MaxValue]);
        BigInteger expected = (BigInteger)long.MaxValue * 2 + (BigInteger)1 * long.MaxValue;
        Assert.AreEqual(expected, a.DotBig(b));
    }

    [TestMethod]
    public void Unit_LongVectorN_DotProduct_DimensionMismatch_Throws()
    {
        LongVectorN a = new([1L, 2L]);
        LongVectorN b = new([1L, 2L, 3L]);
        Assert.ThrowsException<InvalidOperationException>(() => a.DotBig(b));
    }

    [TestMethod]
    public void Unit_LongVectorN_Equality_SameValues()
    {
        LongVectorN a = new([1L, 2L, 3L]);
        LongVectorN b = new([1L, 2L, 3L]);
        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
    }

    [TestMethod]
    public void Unit_LongVectorN_Equality_DifferentValues()
    {
        LongVectorN a = new([1L, 2L, 3L]);
        LongVectorN b = new([1L, 2L, 4L]);
        Assert.AreNotEqual(a, b);
        Assert.IsFalse(a == b);
        Assert.IsTrue(a != b);
    }

    [TestMethod]
    public void Unit_LongVectorN_Equality_DifferentDimensions()
    {
        LongVectorN a = new([1L, 2L]);
        LongVectorN b = new([1L, 2L, 3L]);
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void Unit_LongVectorN_HashCode_EqualForEqualVectors()
    {
        LongVectorN a = new([1L, 2L, 3L]);
        LongVectorN b = new([1L, 2L, 3L]);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Unit_LongVectorN_Midpoint_OverflowSafe()
    {
        // long.MinValue = -9223372036854775808, long.MaxValue = 9223372036854775807
        // true average is -0.5, integer midpoint via (a&b)+((a^b)>>1) is -1
        long mid = LongVectorN.Midpoint(long.MinValue, long.MaxValue);
        Assert.AreEqual(-1L, mid);

        long mid2 = LongVectorN.Midpoint(0L, 10L);
        Assert.AreEqual(5L, mid2);

        long mid3 = LongVectorN.Midpoint(-10L, 10L);
        Assert.AreEqual(0L, mid3);
    }

    [TestMethod]
    public void Unit_LongVectorN_Midpoint_Vector()
    {
        LongVectorN min = new([0L, -10L]);
        LongVectorN max = new([10L, 10L]);
        LongVectorN mid = LongVectorN.Midpoint(min, max);
        Assert.AreEqual(5L, mid[0]);
        Assert.AreEqual(0L, mid[1]);
    }

    [TestMethod]
    public void Unit_LongVectorN_Zero_AllZeros()
    {
        LongVectorN zero = LongVectorN.Zero(5);
        Assert.AreEqual(5, zero.Dimensions);
        for (int i = 0; i < 5; i++)
            Assert.AreEqual(0L, zero[i]);
    }

    [TestMethod]
    public void Unit_LongVectorN_ToString_Readable()
    {
        LongVectorN vector = new([1L, 2L, 3L]);
        Assert.AreEqual("(1, 2, 3)", vector.ToString());
    }
}
