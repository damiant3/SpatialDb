#if DIAGNOSTIC
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SpatialDbLib.Diagnostic;
using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;
using SpatialDbLib.Synchronize;
using SpatialDbLibTest.Helpers;

namespace SpatialDbLibTest.Diagnostic
{
    [TestClass]
    [DoNotParallelize]
    public class RaceConditionDiagnosisTests
    {
        public TestContext TestContext { get; set; }

        void DumpDiagnosticsAndFail(string reason)
        {
            var diag = SlimSyncerDiagnostics.DumpHistory();
            var held = LockTracker.DumpHeldLocks();
            // Try to clear diagnostics state so output is complete and stable
            TestContext.WriteLine("Dumping diagnostics and failing test:");
            TestContext.WriteLine(reason);
            TestContext.WriteLine("SlimSyncerDiagnostics:");
            TestContext.WriteLine(diag ?? "<empty>");
            TestContext.WriteLine("LockTracker.DumpHeldLocks:");
            TestContext.WriteLine(held ?? "<empty>");
            Assert.Fail($"{reason}\n\nSlimSyncerDiagnostics:\n{diag}\n\nLockTracker.DumpHeldLocks:\n{held}");
        }

        [TestMethod]
        public void Deterministic_MigrantHasNoHome_Diagnostic()
        {
            SlimSyncerDiagnostics.Enabled = true;
            SpatialLatticeOptions.TrackLocks = true;

            try
            {
                var permutations = new[]
                {
                    (delaySubdivider: false, delayMigration: false),
                    (delaySubdivider: true,  delayMigration: false),
                    (delaySubdivider: false, delayMigration: true),
                    (delaySubdivider: true,  delayMigration: true)
                };

                TestContext.WriteLine("Starting deterministic permutations test");
                foreach (var (delaySubdivider, delayMigration) in permutations)
                {
                    TestContext.WriteLine($"Permutation: delaySubdivider={delaySubdivider}, delayMigration={delayMigration}");

                    // reset hooks/knobs
                    OctetParentNode.SleepThreadId = 0;
                    OctetParentNode.SleepMs = 1;
                    OctetParentNode.UseYield = false;

                    HookSet.Instance.RestetAll();

                    var lattice = new TickableSpatialLattice();
                    var root = (TickableOctetParentNode)lattice.GetRootNode();

                    const int targetChildIndex = 0;
                    var subdividingLeaf = (TickableVenueLeafNode)root.Children[targetChildIndex];
                    // populate subdividing leaf
                    var occupants = new List<TickableSpatialObject>();
                    var basePos = subdividingLeaf.Bounds.Mid;
                    for (int i = 0; i < 63; i++)
                        occupants.Add(new TickableSpatialObject(new LongVector3(basePos.X + i, basePos.Y, basePos.Z)));
                    lattice.Insert(occupants.Cast<ISpatialObject>().ToList());
                    // create mover in sibling leaf
                    var siblingIndex = (targetChildIndex + 1) % 8;
                    var srcLeaf = (TickableVenueLeafNode)root.Children[siblingIndex];
                    var mover = new TickableSpatialObject(srcLeaf.Bounds.Mid) { Velocity = new IntVector3(100, 0, 0) };
                    lattice.Insert(mover);
                    mover.RegisterForTicks();
                    Thread.Sleep(50);
                    // ensure test thread holds no locks
                    int tid = Environment.CurrentManagedThreadId;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.Elapsed < TimeSpan.FromSeconds(2))
                    {
                        if (!LockTracker.DumpHeldLocks().Contains($"Thread {tid} holds"))
                            break;
                        Thread.Sleep(5);
                    }
                    var migrationDelay = new ManualResetEventSlim(true); // set = proceed; reset = wait
                    if (delayMigration) migrationDelay.Reset();

                    // start subdivider task
                    var subdivideTask = Task.Run(() =>
                        root.SubdivideAndMigrate(root, subdividingLeaf, lattice.LatticeDepth, targetChildIndex, branchOrSublattice: true)
                    );

                    if (!HookSet.Instance["SignalSubdivideStart"].Wait(TimeSpan.FromSeconds(5)))
                        DumpDiagnosticsAndFail("Timed out waiting for subdivider to publish thread id (SignalSubdivideStart).");

                    var subdividerTid = OctetParentNode.CurrentSubdividerThreadId;

                    // start migration task
                    var migrationTask = Task.Run(() =>
                    {
                        TickableVenueLeafNode.CurrentTickerThreadId = Environment.CurrentManagedThreadId;
                        HookSet.Instance["SignalTickerStart"].Set();
                        HookSet.Instance["WaitTickerProceed"].Wait();
                        migrationDelay.Wait();
                        mover.UnregisterForTicks();
                        using var objLock = new SlimSyncer(mover.Sync, SlimSyncer.LockMode.Write, "test.forceMove: Object");
                        mover.SetLocalPosition(subdividingLeaf.Bounds.Mid);
                        lattice.Remove(mover);
                        var insertResult = lattice.Insert(mover);

                    });

                    if (!HookSet.Instance["SignalTickerStart"].Wait(TimeSpan.FromSeconds(5)))
                        DumpDiagnosticsAndFail("Timed out waiting for migration thread to publish (SignalTickerStart).");

                    var migrationTid = TickableVenueLeafNode.CurrentTickerThreadId;

                    if (delaySubdivider)
                    {
                        OctetParentNode.SleepThreadId = subdividerTid;
                        OctetParentNode.SleepMs = 10;
                        OctetParentNode.UseYield = false;
                    }
                    else OctetParentNode.SleepThreadId = 0;
                    HookSet.Instance["WaitSubdivideProceed"].Set();
                    if (!HookSet.Instance["SignalAfterLeafLock"].Wait(TimeSpan.FromSeconds(5)))
                        DumpDiagnosticsAndFail("Timed out waiting for subdivider to acquire leaf lock (SignalAfterLeafLock).");
                    if (delayMigration) migrationDelay.Set();
                    HookSet.Instance["BlockAfterLeafLock"].Set();
                    if (!HookSet.Instance["SignalBeforeBucketAndDispatch"].Wait(TimeSpan.FromSeconds(5)))
                        DumpDiagnosticsAndFail("Timed out waiting for subdivider to reach bucket/dispatch hook.");
                    HookSet.Instance["BlockBeforeBucketAndDispatch"].Set();
                    HookSet.Instance["WaitTickerProceed"].Set();
                    var all = Task.WhenAll(subdivideTask, migrationTask);
                    if (!all.Wait(TimeSpan.FromSeconds(5)))
                        DumpDiagnosticsAndFail("Timed out waiting for tasks to complete in permutation run.");
                    if (all.IsFaulted)
                        DumpDiagnosticsAndFail($"Tasks faulted in permutation run: {all.Exception?.Flatten().InnerException}");
                    OctetParentNode.SleepThreadId = 0;
                }

                TestContext.WriteLine("Deterministic permutations test completed successfully");
                return;
            }
            catch (AggregateException agg)
            {
                var diag = SlimSyncerDiagnostics.DumpHistory();
                var held = LockTracker.DumpHeldLocks();
                Assert.Fail($"Deterministic interleave permutations produced exception(s): {agg.Flatten().InnerException}\n\nSlimSyncerDiagnostics:\n{diag}\n\nLockTracker.DumpHeldLocks:\n{held}");
            }
            finally
            {
                // Clean up diagnostics and hooks
                SlimSyncerDiagnostics.Enabled = false;
                SpatialLatticeOptions.TrackLocks = false;
            }
        }

