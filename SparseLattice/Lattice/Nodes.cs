using SparseLattice.Math;
///////////////////////////////
namespace SparseLattice.Lattice;

public interface ISparseNode
{
}

public sealed class SparseBranchNode : ISparseNode
{
    public ushort SplitDimension { get; }
    public long SplitValue { get; }

    private ISparseNode? m_below;
    private ISparseNode? m_above;

    public ISparseNode? Below => m_below;
    public ISparseNode? Above => m_above;

    public SparseBranchNode(ushort splitDimension, long splitValue)
    {
        SplitDimension = splitDimension;
        SplitValue = splitValue;
    }

    internal void SetBelow(ISparseNode? node) => m_below = node;
    internal void SetAbove(ISparseNode? node) => m_above = node;

    public int RealizedChildCount
        => (m_below is not null ? 1 : 0) + (m_above is not null ? 1 : 0);
}

public sealed class FrozenBranchNode : ISparseNode
{
    public ushort SplitDimension { get; }
    public long SplitValue { get; }

    // exactly 0, 1 or 2 entries; array length == RealizedChildCount
    private readonly ISparseNode[] m_children;

    // index 0 = Below (if present), index 1 = Above (if present)
    // use tag bits to record which sides are realized
    private readonly byte m_childMask;

    private const byte s_BelowMask = 0b01;
    private const byte s_AboveMask = 0b10;

    public ISparseNode? Below
        => (m_childMask & s_BelowMask) != 0 ? m_children[0] : null;

    public ISparseNode? Above
        => (m_childMask & s_AboveMask) != 0
            ? m_children[(m_childMask & s_BelowMask) != 0 ? 1 : 0]
            : null;

    public int RealizedChildCount => m_children.Length;

    public FrozenBranchNode(ushort splitDimension, long splitValue, ISparseNode? below, ISparseNode? above)
    {
        SplitDimension = splitDimension;
        SplitValue = splitValue;

        byte mask = 0;
        int count = 0;
        if (below is not null) { mask |= s_BelowMask; count++; }
        if (above is not null) { mask |= s_AboveMask; count++; }

        m_childMask = mask;
        m_children = new ISparseNode[count];

        int writeIndex = 0;
        if (below is not null) m_children[writeIndex++] = below;
        if (above is not null) m_children[writeIndex] = above;
    }
}

public sealed class SparseLeafNode<TPayload> : ISparseNode
{
    private SparseOccupant<TPayload>[] m_occupants;

    public ReadOnlySpan<SparseOccupant<TPayload>> Occupants => m_occupants.AsSpan();
    public int Count => m_occupants.Length;

    public SparseLeafNode(SparseOccupant<TPayload>[] occupants)
    {
        m_occupants = occupants;
    }

    internal void ReplaceOccupants(SparseOccupant<TPayload>[] occupants)
        => m_occupants = occupants;
}

public readonly struct SparseOccupant<TPayload>(SparseVector position, TPayload payload)
{
    public readonly SparseVector Position = position;
    public readonly TPayload Payload = payload;
}

public readonly struct SparseTreeStats
{
    public int TotalNodes { get; init; }
    public int BranchNodes { get; init; }
    public int LeafNodes { get; init; }
    public int TotalOccupants { get; init; }
    public int MaxDepth { get; init; }
    public double AverageLeafOccupancy { get; init; }
}
