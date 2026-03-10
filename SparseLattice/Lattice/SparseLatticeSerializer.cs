using SparseLattice.Math;
///////////////////////////////////////////
namespace SparseLattice.Lattice;

/// <summary>
/// Deterministic binary serializer for a frozen <see cref="EmbeddingLattice{TPayload}"/> tree.
/// The format is compact and versioned; it preserves the full sparse tree structure such that
/// a round-trip save/load produces a byte-identical second save.
///
/// Wire format (little-endian throughout):
///   [4 bytes]  magic   = 0x534C4154  ('SLAT')
///   [2 bytes]  version = 1
///   [node…]    recursive node encoding
///
/// Node encoding:
///   [1 byte]   tag: 0 = leaf, 1 = branch
///   Leaf:
///     [4 bytes]  occupant count N
///     N × occupant:
///       [4 bytes]  total dimension count
///       [4 bytes]  entry count E
///       E × (2-byte dimension, 8-byte value)
///       [payload encoded by IPayloadSerializer]
///   Branch:
///     [2 bytes]  split dimension
///     [8 bytes]  split value
///     [1 byte]   child mask (bit0=below present, bit1=above present)
///     [node…]    below child (if bit0 set)
///     [node…]    above child (if bit1 set)
/// </summary>
public sealed class SparseLatticeSerializer
{
    private const uint s_Magic = 0x544C4153u;  // 'SLAT' LE
    private const ushort s_Version = 1;

    private const byte s_TagLeaf = 0;
    private const byte s_TagBranch = 1;
    private const byte s_BelowMask = 0b01;
    private const byte s_AboveMask = 0b10;

    private SparseLatticeSerializer() { }

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
        writer.Write(s_Magic);
        writer.Write(s_Version);
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
        if (magic != s_Magic)
            throw new InvalidDataException($"Invalid magic bytes: 0x{magic:X8}, expected 0x{s_Magic:X8}.");

        ushort version = reader.ReadUInt16();
        if (version != s_Version)
            throw new InvalidDataException($"Unsupported serialization version {version}. Expected {s_Version}.");

        ISparseNode root = ReadNode<TPayload>(reader, payloadSerializer);
        return EmbeddingLattice<TPayload>.FromFrozenRoot(root);
    }

    // --- write ---

    private static void WriteNode<TPayload>(
        BinaryWriter writer,
        ISparseNode node,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        if (node is SparseLeafNode<TPayload> leaf)
        {
            writer.Write(s_TagLeaf);
            WriteLeaf(writer, leaf, payloadSerializer);
            return;
        }

        ushort splitDimension;
        long splitValue;
        ISparseNode? below;
        ISparseNode? above;

        if (node is FrozenBranchNode frozen)
        {
            splitDimension = frozen.SplitDimension;
            splitValue = frozen.SplitValue;
            below = frozen.Below;
            above = frozen.Above;
        }
        else if (node is SparseBranchNode mutable)
        {
            splitDimension = mutable.SplitDimension;
            splitValue = mutable.SplitValue;
            below = mutable.Below;
            above = mutable.Above;
        }
        else
            throw new InvalidOperationException($"Unknown node type '{node.GetType().FullName}'.");

        writer.Write(s_TagBranch);
        writer.Write(splitDimension);
        writer.Write(splitValue);

        byte childMask = 0;
        if (below is not null) childMask |= s_BelowMask;
        if (above is not null) childMask |= s_AboveMask;
        writer.Write(childMask);

        if (below is not null)
            WriteNode(writer, below, payloadSerializer);
        if (above is not null)
            WriteNode(writer, above, payloadSerializer);
    }

    private static void WriteLeaf<TPayload>(
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

    // --- read ---

    private static ISparseNode ReadNode<TPayload>(
        BinaryReader reader,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        byte tag = reader.ReadByte();
        if (tag == s_TagLeaf)
            return ReadLeaf<TPayload>(reader, payloadSerializer);
        if (tag == s_TagBranch)
            return ReadBranch<TPayload>(reader, payloadSerializer);
        throw new InvalidDataException($"Unknown node tag byte {tag}.");
    }

    private static SparseLeafNode<TPayload> ReadLeaf<TPayload>(
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

    private static FrozenBranchNode ReadBranch<TPayload>(
        BinaryReader reader,
        IPayloadSerializer<TPayload> payloadSerializer)
    {
        ushort splitDimension = reader.ReadUInt16();
        long splitValue = reader.ReadInt64();
        byte childMask = reader.ReadByte();

        ISparseNode? below = (childMask & s_BelowMask) != 0
            ? ReadNode<TPayload>(reader, payloadSerializer)
            : null;
        ISparseNode? above = (childMask & s_AboveMask) != 0
            ? ReadNode<TPayload>(reader, payloadSerializer)
            : null;

        return new FrozenBranchNode(splitDimension, splitValue, below, above);
    }
}

/// <summary>
/// Payload serialization contract. Implement one per payload type to support
/// <see cref="SparseLatticeSerializer.Save{TPayload}"/> and <see cref="SparseLatticeSerializer.Load{TPayload}"/>.
/// </summary>
public interface IPayloadSerializer<TPayload>
{
    void Write(BinaryWriter writer, TPayload payload);
    TPayload Read(BinaryReader reader);
}

/// <summary>Serializes <see cref="string"/> payloads as UTF-8 length-prefixed strings.</summary>
public sealed class StringPayloadSerializer : IPayloadSerializer<string>
{
    public static StringPayloadSerializer Instance { get; } = new();

    public void Write(BinaryWriter writer, string payload)
        => writer.Write(payload);

    public string Read(BinaryReader reader)
        => reader.ReadString();
}

/// <summary>Serializes <see cref="int"/> payloads as 4-byte signed integers.</summary>
public sealed class Int32PayloadSerializer : IPayloadSerializer<int>
{
    public static Int32PayloadSerializer Instance { get; } = new();

    public void Write(BinaryWriter writer, int payload)
        => writer.Write(payload);

    public int Read(BinaryReader reader)
        => reader.ReadInt32();
}
