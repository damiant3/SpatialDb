using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using SpatialDbLib.Diagnostic;
using SpatialDbLib.Lattice;
using SpatialDbLib.Synchronize;
using SpatialDbLib.Simulation;
using SpatialDbLib.Math;

namespace VariantNs_{VARIANT_ID}
{
    public static class VariantEntry
    {
        // Runs one scenario using the embedded mutated implementation.
        public static string RunVariant()
        {
            try
            {
                // Keep detection/diagnostic knobs consistent with harness expectations.
                SlimSyncerDiagnostics.Enabled = true;
                SpatialLatticeOptions.TrackLocks = true;
                // Reset knobs/hook state
                OctetParentNode.SleepThreadId = 0;
                OctetParentNode.SleepMs = 1;
                OctetParentNode.UseYield = false;
                HookSet.Instance.RestetAll();

                // Build lattice & prepare scenario (single permutation run)
                var lattice = new TickableSpatialLattice();
                var root = (TickableOctetParentNode)lattice.GetRootNode();
                const int targetChildIndex = 0;
                var subdividingLeaf = (TickableVenueLeafNode)root.Children[targetChildIndex];

                var occupants = new List<TickableSpatialObject>();
                var basePos = subdividingLeaf.Bounds.Mid;
                for (int i = 0; i < 63; i++)
                    occupants.Add(new TickableSpatialObject(new LongVector3(basePos.X + i, basePos.Y, basePos.Z)));
                lattice.Insert(occupants.Cast<ISpatialObject>().ToList());

                var siblingIndex = (targetChildIndex + 1) % 8;
                var srcLeaf = (TickableVenueLeafNode)root.Children[siblingIndex];
                var mover = new TickableSpatialObject(srcLeaf.Bounds.Mid) { Velocity = new IntVector3(100, 0, 0) };
                lattice.Insert(mover);
                mover.RegisterForTicks();
                Thread.Sleep(20);

                var migrationDelay = new ManualResetEventSlim(true);

                // Local function containing the mutated method body.
                // The generator will replace {METHOD_PARAMS} and {METHOD_BODY}.
                void SubdivideImpl{METHOD_PARAMS}
                {METHOD_BODY}

                // start subdivider task that calls the mutated implementation directly
                var subdivideTask = Task.Run(() =>
                    SubdivideImpl(root, subdividingLeaf, lattice.LatticeDepth, targetChildIndex, branchOrSublattice: true)
                );

                if (!HookSet.Instance["SignalSubdivideStart"].Wait(TimeSpan.FromSeconds(5)))
                    return $"RUN_ERROR: timed out waiting for subdivider start";

                // start migration task
                var migrationTask = Task.Run(() =>
                {
                    TickableVenueLeafNode.CurrentTickerThreadId = Environment.CurrentManagedThreadId;
                    HookSet.Instance["SignalTickerStart"].Set();
                    HookSet.Instance["WaitTickerProceed"].Wait();
                    migrationDelay.Wait();
                    mover.UnregisterForTicks();
                    using var objLock = new SlimSyncer(mover.Sync, SlimSyncer.LockMode.Write, "variant.test.forceMove: Object");
                    mover.SetLocalPosition(subdividingLeaf.Bounds.Mid);
                    lattice.Remove(mover);
                    lattice.Insert(mover);
                });

                if (!HookSet.Instance["SignalTickerStart"].Wait(TimeSpan.FromSeconds(5)))
                    return $"RUN_ERROR: timed out waiting for migration start";

                // let subdivider proceed to acquire leaf lock
                HookSet.Instance["WaitSubdivideProceed"].Set();

                if (!HookSet.Instance["SignalAfterLeafLock"].Wait(TimeSpan.FromSeconds(5)))
                    return $"RUN_ERROR: timed out waiting for subdivider to reach leaf lock";

                migrationDelay.Set();
                HookSet.Instance["BlockAfterLeafLock"].Set();

                // wait for bucket/dispatch point (or timeout)
                HookSet.Instance["SignalBeforeBucketAndDispatch"].Wait(TimeSpan.FromSeconds(5));
                HookSet.Instance["BlockBeforeBucketAndDispatch"].Set();
                HookSet.Instance["WaitTickerProceed"].Set();

                var all = Task.WhenAll(subdivideTask, migrationTask);
                bool completed = all.Wait(TimeSpan.FromSeconds(3));

                var diag = SlimSyncerDiagnostics.DumpHistory();
                var held = LockTracker.DumpHeldLocks();

                // Use same narrow detection rules as harness
                var suspiciousDiagTokens = new[]
                {
                    "Migrant has no home",
                    "Containment invariant violated",
                    "Failed to select child",
                    "(wrong order)"
                };
                bool diagProblem = !string.IsNullOrEmpty(diag) && suspiciousDiagTokens.Any(tok => diag.Contains(tok, StringComparison.OrdinalIgnoreCase));

                if (!completed) return $"DETECTED: tasks timed out (variant={{{VARIANT_ID}}})";
                if (all.IsFaulted) return $"DETECTED: task faulted (variant={{{VARIANT_ID}}}) - {all.Exception?.Flatten().InnerException?.Message}";
                if (diagProblem)
                {
                    var first = diag.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "<diag>";
                    return $"DETECTED: {first}";
                }

                return $"NOT_DETECTED: variant={{{VARIANT_ID}}}";
            }
            catch (Exception ex)
            {
                return $"RUN_EXCEPTION: {ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                SlimSyncerDiagnostics.Enabled = false;
                SpatialLatticeOptions.TrackLocks = false;
                OctetParentNode.SleepThreadId = 0;
            }
        }
    }
}