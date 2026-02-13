#if DIAGNOSTIC
using System.Threading;
///////////////////////////////
namespace SpatialDbLib.Lattice;
public abstract partial class OctetParentNode
{
    // Full diagnostics: real ManualResetEventSlim instances and diagnostic implementations.
    internal static class DiagnosticHooks
    {
        public static ManualResetEventSlim SignalBeforeBucketAndDispatch = new(false);
        public static ManualResetEventSlim BlockBeforeBucketAndDispatch = new(false);
        public static ManualResetEventSlim SignalAfterLeafLock = new(false);
        public static ManualResetEventSlim BlockAfterLeafLock = new(false);
        public static int? CurrentSubdividerThreadId;
        public static ManualResetEventSlim SignalSubdivideStart = new(false);
        public static ManualResetEventSlim WaitSubdivideProceed = new(false);
        public static int? CurrentTickerThreadId;
        public static ManualResetEventSlim SignalTickerStart = new(false);
        public static ManualResetEventSlim WaitTickerProceed = new(false);
        public static int? SleepThreadId = null;
        public static int SleepMs = 1;
        public static bool UseYield = false;
    }

    // Diagnostic variant of SubdivideAndMigrate (contains the signaling/waiting used by tests).
    partial void SubdivideAndMigrate_Impl(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        try
        {
            DiagnosticHooks.CurrentSubdividerThreadId = Environment.CurrentManagedThreadId;
            DiagnosticHooks.SignalSubdivideStart?.Set();
            DiagnosticHooks.WaitSubdivideProceed?.Wait();
        }
        catch { }

        using var parentLock = new SlimSyncer(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent");
        using var leafLock = new SlimSyncer(((ISync)subdividingleaf).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf");

        DiagnosticHooks.SignalAfterLeafLock?.Set();
        if (DiagnosticHooks.SleepThreadId.HasValue && DiagnosticHooks.SleepThreadId.Value == Thread.CurrentThread.ManagedThreadId)
        {
            if (DiagnosticHooks.UseYield) Thread.Yield();
            else Thread.Sleep(System.Math.Max(1, DiagnosticHooks.SleepMs));
        }
        DiagnosticHooks.BlockAfterLeafLock?.Wait();

        if (subdividingleaf.IsRetired) return;
        var migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        var occupantsSnapshot = migrationSnapshot.Objects;
        IChildNode<OctetParentNode> newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }

    // Diagnostic variant of BucketAndDispatchMigrants (contains the signaling/waiting used by tests).
    partial void BucketAndDispatchMigrants_Impl(IList<ISpatialObject> objs)
    {
        DiagnosticHooks.SignalBeforeBucketAndDispatch?.Set();
        DiagnosticHooks.BlockBeforeBucketAndDispatch?.Wait();
        var buckets = new List<ISpatialObject>[8];
        foreach (var obj in objs)
        {
            if (!Bounds.Contains(obj.LocalPosition))
                throw new InvalidOperationException("Migrant has no home.");
            if (SelectChild(obj.LocalPosition) is not SelectChildResult result)
                throw new InvalidOperationException("Containment invariant violated");
            buckets[result.IndexInParent] ??= [];
            buckets[result.IndexInParent].Add(obj);
        }
        for (byte i = 0; i < 8; i++)
        {
            if (buckets[i] == null) continue;
            Children[i].AdmitMigrants(buckets[i]);
        }
    }
}
#endif