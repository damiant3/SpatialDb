using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
///////////////////////////////
namespace SpatialDbLib.Lattice;

// Core spatial object interface
public interface ISpatialObject : ISync
{
    Guid Guid { get; }
    LongVector3 LocalPosition { get; }
    int PositionStackDepth { get; }
    IList<LongVector3> GetPositionStack();
    void SetPositionStack(IList<LongVector3> newStack);
    void AppendPosition(LongVector3 newPosition);
    void SetLocalPosition(LongVector3 newLocalPos);
    bool HasPositionAtDepth(int depth);
    LongVector3 GetPositionAtDepth(int depth);
}

// Proxy contract
public interface ISpatialObjectProxy : ISpatialObject
{
    bool IsCommitted { get; }
    ISpatialObject OriginalObject { get; }
    VenueLeafNode TargetLeaf { get; set; }
    void Commit();
    void Rollback();
}

public class SpatialObject(IList<LongVector3> initialPosition)
    : ISpatialObject
{
    public Guid Guid { get; } = Guid.NewGuid();

    internal protected ReaderWriterLockSlim Sync = new(LockRecursionPolicy.SupportsRecursion);
    ReaderWriterLockSlim ISync.Sync => Sync;

    IList<LongVector3> m_positionStack = [.. initialPosition];

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
        m_positionStack = [.. newStack];
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

public class SpatialObjectProxy : SpatialObject, ISpatialObjectProxy
{
    private readonly ProxyCommitCoordinator<SpatialObject, SpatialObjectProxy> m_coordinator;

    public bool IsCommitted => m_coordinator.IsCommitted;
    public SpatialObject OriginalObject => m_coordinator.OriginalObject;
    ISpatialObject ISpatialObjectProxy.OriginalObject => OriginalObject;

    public VenueLeafNode TargetLeaf
    {
        get => m_coordinator.TargetLeaf;
        set => m_coordinator.TargetLeaf = value;
    }

    public SpatialObjectProxy(SpatialObject originalObj, VenueLeafNode targetLeaf, LongVector3 proposedPosition)
        : base([.. originalObj.GetPositionStack()])
    {
#if DEBUG
        if (originalObj.PositionStackDepth == 0)
            throw new InvalidOperationException("Original object has no position.");
        if (targetLeaf.IsRetired)
            throw new InvalidOperationException("Target leaf is retired.");
#endif
        m_coordinator = new ProxyCommitCoordinator<SpatialObject, SpatialObjectProxy>(originalObj, targetLeaf);
        SetLocalPosition(proposedPosition);
    }

    public virtual void Commit()
        => m_coordinator.Commit(
            transferState: original => original.SetPositionStack(GetPositionStack()),
            clearProxyState: () => SetPositionStack([]),
            proxy: this);

    public void Rollback()
        => m_coordinator.Rollback(this);
}