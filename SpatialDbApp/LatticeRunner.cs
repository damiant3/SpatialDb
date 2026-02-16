using SpatialDbApp.Log;
using SpatialDbApp.Reporting;
using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;
using System.Collections.Concurrent;
using System.Diagnostics;
///////////////////////
namespace SpatialDbApp;

internal class LatticeRunner(MainForm form, RichTextBox logRtb)
{
    readonly LatticeLogger m_logger = new(UiLogSink.CreateFor(logRtb));
    readonly MainForm? m_form = form;

    bool m_isRunning;
    int m_tickCount;
    int m_monitorChecks;
    int m_totalMovementDetected;
    readonly List<TickableSpatialObject> m_failedObjects = [];
    readonly Stopwatch m_stopwatch = new();

    // Shutdown coordination fields (non-volatile as requested)
    bool m_shouldStop;
    Thread? m_tickerThread;
    Thread? m_monitorThread;
    readonly object m_stopSync = new();

    TickableSpatialLattice? m_lattice;
    List<TickableSpatialObject>? m_objects;
    ConcurrentDictionary<TickableSpatialObject, LongVector3>? m_initialPositions;
    ConcurrentDictionary<TickableSpatialObject, LongVector3>? m_lastPositions;
    int m_objectCount;
    int m_displayObjectCount;
    public event Action<int>? TotalObjectCountChanged;
    public event Action<int>? DisplayObjectCountChanged;
    public event Action? RenderingReady;
    int m_spaceRange;
    int m_durationMs;
    bool m_useFrontBuffer;
    List<TickableSpatialObject> m_closestObjects = [];
    public int DisplayObjectCount
    {
        get => m_displayObjectCount;
        set
        {
            var newVal = Math.Max(0, Math.Min(value, m_objectCount));
            if (m_displayObjectCount == newVal) return;
            m_displayObjectCount = newVal;
            DisplayObjectCountChanged?.Invoke(m_displayObjectCount);
        }
    }

