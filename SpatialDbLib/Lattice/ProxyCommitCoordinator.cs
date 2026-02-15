using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
///////////////////////////////
namespace SpatialDbLib.Lattice;
public class ProxyCommitCoordinator<TOriginal, TProxy>(TOriginal originalObject, VenueLeafNode targetLeaf, VenueLeafNode? sourceLeaf = null)
    where TOriginal : ISpatialObject
    where TProxy : ISpatialObject
{
    public enum ProxyState
    {
        Uncommitted,
        Committed,
        RolledBack
    }

    private ProxyState m_state = ProxyState.Uncommitted;
    public TOriginal OriginalObject { get; } = originalObject;
    public VenueLeafNode TargetLeaf { get; set; } = targetLeaf;
    public VenueLeafNode? SourceLeaf { get; set; } = sourceLeaf;
    public bool IsCommitted => m_state == ProxyState.Committed;
    public ProxyState State => m_state;

    public void Commit(
        Action<TOriginal> transferState,
        Action clearProxyState,
        ISpatialObjectProxy proxy)
    {
        if (m_state == ProxyState.Committed)
            throw new InvalidOperationException("Proxy already committed!");

        int CompareBoundsMin(LongVector3 a, LongVector3 b)
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
            VenueLeafNode? second = source;  // insert, for example

            if (second != null)
            {
                // Determine deterministic lock order to avoid deadlocks
                if (CompareBoundsMin(first.Bounds.Min, second.Bounds.Min) > 0)
                {
                    first = target;
                    second = source;
                }
            }
            var locks = new MultiSyncerScope();
            locks.Add(new SlimSyncer(((ISync)first).Sync, SlimSyncer.LockMode.Write, "ProxyCommitCoordinator.Commit: first"));
            if(second != null)
                locks.Add(new SlimSyncer(((ISync)second).Sync, SlimSyncer.LockMode.Write, "ProxyCommitCoordinator.Commit: second"));

            using var _ = locks;
            if (target.IsRetired || (source != null && source.IsRetired))
                continue;

            // Perform transfer into original, swap into target, vacate source, clear proxy state.
            transferState(OriginalObject);
            target.Replace(proxy);
            source?.Vacate(OriginalObject);
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