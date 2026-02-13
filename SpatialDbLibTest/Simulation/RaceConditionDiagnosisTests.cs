#if DIAGNOSTIC
using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;
using SpatialDbLib.Synchronize;
using SpatialDbLibTest.Helpers;

namespace SpatialDbLibTest.Simulation
{
    [TestClass]
    [DoNotParallelize]
    public class RaceConditionDiagnosisTests
    {
        [TestMethod]
        public void Deterministic_MigrantHasNoHome_Diagnostic()
        {
            using var hooks = new DiagnosticHookScope();

            // Enable diagnostic lock tracking
            SlimSyncerDiagnostics.Enabled = true;
            SpatialLatticeOptions.TrackLocks = true;

            // Setup the temporary test hook events
            OctetParentNode.DiagnosticHooks.SignalBeforeBucketAndDispatch = new ManualResetEventSlim(false);
            OctetParentNode.DiagnosticHooks.BlockBeforeBucketAndDispatch = new ManualResetEventSlim(false);

            // Earlier rendezvous in SubdivideAndMigrate plus start/coord signals
            OctetParentNode.DiagnosticHooks.SignalAfterLeafLock = new ManualResetEventSlim(false);
            OctetParentNode.DiagnosticHooks.BlockAfterLeafLock = new ManualResetEventSlim(false);

            OctetParentNode.DiagnosticHooks.SignalSubdivideStart = new ManualResetEventSlim(false);
            OctetParentNode.DiagnosticHooks.WaitSubdivideProceed = new ManualResetEventSlim(false);
            OctetParentNode.DiagnosticHooks.SignalTickerStart = new ManualResetEventSlim(false);
            OctetParentNode.DiagnosticHooks.WaitTickerProceed = new ManualResetEventSlim(false);

            void DumpDiagnosticsAndFail(string reason)
            {
                var diag = SlimSyncerDiagnostics.DumpHistory();
                var held = LockTracker.DumpHeldLocks();
                // Try to clear diagnostics state so output is complete and stable
                SlimSyncerDiagnostics.Enabled = false;
                SpatialLatticeOptions.TrackLocks = false;
                OctetParentNode.DiagnosticHooks.SignalBeforeBucketAndDispatch = null;
                OctetParentNode.DiagnosticHooks.BlockBeforeBucketAndDispatch = null;
                OctetParentNode.DiagnosticHooks.SignalAfterLeafLock = null;
                OctetParentNode.DiagnosticHooks.BlockAfterLeafLock = null;
                OctetParentNode.DiagnosticHooks.SignalSubdivideStart = null;
                OctetParentNode.DiagnosticHooks.WaitSubdivideProceed = null;
                OctetParentNode.DiagnosticHooks.SignalTickerStart = null;
                OctetParentNode.DiagnosticHooks.WaitTickerProceed = null;
                Assert.Fail($"{reason}\n\nSlimSyncerDiagnostics:\n{diag}\n\nLockTracker.DumpHeldLocks:\n{held}");
            }

            try
            {
                // We'll run all permutations of (delaySubdivider, delayMigration) to force interleavings.
                var permutations = new[]
                {
                    (delaySubdivider: false, delayMigration: false),
                    (delaySubdivider: true,  delayMigration: false),
                    (delaySubdivider: false, delayMigration: true),
                    (delaySubdivider: true,  delayMigration: true)
                };

                foreach (var (delaySubdivider, delayMigration) in permutations)
                {
                    // Reset any stateful hooks before each run
                    OctetParentNode.DiagnosticHooks.SleepThreadId = null;
                    OctetParentNode.DiagnosticHooks.SleepMs = 1;
                    OctetParentNode.DiagnosticHooks.UseYield = false;
                    OctetParentNode.DiagnosticHooks.CurrentSubdividerThreadId = null;
                    OctetParentNode.DiagnosticHooks.CurrentTickerThreadId = null;
                    OctetParentNode.DiagnosticHooks.SignalBeforeBucketAndDispatch.Reset();
                    OctetParentNode.DiagnosticHooks.BlockBeforeBucketAndDispatch.Reset();
                    OctetParentNode.DiagnosticHooks.SignalAfterLeafLock.Reset();
                    OctetParentNode.DiagnosticHooks.BlockAfterLeafLock.Reset();
                    OctetParentNode.DiagnosticHooks.SignalSubdivideStart.Reset();
                    OctetParentNode.DiagnosticHooks.WaitSubdivideProceed.Reset();
                    OctetParentNode.DiagnosticHooks.SignalTickerStart.Reset();
                    OctetParentNode.DiagnosticHooks.WaitTickerProceed.Reset();

                    var lattice = new TickableSpatialLattice();
                    var root = (TickableOctetParentNode)lattice.GetRootNode();

                    const int targetChildIndex = 0;
                    var subdividingLeaf = (TickableVenueLeafNode)root.Children[targetChildIndex];

                    // Fill subdividing leaf so migration has occupants to move around.
                    var occupants = new List<TickableSpatialObject>();
                    var basePos = subdividingLeaf.Bounds.Mid;
                    for (int i = 0; i < 4; i++)
                    {
                        var pos = new LongVector3(basePos.X + i, basePos.Y, basePos.Z);
                        occupants.Add(new TickableSpatialObject(pos));
                    }
                    lattice.Insert(occupants.Cast<ISpatialObject>().ToList());

                    // Create mover in sibling leaf
                    var siblingIndex = (targetChildIndex + 1) % 8;
                    var srcLeaf = (TickableVenueLeafNode)root.Children[siblingIndex];
                    var mover = new TickableSpatialObject(srcLeaf.Bounds.Mid)
                    {
                        Velocity = new IntVector3(100, 0, 0) // ensure movement if ticked
                    };
                    lattice.Insert(mover);
                    mover.RegisterForTicks();

                    // small sleep so RegisterForTicks doesn't make first Tick delta==0
                    Thread.Sleep(50);

                    // Ensure no leftover locks held by test thread before starting tasks
                    int tid = Thread.CurrentThread.ManagedThreadId;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.Elapsed < TimeSpan.FromSeconds(2))
                    {
                        var held = LockTracker.DumpHeldLocks();
                        if (!held.Contains($"Thread {tid} holds"))
                            break;
                        Thread.Sleep(5);
                    }

                    Exception capturedException = null;

                    // migration coordination primitive (test-side)
                    var migrationDelay = new ManualResetEventSlim(true); // set = proceed; reset = wait
                    if (delayMigration)
                        migrationDelay.Reset();

                    // Start subdivider task (it will set CurrentSubdividerThreadId and wait on WaitSubdivideProceed)
                    var subdivideTask = Task.Run(() =>
                    {
                        try
                        {
                            root.SubdivideAndMigrate(root, subdividingLeaf, lattice.LatticeDepth, targetChildIndex, branchOrSublattice: true);
                        }
                        catch (Exception ex)
                        {
                            capturedException = ex;
                            throw;
                        }
                    });

                    // Wait for subdivider to publish its ManagedThreadId (set by library hook)
                    if (!OctetParentNode.DiagnosticHooks.SignalSubdivideStart!.Wait(TimeSpan.FromSeconds(5)))
                        DumpDiagnosticsAndFail("Timed out waiting for subdivider to publish thread id (SignalSubdivideStart).");

                    var subdividerTid = OctetParentNode.DiagnosticHooks.CurrentSubdividerThreadId
                        ?? throw new InvalidOperationException("Subdivider thread id not set by hook");

                    // Start migration task (forced move). It will publish its thread id using library hooks
                    var migrationTask = Task.Run(() =>
                    {
                        try
                        {
                            // publish our thread id to hooks so test can target SleepThreadId
                            OctetParentNode.DiagnosticHooks.CurrentTickerThreadId = Thread.CurrentThread.ManagedThreadId;
                            OctetParentNode.DiagnosticHooks.SignalTickerStart?.Set();
                            OctetParentNode.DiagnosticHooks.WaitTickerProceed?.Wait();

                            // optionally wait (test-controlled) to create desired interleave
                            migrationDelay.Wait();

                            // perform the forced move while respecting object lock
                            mover.UnregisterForTicks();
                            using var objLock = new SlimSyncer(mover.Sync, SlimSyncer.LockMode.Write, "test.forceMove: Object");
                            mover.SetLocalPosition(subdividingLeaf.Bounds.Mid);
                            lattice.Remove(mover);
                            var insertResult = lattice.Insert(mover);
                        }
                        catch (Exception ex)
                        {
                            capturedException = ex;
                            throw;
                        }
                    });

                    // Wait for migration thread to publish id
                    if (!OctetParentNode.DiagnosticHooks.SignalTickerStart!.Wait(TimeSpan.FromSeconds(5)))
                        DumpDiagnosticsAndFail("Timed out waiting for migration thread to publish (SignalTickerStart).");

                    var migrationTid = OctetParentNode.DiagnosticHooks.CurrentTickerThreadId
                        ?? Thread.CurrentThread.ManagedThreadId; // fallback

                    // Choose delay target based on permutation
                    if (delaySubdivider)
                    {
                        OctetParentNode.DiagnosticHooks.SleepThreadId = subdividerTid;
                        OctetParentNode.DiagnosticHooks.SleepMs = 10;
                        OctetParentNode.DiagnosticHooks.UseYield = false;
                    }
                    else if (delayMigration)
                    {
                        // We'll simulate delay on migration by keeping migrationDelay reset until we want it to run.
                        OctetParentNode.DiagnosticHooks.SleepThreadId = null;
                    }
                    else
                    {
                        OctetParentNode.DiagnosticHooks.SleepThreadId = null;
                    }

                    // release library's pre-lock pause so subdivider proceeds to acquire locks
                    OctetParentNode.DiagnosticHooks.WaitSubdivideProceed!.Set();

                    // Wait for subdivider to acquire its leaf lock (library sets SignalAfterLeafLock)
                    if (!OctetParentNode.DiagnosticHooks.SignalAfterLeafLock!.Wait(TimeSpan.FromSeconds(5)))
                        DumpDiagnosticsAndFail("Timed out waiting for subdivider to acquire leaf lock (SignalAfterLeafLock).");

                    // If migration is delayed by permutation, allow migration to proceed now (so it runs while subdivider is paused at BlockAfterLeafLock)
                    if (delayMigration)
                        migrationDelay.Set(); // allow migration to perform forced move now

                    // Release subdivider from its post-leaf-lock block so it will continue to bucket & dispatch while migration may run concurrently.
                    OctetParentNode.DiagnosticHooks.BlockAfterLeafLock!.Set();

                    // Wait for subdivider to reach bucket-and-dispatch hook
                    if (!OctetParentNode.DiagnosticHooks.SignalBeforeBucketAndDispatch!.Wait(TimeSpan.FromSeconds(5)))
                        DumpDiagnosticsAndFail("Timed out waiting for subdivider to reach bucket/dispatch hook.");

                    // Allow bucket dispatch to proceed
                    OctetParentNode.DiagnosticHooks.BlockBeforeBucketAndDispatch!.Set();

                    // let migration task proceed if it wasn't already
                    OctetParentNode.DiagnosticHooks.WaitTickerProceed?.Set();

                    // Wait for tasks to complete
                    var all = Task.WhenAll(subdivideTask, migrationTask);
                    if (!all.Wait(TimeSpan.FromSeconds(5)))
                        DumpDiagnosticsAndFail("Timed out waiting for tasks to complete in permutation run.");

                    if (all.IsFaulted)
                        DumpDiagnosticsAndFail($"Tasks faulted in permutation run: {all.Exception?.Flatten().InnerException}");

                    // If any exception was captured, surface it
                    if (capturedException != null)
                        throw new AggregateException("Captured exception during permutation run", capturedException);

                    // clear SleepThreadId for next permutation
                    OctetParentNode.DiagnosticHooks.SleepThreadId = null;
                } // end foreach permutation

                // all permutations passed without throwing; success
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
                OctetParentNode.DiagnosticHooks.SignalBeforeBucketAndDispatch = null;
                OctetParentNode.DiagnosticHooks.BlockBeforeBucketAndDispatch = null;
                OctetParentNode.DiagnosticHooks.SignalAfterLeafLock = null;
                OctetParentNode.DiagnosticHooks.BlockAfterLeafLock = null;
                OctetParentNode.DiagnosticHooks.SignalSubdivideStart = null;
                OctetParentNode.DiagnosticHooks.WaitSubdivideProceed = null;
                OctetParentNode.DiagnosticHooks.SignalTickerStart = null;
                OctetParentNode.DiagnosticHooks.WaitTickerProceed = null;
                OctetParentNode.DiagnosticHooks.SleepThreadId = null;
            }
        }
    }
}
#endif