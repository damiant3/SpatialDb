using System.Diagnostics;
using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;
using System.Collections.Concurrent;

namespace SpatialDbApp;

internal class LatticeRunner(MainForm form, RichTextBox logRtb)
{
    readonly RichTextBox m_logRtb = logRtb;
    readonly MainForm? m_form = form;

    bool m_isRunning;
    int m_tickCount;
    int m_monitorChecks;
    int m_totalMovementDetected;
    readonly List<TickableSpatialObject> m_failedObjects = [];
    readonly Stopwatch m_stopwatch = new();
    bool m_shouldStop;
    TickableSpatialLattice? m_lattice;
    List<TickableSpatialObject>? m_objects;
    ConcurrentDictionary<TickableSpatialObject, LongVector3>? m_initialPositions;
    ConcurrentDictionary<TickableSpatialObject, LongVector3>? m_lastPositions;
    int m_objectCount;
    int m_spaceRange;
    int m_durationMs;
    bool m_useFrontBuffer;

    List<TickableSpatialObject> m_closestObjects = [];
    public List<TickableSpatialObject> ClosestObjects => m_closestObjects;

    void LogLine(string message)
    {
        if (m_logRtb.InvokeRequired)
        {
            try{
                m_logRtb.Invoke(() =>
                {
                    m_logRtb.AppendText(message + Environment.NewLine);
                    m_logRtb.ScrollToCaret();
                    m_logRtb.Refresh();
                });
            } catch { }
            return;
        }
        m_logRtb.AppendText(message + Environment.NewLine);
        m_logRtb.ScrollToCaret();
        m_logRtb.Refresh();
    }
    void Log(string message)
    {
        if (m_logRtb.InvokeRequired)
        {
            try{
                m_logRtb.Invoke(() =>
                {
                    m_logRtb.AppendText(message);
                });
            } catch {}
            return;
        }
        m_logRtb.AppendText(message + Environment.NewLine);
    }



    public void RunGrandSimulation(int objectCount, int durationMs, int spaceRange = int.MaxValue)
    {
        if(m_isRunning)
        {
            LogLine("Simulation already running!");
            return;
        }
        m_isRunning = true;
        m_objectCount = objectCount;
        m_spaceRange = spaceRange;
        m_durationMs = durationMs;
        try
        {
            m_lattice = new TickableSpatialLattice();
            m_objects = [];
            m_initialPositions = new ConcurrentDictionary<TickableSpatialObject, LongVector3>();
            m_lastPositions = new ConcurrentDictionary<TickableSpatialObject, LongVector3>();

            PerformPreflight();

            // Query for 5000 closest objects after preflight
            m_closestObjects = FindClosestObjectsToOrigin(5000, 5000);
            m_form?.Setup3DView(m_closestObjects);

            TickOnceAndTest();
            RunSimulationForDuration();
            FinalReport();
        }
        catch (Exception ex)
        {
            LogLine($"\nEXCEPTION during grand simulation: {ex}");
            throw new InvalidOperationException($"Grand simulation threw an exception: {ex}", ex);
        }
        m_logRtb.Invoke(() =>
        {
            m_logRtb.ScrollToCaret();
            m_logRtb.Refresh();
        });
        m_isRunning = false;
    }

    void PerformPreflight()
    {
        LogLine($"Creating and inserting {m_objectCount} objects with random positions and velocities...");
        for (int i = 0; i < m_objectCount; i++)
        {
            var velspan = 15000;
            var pos = new LongVector3(
                FastRandom.NextInt(-m_spaceRange, m_spaceRange),
                FastRandom.NextInt(-m_spaceRange, m_spaceRange),
                FastRandom.NextInt(-m_spaceRange, m_spaceRange));
            var obj = new TickableSpatialObject(pos)
            {
                Velocity = new IntVector3(
                    FastRandom.NextInt(-velspan, velspan),
                    FastRandom.NextInt(-velspan, velspan),
                    FastRandom.NextInt(-velspan, velspan))
            };
            if (!SimulationPolicy.MeetsMovementThreshold(obj.Velocity))
                throw new InvalidOperationException($"Object velocity {obj.Velocity} should meet movement threshold");
            if (obj.IsStationary)
                throw new InvalidOperationException("Object should not be stationary");
            m_objects!.Add(obj);
            m_initialPositions![obj] = pos;
            m_lastPositions![obj] = pos;
        }
        var insertSw = Stopwatch.StartNew();
        m_lattice!.Insert(m_objects!.Cast<ISpatialObject>().ToList());
        insertSw.Stop();
        LogLine($"Inserted {m_objectCount} objects in {insertSw.ElapsedMilliseconds}ms");
        LogLine("\n=== Exhaustive Post-Insert Validation ===");
        var postInsertIssues = new List<string>();
        var objectInOccupantsCount = 0;
        var proxyInOccupantsCount = 0;
        var tickableNotRegisteredCount = 0;

        foreach (var obj in m_objects!)
        {
            if (m_lattice.ResolveOccupyingLeaf(obj) is not TickableVenueLeafNode leaf)
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
                postInsertIssues.Add($"Object {obj.Guid}: In Occupants but NOT in m_tickable_objects!");
            }

            var proxyRegistered = tickables.FirstOrDefault(t => t is ISpatialObjectProxy proxy && proxy.OriginalObject == obj);
            if (proxyRegistered != null)
                postInsertIssues.Add($"Object {obj.Guid}: PROXY registered in m_tickable_objects instead of original!");
        }
        LogLine($"Post-insert check complete:");
        LogLine($"  Original objects in Occupants: {objectInOccupantsCount}");
        LogLine($"  Proxies still in Occupants: {proxyInOccupantsCount}");
        LogLine($"  Objects NOT in m_tickable_objects: {tickableNotRegisteredCount}");

