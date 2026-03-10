using System.Numerics;
using SparseLattice.Math;
/////////////////////////////////////////////
namespace SparseLattice.Test.Math;

[TestClass]
public sealed class HyperRegionTests
{
    [TestMethod]
    public void Unit_HyperRegion_Construction_Valid()
    {
        LongVectorN min = new([-10L, -20L, -30L]);
        LongVectorN max = new([10L, 20L, 30L]);
        HyperRegion region = new(min, max);
        Assert.AreEqual(3, region.Dimensions);
        Assert.AreEqual(min, region.Min);
        Assert.AreEqual(max, region.Max);
    }

    [TestMethod]
    public void Unit_HyperRegion_Construction_DimensionMismatch_Throws()
    {
        LongVectorN min = new([-10L, -20L]);
        LongVectorN max = new([10L, 20L, 30L]);
        Assert.ThrowsException<InvalidOperationException>(() => new HyperRegion(min, max));
    }

    [TestMethod]
    public void Unit_HyperRegion_MidpointAt_Correct()
    {
        LongVectorN min = new([0L, -10L]);
        LongVectorN max = new([10L, 10L]);
        HyperRegion region = new(min, max);
        Assert.AreEqual(5L, region.MidpointAt(0));
        Assert.AreEqual(0L, region.MidpointAt(1));
    }

    [TestMethod]
    public void Unit_HyperRegion_Contains_InsidePoint()
    {
        LongVectorN min = new([-10L, -10L]);
        LongVectorN max = new([10L, 10L]);
        HyperRegion region = new(min, max);
        Assert.IsTrue(region.Contains(new([0L, 0L])));
        Assert.IsTrue(region.Contains(new([-10L, -10L])));
        Assert.IsTrue(region.Contains(new([10L, 10L])));
    }

    [TestMethod]
    public void Unit_HyperRegion_Contains_OutsidePoint()
    {
        LongVectorN min = new([-10L, -10L]);
        LongVectorN max = new([10L, 10L]);
        HyperRegion region = new(min, max);
        Assert.IsFalse(region.Contains(new([11L, 0L])));
        Assert.IsFalse(region.Contains(new([0L, -11L])));
    }

    [TestMethod]
    public void Unit_HyperRegion_IntersectsHypersphere_CenterInside()
    {
        LongVectorN min = new([-10L, -10L]);
        LongVectorN max = new([10L, 10L]);
        HyperRegion region = new(min, max);

        LongVectorN center = new([0L, 0L]);
        Assert.IsTrue(region.IntersectsHypersphere(center, 1));
    }

    [TestMethod]
    public void Unit_HyperRegion_IntersectsHypersphere_CenterOutside_CloseEnough()
    {
        LongVectorN min = new([0L, 0L]);
        LongVectorN max = new([10L, 10L]);
        HyperRegion region = new(min, max);

        LongVectorN center = new([-5L, 5L]);
        // closest point on region to center is (0, 5), distance = 5, dist^2 = 25
        Assert.IsTrue(region.IntersectsHypersphere(center, 25));
        Assert.IsFalse(region.IntersectsHypersphere(center, 24));
    }

    [TestMethod]
    public void Unit_HyperRegion_LowerHalf_Correct()
    {
        LongVectorN min = new([0L, 0L]);
        LongVectorN max = new([100L, 100L]);
        HyperRegion region = new(min, max);
        HyperRegion lower = region.LowerHalf(0);
        Assert.AreEqual(0L, lower.Min[0]);
        Assert.AreEqual(50L, lower.Max[0]);
        Assert.AreEqual(0L, lower.Min[1]);
        Assert.AreEqual(100L, lower.Max[1]);
    }

    [TestMethod]
    public void Unit_HyperRegion_UpperHalf_Correct()
    {
        LongVectorN min = new([0L, 0L]);
        LongVectorN max = new([100L, 100L]);
        HyperRegion region = new(min, max);
        HyperRegion upper = region.UpperHalf(0);
        Assert.AreEqual(50L, upper.Min[0]);
        Assert.AreEqual(100L, upper.Max[0]);
        Assert.AreEqual(0L, upper.Min[1]);
        Assert.AreEqual(100L, upper.Max[1]);
    }

    [TestMethod]
    public void Unit_HyperRegion_ContainsSparse()
    {
        LongVectorN min = new([-10L, -10L, -10L]);
        LongVectorN max = new([10L, 10L, 10L]);
        HyperRegion region = new(min, max);

        SparseVector inside = new([new(1, 5L)], 3);
        Assert.IsTrue(region.ContainsSparse(inside));

        SparseVector outside = new([new(0, 20L)], 3);
        Assert.IsFalse(region.ContainsSparse(outside));
    }

    [TestMethod]
    public void Unit_HyperRegion_Equality()
    {
        LongVectorN min = new([0L, 0L]);
        LongVectorN max = new([10L, 10L]);
        HyperRegion a = new(min, max);
        HyperRegion b = new(min, max);
        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
    }
}
