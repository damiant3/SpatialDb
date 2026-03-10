using SparseLattice.Lattice;
using SparseLattice.Math;
//////////////////////////////////////////////
namespace SparseLattice.Test.Lattice;

/// <summary>
/// Tests for <see cref="SparseLatticeSerializer"/>: round-trip correctness,
/// byte-identity of two consecutive saves, error conditions, and frozen-only guard.
/// </summary>
[TestClass]
public sealed class SerializerTests
{
    [TestMethod]
    public void Unit_Serializer_ThrowsIfNotFrozen()
    {
        EmbeddingLattice<int> lattice = new(BuildItems(10), new LatticeOptions { LeafThreshold = 4 });
        using MemoryStream ms = new();
        Assert.ThrowsException<InvalidOperationException>(
            () => SparseLatticeSerializer.Save(lattice, ms, Int32PayloadSerializer.Instance),
            "Save must throw if lattice is not frozen.");
    }

    [TestMethod]
    public void Unit_Serializer_SaveLoad_EmptyLattice()
    {
        EmbeddingLattice<string> original = new([], new LatticeOptions());
        original.Freeze();

        EmbeddingLattice<string> loaded = SaveAndLoad(original, StringPayloadSerializer.Instance);

        Assert.IsTrue(loaded.IsFrozen);
        SparseTreeStats stats = loaded.CollectStats();
        Assert.AreEqual(0, stats.TotalOccupants);
        Assert.AreEqual(1, stats.LeafNodes);
        Assert.AreEqual(0, stats.BranchNodes);
    }

    [TestMethod]
    public void Unit_Serializer_SaveLoad_SingleItem()
    {
        SparseVector vector = new([new(0, 12345L), new(3, -67890L)], 10);
        SparseOccupant<string>[] items = [new(vector, "hello")];
        EmbeddingLattice<string> original = new(items, new LatticeOptions());
        original.Freeze();

        EmbeddingLattice<string> loaded = SaveAndLoad(original, StringPayloadSerializer.Instance);

        SparseTreeStats stats = loaded.CollectStats();
        Assert.AreEqual(1, stats.TotalOccupants);
        Assert.AreEqual("hello", FirstPayload(loaded, vector));
    }

    [TestMethod]
    public void Unit_Serializer_SaveLoad_PreservesAllOccupants()
    {
        SparseOccupant<int>[] items = BuildItems(40);
        EmbeddingLattice<int> original = new(items, new LatticeOptions { LeafThreshold = 4 });
        original.Freeze();

        EmbeddingLattice<int> loaded = SaveAndLoad(original, Int32PayloadSerializer.Instance);

        Assert.AreEqual(
            original.CollectStats().TotalOccupants,
            loaded.CollectStats().TotalOccupants,
            "Occupant count must be identical after round-trip.");
    }

    [TestMethod]
    public void Unit_Serializer_SaveLoad_TreeStructureIdentical()
    {
        SparseOccupant<int>[] items = BuildItems(40);
        EmbeddingLattice<int> original = new(items, new LatticeOptions { LeafThreshold = 4 });
        original.Freeze();

        EmbeddingLattice<int> loaded = SaveAndLoad(original, Int32PayloadSerializer.Instance);

        SparseTreeStats o = original.CollectStats();
        SparseTreeStats l = loaded.CollectStats();

        Assert.AreEqual(o.TotalNodes, l.TotalNodes, "TotalNodes must match.");
        Assert.AreEqual(o.BranchNodes, l.BranchNodes, "BranchNodes must match.");
        Assert.AreEqual(o.LeafNodes, l.LeafNodes, "LeafNodes must match.");
        Assert.AreEqual(o.MaxDepth, l.MaxDepth, "MaxDepth must match.");
    }

    [TestMethod]
    public void Unit_Serializer_TwoConsecutiveSaves_ProduceBytIdenticalOutput()
    {
        SparseOccupant<int>[] items = BuildItems(30);
        EmbeddingLattice<int> lattice = new(items, new LatticeOptions { LeafThreshold = 4 });
        lattice.Freeze();

        byte[] first = Serialize(lattice, Int32PayloadSerializer.Instance);
        byte[] second = Serialize(lattice, Int32PayloadSerializer.Instance);

        CollectionAssert.AreEqual(first, second,
            "Two consecutive serializations of the same frozen lattice must produce identical bytes.");
    }

    [TestMethod]
    public void Unit_Serializer_LoadedLattice_QueryMatchesOriginal()
    {
        SparseOccupant<int>[] items = BuildItems(50);
        EmbeddingLattice<int> original = new(items, new LatticeOptions { LeafThreshold = 4 });
        original.Freeze();

        EmbeddingLattice<int> loaded = SaveAndLoad(original, Int32PayloadSerializer.Instance);

        SparseVector center = new([new(0, 500L), new(1, 500L)], 5);
        BigInteger radius = 400_000;

        List<SparseOccupant<int>> fromOriginal = original.QueryWithinDistanceL2(center, radius);
        List<SparseOccupant<int>> fromLoaded = loaded.QueryWithinDistanceL2(center, radius);

        CollectionAssert.AreEqual(
            PayloadsSorted(fromOriginal),
            PayloadsSorted(fromLoaded),
            "Loaded lattice must return identical query results.");
    }

