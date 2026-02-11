using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SpatialDbApp
{

    internal static class FastRandom
    {
        private static readonly ThreadLocal<Random> s_rng = new(() => new Random(Environment.TickCount ^ Environment.CurrentManagedThreadId));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NextInt(int max) => s_rng.Value!.Next(max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NextInt(int min, int max) => s_rng.Value!.Next(min, max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long NextLong(long min, long max) => s_rng.Value!.NextInt64(min, max);
    }

    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private void BtnStartSimulation_Click(object sender, EventArgs e)
        {
            // Disable button to prevent multiple starts
            btnStartSimulation.Enabled = false;

            // Clear previous logs
            rtbTypeSummary.Clear();

            // Run simulation in background thread
            Task.Run(() => RunSimulation());
        }

        private void Log(string message)
        {
            Invoke(() =>
            {
                rtbTypeSummary.AppendText(message);
            });
        }

        private void LogLine(string message)
        {
            Invoke(() =>
            {
                rtbTypeSummary.AppendText(message + "\n");
                rtbTypeSummary.ScrollToCaret();
            });
        }

        private void RunSimulation()
        {
            int objectCount = (int)nudObjectCount.Value;
            int durationMs = ((int)nudDuration.Value) * 1000;
            int spaceRange = int.MaxValue;

            try
            {
                var lattice = new TickableSpatialLattice();
                var objects = new List<TickableSpatialObject>();
                var initialPositions = new ConcurrentDictionary<TickableSpatialObject, LongVector3>();
                var lastPositions = new ConcurrentDictionary<TickableSpatialObject, LongVector3>();
                LogLine($"Creating and inserting {objectCount} objects with random positions and velocities...");
                var insertSw = Stopwatch.StartNew();
                for (int i = 0; i < objectCount; i++)
                {
                    var pos = new LongVector3(
                        FastRandom.NextLong(-spaceRange, spaceRange),
                        FastRandom.NextLong(-spaceRange, spaceRange),
                        FastRandom.NextLong(-spaceRange, spaceRange));
                    var obj = new TickableSpatialObject(pos)
                    {
                        Velocity = new IntVector3(
                            FastRandom.NextInt(50, 500),
                            FastRandom.NextInt(50, 500),
                            FastRandom.NextInt(50, 500))
                    };
                    lattice.Insert(new List<ISpatialObject> { obj });
                    objects.Add(obj);
                    initialPositions[obj] = pos;
                    lastPositions[obj] = pos;
                }
                insertSw.Stop();
                LogLine($"Inserted {objectCount} objects in {insertSw.ElapsedMilliseconds}ms");

                LogLine("\n=== Exhaustive Post-Insert Validation ===");
                var postInsertIssues = new List<string>();
                var objectInOccupantsCount = 0;
                var proxyInOccupantsCount = 0;
                var tickableNotRegisteredCount = 0;

                foreach (var obj in objects)
                {
                    if (lattice.ResolveOccupyingLeaf(obj) is not TickableVenueLeafNode leaf)
                    {
                        postInsertIssues.Add($"Object {obj.Guid} has no leaf after insert!");
                        continue;
                    }
                    if (leaf.IsRetired)
                    {
                        postInsertIssues.Add($"Leaf retired on {obj.Guid}!");
                        continue;
                    }
                    var occupants = leaf.Occupants;
                    if (occupants == null)
                    {
                        postInsertIssues.Add($"Occupants is null for leaf. {obj.Guid}");
                        continue;
                    }
                    var originalInOccupants = occupants.Contains(obj);
                    if (originalInOccupants) objectInOccupantsCount++;
                    var proxyInOccupants = occupants.FirstOrDefault(o => o is ISpatialObjectProxy proxy && proxy.OriginalObject == obj);
                    if (proxyInOccupants != null)
                    {
                        proxyInOccupantsCount++;
                        postInsertIssues.Add($"Object {obj.Guid}: PROXY still in Occupants after insert - not committed!");
                    }
                    var tickables = leaf.m_tickableObjects;
                    if (tickables == null)
                    {
                        postInsertIssues.Add($"Could not access m_tickableObjects for {obj.Guid}");
                        return;
                    }

                    var isRegistered = tickables.Contains(obj);
                    if (!isRegistered)
                    {
                        tickableNotRegisteredCount++;
                        postInsertIssues.Add($"Object {obj.Guid}: In Occupants but NOT in m_tickableObjects!");
                    }

                    var proxyRegistered = tickables.FirstOrDefault(t => t is ISpatialObjectProxy proxy && proxy.OriginalObject == obj);
                    if (proxyRegistered != null)
                        postInsertIssues.Add($"Object {obj.Guid}: PROXY registered in m_tickableObjects instead of original!");
                }
                LogLine($"Post-insert check complete:");
                LogLine($"  Original objects in Occupants: {objectInOccupantsCount}");
                LogLine($"  Proxies still in Occupants: {proxyInOccupantsCount}");
                LogLine($"  Objects NOT in m_tickableObjects: {tickableNotRegisteredCount}");

                if (postInsertIssues.Count > 0)
                {
                    LogLine($"\n⚠ POST-INSERT ISSUES ({postInsertIssues.Count} total, showing first 20):");
                    foreach (var issue in postInsertIssues.Take(20))
                        LogLine($"  {issue}");
                    LogLine("Continuing despite issues...");
                }
                else
                {
                    LogLine("✓ Post-insert validation PASSED - all proxies committed, all objects in m_tickableObjects");
                }

                LogLine("\nCalling RegisterForTicks() on all objects...");
                var registrationFailures = new List<(TickableSpatialObject obj, string reason)>();

                foreach (var obj in objects)
                {
                    obj.RegisterForTicks();
                    if (lattice.ResolveOccupyingLeaf(obj) is TickableVenueLeafNode leaf)
                    {
                        var tickables = leaf.m_tickableObjects;
                        if (tickables == null || !tickables.Contains(obj))
                        {
                            var reason = tickables == null
                                ? "m_tickableObjects is null"
                                : $"Object not in tickables list (count: {tickables.Count})";
                            registrationFailures.Add((obj, reason));
                        }
                    }
                    else registrationFailures.Add((obj, "No leaf found"));
                }

                if (registrationFailures.Count > 0)
                {
                    LogLine($"\n⚠ REGISTRATION FAILURES: {registrationFailures.Count} objects failed to register!");
                    foreach (var (obj, reason) in registrationFailures.Take(10))
                    {
                        LogLine($"  Object {obj.Guid}: {reason}");
                        LogLine($"    Pos: {obj.LocalPosition}, Vel: {obj.Velocity}, Stationary: {obj.IsStationary}");
                    }
                    LogLine("Continuing despite registration failures...");
                }
                else
                {
                    LogLine($"✓ All {objectCount} objects successfully registered for ticks");
                }

                LogLine("Running pre-flight validation...");

                var preFlightIssues = new List<string>();
                var unregisteredCount = 0;
                var missingLeafCount = 0;
                var wrongPositionCount = 0;

                foreach (var obj in objects)
                {
                    if (lattice.ResolveOccupyingLeaf(obj) is not TickableVenueLeafNode leaf)
                    {
                        missingLeafCount++;
                        preFlightIssues.Add($"Object at {obj.LocalPosition} has no occupying leaf");
                        continue;
                    }

                    var tickableList = leaf.m_tickableObjects;
                    if (tickableList == null || !tickableList.Contains(obj))
                    {
                        unregisteredCount++;
                        preFlightIssues.Add($"Object at {obj.LocalPosition} with velocity {obj.Velocity} NOT registered in leaf");
                    }

                    var expectedPos = initialPositions[obj];
                    if (obj.LocalPosition != expectedPos)
                    {
                        wrongPositionCount++;
                        preFlightIssues.Add($"Object position changed during setup: expected {expectedPos}, got {obj.LocalPosition}");
                    }
                }

                LogLine($"Pre-flight check complete:");
                LogLine($"  Objects missing leaf: {missingLeafCount}");
                LogLine($"  Objects not registered in leaf: {unregisteredCount}");
                LogLine($"  Objects with wrong position: {wrongPositionCount}");

                if (preFlightIssues.Count > 0)
                {
                    LogLine($"\n⚠ PRE-FLIGHT FAILURES ({preFlightIssues.Count} issues, showing first 20):");
                    foreach (var issue in preFlightIssues.Take(20))
                        LogLine($"  {issue}");
                    LogLine("Continuing despite pre-flight issues...");
                }
                else
                {
                    LogLine("✓ Pre-flight validation PASSED - all objects properly registered");
                }

                Thread.Sleep(50);
                lattice.Tick();
                LogLine("\n=== Post-Tick Validation ===");
                var postTickIssues = new List<string>();
                var movedCount = 0;
                var unchangedCount = 0;
                var missingFromLeafCount = 0;
                var notInTickablesCount = 0;
                var leafChangedCount = 0;
                var movedButNotInTickables = 0;
                var unchangedButNotInTickables = 0;

                foreach (var obj in objects)
                {
                    var currentPos = obj.LocalPosition;
                    var initialPos = initialPositions[obj];
                    if (lattice.ResolveOccupyingLeaf(obj) is not TickableVenueLeafNode currentLeaf)
                    {
                        missingFromLeafCount++;
                        postTickIssues.Add($"Object {obj.Guid} has no leaf after tick!");
                        continue;
                    }

                    var hasMoved = currentPos != initialPos;
                    if (hasMoved)
                    {
                        movedCount++;
                        lastPositions[obj] = currentPos;
                    }
                    else unchangedCount++;

                    var tickables = currentLeaf.m_tickableObjects;
                    var isInTickables = tickables != null && tickables.Contains(obj);
                    if (!isInTickables)
                    {
                        notInTickablesCount++;
                        if (hasMoved)
                        {
                            movedButNotInTickables++;
                            postTickIssues.Add($"Object {obj.Guid} MOVED but NOT in m_tickableObjects after tick! Old: {initialPos}, New: {currentPos}");
                        }
                        else
                        {
                            unchangedButNotInTickables++;
                            postTickIssues.Add($"Object {obj.Guid} UNMOVED and NOT in m_tickableObjects after tick!");
                        }
                    }
                    if (!currentLeaf.Bounds.Contains(initialPos))
                        leafChangedCount++;
                    if (currentLeaf.IsRetired)
                        postTickIssues.Add($"Object {obj.Guid} in RETIRED leaf after tick!");
                }
                LogLine($"Post-tick check complete:");
                LogLine($"  Objects that moved: {movedCount}/{objectCount}");
                LogLine($"  Objects unchanged: {unchangedCount}/{objectCount}");
                LogLine($"  Objects that crossed leaf boundaries: {leafChangedCount}");
                LogLine($"  Objects missing from leaf: {missingFromLeafCount}");
                LogLine($"  Objects NOT in m_tickableObjects: {notInTickablesCount}");
                LogLine($"    - Moved but not in tickables: {movedButNotInTickables}");
                LogLine($"    - Unmoved and not in tickables: {unchangedButNotInTickables}");

                if (postTickIssues.Count > 0)
                {
                    LogLine($"\n⚠ POST-TICK ISSUES ({postTickIssues.Count} total, showing first 20):");
                    foreach (var issue in postTickIssues.Take(20))
                        LogLine($"  {issue}");
                    LogLine("Continuing despite post-tick issues...");
                }
                else
                {
                    if (movedCount == 0)
                        LogLine("⚠ WARNING: No objects moved after first tick! This may indicate a timing or registration issue.");
                    else
                        LogLine($"✓ Post-tick validation PASSED - {movedCount} objects moved successfully");
                }

                LogLine($"\nStarting concurrent ticker and monitor threads for {durationMs}ms...");

                var tickCount = 0;
                var monitorChecks = 0;
                var totalMovementDetected = 0;
                var failedObjects = new List<TickableSpatialObject>();
                var stopwatch = Stopwatch.StartNew();
                var shouldStop = false;

                var tickerThread = new Thread(() =>
                {
                    while (!shouldStop)
                    {
                        SpatialTicker.TickParallel(lattice);
                        Interlocked.Increment(ref tickCount);
                        Thread.Yield();
                    }
                });

                var monitorThread = new Thread(() =>
                {
                    while (!shouldStop)
                    {
                        var movedThisCheck = 0;
                        var currentFailures = new List<TickableSpatialObject>();
                        foreach (var obj in objects)
                        {
                            if (obj.IsStationary)
                            {
                                currentFailures.Add(obj);
                                continue;
                            }
                            var leaf = lattice.ResolveOccupyingLeaf(obj);
                            if (leaf == null)
                            {
                                currentFailures.Add(obj);
                                continue;
                            }
                            var currentPos = obj.LocalPosition;
                            var previousPos = lastPositions[obj];
                            if (currentPos != previousPos)
                            {
                                movedThisCheck++;
                                lastPositions[obj] = currentPos;
                            }
                        }

                        Interlocked.Add(ref totalMovementDetected, movedThisCheck);
                        Interlocked.Increment(ref monitorChecks);

                        lock (failedObjects)
                        {
                            foreach (var failed in currentFailures)
                                if (!failedObjects.Contains(failed))
                                    failedObjects.Add(failed);
                        }
                        Log(".");
                        Thread.Sleep(10);
                    }
                });
                tickerThread.Start();
                Thread.Sleep(100);
                monitorThread.Start();
                Thread.Sleep(durationMs);
                shouldStop = true;
                tickerThread.Join();
                monitorThread.Join();
                stopwatch.Stop();
                var finalStationaryCount = 0;
                var finalMissingCount = 0;
                var finalNoMovementCount = 0;
                var unmoved = new List<(TickableSpatialObject obj, string diagnosis)>();
                foreach (var obj in objects)
                {
                    if (obj.IsStationary) finalStationaryCount++;
                    var leaf = lattice.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;
                    if (leaf == null) finalMissingCount++;
                    var finalPos = obj.LocalPosition;
                    var initialPos = initialPositions[obj];
                    if (finalPos == initialPos)
                    {
                        finalNoMovementCount++;
                        var diagnosis = new System.Text.StringBuilder();
                        diagnosis.Append($"Pos: {initialPos}, Vel: {obj.Velocity}");
                        var hasOccupyingLeaf = leaf != null;
                        diagnosis.Append($", HasLeaf: {hasOccupyingLeaf}");
                        var originalLeaf = lattice.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;
                        var initialLeafStillExists = false;
                        var movedToNewLeaf = false;
                        if (leaf != null)
                        {
                            initialLeafStillExists = leaf.Bounds.Contains(initialPos);
                            movedToNewLeaf = !leaf.Bounds.Contains(initialPos);
                        }
                        diagnosis.Append($", LeafContainsInitialPos: {initialLeafStillExists}");
                        diagnosis.Append($", MovedLeaf: {movedToNewLeaf}");
                        var isRegisteredInLeaf = false;
                        var isLeafRetired = false;
                        if (leaf != null)
                        {
                            var tickableList2 = leaf.m_tickableObjects;
                            isRegisteredInLeaf = tickableList2?.Contains(obj) ?? false;
                            isLeafRetired = leaf.IsRetired;
                        }
                        diagnosis.Append($", RegInLeaf: {isRegisteredInLeaf}");
                        diagnosis.Append($", LeafRetired: {isLeafRetired}");
                        diagnosis.Append($", Stationary: {obj.IsStationary}");
                        unmoved.Add((obj, diagnosis.ToString()));
                    }
                }

                LogLine($"\n=== Simulation Results ===");
                LogLine($"Duration: {stopwatch.ElapsedMilliseconds}ms");
                LogLine($"Total ticks: {tickCount}");
                LogLine($"Ticks/sec: {tickCount * 1000.0 / stopwatch.ElapsedMilliseconds:N0}");
                LogLine($"Avg tick duration: {(tickCount > 0 ? stopwatch.ElapsedMilliseconds / (double)tickCount : 0):N2}ms");
                LogLine($"Monitor checks: {monitorChecks}");
                LogLine($"Total movement events detected: {totalMovementDetected}");
                LogLine($"Avg movements per check: {(monitorChecks > 0 ? totalMovementDetected / (double)monitorChecks : 0):N1}");
                LogLine($"\n=== Validation ===");
                LogLine($"Objects that became stationary: {finalStationaryCount}");
                LogLine($"Objects missing from lattice: {finalMissingCount}");
                LogLine($"Objects that never moved from initial position: {finalNoMovementCount}");

                if (finalNoMovementCount > 0)
                {
                    LogLine($"\n=== Unmoved Object Diagnostics ({finalNoMovementCount} total) ===");
                    foreach (var (obj, diagnosis) in unmoved.Take(10))
                        LogLine($"  {diagnosis}");
                    var unmovedWithLeaf = unmoved.Count(x => x.diagnosis.Contains("HasLeaf: True"));
                    var unmovedRegistered = unmoved.Count(x => x.diagnosis.Contains("RegInLeaf: True"));
                    LogLine($"\nUnmoved objects WITH occupying leaf: {unmovedWithLeaf}/{finalNoMovementCount}");
                    LogLine($"Unmoved objects REGISTERED in leaf: {unmovedRegistered}/{finalNoMovementCount}");
                    var unmovedPercentage = finalNoMovementCount / (double)objectCount * 100;
                    LogLine($"Unmoved percentage: {unmovedPercentage:N3}%");
                    var expectedTicksPerObject = tickCount / (double)objectCount;
                    LogLine($"Expected ticks per object: {expectedTicksPerObject:N2}");
                    if (expectedTicksPerObject < 0.1)
                        LogLine("WARNING: Tick rate too low to guarantee all objects move");
                }

                LogLine("\n✓ Grand simulation completed");
            }
            catch (Exception ex)
            {
                LogLine($"\nEXCEPTION during grand simulation: {ex}");
            }
            finally
            {
                // Re-enable button
                Invoke(() => btnStartSimulation.Enabled = true);
            }
        }
    }
}
