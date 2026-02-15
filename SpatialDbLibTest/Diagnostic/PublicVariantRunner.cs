#if DIAGNOSTIC
using SpatialDbLib.Diagnostic;
using SpatialDbLib.Lattice;
using SpatialDbLib.Synchronize;
using SpatialDbLib.Simulation;
using SpatialDbLib.Math;
//////////////////////////////////////
namespace SpatialDbLibTest.Diagnostic;
public static class PublicVariantRunner
{
    // variantName: "Correct", "LockOrderWrong", "NoLeafLockTaken", "DisposeBeforeRetire"
    public static VariantRunResult RunVariant(string variantName, bool delaySubdivider, bool delayMigration)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var permutation = $"perm(dSub={delaySubdivider},dMig={delayMigration})";

        try
        {
            // Map name to enum
            if (!Enum.TryParse<OctetParentNode.SubdivideVariant>(variantName, out var variant))
                variant = OctetParentNode.SubdivideVariant.Correct;

            SlimSyncerDiagnostics.Enabled = true;
            SpatialLatticeOptions.TrackLocks = true;

            OctetParentNode.SleepThreadId = 0;
            OctetParentNode.SleepMs = 1;
            OctetParentNode.UseYield = false;
            OctetParentNode.SelectedSubdivideVariant = variant;
            HookSet.Instance.RestetAll();

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

            var subdivideTask = Task.Run(() =>
                root.SubdivideAndMigrate(root, subdividingLeaf, lattice.LatticeDepth, targetChildIndex, branchOrSublattice: true)
            );

            if (!HookSet.Instance["SignalSubdivideStart"].Wait(TimeSpan.FromSeconds(5)))
            {
                return new VariantRunResult
                {
                    VariantName = variantName,
                    Permutation = permutation,
                    Completed = false,
                    Faulted = true,
                    ErrorMessage = "timed out waiting for subdivider start",
                    Elapsed = sw.Elapsed,
                    StackTrace = Environment.StackTrace
                };
            }

            var subdividerTid = OctetParentNode.CurrentSubdividerThreadId;

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
            {
                return new VariantRunResult
                {
                    VariantName = variantName,
                    Permutation = permutation,
                    Completed = false,
                    Faulted = true,
                    ErrorMessage = "timed out waiting for migration start",
                    Elapsed = sw.Elapsed,
                    StackTrace = Environment.StackTrace
                };
            }

            if (delaySubdivider)
            {
                OctetParentNode.SleepThreadId = subdividerTid;
                OctetParentNode.SleepMs = 10;
                OctetParentNode.UseYield = false;
            }
            else OctetParentNode.SleepThreadId = 0;

            HookSet.Instance["WaitSubdivideProceed"].Set();
            if (!HookSet.Instance["SignalAfterLeafLock"].Wait(TimeSpan.FromSeconds(5)))
            {
                return new VariantRunResult
                {
                    VariantName = variantName,
                    Permutation = permutation,
                    Completed = false,
                    Faulted = true,
                    ErrorMessage = "timed out waiting for subdivider to reach leaf lock",
                    Elapsed = sw.Elapsed,
                    StackTrace = Environment.StackTrace
                };
            }

            migrationDelay.Set();
            HookSet.Instance["BlockAfterLeafLock"].Set();

            HookSet.Instance["SignalBeforeBucketAndDispatch"].Wait(TimeSpan.FromSeconds(5));
            HookSet.Instance["BlockBeforeBucketAndDispatch"].Set();
            HookSet.Instance["WaitTickerProceed"].Set();

            var all = Task.WhenAll(subdivideTask, migrationTask);
            bool completed = all.Wait(TimeSpan.FromSeconds(3));

            var diag = SlimSyncerDiagnostics.DumpHistory();
            var held = LockTracker.DumpHeldLocks();

            var suspiciousDiagTokens = new[]
            {
                "Migrant has no home",
                "Containment invariant violated",
                "Failed to select child",
                "(wrong order)"
            };
            bool diagProblem = !string.IsNullOrEmpty(diag) && suspiciousDiagTokens.Any(tok => diag.Contains(tok, StringComparison.OrdinalIgnoreCase));

            if (!completed)
            {
                return new VariantRunResult
                {
                    VariantName = variantName,
                    Permutation = permutation,
                    Completed = false,
                    Detected = true,
                    Faulted = false,
                    ErrorMessage = $"tasks timed out ({variantName})",
                    Diagnostics = ShortNonEmpty(diag),
                    LockDump = ShortNonEmpty(held),
                    Elapsed = sw.Elapsed,
                    StackTrace = Environment.StackTrace
                };
            }

            if (all.IsFaulted)
            {
                var ex = all.Exception?.Flatten().InnerException;
                return new VariantRunResult
                {
                    VariantName = variantName,
                    Permutation = permutation,
                    Completed = false,
                    Detected = true,
                    Faulted = true,
                    ErrorMessage = ex?.Message ?? "task faulted",
                    Diagnostics = ShortNonEmpty(diag),
                    LockDump = ShortNonEmpty(held),
                    Elapsed = sw.Elapsed,
                    StackTrace = ex?.StackTrace ?? Environment.StackTrace
                };
            }

            if (diagProblem)
            {
                var first = diag.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "<diag>";
                return new VariantRunResult
                {
                    VariantName = variantName,
                    Permutation = permutation,
                    Completed = true,
                    Detected = true,
                    Faulted = false,
                    ErrorMessage = first,
                    Diagnostics = ShortNonEmpty(diag),
                    LockDump = ShortNonEmpty(held),
                    Elapsed = sw.Elapsed,
                    StackTrace = Environment.StackTrace
                };
            }

            return new VariantRunResult
            {
                VariantName = variantName,
                Permutation = permutation,
                Completed = true,
                Detected = false,
                Faulted = false,
                Diagnostics = ShortNonEmpty(diag),
                LockDump = ShortNonEmpty(held),
                Elapsed = sw.Elapsed,
                StackTrace = "" // no need to capture on success
            };
        }
        catch (Exception ex)
        {
            return new VariantRunResult
            {
                VariantName = variantName,
                Permutation = permutation,
                Completed = false,
                Detected = true,
                Faulted = true,
                ErrorMessage = ex.Message,
                Diagnostics = ShortNonEmpty(SlimSyncerDiagnostics.DumpHistory()),
                LockDump = ShortNonEmpty(LockTracker.DumpHeldLocks()),
                Elapsed = sw.Elapsed,
                StackTrace = ex.StackTrace ?? Environment.StackTrace
            };
        }
        finally
        {
            OctetParentNode.SelectedSubdivideVariant = OctetParentNode.SubdivideVariant.Correct;
            OctetParentNode.SleepThreadId = 0;
            SlimSyncerDiagnostics.Enabled = false;
            SpatialLatticeOptions.TrackLocks = false;
        }

        static string ShortNonEmpty(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 6) return s.Trim();
            return string.Join(Environment.NewLine, lines.Take(6)) + Environment.NewLine + "...(truncated)";
        }
    }
}
#endif