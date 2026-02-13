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

        // Diagnostic: announce commit attempt (tests enable SlimSyncerDiagnostics)
        if (SlimSyncerDiagnostics.Enabled)
            Console.WriteLine($"ProxyCommitCoordinator.Commit: entering. Proxy.TargetLeaf.Bounds={TargetLeaf?.Bounds}, Proxy.Original={OriginalObject.Guid}");

        while (true)
        {
            var leaf = TargetLeaf ?? throw new InvalidOperationException("TargetLeaf is null in ProxyCommitCoordinator.Commit.");
            if (SlimSyncerDiagnostics.Enabled)
                Console.WriteLine($"ProxyCommitCoordinator.Commit: attempting leaf lock for leaf.Bounds={leaf.Bounds}, leaf.IsRetired={leaf.IsRetired}");

            using var leafLock = new SlimSyncer(((ISync)leaf).Sync, SlimSyncer.LockMode.Write, "ProxyCommitCoordinator.Commit: Leaf");

            if (leaf.IsRetired)
            {
                if (SlimSyncerDiagnostics.Enabled)
                    Console.WriteLine("ProxyCommitCoordinator.Commit: leaf.IsRetired -> retrying with updated TargetLeaf");
                continue; // Retry with potentially updated TargetLeaf
            }

            if (SlimSyncerDiagnostics.Enabled)
                Console.WriteLine($"ProxyCommitCoordinator.Commit: performing transferState for Original={OriginalObject.Guid}");

            transferState(OriginalObject);

            if (SlimSyncerDiagnostics.Enabled)
                Console.WriteLine($"ProxyCommitCoordinator.Commit: calling leaf.Replace for leaf.Bounds={leaf.Bounds}");

            leaf.Replace(proxy);

            if (SlimSyncerDiagnostics.Enabled)
                Console.WriteLine($"ProxyCommitCoordinator.Commit: clearing proxy state and marking committed for Original={OriginalObject.Guid}");

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