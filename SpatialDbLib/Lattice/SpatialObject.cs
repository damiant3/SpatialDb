///////////////////////////////
namespace SpatialDbLib.Lattice;

public class SpatialObject(LongVector3 initialPosition)
{
    public Guid Guid { get; } = Guid.NewGuid();

    IList<LongVector3> m_positionStack = [initialPosition];

    public IList<LongVector3> GetPositionStack()
    {
        using var s = new SlimSyncer(m_positionLock, SlimSyncer.LockMode.Read);
        return [.. m_positionStack];
    }

    public void SetPositionStack(IList<LongVector3> newStack)
    {
        using var s = new SlimSyncer(m_positionLock, SlimSyncer.LockMode.Write);
        m_positionStack = newStack;
    }

    public void SetLocalPosition(LongVector3 newLocalPos)
    {
        using var s = new SlimSyncer(m_positionLock, SlimSyncer.LockMode.Write);
        m_positionStack[^1] = newLocalPos;
    }

    public void SetPositionAtDepth(int depth, LongVector3 newPos)
    {

        if (depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth cannot be negative.");
        }
        if (PositionStackDepth < depth - 1)
        {
            throw new InvalidOperationException("Object stack not hydrated to depth-1 on set at depth: " + depth);
        }
        using var s = new SlimSyncer(m_positionLock, SlimSyncer.LockMode.Write);
        if (PositionStackDepth == depth)
        {
            m_positionStack.Add(newPos);
            return;
        }
        m_positionStack[depth] = newPos; // doesn't chop off end, should.
    }

    public int PositionStackDepth
    {
        get
        {
            return m_positionStack.Count;
        }
    }
    public LongVector3 LocalPosition
    {
        get
        {
//#if DEBUG
//            if (LatticePositionStackDepth == 0)
//            {
//                Debugger.Break();
//                throw new InvalidOperationException("LatticePositionStack is empty.");
//            }
//#endif
            return m_positionStack[^1];

        }
    }

    public readonly ReaderWriterLockSlim m_positionLock = new(LockRecursionPolicy.SupportsRecursion);

    public ulong GetDiscriminator()
    {
        Span<byte> bytes = stackalloc byte[16];
        Guid.TryWriteBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }
}

public class SpatialObjectProxy : SpatialObject
{
    public enum ProxyState
    {
        Uncommitted,
        Committed,
        RolledBack
    }
    private ProxyState m_proxyState;
    public bool IsCommitted => ProxyState.Committed == m_proxyState;
    public SpatialObject OriginalObject { get; }
    public OccupantLeafNode TargetLeaf { get; set; }

    public SpatialObjectProxy(SpatialObject originalObj, OccupantLeafNode targetleaf, LongVector3 proposedPosition)
        : base(proposedPosition)
    {
#if DEBUG
        if (originalObj.PositionStackDepth == 0)
            throw new InvalidOperationException("Original object has no position.");
#endif
        m_proxyState = ProxyState.Uncommitted;
        OriginalObject = originalObj;
        TargetLeaf = targetleaf;

        SetPositionStack(originalObj.GetPositionStack());
        OriginalObject.SetLocalPosition(proposedPosition);
    }

    public void Commit()
    {
        if (IsCommitted)
            throw new InvalidOperationException("Proxy already swapped!");

        while (true)
        {
            var leaf = TargetLeaf;
            var parent = leaf.Parent;

            using var s = new MultiObjectScope<object>
            (
                [parent, leaf, OriginalObject, this],
                [

                    new(parent.m_dependantsSync, SlimSyncer.LockMode.Write),
                    new(leaf.m_dependantsSync, SlimSyncer.LockMode.Write),
                    new(OriginalObject.m_positionLock, SlimSyncer.LockMode.Write),
                    new(m_positionLock, SlimSyncer.LockMode.Write),
                ]
            );

            if (leaf.IsRetired)
            {
                if (!ReferenceEquals(TargetLeaf, leaf))
                    continue;
                throw new InvalidOperationException("Target leaf is retired.");
            }

            if (!leaf.Contains(this))
            {
                if (!ReferenceEquals(leaf, TargetLeaf))
                    continue;
                throw new InvalidOperationException("Target leaf does not contain this proxy.");
            }
            OriginalObject.SetPositionStack(GetPositionStack());
            leaf.Replace(this);
            SetPositionStack([]);
            m_proxyState = ProxyState.Committed;
            break;
        }
    }

    public void Rollback()
    {
        if (m_proxyState != ProxyState.Uncommitted) return;
        TargetLeaf.Leave(this);
        m_proxyState = ProxyState.RolledBack;
    }
}