        // New test: exercise each intentionally-broken Subdivide variant and assert the diagnostic detects a problem.
        [TestMethod]
        public void Diagnostic_BrokenSubdivideVariants_AreDetected()
        {
            SlimSyncerDiagnostics.Enabled = true;
            SpatialLatticeOptions.TrackLocks = true;

            var variantsToTest = new[]
            {
                OctetParentNode.SubdivideVariant.LockOrderWrong,
                OctetParentNode.SubdivideVariant.NoLeafLockTaken,
                OctetParentNode.SubdivideVariant.DisposeBeforeRetire
            };

            var permutations = new[]
            {
                (delaySubdivider: false, delayMigration: false),
                (delaySubdivider: true,  delayMigration: false),
                (delaySubdivider: false, delayMigration: true),
                (delaySubdivider: true,  delayMigration: true)
            };

            TestContext.WriteLine("Starting diagnostic checks for broken Subdivide variants (variants x permutations)");

            try
            {
                foreach (var variant in variantsToTest)
                {
                    foreach (var (delaySubdivider, delayMigration) in permutations)
                    {
                        TestContext.WriteLine($"--- Running variant: {variant}  permutation: delaySubdivider={delaySubdivider}, delayMigration={delayMigration} ---");

                        // Reset global knobs and hooks
                        OctetParentNode.SleepThreadId = 0;
                        OctetParentNode.SleepMs = 1;
                        OctetParentNode.UseYield = false;
                        OctetParentNode.SelectedSubdivideVariant = variant;
                        HookSet.Instance.RestetAll();

                        // Build lattice & nodes
                        var lattice = new TickableSpatialLattice();
                        var root = (TickableOctetParentNode)lattice.GetRootNode();

                        const int targetChildIndex = 0;
                        var subdividingLeaf = (TickableVenueLeafNode)root.Children[targetChildIndex];

                        // populate subdividing leaf to force subdivision
                        var occupants = new List<TickableSpatialObject>();
                        var basePos = subdividingLeaf.Bounds.Mid;
                        for (int i = 0; i < 63; i++)
                            occupants.Add(new TickableSpatialObject(new LongVector3(basePos.X + i, basePos.Y, basePos.Z)));
                        lattice.Insert(occupants.Cast<ISpatialObject>().ToList());

                        // create mover in sibling leaf
                        var siblingIndex = (targetChildIndex + 1) % 8;
                        var srcLeaf = (TickableVenueLeafNode)root.Children[siblingIndex];
                        var mover = new TickableSpatialObject(srcLeaf.Bounds.Mid) { Velocity = new IntVector3(100, 0, 0) };
                        lattice.Insert(mover);
                        mover.RegisterForTicks();
                        Thread.Sleep(50);

                        // prepare synchronization to provoke the interleave
                        var migrationDelay = new ManualResetEventSlim(true);
                        if (delayMigration) migrationDelay.Reset();

                        // start subdivider
                        var subdivideTask = Task.Run(() =>
                            root.SubdivideAndMigrate(root, subdividingLeaf, lattice.LatticeDepth, targetChildIndex, branchOrSublattice: true)
                        );

                        // wait for subdivider to signal it started
                        if (!HookSet.Instance["SignalSubdivideStart"].Wait(TimeSpan.FromSeconds(5)))
                            DumpDiagnosticsAndFail($"[{variant}] Timed out waiting for subdivider start.");

                        var subdividerTid = OctetParentNode.CurrentSubdividerThreadId;

                        // start migration that will move the mover into subdividing leaf
                        var migrationTask = Task.Run(() =>
                        {
                            TickableVenueLeafNode.CurrentTickerThreadId = Environment.CurrentManagedThreadId;
                            HookSet.Instance["SignalTickerStart"].Set();
                            HookSet.Instance["WaitTickerProceed"].Wait();
                            migrationDelay.Wait();
                            mover.UnregisterForTicks();
                            using var objLock = new SlimSyncer(mover.Sync, SlimSyncer.LockMode.Write, "test.forceMove: Object");
                            mover.SetLocalPosition(subdividingLeaf.Bounds.Mid);
                            lattice.Remove(mover);
                            var insertResult = lattice.Insert(mover);
                        });

                        if (!HookSet.Instance["SignalTickerStart"].Wait(TimeSpan.FromSeconds(5)))
                            DumpDiagnosticsAndFail($"[{variant}] Timed out waiting for migration start.");

                        // Delay subdivider a bit to encourage interleaving -> set SleepThreadId to subdivider when requested
                        if (delaySubdivider)
                        {
                            OctetParentNode.SleepThreadId = subdividerTid;
                            OctetParentNode.SleepMs = 10;
                            OctetParentNode.UseYield = false;
                        }
                        else OctetParentNode.SleepThreadId = 0;

                        // let subdivider proceed to acquire leaf lock
                        HookSet.Instance["WaitSubdivideProceed"].Set();

                        // wait for subdivider to actually acquire leaf lock (or signal)
                        if (!HookSet.Instance["SignalAfterLeafLock"].Wait(TimeSpan.FromSeconds(5)))
                            DumpDiagnosticsAndFail($"[{variant}] Timed out waiting for subdivider to reach leaf lock.");

                        // now allow migration to proceed (this is the dangerous interleave)
                        migrationDelay.Set();

                        // unblock subdivider to continue after we allowed migration to run
                        HookSet.Instance["BlockAfterLeafLock"].Set();

                        // wait for subdivider to reach bucket/dispatch diagnostic hook (or timeout)
                        var reachedBucket = HookSet.Instance["SignalBeforeBucketAndDispatch"].Wait(TimeSpan.FromSeconds(5));

                        // make sure to unblock bucket/dispatch so threads can finish if possible
                        HookSet.Instance["BlockBeforeBucketAndDispatch"].Set();
                        HookSet.Instance["WaitTickerProceed"].Set();

                        // Wait for tasks to complete with a short timeout. For a broken variant we expect a failure or hang.
                        var all = Task.WhenAll(subdivideTask, migrationTask);
                        bool completed = all.Wait(TimeSpan.FromSeconds(3));

                        // Gather diagnostics to decide if test detected the problem.
                        var diag = SlimSyncerDiagnostics.DumpHistory();
                        var held = LockTracker.DumpHeldLocks();

                        // Determine detection heuristics:
                        bool detected =
                            !completed ||
                            all.IsFaulted ||
                            (diag?.Length > 0) ||
                            (held?.Contains("holds") ?? false);

                        // concise variant explanation helper
                        static string ExplainVariant(OctetParentNode.SubdivideVariant v) => v switch
                        {
                            OctetParentNode.SubdivideVariant.LockOrderWrong => "Leaf lock taken before parent lock (possible deadlock)",
                            OctetParentNode.SubdivideVariant.NoLeafLockTaken => "Leaf lock omitted (races while snapshotting occupants)",
                            OctetParentNode.SubdivideVariant.DisposeBeforeRetire => "Snapshot disposed before retiring leaf (ordering bug)",
                            _ => "Unknown variant"
                        };

                        var explanation = ExplainVariant(variant);
                        var firstDiagLine = string.IsNullOrEmpty(diag)
                            ? "<no diagnostics>"
                            : diag.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "<diagnostics>";

                        if (!completed)
                        {
                            TestContext.WriteLine($"[DETECTED] {variant} / perm(dSub={delaySubdivider},dMig={delayMigration}): {explanation} — tasks timed out.");
                        }
                        else if (all.IsFaulted)
                        {
                            var exMsg = all.Exception?.Flatten().InnerException?.Message ?? "aggregate exception";
                            TestContext.WriteLine($"[DETECTED] {variant} / perm(dSub={delaySubdivider},dMig={delayMigration}): {explanation} — task faulted: {exMsg}");
                        }
                        else if (!string.IsNullOrEmpty(diag))
                        {
                            TestContext.WriteLine($"[DETECTED] {variant} / perm(dSub={delaySubdivider},dMig={delayMigration}): {explanation} — diagnostic: {firstDiagLine}");
                        }
                        else if (held?.Contains("holds") ?? false)
                        {
                            TestContext.WriteLine($"[DETECTED] {variant} / perm(dSub={delaySubdivider},dMig={delayMigration}): {explanation} — LockTracker shows held locks");
                        }
                        else
                        {
                            TestContext.WriteLine($"[NOT DETECTED] {variant} / perm(dSub={delaySubdivider},dMig={delayMigration}): expected a problem but none was detected. Check test coordination.");
                            Assert.Fail($"Broken variant {variant} perm(dSub={delaySubdivider},dMig={delayMigration}) did NOT produce detectable diagnostics. SlimSyncerDiagnostics length={diag?.Length ?? 0}, LockTracker contains-holds={(held?.Contains("holds") ?? false)}");
                        }

                        // cleanup
                        OctetParentNode.SleepThreadId = 0;
                    } // end permutations
                } // end variants

                TestContext.WriteLine("All broken variants x permutations produced detectable diagnostics");
            }
            finally
            {
                // restore defaults and turn off diagnostics
                OctetParentNode.SelectedSubdivideVariant = OctetParentNode.SubdivideVariant.Correct;
                SlimSyncerDiagnostics.Enabled = false;
                SpatialLatticeOptions.TrackLocks = false;
            }
        }
    }
}
#endif