    public void SetTotalObjects(int newTotal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newTotal);
        var oldTotal = m_objectCount;
        m_objectCount = newTotal;
        var wasTracking = (m_displayObjectCount == oldTotal);
        if (wasTracking) m_displayObjectCount = newTotal;
        else if (m_displayObjectCount > newTotal) m_displayObjectCount = newTotal;
        TotalObjectCountChanged?.Invoke(m_objectCount);
        DisplayObjectCountChanged?.Invoke(m_displayObjectCount);
    }
    void LogLine(string message) => m_logger.LogLine(message);
    void Log(string message) => m_logger.Log(message);

    /// <summary>
    /// Request the running simulation to stop. Returns quickly.
    /// </summary>
    public void RequestStop()
    {
        m_shouldStop = true;
        try
        {
            lock (m_stopSync)
            {
                try { m_monitorThread?.Interrupt(); } catch { }
                try { m_tickerThread?.Interrupt(); } catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Wait up to <paramref name="timeoutMs"/> for worker threads to exit.
    /// </summary>
    public bool WaitForStop(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            Thread? t = m_tickerThread;
            if (t != null)
            {
                var remaining = timeoutMs - (int)sw.ElapsedMilliseconds;
                if (remaining > 0) t.Join(remaining);
            }

            t = m_monitorThread;
            if (t != null)
            {
                var remaining = timeoutMs - (int)sw.ElapsedMilliseconds;
                if (remaining > 0) t.Join(remaining);
            }
        }
        catch { }

        return !(m_tickerThread?.IsAlive ?? false) && !(m_monitorThread?.IsAlive ?? false);
    }

    public void RunGrandSimulation(int objectCount, int durationMs, int spaceRange = int.MaxValue)
    {
        if (m_isRunning)
        {
            LogLine("Simulation already running!");
            return;
        }
        m_isRunning = true;
        m_shouldStop = false;

        SetTotalObjects(objectCount);
        m_spaceRange = spaceRange;
        m_durationMs = durationMs;
        try
        {
            m_lattice = new TickableSpatialLattice();
            m_objects = [];
            m_initialPositions = new ConcurrentDictionary<TickableSpatialObject, LongVector3>();
            m_lastPositions = new ConcurrentDictionary<TickableSpatialObject, LongVector3>();
            PerformPreflight();
            var requested = (DisplayObjectCount > 0) ? DisplayObjectCount : Math.Min(5000, m_objectCount);
            var toRender = Math.Max(0, Math.Min(requested, m_objectCount));
            m_closestObjects = FindClosestObjectsToOrigin(toRender, toRender);
            m_form?.Setup3DView(m_closestObjects);
            RenderingReady?.Invoke();
            TickOnceAndTest();
            RunSimulationForDuration();
            FinalReport();
        }
        catch (Exception ex)
        {
            LogLine($"\nEXCEPTION during grand simulation: {ex}");
            throw new InvalidOperationException($"Grand simulation threw an exception: {ex}", ex);
        }
        finally
        {
            // Ensure worker threads are stopped when the run finishes or is aborted.
            RequestStop();
            WaitForStop(5000);
            try { m_logger.ScrollToEnd(); } catch { }
            m_isRunning = false;
        }
    }

    void PerformPreflight()
    {
        LogLine($"Creating and inserting {m_objectCount} objects with random positions and velocities...");
        for (int i = 0; i < m_objectCount; i++)
        {
            var pos = new LongVector3(
                FastRandom.NextInt(-m_spaceRange, m_spaceRange),
                FastRandom.NextInt(-m_spaceRange, m_spaceRange),
                FastRandom.NextInt(-m_spaceRange, m_spaceRange));
            const double targetSpeed = 45000.0;
            double dx = -pos.X;
            double dy = -pos.Y;
            double dz = -pos.Z;
            double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            IntVector3 velocity;
            if (dist <= double.Epsilon)
            {
                velocity = new IntVector3(
                    FastRandom.NextInt(-2500, 2500),
                    FastRandom.NextInt(-2500, 2500),
                    FastRandom.NextInt(-2500, 2500));
            }
            else
            {
                double scale = targetSpeed / dist;
                long vxL = (long)Math.Round(dx * scale);
                long vyL = (long)Math.Round(dy * scale);
                long vzL = (long)Math.Round(dz * scale);
                int vx = (int)Math.Clamp(vxL, int.MinValue, int.MaxValue);
                int vy = (int)Math.Clamp(vyL, int.MinValue, int.MaxValue);
                int vz = (int)Math.Clamp(vzL, int.MinValue, int.MaxValue);
                velocity = new IntVector3(vx, vy, vz);
            }

            var obj = new TickableSpatialObject(pos)
            {
                Velocity = velocity
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
            if (occupants.Contains(obj)) objectInOccupantsCount++;
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

            if (!tickables.Contains(obj))
            {
                tickableNotRegisteredCount++;
                postInsertIssues.Add($"Object {obj.Guid}: In Occupants but NOT in m_tickable_objects!");
            }

            var proxyRegistered = tickables.FirstOrDefault(t => t is ISpatialObjectProxy proxy && proxy.OriginalObject == obj);
            if (proxyRegistered != null)
                postInsertIssues.Add($"Object {obj.Guid}: PROXY registered in m_tickable_objects instead of original!");
        }
        LogLine(ReportBuilder.BuildPostInsertSummary(
            objectInOccupantsCount,
            proxyInOccupantsCount,
            tickableNotRegisteredCount,
            postInsertIssues,
            m_objectCount));
        if (postInsertIssues.Count > 0)
            throw new InvalidOperationException($"Post-insert validation failed! {postInsertIssues.Count} issues detected BEFORE RegisterForTicks() was even called!");
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
        LogLine(ReportBuilder.BuildPreFlightSummary(missingLeafCount, unregisteredCount, wrongPositionCount, preFlightIssues));
        if (preFlightIssues.Count > 0)
            throw new InvalidOperationException($"Pre-flight validation failed with {preFlightIssues.Count} issues. Objects not properly registered before simulation start!");
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
        LogLine(ReportBuilder.PostTickReport(
            movedCount,
            unchangedCount,
            missingFromLeafCount,
            notInTickablesCount,
            leafChangedCount,
            movedButNotInTickables,
            unchangedButNotInTickables,
            m_objectCount));

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
                    ReportBuilder.ReportMovedButUnregistered(failedMovedObj, failedLeaf, m_initialPositions);
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
        LogLine($"\nStarting concurrent ticker and monitor threads for {m_durationMs}ms...");
        m_stopwatch.Restart();
        m_tickerThread = new Thread(() =>
        {
            try
            {
                var everyOther = true;
                while (!m_shouldStop)
                {
                    SpatialTicker.TickParallel(m_lattice!);
                    Interlocked.Increment(ref m_tickCount);
                    Thread.Yield(); // GC concession
                    everyOther = !everyOther;
#if !RenderHandler
                    Update3D();
#endif
                }
            }
            catch (ThreadInterruptedException)
            {
                // Expected on shutdown
            }
            catch (Exception ex)
            {
                try { LogLine($"\nTicker thread exception: {ex}"); } catch { }
            }
        })
        { IsBackground = true };

        m_monitorThread = new Thread(() =>
        {
            try
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

                    try
                    {
                        Thread.SpinWait(10);
                        Log(".");
                    }
                    catch (ThreadInterruptedException)
                    {
                        if (m_shouldStop) break;
                    }

                    Log(".");
                }
            }
            catch (ThreadInterruptedException)
            {
                // expected on shutdown
            }
            catch (Exception ex)
            {
                try { LogLine($"\nMonitor thread exception: {ex}"); } catch { }
            }
        })
        { IsBackground = true };
        m_tickerThread.Start();
        Thread.Sleep(100);
        m_monitorThread.Start();
        var sw = Stopwatch.StartNew();
        while (!m_shouldStop && sw.ElapsedMilliseconds < m_durationMs)
        {
            Thread.Sleep(50);
        }
        m_shouldStop = true;
        WaitForStop(m_durationMs > 5000 ? 5000 : 2000);
        if (m_tickerThread?.IsAlive == true || m_monitorThread?.IsAlive == true)
        {
            RequestStop();
            WaitForStop(2000);
        }

        m_tickerThread = null;
        m_monitorThread = null;

        m_stopwatch.Stop();
    }

    void FinalReport()
    {
        if (m_objects == null || m_lattice == null) return;
        LogLine(ReportBuilder.FinalReport(m_objects, m_lattice, m_initialPositions, m_stopwatch, m_tickCount, m_totalMovementDetected, m_monitorChecks, m_objectCount));
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
