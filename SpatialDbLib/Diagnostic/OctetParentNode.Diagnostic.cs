#if DIAGNOSTIC
using SpatialDbLib.Synchronize;
using SpatialDbLib.Diagnostic;
///////////////////////////////
namespace SpatialDbLib.Lattice;
public abstract partial class OctetParentNode
{
    static OctetParentNode()
    {
        var hookNames = new[]
        {
            "SignalBeforeBucketAndDispatch",
            "BlockBeforeBucketAndDispatch",
            "SignalAfterLeafLock",
            "BlockAfterLeafLock",
            "SignalSubdivideStart",
            "WaitSubdivideProceed",
            "SignalTickerStart",
            "WaitTickerProceed"
        };

        foreach (var name in hookNames)
        {
            // Access to create (via indexer) and reset to a deterministic non-signaled state.
            HookSet.Instance[name].Reset();
        }
    }
    public static int SleepThreadId { get; set; } = 0;
    public static int SleepMs { get; set; } = 1;
    public static bool UseYield { get; set; } = false;
    public static int CurrentSubdividerThreadId { get; set; }
    public enum SubdivideVariant
    {
        Correct,
        LockOrderWrong,
        NoLeafLockTaken,
        DisposeBeforeRetire
    }
    public static SubdivideVariant SelectedSubdivideVariant { get; set; } = SubdivideVariant.Correct;

    // Runtime-injectable delegate and registry (no extra using directives).
    public delegate void SubdivideImplDelegate(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice);
    private static readonly Dictionary<SubdivideVariant, SubdivideImplDelegate> s_subdivideImplRegistry = [];
    private static readonly object s_subdivideRegistryLock = new();

    public static void RegisterSubdivideImpl(SubdivideVariant variant, SubdivideImplDelegate impl)
    {
        if (impl == null) throw new ArgumentNullException(nameof(impl));
        lock (s_subdivideRegistryLock)
        {
            s_subdivideImplRegistry[variant] = impl;
        }
    }

    public static void UnregisterSubdivideImpl(SubdivideVariant variant)
    {
        lock (s_subdivideRegistryLock)
        {
            s_subdivideImplRegistry.Remove(variant);
        }
    }

