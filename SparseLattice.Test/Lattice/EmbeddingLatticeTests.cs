using System.Numerics;
using SparseLattice.Lattice;
using SparseLattice.Math;
////////////////////////////////////////////
namespace SparseLattice.Test.Lattice;

[TestClass]
public sealed class EmbeddingLatticeTests
{
    [TestMethod]
    public void Unit_Query_EmptyIndex_ReturnsEmpty()
    {
        EmbeddingLattice<string> lattice = new([], new LatticeOptions { LeafThreshold = 4 });
        SparseVector center = new([], 10);
        List<SparseOccupant<string>> results = lattice.QueryWithinDistanceL2(center, 1000);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void Unit_Query_FindExactNeighbor()
    {
        SparseVector target = new([new(0, 100L), new(1, 200L)], 5);
        SparseVector farAway = new([new(0, 9000L), new(1, 9000L)], 5);
        SparseOccupant<string>[] items =
        [
            new(target, "close"),
            new(farAway, "far"),
        ];

        EmbeddingLattice<string> lattice = new(items, new LatticeOptions { LeafThreshold = 1 });
        lattice.Freeze();

        SparseVector query = new([new(0, 105L), new(1, 205L)], 5);
        // distance to "close": sqrt((105-100)^2 + (205-200)^2) = sqrt(50), dist^2 = 50
        List<SparseOccupant<string>> results = lattice.QueryWithinDistanceL2(query, 100);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("close", results[0].Payload);
    }

    [TestMethod]
    public void Unit_Query_FindsAllWithinRadius()
    {
        SparseOccupant<int>[] items = new SparseOccupant<int>[10];
        for (int i = 0; i < 10; i++)
        {
            long value = (i + 1) * 10L;
            items[i] = new(new([new(0, value)], 5), i);
        }

        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 2 });
        lattice.Freeze();

        SparseVector center = new([new(0, 50L)], 5);
        // items at 10,20,30,40,50,60,70,80,90,100
        // distances from 50: 40,30,20,10,0,10,20,30,40,50
        // dist^2: 1600,900,400,100,0,100,400,900,1600,2500
        // radius^2 = 900 -> items at 20,30,40,50,60,70,80 = 7 items
        List<SparseOccupant<int>> results = lattice.QueryWithinDistanceL2(center, 900);
        Assert.AreEqual(7, results.Count);
    }

    [TestMethod]
    public void Unit_QueryL1_FindsCorrectNeighbors()
    {
        SparseOccupant<string>[] items =
        [
            new(new([new(0, 10L), new(1, 10L)], 5), "a"),
            new(new([new(0, 100L), new(1, 100L)], 5), "b"),
        ];

        EmbeddingLattice<string> lattice = new(items, new LatticeOptions { LeafThreshold = 1 });
        lattice.Freeze();

        SparseVector center = new([new(0, 12L), new(1, 12L)], 5);
        // L1 to "a": |12-10| + |12-10| = 4; L1 to "b": |12-100| + |12-100| = 176
        List<SparseOccupant<string>> results = lattice.QueryWithinDistanceL1(center, 10);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("a", results[0].Payload);
    }

    [TestMethod]
    public void Unit_QueryKNearest_ReturnsTopK()
    {
        SparseOccupant<int>[] items = new SparseOccupant<int>[20];
        for (int i = 0; i < 20; i++)
        {
            long value = (i + 1) * 100L;
            items[i] = new(new([new(0, value)], 5), i);
        }

        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
        lattice.Freeze();

        SparseVector center = new([new(0, 500L)], 5);
        List<SparseOccupant<int>> results = lattice.QueryKNearestL2(center, 3);
        Assert.AreEqual(3, results.Count);
        // nearest to 500: 500(idx4), 400(idx3), 600(idx5)
        int[] expectedPayloads = [4, 3, 5];
        int[] actualPayloads = [results[0].Payload, results[1].Payload, results[2].Payload];
        CollectionAssert.AreEqual(expectedPayloads, actualPayloads);
    }

    [TestMethod]
    public void Unit_Freeze_SetsFlag()
    {
        EmbeddingLattice<string> lattice = new([], new LatticeOptions());
        Assert.IsFalse(lattice.IsFrozen);
        lattice.Freeze();
        Assert.IsTrue(lattice.IsFrozen);
    }

    [TestMethod]
    public void Integration_QueryMatchesBruteForce()
    {
        SparseOccupant<int>[] items = new SparseOccupant<int>[50];
        for (int i = 0; i < 50; i++)
        {
            long xVal = ((i * 37) % 100 + 1) * 10L;
            long yVal = ((i * 73) % 100 + 1) * 10L;
            SparseVector vector = new([new(0, xVal), new(1, yVal)], 5);
            items[i] = new(vector, i);
        }

        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
        lattice.Freeze();

        SparseVector center = new([new(0, 500L), new(1, 500L)], 5);
        BigInteger radiusSquared = 100000;

        List<SparseOccupant<int>> latticeResults = lattice.QueryWithinDistanceL2(center, radiusSquared);

        List<int> bruteForcePayloads = [];
        foreach (SparseOccupant<int> item in items)
            if (center.DistanceSquaredL2(item.Position) <= radiusSquared)
                bruteForcePayloads.Add(item.Payload);

        List<int> latticePayloads = [];
        foreach (SparseOccupant<int> result in latticeResults)
            latticePayloads.Add(result.Payload);

        bruteForcePayloads.Sort();
        latticePayloads.Sort();

        CollectionAssert.AreEqual(bruteForcePayloads, latticePayloads,
            $"Lattice returned {latticePayloads.Count} items, brute force returned {bruteForcePayloads.Count} items");
    }

    [TestMethod]
    public void Integration_ConcurrentReadsSafe()
    {
        SparseOccupant<int>[] items = new SparseOccupant<int>[100];
        for (int i = 0; i < 100; i++)
        {
            long value = (i + 1) * 50L;
            items[i] = new(new([new(0, value)], 10), i);
        }

        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
        lattice.Freeze();

        SparseVector center = new([new(0, 2500L)], 10);
        BigInteger radiusSquared = 500 * 500;

        List<SparseOccupant<int>> baseline = lattice.QueryWithinDistanceL2(center, radiusSquared);
        int expectedCount = baseline.Count;

        Parallel.For(0, 50, _ =>
        {
            List<SparseOccupant<int>> results = lattice.QueryWithinDistanceL2(center, radiusSquared);
            Assert.AreEqual(expectedCount, results.Count);
        });
    }
}
