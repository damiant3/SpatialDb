using SparseLattice.Math;
///////////////////////////////////////////
namespace SparseLattice.Lattice;

// Wire format (little-endian): [4B magic 'SLAT'] [2B version=1] [node…]
// Node: [1B tag: 0=leaf, 1=branch]
// Leaf: [4B count] N × [4B dims, 4B entries, E×(2B dim + 8B val), payload]
// Branch: [2B splitDim] [8B splitVal] [1B childMask] [node…below] [node…above]
public sealed class SparseLatticeSerializer
{
    const uint Magic = 0x544C4153u;
    const ushort Version = 1;

    const byte TagLeaf = 0;
    const byte TagBranch = 1;
    const byte BelowMask = 0b01;
    const byte AboveMask = 0b10;

    SparseLatticeSerializer() { }

    /// <summary>
    /// Serializes the frozen lattice to <paramref name="stream"/>.
    /// The lattice must be frozen before serialization.
    /// <typeparamref name="TPayload"/> must be serializable via <paramref name="payloadSerializer"/>.
    /// </summary>
    public static void Save<TPayload>(
        EmbeddingLattice<TPayload> lattice,
        Stream stream,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        if (!lattice.IsFrozen)
            throw new InvalidOperationException("Lattice must be frozen before serialization.");

        using BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        WriteNode(writer, lattice.Root, payloadSerializer);
    }

    /// <summary>
    /// Deserializes a lattice from <paramref name="stream"/> using <paramref name="payloadSerializer"/>.
    /// The returned lattice is already frozen (the loaded structure is immutable).
    /// </summary>
    public static EmbeddingLattice<TPayload> Load<TPayload>(
        Stream stream,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        using BinaryReader reader = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        uint magic = reader.ReadUInt32();
        if (magic != Magic)
            throw new InvalidDataException($"Invalid magic bytes: 0x{magic:X8}, expected 0x{Magic:X8}.");

        ushort version = reader.ReadUInt16();
        if (version != Version)
            throw new InvalidDataException($"Unsupported serialization version {version}. Expected {Version}.");

        ISparseNode root = ReadNode<TPayload>(reader, payloadSerializer);
        return EmbeddingLattice<TPayload>.FromFrozenRoot(root);
    }

    static void WriteNode<TPayload>(
        BinaryWriter writer,
        ISparseNode node,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        if (node is SparseLeafNode<TPayload> leaf)
        {
            writer.Write(TagLeaf);
            WriteLeaf(writer, leaf, payloadSerializer);
            return;
        }

        if (!node.TryGetBranch(out ushort splitDimension, out long splitValue,
                out ISparseNode? below, out ISparseNode? above))
            throw new InvalidOperationException($"Unknown node type '{node.GetType().FullName}'.");

        writer.Write(TagBranch);
        writer.Write(splitDimension);
        writer.Write(splitValue);

        byte childMask = 0;
        if (below is not null) childMask |= BelowMask;
        if (above is not null) childMask |= AboveMask;
        writer.Write(childMask);

        if (below is not null)
            WriteNode(writer, below, payloadSerializer);
        if (above is not null)
            WriteNode(writer, above, payloadSerializer);
    }

    static void WriteLeaf<TPayload>(
        BinaryWriter writer,
        SparseLeafNode<TPayload> leaf,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        ReadOnlySpan<SparseOccupant<TPayload>> occupants = leaf.Occupants;
        writer.Write(occupants.Length);
        foreach (SparseOccupant<TPayload> occupant in occupants)
        {
            ReadOnlySpan<SparseEntry> entries = occupant.Position.Entries;
            writer.Write(occupant.Position.TotalDimensions);
            writer.Write(entries.Length);
            foreach (SparseEntry entry in entries)
            {
                writer.Write(entry.Dimension);
                writer.Write(entry.Value);
            }
            payloadSerializer.Write(writer, occupant.Payload);
        }
    }

    static ISparseNode ReadNode<TPayload>(
        BinaryReader reader,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        byte tag = reader.ReadByte();
        if (tag == TagLeaf)
            return ReadLeaf<TPayload>(reader, payloadSerializer);
        if (tag == TagBranch)
            return ReadBranch<TPayload>(reader, payloadSerializer);
        throw new InvalidDataException($"Unknown node tag byte {tag}.");
    }

    static SparseLeafNode<TPayload> ReadLeaf<TPayload>(
        BinaryReader reader,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        int count = reader.ReadInt32();
        SparseOccupant<TPayload>[] occupants = new SparseOccupant<TPayload>[count];
        for (int i = 0; i < count; i++)
        {
            int totalDimensions = reader.ReadInt32();
            int entryCount = reader.ReadInt32();
            SparseEntry[] entries = new SparseEntry[entryCount];
            for (int e = 0; e < entryCount; e++)
            {
                ushort dimension = reader.ReadUInt16();
                long value = reader.ReadInt64();
                entries[e] = new SparseEntry(dimension, value);
            }
            SparseVector position = new(entries, totalDimensions);
            TPayload payload = payloadSerializer.Read(reader);
            occupants[i] = new SparseOccupant<TPayload>(position, payload);
        }
        return new SparseLeafNode<TPayload>(occupants);
    }

    static FrozenBranchNode ReadBranch<TPayload>(
        BinaryReader reader,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        ushort splitDimension = reader.ReadUInt16();
        long splitValue = reader.ReadInt64();
        byte childMask = reader.ReadByte();

        ISparseNode? below = (childMask & BelowMask) != 0
            ? ReadNode<TPayload>(reader, payloadSerializer)
            : null;
        ISparseNode? above = (childMask & AboveMask) != 0
            ? ReadNode<TPayload>(reader, payloadSerializer)
            : null;

        return new FrozenBranchNode(splitDimension, splitValue, below, above);
    }
}

public interface IPayloadSerializer<TPayload>
{
    void Write(BinaryWriter writer, TPayload payload);
    TPayload Read(BinaryReader reader);
}

public sealed class StringPayloadSerializer : IPayloadSerializer<string>
{
    public static StringPayloadSerializer Instance { get; } = new();

    public void Write(BinaryWriter writer, string payload)
        => writer.Write(payload);

    public string Read(BinaryReader reader)
        => reader.ReadString();
}

public sealed class Int32PayloadSerializer : IPayloadSerializer<int>
{
    public static Int32PayloadSerializer Instance { get; } = new();

    public void Write(BinaryWriter writer, int payload)
        => writer.Write(payload);

    public int Read(BinaryReader reader)
        => reader.ReadInt32();
}
