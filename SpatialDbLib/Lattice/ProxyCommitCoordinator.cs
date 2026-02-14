using SpatialDbLib.Synchronize;
///////////////////////////////
namespace SpatialDbLib.Lattice;
public class ProxyCommitCoordinator<TOriginal, TProxy>(TOriginal originalObject, VenueLeafNode targetLeaf)
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
    public bool IsCommitted => m_state == ProxyState.Committed;
    public ProxyState State => m_state;

    public void Commit(Action<TOriginal> transferState, Action clearProxyState, ISpatialObjectProxy proxy)
    {
        if (m_state == ProxyState.Committed)
            throw new InvalidOperationException("Proxy already committed!");

        while (true)
        {
            var leaf = TargetLeaf ?? throw new InvalidOperationException("TargetLeaf is null in ProxyCommitCoordinator.Commit.");
            using var leafLock = new SlimSyncer(((ISync)leaf).Sync, SlimSyncer.LockMode.Write, "ProxyCommitCoordinator.Commit: Leaf");
            if (leaf.IsRetired)
                continue;
            transferState(OriginalObject);
            leaf.Replace(proxy);
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