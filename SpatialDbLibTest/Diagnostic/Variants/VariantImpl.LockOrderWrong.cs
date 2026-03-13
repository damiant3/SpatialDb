public static class VariantImpl
{
    public static void Impl(SpatialDbLib.Lattice.OctetParentNode parent, SpatialDbLib.Lattice.VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        SpatialDbLib.Diagnostic.HookSet.Instance["SignalSubdivideStart"].Set();
        SpatialDbLib.Diagnostic.HookSet.Instance["WaitSubdivideProceed"].Wait();
        SpatialDbLib.Lattice.OctetParentNode.CurrentSubdividerThreadId = System.Environment.CurrentManagedThreadId;

        using Common.Core.Sync.SlimSyncer leafLock = new(((Common.Core.Sync.ISync)subdividingleaf).Sync, Common.Core.Sync.SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf (injected wrong order)");
        using Common.Core.Sync.SlimSyncer parentLock = new(((Common.Core.Sync.ISync)parent).Sync, Common.Core.Sync.SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent (injected wrong order)");

        SpatialDbLib.Diagnostic.HookSet.Instance["SignalAfterLeafLock"].Set();
        if (SpatialDbLib.Lattice.OctetParentNode.SleepThreadId == System.Environment.CurrentManagedThreadId)
        {
            if (SpatialDbLib.Lattice.OctetParentNode.UseYield) System.Threading.Thread.Yield();
            else System.Threading.Thread.Sleep(System.Math.Max(1, SpatialDbLib.Lattice.OctetParentNode.SleepMs));
        }
        SpatialDbLib.Diagnostic.HookSet.Instance["BlockAfterLeafLock"].Wait();

        if (subdividingleaf.IsRetired) return;
        Common.Core.Sync.MultiObjectScope<SpatialDbLib.Lattice.ISpatialObject> migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        System.Collections.Generic.List<SpatialDbLib.Lattice.ISpatialObject> occupantsSnapshot = migrationSnapshot.Objects;
        SpatialDbLib.Lattice.IInternalChildNode newBranch = parent.CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }
}