    [TestMethod]
    public void Unit_Serializer_KNNFromLoadedLattice_MatchesOriginal()
    {
        SparseOccupant<int>[] items = BuildItems(50);
        EmbeddingLattice<int> original = new(items, new LatticeOptions { LeafThreshold = 4 });
        original.Freeze();

        EmbeddingLattice<int> loaded = SaveAndLoad(original, Int32PayloadSerializer.Instance);

        SparseVector center = new([new(0, 500L), new(1, 500L)], 5);

        List<SparseOccupant<int>> fromOriginal = original.QueryKNearestL2(center, 5);
        List<SparseOccupant<int>> fromLoaded = loaded.QueryKNearestL2(center, 5);

        CollectionAssert.AreEqual(
            PayloadsSorted(fromOriginal),
            PayloadsSorted(fromLoaded),
            "KNN results from loaded lattice must match original.");
    }

    [TestMethod]
    public void Unit_Serializer_InvalidMagic_Throws()
    {
        using MemoryStream ms = new(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x00, 0x01 });
        Assert.ThrowsException<InvalidDataException>(
            () => SparseLatticeSerializer.Load(ms, Int32PayloadSerializer.Instance));
    }

    [TestMethod]
    public void Unit_Serializer_InvalidVersion_Throws()
    {
        // Write valid magic then wrong version
        using MemoryStream ms = new();
        using (BinaryWriter writer = new(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x544C4153u);  // correct magic
            writer.Write((ushort)99);   // wrong version
        }
        ms.Seek(0, SeekOrigin.Begin);
        Assert.ThrowsException<InvalidDataException>(
            () => SparseLatticeSerializer.Load(ms, Int32PayloadSerializer.Instance));
    }

    [TestMethod]
    public void Unit_Serializer_LoadedLattice_IsFrozen()
    {
        EmbeddingLattice<int> original = new(BuildItems(10), new LatticeOptions { LeafThreshold = 4 });
        original.Freeze();
        EmbeddingLattice<int> loaded = SaveAndLoad(original, Int32PayloadSerializer.Instance);
        Assert.IsTrue(loaded.IsFrozen, "Loaded lattice must be frozen.");
    }

    [TestMethod]
    public void Unit_Serializer_LoadedLattice_StringPayloads_Preserved()
    {
        SparseOccupant<string>[] items =
        [
            new(new([new(0, 100L)], 5), "alpha"),
            new(new([new(0, 900L)], 5), "omega"),
        ];
        EmbeddingLattice<string> original = new(items, new LatticeOptions { LeafThreshold = 2 });
        original.Freeze();

        EmbeddingLattice<string> loaded = SaveAndLoad(original, StringPayloadSerializer.Instance);

        SparseVector nearFirst = new([new(0, 110L)], 5);
        List<SparseOccupant<string>> results = loaded.QueryKNearestL2(nearFirst, 1);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("alpha", results[0].Payload);
    }

    // --- helpers ---

    private static SparseOccupant<int>[] BuildItems(int count)
    {
        SparseOccupant<int>[] items = new SparseOccupant<int>[count];
        for (int i = 0; i < count; i++)
        {
            long xVal = ((i * 37) % 100 + 1) * 10L;
            long yVal = ((i * 73) % 100 + 1) * 10L;
            items[i] = new(new SparseVector([new SparseEntry(0, xVal), new SparseEntry(1, yVal)], 5), i);
        }
        return items;
    }

    private static EmbeddingLattice<TPayload> SaveAndLoad<TPayload>(
        EmbeddingLattice<TPayload> lattice,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        using MemoryStream ms = new();
        SparseLatticeSerializer.Save(lattice, ms, payloadSerializer);
        ms.Seek(0, SeekOrigin.Begin);
        return SparseLatticeSerializer.Load(ms, payloadSerializer);
    }

    private static byte[] Serialize<TPayload>(
        EmbeddingLattice<TPayload> lattice,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        using MemoryStream ms = new();
        SparseLatticeSerializer.Save(lattice, ms, payloadSerializer);
        return ms.ToArray();
    }

    private static string? FirstPayload(EmbeddingLattice<string> lattice, SparseVector position)
    {
        SparseVector query = new(position.Entries.ToArray(), position.TotalDimensions);
        List<SparseOccupant<string>> results = lattice.QueryKNearestL2(query, 1);
        return results.Count > 0 ? results[0].Payload : null;
    }

    private static List<int> PayloadsSorted(List<SparseOccupant<int>> occupants)
    {
        List<int> payloads = new(occupants.Count);
        foreach (SparseOccupant<int> o in occupants)
            payloads.Add(o.Payload);
        payloads.Sort();
        return payloads;
    }
}
