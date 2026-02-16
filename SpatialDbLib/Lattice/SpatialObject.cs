using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
///////////////////////////////
namespace SpatialDbLib.Lattice;

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
        if (depth < 0) return false;
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
    public VenueLeafNode? SourceLeaf { get; set; }
    public SpatialObjectProxy(SpatialObject originalObj, VenueLeafNode targetLeaf, LongVector3 proposedPosition)
        : base([.. originalObj.GetPositionStack()])
    {
        m_coordinator = new ProxyCommitCoordinator<SpatialObject, SpatialObjectProxy>(originalObj, targetLeaf);
        SetLocalPosition(proposedPosition);
    }
    public virtual void Commit() => m_coordinator.Commit(oo => oo.SetPositionStack(GetPositionStack()), () => SetPositionStack([]), this);
    public void Rollback() => m_coordinator.Rollback(this);
}
public class ProxyCommitCoordinator<TOriginal, TProxy>(TOriginal originalObject, VenueLeafNode targetLeaf, VenueLeafNode? sourceLeaf = null)
    where TOriginal : ISpatialObject
    where TProxy : ISpatialObject
{
    public enum ProxyState { Uncommitted, Committed, RolledBack }
    private ProxyState m_state = ProxyState.Uncommitted;
    public TOriginal OriginalObject { get; } = originalObject;
    public VenueLeafNode TargetLeaf { get; set; } = targetLeaf;
    public VenueLeafNode? SourceLeaf { get; set; } = sourceLeaf;
    public bool IsCommitted => m_state == ProxyState.Committed;
    public ProxyState State => m_state;
    public void Commit(Action<TOriginal> transferState, Action clearProxyState, ISpatialObjectProxy proxy)
    {
        if (m_state == ProxyState.Committed) throw new InvalidOperationException("Proxy already committed!");
        static int CompareBoundsMin(LongVector3 a, LongVector3 b)
        {
            var c = a.X.CompareTo(b.X);
            if (c != 0) return c;
            c = a.Y.CompareTo(b.Y);
            if (c != 0) return c;
            return a.Z.CompareTo(b.Z);
        }
        while (true)
        {
            var target = TargetLeaf ?? throw new InvalidOperationException("TargetLeaf is null in ProxyCommitCoordinator.Commit.");
            var source = SourceLeaf;
            VenueLeafNode first = target;
            VenueLeafNode? second = source;
            if (second != null && CompareBoundsMin(first.Bounds.Min, second.Bounds.Min) > 0)
            {
                first = target;
                second = source;
            }
            var locks = new MultiSyncerScope();
            locks.Add(new SlimSyncer(((ISync)first).Sync, SlimSyncer.LockMode.Write, "ProxyCommitCoordinator.Commit: first"));
            if (second != null) locks.Add(new SlimSyncer(((ISync)second).Sync, SlimSyncer.LockMode.Write, "ProxyCommitCoordinator.Commit: second"));
            using var _ = locks;
            if (target.IsRetired || (source != null && source.IsRetired)) continue;
            source?.Vacate(OriginalObject);
            transferState(OriginalObject);
            target.Replace(proxy);
            clearProxyState();
            m_state = ProxyState.Committed;
            break;
        }
    }
    public void Rollback(ISpatialObjectProxy proxy)
    {
        if (m_state != ProxyState.Uncommitted) return;
        TargetLeaf.Vacate(proxy);
        m_state = ProxyState.RolledBack;
    }
}