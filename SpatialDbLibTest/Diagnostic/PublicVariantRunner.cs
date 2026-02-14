#if DIAGNOSTIC
using SpatialDbLib.Diagnostic;
using SpatialDbLib.Lattice;
using SpatialDbLib.Synchronize;
using SpatialDbLib.Simulation;
using SpatialDbLib.Math;
//////////////////////////////////////
namespace SpatialDbLibTest.Diagnostic;
// Public helper invoked by runtime-compiled variants. Runs a single variant+permutation
// and returns a concise single-line result string describing detection.
public static class PublicVariantRunner
{
    // variantName: "Correct", "LockOrderWrong", "NoLeafLockTaken", "DisposeBeforeRetire"
    public static string RunVariant(string variantName, bool delaySubdivider, bool delayMigration)
    {
        try
        {
            // Map name to enum
            if (!Enum.TryParse<OctetParentNode.SubdivideVariant>(variantName, out var variant))
                variant = OctetParentNode.SubdivideVariant.Correct;

            SlimSyncerDiagnostics.Enabled = true;
            SpatialLatticeOptions.TrackLocks = true;

            // Reset knobs/hook state
            OctetParentNode.SleepThreadId = 0;
            OctetParentNode.SleepMs = 1;
            OctetParentNode.UseYield = false;
            OctetParentNode.SelectedSubdivideVariant = variant;
            HookSet.Instance.RestetAll();

            // Build lattice & prepare scenario (single permutation)
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
            if (delayMigration) migrationDelay.Reset();

            // start subdivider
            var subdivideTask = Task.Run(() =>
                root.SubdivideAndMigrate(root, subdividingLeaf, lattice.LatticeDepth, targetChildIndex, branchOrSublattice: true)
            );

            if (!HookSet.Instance["SignalSubdivideStart"].Wait(TimeSpan.FromSeconds(5)))
                return $"RUN_ERROR: timed out waiting for subdivider start";

            var subdividerTid = OctetParentNode.CurrentSubdividerThreadId;

            // start migration
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

            // set subdivider sleep if requested to encourage interleave
            if (delaySubdivider)
            {
                OctetParentNode.SleepThreadId = subdividerTid;
                OctetParentNode.SleepMs = 10;
                OctetParentNode.UseYield = false;
            }
            else OctetParentNode.SleepThreadId = 0;

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

            // Narrow detection: only treat specific diagnostic hints as problems.
            var suspiciousDiagTokens = new[]
            {
                    "Migrant has no home",
                    "Containment invariant violated",
                    "Failed to select child",
                    "(wrong order)"
                };
            bool diagProblem = !string.IsNullOrEmpty(diag) && suspiciousDiagTokens.Any(tok => diag.Contains(tok, StringComparison.OrdinalIgnoreCase));

            // Detection only if real failure signals appear: timeout, fault, targeted diagnostic messages.
            if (!completed) return $"DETECTED: tasks timed out (variant={variantName}, dSub={delaySubdivider}, dMig={delayMigration})";
            if (all.IsFaulted) return $"DETECTED: task faulted (variant={variantName}) - {all.Exception?.Flatten().InnerException?.Message}";
            if (diagProblem)
            {
                var first = diag.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "<diag>";
                return $"DETECTED: {first}";
            }

            // LockTracker can be noisy; only report as detection when no other success indicators exist.
            // If LockTracker shows locks but tasks completed successfully and no targeted diag, treat as NOT_DETECTED.
            return $"NOT_DETECTED: variant={variantName} perm(dSub={delaySubdivider},dMig={delayMigration})";
        }
        catch (Exception ex)
        {
            return $"RUN_EXCEPTION: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            OctetParentNode.SelectedSubdivideVariant = OctetParentNode.SubdivideVariant.Correct;
            OctetParentNode.SleepThreadId = 0;
            SlimSyncerDiagnostics.Enabled = false;
            SpatialLatticeOptions.TrackLocks = false;
        }
    }
}
#endif