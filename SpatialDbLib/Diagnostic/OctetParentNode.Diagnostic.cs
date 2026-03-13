#if DIAGNOSTIC
using Common.Core.Sync;
using SpatialDbLib.Diagnostic;
///////////////////////////////
namespace SpatialDbLib.Lattice;
public abstract partial class OctetParentNode
{
    static OctetParentNode()
    {
        string[] hookNames =
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

        foreach (string name in hookNames)
            HookSet.Instance[name].Reset();
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

    partial void Subdivide_Impl(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
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
        HookSet.Instance["SignalSubdivideStart"].Set();
        HookSet.Instance["WaitSubdivideProceed"].Wait();
        CurrentSubdividerThreadId = Environment.CurrentManagedThreadId;

        using SlimSyncer parentLock = new(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent");
        using SlimSyncer leafLock = new(((ISync)subdividingleaf).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf");

        HookSet.Instance["SignalAfterLeafLock"].Set();
        if (SleepThreadId == Environment.CurrentManagedThreadId)
        {
            if (UseYield) Thread.Yield();
            else Thread.Sleep(System.Math.Max(1, SleepMs));
        }
        HookSet.Instance["BlockAfterLeafLock"].Wait();

        if (subdividingleaf.IsRetired) return;
        MultiObjectScope<ISpatialObject> migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        List<ISpatialObject> occupantsSnapshot = migrationSnapshot.Objects;
        IInternalChildNode newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }

    partial void Migrate_Impl(IList<ISpatialObject> objs)
    {
        HookSet.Instance["SignalBeforeBucketAndDispatch"].Set();
        HookSet.Instance["BlockBeforeBucketAndDispatch"].Wait();

        List<ISpatialObject>[] buckets = new List<ISpatialObject>[8];
        foreach (ISpatialObject obj in objs)
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
            Children[i].Migrate(buckets[i]);
        }
    }

    void SubdivideAndMigrate_Impl_LockOrderWrong(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        HookSet.Instance["SignalSubdivideStart"].Set();
        HookSet.Instance["WaitSubdivideProceed"].Wait();
        CurrentSubdividerThreadId = Environment.CurrentManagedThreadId;

        using SlimSyncer leafLock = new(((ISync)subdividingleaf).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf (wrong order)");
        using SlimSyncer parentLock = new(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent (wrong order)");

        HookSet.Instance["SignalAfterLeafLock"].Set();
        if (SleepThreadId == Environment.CurrentManagedThreadId)
        {
            if (UseYield) Thread.Yield();
            else Thread.Sleep(System.Math.Max(1, SleepMs));
        }
        HookSet.Instance["BlockAfterLeafLock"].Wait();

        if (subdividingleaf.IsRetired) return;
        MultiObjectScope<ISpatialObject> migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        List<ISpatialObject> occupantsSnapshot = migrationSnapshot.Objects;
        IInternalChildNode newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }

    void SubdivideAndMigrate_Impl_NoLeafLockTaken(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        HookSet.Instance["SignalSubdivideStart"].Set();
        HookSet.Instance["WaitSubdivideProceed"].Wait();
        CurrentSubdividerThreadId = Environment.CurrentManagedThreadId;

        using SlimSyncer parentLock = new(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent (no leaf lock)");

        HookSet.Instance["SignalAfterLeafLock"].Set();
        if (SleepThreadId == Environment.CurrentManagedThreadId)
        {
            if (UseYield) Thread.Yield();
            else Thread.Sleep(System.Math.Max(1, SleepMs));
        }
        HookSet.Instance["BlockAfterLeafLock"].Wait();

        if (subdividingleaf.IsRetired) return;
        MultiObjectScope<ISpatialObject> migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        List<ISpatialObject> occupantsSnapshot = migrationSnapshot.Objects;
        IInternalChildNode newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }

    void SubdivideAndMigrate_Impl_DisposeBeforeRetire(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        HookSet.Instance["SignalSubdivideStart"].Set();
        HookSet.Instance["WaitSubdivideProceed"].Wait();
        CurrentSubdividerThreadId = Environment.CurrentManagedThreadId;

        using SlimSyncer parentLock = new(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent");
        using SlimSyncer leafLock = new(((ISync)subdividingleaf).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf");

        HookSet.Instance["SignalAfterLeafLock"].Set();
        if (SleepThreadId == Environment.CurrentManagedThreadId)
        {
            if (UseYield) Thread.Yield();
            else Thread.Sleep(System.Math.Max(1, SleepMs));
        }
        HookSet.Instance["BlockAfterLeafLock"].Wait();

        if (subdividingleaf.IsRetired) return;
        MultiObjectScope<ISpatialObject> migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        List<ISpatialObject> occupantsSnapshot = migrationSnapshot.Objects;
        IInternalChildNode newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;

        migrationSnapshot.Dispose();
        subdividingleaf.Retire();
    }
}
#endif