        if (postInsertIssues.Count > 0)
        {
            LogLine($"\n⚠ POST-INSERT ISSUES ({postInsertIssues.Count} total, showing first 20):");
            foreach (var issue in postInsertIssues.Take(20))
                LogLine($"  {issue}");
            throw new InvalidOperationException($"Post-insert validation failed! {postInsertIssues.Count} issues detected BEFORE RegisterForTicks() was even called!");
        }
        LogLine("✓ Post-insert validation PASSED - all proxies committed, all objects in m_tickable_objects");
        LogLine("\nCalling RegisterForTicks() on all objects...");
        var registrationFailures = new List<(TickableSpatialObject obj, string reason)>();

        foreach (var obj in m_objects)
        {
            obj.RegisterForTicks();
            if (m_lattice.ResolveOccupyingLeaf(obj) is TickableVenueLeafNode leaf)
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
            throw new InvalidOperationException($"RegisterForTicks() failed for {registrationFailures.Count} objects!");
        }
        LogLine($"✓ All {m_objectCount} objects successfully registered for ticks");

        LogLine("Running pre-flight validation...");

        var preFlightIssues = new List<string>();
        var unregisteredCount = 0;
        var missingLeafCount = 0;
        var wrongPositionCount = 0;

        foreach (var obj in m_objects)
        {
            if (m_lattice.ResolveOccupyingLeaf(obj) is not TickableVenueLeafNode leaf)
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

            var expectedPos = m_initialPositions![obj];
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
            throw new InvalidOperationException($"Pre-flight validation failed with {preFlightIssues.Count} issues. Objects not properly registered before simulation start!");
        }
        LogLine("✓ Pre-flight validation PASSED - all objects properly registered");
    }

    void TickOnceAndTest()
    {
        Thread.Sleep(50);
        m_lattice!.Tick();

        LogLine("\n=== Post-Tick Validation ===");
        var postTickIssues = new List<string>();
        var movedCount = 0;
        var unchangedCount = 0;
        var missingFromLeafCount = 0;
        var notInTickablesCount = 0;
        var leafChangedCount = 0;
        var movedButNotInTickables = 0;
        var unchangedButNotInTickables = 0;

        foreach (var obj in m_objects!)
        {
            var currentPos = obj.LocalPosition;
            var initialPos = m_initialPositions![obj];
            if (m_lattice.ResolveOccupyingLeaf(obj) is not TickableVenueLeafNode currentLeaf)
            {
                missingFromLeafCount++;
                postTickIssues.Add($"Object {obj.Guid} has no leaf after tick!");
                continue;
            }

            var hasMoved = currentPos != initialPos;
            if (hasMoved)
            {
                movedCount++;
                m_lastPositions![obj] = currentPos;
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
                    postTickIssues.Add($"Object {obj.Guid} MOVED but NOT in m_tickable_objects after tick! Old: {initialPos}, New: {currentPos}");
                }
                else
                {
                    unchangedButNotInTickables++;
                    postTickIssues.Add($"Object {obj.Guid} UNMOVED and NOT in m_tickable_objects after tick!");
                }
            }
            if (!currentLeaf.Bounds.Contains(initialPos))
                leafChangedCount++;
            if (currentLeaf.IsRetired)
                postTickIssues.Add($"Object {obj.Guid} in RETIRED leaf after tick!");
        }
        if (unchangedButNotInTickables > 0)
        {
            var firstUnmovedUnregistered = m_objects.FirstOrDefault(o =>
            {
                var pos = o.LocalPosition;
                var init = m_initialPositions![o];
                if (m_lattice.ResolveOccupyingLeaf(o) is not TickableVenueLeafNode leaf) return false;
                var tickables = leaf.m_tickableObjects;
                return pos == init && (tickables == null || !tickables.Contains(o));
            });

            if (firstUnmovedUnregistered != null)
                LogLine($"\n🔍 REGISTRATION HISTORY FOR UNMOVED OBJECT {firstUnmovedUnregistered.Guid}:");
        }
        LogLine($"Post-tick check complete:");
        LogLine($"  Objects that moved: {movedCount}/{m_objectCount}");
        LogLine($"  Objects unchanged: {unchangedCount}/{m_objectCount}");
        LogLine($"  Objects that crossed leaf boundaries: {leafChangedCount}");
        LogLine($"  Objects missing from leaf: {missingFromLeafCount}");
        LogLine($"  Objects NOT in m_tickable_objects: {notInTickablesCount}");
        LogLine($"    - Moved but not in tickables: {movedButNotInTickables}");
        LogLine($"    - Unmoved and not in tickables: {unchangedButNotInTickables}");

        if (postTickIssues.Count > 0)
        {
            LogLine($"\n⚠ POST-TICK ISSUES ({postTickIssues.Count} total, showing first 20):");
            foreach (var issue in postTickIssues.Take(20))
                LogLine($"  {issue}");

            if (movedButNotInTickables > 0)
            {
                var failedMovedObj = m_objects.FirstOrDefault(o =>
                {
                    var pos = o.LocalPosition;
                    var init = m_initialPositions![o];
                    if (m_lattice.ResolveOccupyingLeaf(o) is not TickableVenueLeafNode leaf) return false;
                    var tickables = leaf.m_tickableObjects;
                    return pos != init && (tickables == null || !tickables.Contains(o));
                });

                if (failedMovedObj != null)
                {
                    var failedLeaf = m_lattice.ResolveOccupyingLeaf(failedMovedObj) as TickableVenueLeafNode;
                    LogLine($"\n=== Detailed diagnosis of first moved-but-unregistered object ===");
                    LogLine($"  GUID: {failedMovedObj.Guid}");
                    LogLine($"  Initial pos: {m_initialPositions![failedMovedObj]}");
                    LogLine($"  Current pos: {failedMovedObj.LocalPosition}");
                    LogLine($"  Velocity: {failedMovedObj.Velocity}");
                    LogLine($"  IsStationary: {failedMovedObj.IsStationary}");
                    LogLine($"  Leaf bounds: {failedLeaf?.Bounds}");
                    LogLine($"  Leaf IsRetired: {failedLeaf?.IsRetired}");
                    LogLine($"  In leaf.Occupants: {failedLeaf?.Occupants?.Contains(failedMovedObj)}");
                    LogLine($"  Leaf tickables count: {failedLeaf?.m_tickableObjects?.Count}");
                }
            }
            throw new InvalidOperationException($"Post-tick validation failed! {postTickIssues.Count} issues detected after first tick ({movedButNotInTickables} moved but lost registration)!");
        }

        if (movedCount == 0)
            LogLine("⚠ WARNING: No objects moved after first tick! This may indicate a timing or registration issue.");
        else
            LogLine($"✓ Post-tick validation PASSED - {movedCount} objects moved successfully");
    }

    void RunSimulationForDuration()
    {
        // after testing one tick, we have high confidence that all objects are properly set up and will move when ticked.
        // Now we can start the concurrent simulation with many threads pumping ticks and another monitoring object movement.
        LogLine($"\nStarting concurrent ticker and monitor threads for {m_durationMs}ms...");
        m_stopwatch.Restart();

        var tickerThread = new Thread(() =>
        {
            while (!m_shouldStop)
            {
                SpatialTicker.TickParallel(m_lattice!);
                Interlocked.Increment(ref m_tickCount);
                Thread.Yield(); // GC concession
#if !RenderHandler
                Update3D();
#endif
            }
        });

        var monitorThread = new Thread(() =>
        {
            while (!m_shouldStop)
            {
                var movedThisCheck = 0;
                var currentFailures = new List<TickableSpatialObject>();
                foreach (var obj in m_objects!)
                {
                    if (obj.IsStationary)
                    {
                        currentFailures.Add(obj);
                        continue;
                    }
                    var leaf = m_lattice!.ResolveOccupyingLeaf(obj);
                    if (leaf == null)
                    {
                        currentFailures.Add(obj);
                        continue;
                    }
                    var currentPos = obj.LocalPosition;
                    var previousPos = m_lastPositions![obj];
                    if (currentPos != previousPos)
                    {
                        movedThisCheck++;
                        m_lastPositions[obj] = currentPos;
                    }
                }

                Interlocked.Add(ref m_totalMovementDetected, movedThisCheck);
                Interlocked.Increment(ref m_monitorChecks);

                lock (m_failedObjects)
                {
                    foreach (var failed in currentFailures)
                        if (!m_failedObjects.Contains(failed))
                            m_failedObjects.Add(failed);
                }
                Thread.Sleep(10);
                Log(".");
            }
        });
        tickerThread.Start();
        Thread.Sleep(100);
        monitorThread.Start();
        Thread.Sleep(m_durationMs);
        m_shouldStop = true;
        tickerThread.Join();
        monitorThread.Join();
        m_stopwatch.Stop();
    }

    void FinalReport()
    {
        var finalStationaryCount = 0;
        var finalMissingCount = 0;
        var finalNoMovementCount = 0;
        var unmoved = new List<(TickableSpatialObject obj, string diagnosis)>();
        foreach (var obj in m_objects!)
        {
            if (obj.IsStationary) finalStationaryCount++;
            var leaf = m_lattice!.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;
            if (leaf == null) finalMissingCount++;
            var finalPos = obj.LocalPosition;
            var initialPos = m_initialPositions![obj];
            if (finalPos == initialPos)
            {
                finalNoMovementCount++;
                var diagnosis = new System.Text.StringBuilder();
                diagnosis.Append($"Pos: {initialPos}, Vel: {obj.Velocity}");
                var hasOccupyingLeaf = leaf != null;
                diagnosis.Append($", HasLeaf: {hasOccupyingLeaf}");
                var originalLeaf = m_lattice.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;
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
        LogLine($"Duration: {m_stopwatch.ElapsedMilliseconds}ms");
        LogLine($"Total ticks: {m_tickCount}");
        LogLine($"Ticks/sec: {m_tickCount * 1000.0 / m_stopwatch.ElapsedMilliseconds:N0}");
        LogLine($"Avg tick duration: {(m_tickCount > 0 ? m_stopwatch.ElapsedMilliseconds / (double)m_tickCount : 0):N2}ms");
        LogLine($"Monitor checks: {m_monitorChecks}");
        LogLine($"Total movement events detected: {m_totalMovementDetected}");
        LogLine($"Avg movements per check: {(m_monitorChecks > 0 ? m_totalMovementDetected / (double)m_monitorChecks : 0):N1}");
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
            var unmovedPercentage = finalNoMovementCount / (double)m_objectCount * 100;
            LogLine($"Unmoved percentage: {unmovedPercentage:N3}%");
            var expectedTicksPerObject = m_tickCount / (double)m_objectCount;
            LogLine($"Expected ticks per object: {expectedTicksPerObject:N2}");
            if (expectedTicksPerObject < 0.1)
                LogLine("WARNING: Tick rate too low to guarantee all objects move");
        }

        if (finalStationaryCount != 0)
            throw new InvalidOperationException("No objects should become stationary during simulation");
        if (finalMissingCount != 0)
            throw new InvalidOperationException("All objects should remain in the lattice throughout simulation");
        var minExpectedTicksPerObject = 0.5;
        if (m_tickCount / (double)m_objectCount >= minExpectedTicksPerObject)
        {
            if (finalNoMovementCount != 0)
                throw new InvalidOperationException($"All objects should have moved at least once during simulation (had {m_tickCount} ticks for {m_objectCount} objects)");
        }
        else
            LogLine($"⚠ Skipping movement assertion: Only {m_tickCount / (double)m_objectCount:N2} ticks/object (< {minExpectedTicksPerObject})");

        if (m_tickCount <= 0)
            throw new InvalidOperationException("Ticker should have completed at least one tick");
        if (m_totalMovementDetected <= 0)
            throw new InvalidOperationException("Monitor should have detected movement");
        LogLine("\n✓ Grand simulation test PASSED");
    }

    // Call this from your tick loop (or from CompositionTarget.Rendering)
    public void Update3D()
    {
        m_form?.Update3DView(m_closestObjects, m_useFrontBuffer);
        m_useFrontBuffer = !m_useFrontBuffer;
    }

    public List<TickableSpatialObject> FindClosestObjectsToOrigin(int minCount, int maxCount)
    {
        var center = new LongVector3(0, 0, 0);
        ulong radius = 1000; // start with a small radius
        var collected = new HashSet<TickableSpatialObject>();

        // Expand radius until we have at least minCount objects or hit a large enough radius
        while (collected.Count < minCount && radius < (ulong)long.MaxValue / 2)
        {
            var query = m_lattice!.QueryWithinDistance(center, radius);
            foreach (var obj in query)
            {
                if (obj is TickableSpatialObject tickable)
                    collected.Add(tickable);
            }
            radius *= 2; // double the radius
        }

        // Sort by squared distance to get the closest
        var sorted = collected.OrderBy(obj =>
        {
            var pos = obj.LocalPosition;
            long sqDist = pos.X * pos.X + pos.Y * pos.Y + pos.Z * pos.Z;
            return sqDist;
        }).ToList();

        int targetCount = Math.Min(maxCount, sorted.Count);
        if (targetCount < minCount && sorted.Count >= minCount)
            targetCount = minCount;

        return sorted.Take(targetCount).ToList();
    }
}
