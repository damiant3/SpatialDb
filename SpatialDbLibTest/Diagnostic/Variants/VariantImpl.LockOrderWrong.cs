public static class VariantImpl
{
    // Must match OctetParentNode.SubdivideImplDelegate
    public static void Impl(SpatialDbLib.Lattice.OctetParentNode parent, SpatialDbLib.Lattice.VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        // diagnostic coordination
        SpatialDbLib.Diagnostic.HookSet.Instance["SignalSubdivideStart"].Set();
        SpatialDbLib.Diagnostic.HookSet.Instance["WaitSubdivideProceed"].Wait();
        SpatialDbLib.Lattice.OctetParentNode.CurrentSubdividerThreadId = System.Environment.CurrentManagedThreadId;

        // WRONG: acquire leaf lock before parent lock (deadlock risk)
        using var leafLock = new SpatialDbLib.Synchronize.SlimSyncer(((SpatialDbLib.Synchronize.ISync)subdividingleaf).Sync, SpatialDbLib.Synchronize.SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf (injected wrong order)");
        using var parentLock = new SpatialDbLib.Synchronize.SlimSyncer(((SpatialDbLib.Synchronize.ISync)parent).Sync, SpatialDbLib.Synchronize.SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent (injected wrong order)");

        // diagnostic
        SpatialDbLib.Diagnostic.HookSet.Instance["SignalAfterLeafLock"].Set();
        if (SpatialDbLib.Lattice.OctetParentNode.SleepThreadId == System.Environment.CurrentManagedThreadId)
        {
            if (SpatialDbLib.Lattice.OctetParentNode.UseYield) System.Threading.Thread.Yield();
            else System.Threading.Thread.Sleep(System.Math.Max(1, SpatialDbLib.Lattice.OctetParentNode.SleepMs));
        }
        SpatialDbLib.Diagnostic.HookSet.Instance["BlockAfterLeafLock"].Wait();

        if (subdividingleaf.IsRetired) return;
        var migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        var occupantsSnapshot = migrationSnapshot.Objects;
        var newBranch = parent.CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }
}