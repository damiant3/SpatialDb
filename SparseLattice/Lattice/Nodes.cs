using SparseLattice.Math;
///////////////////////////////
namespace SparseLattice.Lattice;

public interface ISparseNode
{
}

public static class SparseNodeExtensions
{
    public static bool TryGetBranch(this ISparseNode node, out ushort splitDimension, out long splitValue,
        out ISparseNode? below, out ISparseNode? above)
    {
        if (node is FrozenBranchNode frozen)
        {
            splitDimension = frozen.SplitDimension;
            splitValue     = frozen.SplitValue;
            below          = frozen.Below;
            above          = frozen.Above;
            return true;
        }
        if (node is SparseBranchNode mutable)
        {
            splitDimension = mutable.SplitDimension;
            splitValue     = mutable.SplitValue;
            below          = mutable.Below;
            above          = mutable.Above;
            return true;
        }
        splitDimension = 0;
        splitValue     = 0;
        below          = null;
        above          = null;
        return false;
    }
}

public sealed class SparseBranchNode : ISparseNode
{
    public ushort SplitDimension { get; }
    public long SplitValue { get; }

    ISparseNode? m_below;
    ISparseNode? m_above;

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
    readonly ISparseNode[] m_children;

    readonly byte m_childMask;

    const byte s_belowMask = 0b01;
    const byte s_aboveMask = 0b10;

    public ISparseNode? Below
        => (m_childMask & s_belowMask) != 0 ? m_children[0] : null;

    public ISparseNode? Above
        => (m_childMask & s_aboveMask) != 0
            ? m_children[(m_childMask & s_belowMask) != 0 ? 1 : 0]
            : null;

    public int RealizedChildCount => m_children.Length;

    public FrozenBranchNode(ushort splitDimension, long splitValue, ISparseNode? below, ISparseNode? above)
    {
        SplitDimension = splitDimension;
        SplitValue = splitValue;

        byte mask = 0;
        int count = 0;
        if (below is not null) { mask |= s_belowMask; count++; }
        if (above is not null) { mask |= s_aboveMask; count++; }

        m_childMask = mask;
        m_children = new ISparseNode[count];

        int writeIndex = 0;
        if (below is not null) m_children[writeIndex++] = below;
        if (above is not null) m_children[writeIndex] = above;
    }
}

public sealed class SparseLeafNode<TPayload> : ISparseNode
{
    SparseOccupant<TPayload>[] m_occupants;

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
