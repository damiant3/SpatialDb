///////////////////////////////
using SpatialDbLib.Synchronize;

namespace SpatialDbLib.Lattice;

public class SpatialObject(IList<LongVector3> initialPosition)
    : ISync
{
    public Guid Guid { get; } = Guid.NewGuid();

    protected ReaderWriterLockSlim Sync = new(LockRecursionPolicy.SupportsRecursion);
    ReaderWriterLockSlim ISync.Sync => Sync;

    IList<LongVector3> m_positionStack = initialPosition;

    public IList<LongVector3> GetPositionStack()
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "SpatialObject.GetPositionStack");
        return [.. m_positionStack];
    }

    public void AppendPosition(LongVector3 newPosition)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "SpatialObject.AppendPosition");
        m_positionStack.Add(newPosition);
    }

    public void SetLocalPosition(LongVector3 newLocalPos)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "SpatialObject.SetLocalPosition");
        m_positionStack[^1] = newLocalPos;
    }

    public void SetPositionStack(IList<LongVector3> newStack)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "SpatialObject.SetPositionStack");
        m_positionStack = newStack;
    }

    public int PositionStackDepth
    {
        get
        {
            using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "SpatialObject.PositionStackDepth");
            return m_positionStack.Count;
        }
    }

    public bool HasPositionAtDepth(int depth)
    {
        if (depth < 0)
            return false;

        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "SpatialObject.HasPositionAtDepth");
        return depth < m_positionStack.Count;
    }

    public LongVector3 GetPositionAtDepth(int depth)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "SpatialObject.GetPositionAtDepth");

        if ((uint)depth >= (uint)m_positionStack.Count)
            throw new ArgumentOutOfRangeException(nameof(depth));

        return m_positionStack[depth];
    }

    public LongVector3 LocalPosition
    {
        get
        {
            using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "SpatialObject.LocalPosition");
            return m_positionStack[^1];
        }
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
    public VenueLeafNode TargetLeaf { get; set; }

    public SpatialObjectProxy(SpatialObject originalObj, VenueLeafNode targetleaf, LongVector3 proposedPosition)
        : base([.. originalObj.GetPositionStack()])
    {
#if DEBUG
        if (originalObj.PositionStackDepth == 0)
            throw new InvalidOperationException("Original object has no position.");
#endif
        m_proxyState = ProxyState.Uncommitted;
        OriginalObject = originalObj;
        TargetLeaf = targetleaf;
        SetLocalPosition(proposedPosition);
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

                    new(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SpatialObjectProxy.Commit: Parent"),
                    new(((ISync)leaf).Sync, SlimSyncer.LockMode.Write, "SpatialObjectProxy.Commit: Leaf"),
                    new(((ISync)OriginalObject).Sync, SlimSyncer.LockMode.Write, "SpatialObjectProxy.Commit: OriginalObject"),
                    new(Sync, SlimSyncer.LockMode.Write, "SpatialObjectProxy.Commit: Proxy"),
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
        TargetLeaf.Vacate(this);
        m_proxyState = ProxyState.RolledBack;
    }
}