    partial void SubdivideAndMigrate_Impl(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        switch (SelectedSubdivideVariant)
        {
            case SubdivideVariant.Correct:
                SubdivideAndMigrate_ImplCorrect(parent, subdividingleaf, latticeDepth, childIndex, branchOrSublattice);
                break;
            case SubdivideVariant.LockOrderWrong:
                SubdivideAndMigrate_Impl_LockOrderWrong(parent, subdividingleaf, latticeDepth, childIndex, branchOrSublattice);
                break;
            case SubdivideVariant.NoLeafLockTaken:
                SubdivideAndMigrate_Impl_NoLeafLockTaken(parent, subdividingleaf, latticeDepth, childIndex, branchOrSublattice);
                break;
            case SubdivideVariant.DisposeBeforeRetire:
                SubdivideAndMigrate_Impl_DisposeBeforeRetire(parent, subdividingleaf, latticeDepth, childIndex, branchOrSublattice);
                break;
            default:
                throw new InvalidOperationException($"Unknown SubdivideVariant: {SelectedSubdivideVariant}");
        }
    }
    void SubdivideAndMigrate_ImplCorrect(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        // diagnostic
        HookSet.Instance["SignalSubdivideStart"].Set();
        HookSet.Instance["WaitSubdivideProceed"].Wait();
        CurrentSubdividerThreadId = Environment.CurrentManagedThreadId;

        // prod
        using var parentLock = new SlimSyncer(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent");
        using var leafLock = new SlimSyncer(((ISync)subdividingleaf).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf");

        // diagnostic
        HookSet.Instance["SignalAfterLeafLock"].Set();
        if (SleepThreadId == Environment.CurrentManagedThreadId)
        {
            if (UseYield) Thread.Yield();
            else Thread.Sleep(System.Math.Max(1, SleepMs));
        }
        HookSet.Instance["BlockAfterLeafLock"].Wait();

        // prod
        if (subdividingleaf.IsRetired) return;
        var migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        var occupantsSnapshot = migrationSnapshot.Objects;
        IChildNode<OctetParentNode> newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }

    partial void BucketAndDispatchMigrants_Impl(IList<ISpatialObject> objs)
    {
        // diagnostic
        HookSet.Instance["SignalBeforeBucketAndDispatch"].Set();
        HookSet.Instance["BlockBeforeBucketAndDispatch"].Wait();

        // prod
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

    // WRONG LOCK ORDER: acquire leaf lock first, then parent lock (can deadlock with other threads)
    void SubdivideAndMigrate_Impl_LockOrderWrong(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        // diagnostic
        HookSet.Instance["SignalSubdivideStart"].Set();
        HookSet.Instance["WaitSubdivideProceed"].Wait();
        CurrentSubdividerThreadId = Environment.CurrentManagedThreadId;

        // WRONG: leaf lock acquired before parent lock
        using var leafLock = new SlimSyncer(((ISync)subdividingleaf).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf (wrong order)");
        using var parentLock = new SlimSyncer(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent (wrong order)");

        // diagnostic
        HookSet.Instance["SignalAfterLeafLock"].Set();
        if (SleepThreadId == Environment.CurrentManagedThreadId)
        {
            if (UseYield) Thread.Yield();
            else Thread.Sleep(System.Math.Max(1, SleepMs));
        }
        HookSet.Instance["BlockAfterLeafLock"].Wait();

        if (subdividingleaf.IsRetired) return;
        var migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        var occupantsSnapshot = migrationSnapshot.Objects;
        IChildNode<OctetParentNode> newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }

    // NO LEAF LOCK: only acquire parent lock (leaves occupant locks later) — can allow races with tickers/other movers
    void SubdivideAndMigrate_Impl_NoLeafLockTaken(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        // diagnostic
        HookSet.Instance["SignalSubdivideStart"].Set();
        HookSet.Instance["WaitSubdivideProceed"].Wait();
        CurrentSubdividerThreadId = Environment.CurrentManagedThreadId;

        // only parent lock taken — missing the leaf write lock
        using var parentLock = new SlimSyncer(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent (no leaf lock)");

        // diagnostic
        HookSet.Instance["SignalAfterLeafLock"].Set();
        if (SleepThreadId == Environment.CurrentManagedThreadId)
        {
            if (UseYield) Thread.Yield();
            else Thread.Sleep(System.Math.Max(1, SleepMs));
        }
        HookSet.Instance["BlockAfterLeafLock"].Wait();

        if (subdividingleaf.IsRetired) return;
        var migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration(); // snapshot will try to acquire occupant locks without leaf lock held
        var occupantsSnapshot = migrationSnapshot.Objects;
        IChildNode<OctetParentNode> newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }

    // ORDERING BUG: dispose the migration snapshot before retiring the leaf (less plausible but intentionally wrong)
    void SubdivideAndMigrate_Impl_DisposeBeforeRetire(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        // diagnostic
        HookSet.Instance["SignalSubdivideStart"].Set();
        HookSet.Instance["WaitSubdivideProceed"].Wait();
        CurrentSubdividerThreadId = Environment.CurrentManagedThreadId;

        using var parentLock = new SlimSyncer(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent");
        using var leafLock = new SlimSyncer(((ISync)subdividingleaf).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf");

        // diagnostic
        HookSet.Instance["SignalAfterLeafLock"].Set();
        if (SleepThreadId == Environment.CurrentManagedThreadId)
        {
            if (UseYield) Thread.Yield();
            else Thread.Sleep(System.Math.Max(1, SleepMs));
        }
        HookSet.Instance["BlockAfterLeafLock"].Wait();

        if (subdividingleaf.IsRetired) return;
        var migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        var occupantsSnapshot = migrationSnapshot.Objects;
        IChildNode<OctetParentNode> newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;

        // WRONG ORDER: dispose snapshot first, then retire leaf
        migrationSnapshot.Dispose();
        subdividingleaf.Retire();
    }
}
#endif