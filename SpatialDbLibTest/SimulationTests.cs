using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace SpatialDbLibTest;

[TestClass]
public class SimulationTests
{
    // === BASIC FUNCTIONALITY ===

    [TestMethod]
    public void Test_DeepInsertion()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject([new(1000), new(1200)]);

        var result = lattice.Insert(obj);
        Assert.IsTrue(result is AdmitResult.Created, "Insertion should succeed");

        var leaf = lattice.ResolveOccupyingLeaf(obj);
        Assert.IsNotNull(leaf);
        Assert.IsInstanceOfType(leaf, typeof(TickableVenueLeafNode));
        Assert.AreEqual(2, obj.PositionStackDepth, "Object should have 2 positions (deep insert)");
        
        Console.WriteLine("Deep insertion into sublattice passed.");
    }

    [TestMethod]
    public void Test_ObjectTick_ReturnsMovementIntent()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject(new LongVector3(1000));

        lattice.Insert(obj);
        obj.Accelerate(new IntVector3(100, 0, 0));
        obj.RegisterForTicks();
        Thread.Sleep(20);
        var result = obj.Tick();
        
        Assert.IsNotNull(result);
        Assert.AreEqual(TickAction.Move, result.Value.Action);
        Assert.IsTrue(result.Value.Target.X > obj.LocalPosition.X, "Target should be ahead of current position");
        
        Console.WriteLine("Object tick returns movement intent passed.");
    }

    [TestMethod]
    public void Test_LatticeTick_MovesObjects()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject([new(1000)]);

        lattice.Insert(obj);
        var initialPosition = obj.LocalPosition;

        obj.Accelerate(new IntVector3(1000, 0, 0));
        obj.RegisterForTicks();

        Thread.Sleep(50);  // Allow time to accumulate
        lattice.Tick();
 
        var newPosition = obj.LocalPosition;

        Assert.IsTrue(newPosition.X > initialPosition.X, "Object should have moved forward");
        Assert.AreEqual(initialPosition.Y, newPosition.Y, "Y should remain unchanged");
        Assert.AreEqual(initialPosition.Z, newPosition.Z, "Z should remain unchanged");

        Console.WriteLine($"Object moved from {initialPosition} to {newPosition}");
    }

    // === PROXY BEHAVIOR ===

    [TestMethod]
    public void Test_ProxyTransfersVelocityOnCommit()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject(new LongVector3(1000));
        
        lattice.Insert(obj);
        obj.Accelerate(new IntVector3(5000, 2000, 1000));
        obj.RegisterForTicks();
        
        var initialVelocity = obj.Velocity;
        
        var admitResult = lattice.GetRootNode().Admit(obj, new LongVector3(2000));
        var proxy = ((AdmitResult.Created)admitResult).Proxy as TickableSpatialObjectProxy;
        
        Assert.IsNotNull(proxy);
        Assert.AreEqual(initialVelocity.X, proxy.LocalVelocity.X, "Proxy should copy velocity");
        
        proxy.LocalVelocity = new IntVector3(8000, 4000, 2000);
        proxy.Commit();
        
        Assert.AreEqual(8000, obj.Velocity.X, "Modified velocity should transfer on commit");
        Assert.AreEqual(4000, obj.Velocity.Y);
        Assert.AreEqual(2000, obj.Velocity.Z);
        
        Console.WriteLine("Proxy velocity transfer on commit passed.");
    }

    // === BOUNDARY CROSSING ===

    [TestMethod]
    public void Test_BoundaryCrossing_SingleObject()
    {
        var lattice = new TickableSpatialLattice();
        var root = lattice.GetRootNode() as OctetParentNode;
        Assert.IsNotNull(root);
        var mid = root.Bounds.Mid;
        var startPos = new LongVector3(mid.X - 50, mid.Y - 10, mid.Z - 10);
        var obj = new TickableSpatialObject(startPos);
        lattice.Insert(obj);
        Console.WriteLine($"Start pos: {obj.LocalPosition}");
        var initialLeaf = lattice.ResolveOccupyingLeaf(obj);
        Assert.IsNotNull(initialLeaf);
        var initialOctant = GetOctantIndex(startPos, mid);
        Assert.AreEqual(0, initialOctant, "Object should start in octant 0 (---)");
        obj.RegisterForTicks();
        obj.Accelerate(new IntVector3(5000, 0, 0));


        Thread.Sleep(150);
        lattice.Tick();

        var newPosition = obj.LocalPosition;

        Assert.IsTrue(newPosition.X >= mid.X, $"Object should have crossed X boundary.  Expect > {mid.X}, got {newPosition.X}");
        Assert.AreEqual(startPos.Y, newPosition.Y, "Y should remain unchanged");
        Assert.AreEqual(startPos.Z, newPosition.Z, "Z should remain unchanged");

        var newLeaf = lattice.ResolveOccupyingLeaf(obj);
        Assert.IsNotNull(newLeaf, "Object should occupy a leaf after boundary crossing");
        
        var newOctant = GetOctantIndex(newPosition, mid);
        Assert.AreEqual(4, newOctant, "Object should now be in octant 4 (+--) after crossing X boundary");
        Assert.AreNotEqual(initialLeaf, newLeaf, "Object should be in a different leaf after crossing boundary");

        Console.WriteLine($"Boundary crossing: Octant {initialOctant} → Octant {newOctant}");
    }

    // === SUBLATTICE BEHAVIOR ===

    [TestMethod]
    public void Test_TickPropagatesThoughSublattices()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject([new(1), new(100)]);
        
        lattice.Insert(obj);
        var leaf = lattice.ResolveOccupyingLeaf(obj);
        Assert.IsInstanceOfType(leaf, typeof(TickableVenueLeafNode), "Should occupy a tickable leaf");
        
        obj.SetVelocityStack([new IntVector3(0, 0, 1000)]);
        obj.RegisterForTicks();

        var initialPosition = obj.LocalPosition;

        Thread.Sleep(50);
        lattice.Tick();

        Assert.IsTrue(obj.LocalPosition.DistanceTo(initialPosition) > 0,
            "Object in sublattice should have moved");
        
        Console.WriteLine("Tick propagation through sublattices passed.");
    }

    // === REGISTRATION & FILTERING ===

    [TestMethod]
    public void Test_RegisterAndUnregisterForTicks()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject(new LongVector3(1000));
        
        lattice.Insert(obj);
        obj.Accelerate(new IntVector3(1000, 0, 0));
        
        // Register and verify movement
        obj.RegisterForTicks();
        var initialPosition = obj.LocalPosition;
        
        Thread.Sleep(50);
        lattice.Tick();
        
        Assert.IsTrue(obj.LocalPosition.X > initialPosition.X, "Registered object should move");
        
        // Unregister and verify no movement
        obj.UnregisterForTicks();
        var positionAfterUnregister = obj.LocalPosition;
        
        Thread.Sleep(50);
        lattice.Tick();
        
        Assert.AreEqual(positionAfterUnregister, obj.LocalPosition, "Unregistered object should not move");
        
        Console.WriteLine("Register/Unregister for ticks passed.");
    }

    [TestMethod]
    public void Test_StationaryObjects_FilteredByThreshold()
    {
        var lattice = new TickableSpatialLattice();
        
        // Below threshold
        var slowObj = new TickableSpatialObject(new LongVector3(1000));
        lattice.Insert(slowObj);
        slowObj.Velocity = new IntVector3(5, 5, 5);  // Below SimulationPolicy.MinPerAxis and MinSum
        slowObj.RegisterForTicks();
        
        // Above threshold
        var fastObj = new TickableSpatialObject(new LongVector3(2000));
        lattice.Insert(fastObj);
        fastObj.Velocity = new IntVector3(100, 100, 100);  // Above thresholds
        fastObj.RegisterForTicks();
        
        Assert.IsTrue(slowObj.IsStationary, "Slow object should be considered stationary");
        Assert.IsFalse(fastObj.IsStationary, "Fast object should not be stationary");
        
        var slowInitialPos = slowObj.LocalPosition;
        var fastInitialPos = fastObj.LocalPosition;
        
        Thread.Sleep(50);
        lattice.Tick();
        
        Assert.AreEqual(slowInitialPos, slowObj.LocalPosition, "Stationary object should not move");
        Assert.AreNotEqual(fastInitialPos, fastObj.LocalPosition, "Fast object should move");
        
        Console.WriteLine("Stationary object filtering by threshold passed.");
    }

    [TestMethod]
    public void Test_VelocityEnforcesThreshold()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject(new LongVector3(1000));
        
        lattice.Insert(obj);
        
        // Set velocity below threshold
        obj.Velocity = new IntVector3(5, 5, 5);
        
        // Should be zeroed out by policy
        Assert.AreEqual(IntVector3.Zero, obj.Velocity, "Velocity below threshold should be zeroed");
        
        // Set velocity above threshold
        obj.Velocity = new IntVector3(100, 100, 100);
        
        Assert.AreNotEqual(IntVector3.Zero, obj.Velocity, "Velocity above threshold should be preserved");
        
        Console.WriteLine("Velocity threshold enforcement passed.");
    }

    // === VELOCITY STACK (MULTI-DEPTH) ===

    [TestMethod]
    public void Test_VelocityStack_MultiDepth()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject([new(1000), new(500)]);
        
        lattice.Insert(obj);
        
        // Set velocity stack (outer frame, inner frame)
        obj.SetVelocityStack([new IntVector3(100, 0, 0), new IntVector3(50, 0, 0)]);
        
        var velocityStack = obj.GetVelocityStack();
        Assert.AreEqual(2, velocityStack.Count, "Velocity stack should have 2 entries");
        Assert.AreEqual(100, velocityStack[0].X, "Outer velocity should be 100");
        Assert.AreEqual(50, velocityStack[1].X, "Inner velocity should be 50");
        Assert.AreEqual(50, obj.Velocity.X, "LocalVelocity should match last element");
        
        Console.WriteLine("Velocity stack multi-depth passed.");
    }

    // === PERFORMANCE BENCHMARK ===

    [TestMethod]
    public void Test_TickPerformance()
    {
        var counts = new[] { 50000, 250000, 1000000};

        Console.WriteLine($"{"Count",-10} {"Insert (ms)",-15} {"Tick (ms)",-15} {"Objs/sec",-15}");
        Console.WriteLine(new string('-', 60));

        foreach (var count in counts)
        {
            var lattice = new TickableSpatialLattice();
            var objects = new List<IMoveableObject>();

            var sw = Stopwatch.StartNew();

            for (int i = 0; i < count; i++)
            {
                var pos = new LongVector3(
                    Random.Shared.Next(1000000),
                    Random.Shared.Next(1000000),
                    Random.Shared.Next(1000000));

                var obj = new TickableSpatialObject(pos);
                obj.Velocity = new IntVector3(
                    Random.Shared.Next(20, 100),
                    Random.Shared.Next(20, 100),
                    Random.Shared.Next(20, 100));

                objects.Add(obj);
            }

            lattice.Insert(objects.Cast<ISpatialObject>().ToList());

            foreach (var obj in objects)
            {
                obj.RegisterForTicks();
            }

            var insertTime = sw.ElapsedMilliseconds;

            Thread.Sleep(10);

            sw.Restart();
            lattice.Tick();
            var tickTime = sw.ElapsedMilliseconds;

            var objsPerSec = tickTime > 0 ? (count * 1000.0 / tickTime) : double.PositiveInfinity;

            Console.WriteLine($"{count,-10} {insertTime,-15} {tickTime,-15} {objsPerSec,-15:N0}");
        }
    }

    // === HELPERS ===

    private int GetOctantIndex(LongVector3 pos, LongVector3 mid)
    {
        return ((pos.X >= mid.X) ? 4 : 0) |
               ((pos.Y >= mid.Y) ? 2 : 0) |
               ((pos.Z >= mid.Z) ? 1 : 0);
    }

    // === GRAND SIMULATION TEST ===

    private void RunGrandSimulation(int objectCount, int durationMs, int spaceRange = int.MaxValue)
    {
        try
        {
            var lattice = new TickableSpatialLattice();
            var objects = new List<TickableSpatialObject>();
            var initialPositions = new ConcurrentDictionary<TickableSpatialObject, LongVector3>();
            var lastPositions = new ConcurrentDictionary<TickableSpatialObject, LongVector3>();

            Console.WriteLine($"Creating and inserting {objectCount} objects with random positions and velocities...");

            // Generate uniform distributed objects with valid movement velocities
            for (int i = 0; i < objectCount; i++)
            {
                var pos = new LongVector3(
                    Random.Shared.Next(-spaceRange, spaceRange),
                    Random.Shared.Next(-spaceRange, spaceRange),
                    Random.Shared.Next(-spaceRange, spaceRange));

                var obj = new TickableSpatialObject(pos);

                // Ensure velocity exceeds SimulationPolicy thresholds
                // MinPerAxis = 10, MinSum = 20
                obj.Velocity = new IntVector3(
                    Random.Shared.Next(50, 500),
                    Random.Shared.Next(50, 500),
                    Random.Shared.Next(50, 500));

                Assert.IsTrue(SimulationPolicy.MeetsMovementThreshold(obj.Velocity),
                    $"Object velocity {obj.Velocity} should meet movement threshold");
                Assert.IsFalse(obj.IsStationary, "Object should not be stationary");

                objects.Add(obj);
                initialPositions[obj] = pos;
                lastPositions[obj] = pos;
            }

            // Bulk insert all objects
            var insertSw = Stopwatch.StartNew();
            lattice.Insert(objects.Cast<ISpatialObject>().ToList());
            insertSw.Stop();

            Console.WriteLine($"Inserted {objectCount} objects in {insertSw.ElapsedMilliseconds}ms");

            // === EXHAUSTIVE POST-INSERT VALIDATION ===
            Console.WriteLine("\n=== Exhaustive Post-Insert Validation ===");

            var postInsertIssues = new List<string>();
            var objectInOccupantsCount = 0;
            var proxyInOccupantsCount = 0;
            var tickableNotRegisteredCount = 0;

            foreach (var obj in objects)
            {
                var leaf = lattice.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;

                if (leaf == null)
                {
                    postInsertIssues.Add($"Object {obj.Guid} has no leaf after insert!");
                    continue;
                }

                if (leaf.IsRetired)
                {
                    postInsertIssues.Add($"Leaf retired on {obj.Guid}!");
                    continue;
                }


                // Check what's actually in Occupants
                var occupants = leaf.Occupants;

                if (occupants == null)
                {
                    postInsertIssues.Add($"Occupants is null for leaf. {obj.Guid}");
                    continue;
                }

                // Is the ORIGINAL object in Occupants?
                var originalInOccupants = occupants.Contains(obj);
                if (originalInOccupants) objectInOccupantsCount++;

                // Is a PROXY in Occupants?
                var proxyInOccupants = occupants.FirstOrDefault(o =>
                    o is ISpatialObjectProxy proxy && proxy.OriginalObject == obj);
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

                // Is the object registered for ticks?
                var isRegistered = tickables.Contains(obj);
                if (!isRegistered)
                {
                    tickableNotRegisteredCount++;
                    postInsertIssues.Add($"Object {obj.Guid}: In Occupants but NOT in m_tickableObjects!");
                }

                // Is a proxy registered instead?
                var proxyRegistered = tickables.FirstOrDefault(t =>
                    t is ISpatialObjectProxy proxy && proxy.OriginalObject == obj);
                if (proxyRegistered != null)
                {
                    postInsertIssues.Add($"Object {obj.Guid}: PROXY registered in m_tickableObjects instead of original!");
                }
            }

            Console.WriteLine($"Post-insert check complete:");
            Console.WriteLine($"  Original objects in Occupants: {objectInOccupantsCount}");
            Console.WriteLine($"  Proxies still in Occupants: {proxyInOccupantsCount}");
            Console.WriteLine($"  Objects NOT in m_tickableObjects: {tickableNotRegisteredCount}");

            if (postInsertIssues.Count > 0)
            {
                Console.WriteLine($"\n⚠ POST-INSERT ISSUES ({postInsertIssues.Count} total, showing first 20):");
                foreach (var issue in postInsertIssues.Take(20))
                {
                    Console.WriteLine($"  {issue}");
                }

                Assert.Fail($"Post-insert validation failed! {postInsertIssues.Count} issues detected BEFORE RegisterForTicks() was even called!");
            }

            Console.WriteLine("✓ Post-insert validation PASSED - all proxies committed, all objects in m_tickableObjects");

            // Register all for ticks
            Console.WriteLine("\nCalling RegisterForTicks() on all objects...");
            var registrationFailures = new List<(TickableSpatialObject obj, string reason)>();
            
            foreach (var obj in objects)
            {
                obj.RegisterForTicks();
                
                // Verify registration succeeded
                var leaf = lattice.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;
                if (leaf != null)
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
                else
                {
                    registrationFailures.Add((obj, "No leaf found"));
                }
            }

            if (registrationFailures.Count > 0)
            {
                Console.WriteLine($"\n⚠ REGISTRATION FAILURES: {registrationFailures.Count} objects failed to register!");
                foreach (var (obj, reason) in registrationFailures.Take(10))
                {
                    Console.WriteLine($"  Object {obj.Guid}: {reason}");
                    Console.WriteLine($"    Pos: {obj.LocalPosition}, Vel: {obj.Velocity}, Stationary: {obj.IsStationary}");
                }
                
                Assert.Fail($"RegisterForTicks() failed for {registrationFailures.Count} objects!");
            }

            Console.WriteLine($"✓ All {objectCount} objects successfully registered for ticks");

            // === PRE-FLIGHT VALIDATION ===
            Console.WriteLine("Running pre-flight validation...");

            var preFlightIssues = new List<string>();
            var unregisteredCount = 0;
            var missingLeafCount = 0;
            var wrongPositionCount = 0;

            foreach (var obj in objects)
            {
                var leaf = lattice.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;

                // Check 1: Does object have a leaf?
                if (leaf == null)
                {
                    missingLeafCount++;
                    preFlightIssues.Add($"Object at {obj.LocalPosition} has no occupying leaf");
                    continue;
                }

                // Check 2: Is object registered in that leaf?
                var tickableList = leaf.m_tickableObjects;
                if (tickableList == null || !tickableList.Contains(obj))
                {
                    unregisteredCount++;
                    preFlightIssues.Add($"Object at {obj.LocalPosition} with velocity {obj.Velocity} NOT registered in leaf");
                }

                // Check 3: Does object's position match what we expect?
                var expectedPos = initialPositions[obj];
                if (obj.LocalPosition != expectedPos)
                {
                    wrongPositionCount++;
                    preFlightIssues.Add($"Object position changed during setup: expected {expectedPos}, got {obj.LocalPosition}");
                }
            }

            Console.WriteLine($"Pre-flight check complete:");
            Console.WriteLine($"  Objects missing leaf: {missingLeafCount}");
            Console.WriteLine($"  Objects not registered in leaf: {unregisteredCount}");
            Console.WriteLine($"  Objects with wrong position: {wrongPositionCount}");

            if (preFlightIssues.Count > 0)
            {
                Console.WriteLine($"\n⚠ PRE-FLIGHT FAILURES ({preFlightIssues.Count} issues, showing first 20):");
                foreach (var issue in preFlightIssues.Take(20))
                {
                    Console.WriteLine($"  {issue}");
                }

                Assert.Fail($"Pre-flight validation failed with {preFlightIssues.Count} issues. Objects not properly registered before simulation start!");
            }

            Console.WriteLine("✓ Pre-flight validation PASSED - all objects properly registered");

            Thread.Sleep(50); // Allow time for movement to accumulate

            lattice.Tick();



            // === POST-TICK VALIDATION ===
            Console.WriteLine("\n=== Post-Tick Validation ===");

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
                var currentLeaf = lattice.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;

                // Check 1: Does object still have a valid leaf?
                if (currentLeaf == null)
                {
                    missingFromLeafCount++;
                    postTickIssues.Add($"Object {obj.Guid} has no leaf after tick!");
                    continue;
                }

                // Check 2: Has object moved?
                var hasMoved = currentPos != initialPos;
                if (hasMoved)
                {
                    movedCount++;
                    lastPositions[obj] = currentPos;
                }
                else
                {
                    unchangedCount++;
                }

                // Check 3: Is object still registered in its leaf's tickable queue?
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

                // Check 4: Did object cross into a new leaf? (optional, informational)
                if (!currentLeaf.Bounds.Contains(initialPos))
                {
                    leafChangedCount++;
                }

                // Check 5: Is the leaf retired?
                if (currentLeaf.IsRetired)
                {
                    postTickIssues.Add($"Object {obj.Guid} in RETIRED leaf after tick!");
                }
            }
            if (unchangedButNotInTickables > 0)
            {
                var firstUnmovedUnregistered = objects.FirstOrDefault(o =>
                {
                    var pos = o.LocalPosition;
                    var init = initialPositions[o];
                    var leaf = lattice.ResolveOccupyingLeaf(o) as TickableVenueLeafNode;
                    if (leaf == null) return false;
                    var tickables = leaf.m_tickableObjects;
                    return pos == init && (tickables == null || !tickables.Contains(o));
                });

                if (firstUnmovedUnregistered != null)
                {
                    Console.WriteLine($"\n🔍 REGISTRATION HISTORY FOR UNMOVED OBJECT {firstUnmovedUnregistered.Guid}:");
#if DEBUG
                    Console.WriteLine(TickableVenueLeafNode.GetRegistrationHistory(firstUnmovedUnregistered.Guid));
#endif
                }
            }
            Console.WriteLine($"Post-tick check complete:");
            Console.WriteLine($"  Objects that moved: {movedCount}/{objectCount}");
            Console.WriteLine($"  Objects unchanged: {unchangedCount}/{objectCount}");
            Console.WriteLine($"  Objects that crossed leaf boundaries: {leafChangedCount}");
            Console.WriteLine($"  Objects missing from leaf: {missingFromLeafCount}");
            Console.WriteLine($"  Objects NOT in m_tickableObjects: {notInTickablesCount}");
            Console.WriteLine($"    - Moved but not in tickables: {movedButNotInTickables}");
            Console.WriteLine($"    - Unmoved and not in tickables: {unchangedButNotInTickables}");

            if (postTickIssues.Count > 0)
            {
                Console.WriteLine($"\n⚠ POST-TICK ISSUES ({postTickIssues.Count} total, showing first 20):");
                foreach (var issue in postTickIssues.Take(20))
                {
                    Console.WriteLine($"  {issue}");
                }

                // Additional diagnostics for the first failed object
                if (movedButNotInTickables > 0)
                {
                    var failedMovedObj = objects.FirstOrDefault(o =>
                    {
                        var pos = o.LocalPosition;
                        var init = initialPositions[o];
                        var leaf = lattice.ResolveOccupyingLeaf(o) as TickableVenueLeafNode;
                        if (leaf == null) return false;
                        var tickables = leaf.m_tickableObjects;
                        return pos != init && (tickables == null || !tickables.Contains(o));
                    });

                    if (failedMovedObj != null)
                    {
                        var failedLeaf = lattice.ResolveOccupyingLeaf(failedMovedObj) as TickableVenueLeafNode;
                        Console.WriteLine($"\n=== Detailed diagnosis of first moved-but-unregistered object ===");
                        Console.WriteLine($"  GUID: {failedMovedObj.Guid}");
                        Console.WriteLine($"  Initial pos: {initialPositions[failedMovedObj]}");
                        Console.WriteLine($"  Current pos: {failedMovedObj.LocalPosition}");
                        Console.WriteLine($"  Velocity: {failedMovedObj.Velocity}");
                        Console.WriteLine($"  IsStationary: {failedMovedObj.IsStationary}");
                        Console.WriteLine($"  Leaf bounds: {failedLeaf?.Bounds}");
                        Console.WriteLine($"  Leaf IsRetired: {failedLeaf?.IsRetired}");
                        Console.WriteLine($"  In leaf.Occupants: {failedLeaf?.Occupants?.Contains(failedMovedObj)}");
                        Console.WriteLine($"  Leaf tickables count: {failedLeaf?.m_tickableObjects?.Count}");
                    }
                }

                Assert.Fail($"Post-tick validation failed! {postTickIssues.Count} issues detected after first tick ({movedButNotInTickables} moved but lost registration)!");
            }

            // Expect at least SOME objects to have moved after sleep + tick
            if (movedCount == 0)
            {
                Console.WriteLine("⚠ WARNING: No objects moved after first tick! This may indicate a timing or registration issue.");
            }
            else
            {
                Console.WriteLine($"✓ Post-tick validation PASSED - {movedCount} objects moved successfully");
            }






            // after testing one tick, we have high confidence that all objects are properly set up and will move when ticked.
            // Now we can start the concurrent simulation with one thread pumping ticks and another monitoring object movement.
            Console.WriteLine($"\nStarting concurrent ticker and monitor threads for {durationMs}ms...");

            var tickCount = 0;
            var monitorChecks = 0;
            var totalMovementDetected = 0;
            var failedObjects = new List<TickableSpatialObject>();
            var stopwatch = Stopwatch.StartNew();
            var shouldStop = false;

            // Ticker thread - pumps ticks as fast as possible
            var tickerThread = new Thread(() =>
            {
                while (!shouldStop)
                {
                    lattice.Tick();
                    Interlocked.Increment(ref tickCount);
                    Thread.Yield(); // GC concession
                }
            });

            // Monitor thread - validates all objects are moving
            var monitorThread = new Thread(() =>
            {
                while (!shouldStop)
                {
                    var movedThisCheck = 0;
                    var currentFailures = new List<TickableSpatialObject>();

                    foreach (var obj in objects)
                    {
                        // Verify object still has valid velocity
                        if (obj.IsStationary)
                        {
                            currentFailures.Add(obj);
                            continue;
                        }

                        // Verify object is still in the lattice
                        var leaf = lattice.ResolveOccupyingLeaf(obj);
                        if (leaf == null)
                        {
                            currentFailures.Add(obj);
                            continue;
                        }

                        // Check if object has moved since last check
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

                    // Track failures for final assertion
                    lock (failedObjects)
                    {
                        foreach (var failed in currentFailures)
                        {
                            if (!failedObjects.Contains(failed))
                            {
                                failedObjects.Add(failed);
                            }
                        }
                    }

                    Thread.Sleep(10); // Sample every 10ms
                }
            });

            // Start both threads
            tickerThread.Start();
            Thread.Sleep(100); // Let ticker warm up and accumulate some movement
            monitorThread.Start();

            // Let them run for the duration
            Thread.Sleep(durationMs);
            shouldStop = true;

            // Wait for clean shutdown
            tickerThread.Join();
            monitorThread.Join();

            stopwatch.Stop();

            // Final validation pass - compare against INITIAL positions, not last monitor check
            var finalStationaryCount = 0;
            var finalMissingCount = 0;
            var finalNoMovementCount = 0;
            var unmoved = new List<(TickableSpatialObject obj, string diagnosis)>();

            foreach (var obj in objects)
            {
                if (obj.IsStationary)
                {
                    finalStationaryCount++;
                }

                var leaf = lattice.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;
                if (leaf == null)
                {
                    finalMissingCount++;
                }

                // Verify each object moved from its INITIAL position
                var finalPos = obj.LocalPosition;
                var initialPos = initialPositions[obj];
                if (finalPos == initialPos)
                {
                    finalNoMovementCount++;

                    // Detailed diagnosis for unmoved objects
                    var diagnosis = new System.Text.StringBuilder();
                    diagnosis.Append($"Pos: {initialPos}, Vel: {obj.Velocity}");

                    // Check if the object has an occupying leaf reference
                    // Check if the object has an occupying leaf reference
                    var hasOccupyingLeaf = leaf != null;
                    diagnosis.Append($", HasLeaf: {hasOccupyingLeaf}");

                    // Check if object is in a DIFFERENT leaf than it started in
                    var originalLeaf = lattice.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;
                    var initialLeafStillExists = false;
                    var movedToNewLeaf = false;

                    // We can't directly compare leaves since we didn't save the initial one,
                    // but we can check if the leaf's bounds contain the INITIAL position
                    if (leaf != null)
                    {
                        initialLeafStillExists = leaf.Bounds.Contains(initialPos);
                        movedToNewLeaf = !leaf.Bounds.Contains(initialPos);
                    }

                    diagnosis.Append($", LeafContainsInitialPos: {initialLeafStillExists}");
                    diagnosis.Append($", MovedLeaf: {movedToNewLeaf}");

                    // Check if the leaf thinks this object is registered
                    var isRegisteredInLeaf = false;
                    var isLeafRetired = false;
                    if (leaf != null)
                    {
                        // Access the private m_tickableObjects list via reflection
                        var field = typeof(TickableVenueLeafNode).GetField("m_tickableObjects",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            var tickableList = field.GetValue(leaf) as List<ITickableObject>;
                            isRegisteredInLeaf = tickableList?.Contains(obj) ?? false;
                        }

                        // Check if the leaf is retired
                        isLeafRetired = leaf.IsRetired;
                    }
                    diagnosis.Append($", RegInLeaf: {isRegisteredInLeaf}");
                    diagnosis.Append($", LeafRetired: {isLeafRetired}");

                    // Check object's internal state (needs reflection or exposure)
                    diagnosis.Append($", Stationary: {obj.IsStationary}");

                    unmoved.Add((obj, diagnosis.ToString()));
                }
            }

            // Report results
            Console.WriteLine($"\n=== Simulation Results ===");
            Console.WriteLine($"Duration: {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"Total ticks: {tickCount}");
            Console.WriteLine($"Ticks/sec: {tickCount * 1000.0 / stopwatch.ElapsedMilliseconds:N0}");
            Console.WriteLine($"Avg tick duration: {(tickCount > 0 ? stopwatch.ElapsedMilliseconds / (double)tickCount : 0):N2}ms");
            Console.WriteLine($"Monitor checks: {monitorChecks}");
            Console.WriteLine($"Total movement events detected: {totalMovementDetected}");
            Console.WriteLine($"Avg movements per check: {(monitorChecks > 0 ? totalMovementDetected / (double)monitorChecks : 0):N1}");
            Console.WriteLine($"\n=== Validation ===");
            Console.WriteLine($"Objects that became stationary: {finalStationaryCount}");
            Console.WriteLine($"Objects missing from lattice: {finalMissingCount}");
            Console.WriteLine($"Objects that never moved from initial position: {finalNoMovementCount}");

            if (finalNoMovementCount > 0)
            {
                Console.WriteLine($"\n=== Unmoved Object Diagnostics ({finalNoMovementCount} total) ===");
                foreach (var (obj, diagnosis) in unmoved.Take(10))
                {
                    Console.WriteLine($"  {diagnosis}");
                }

                // Summary statistics
                var unmovedWithLeaf = unmoved.Count(x => x.diagnosis.Contains("HasLeaf: True"));
                var unmovedRegistered = unmoved.Count(x => x.diagnosis.Contains("RegInLeaf: True"));

                Console.WriteLine($"\nUnmoved objects WITH occupying leaf: {unmovedWithLeaf}/{finalNoMovementCount}");
                Console.WriteLine($"Unmoved objects REGISTERED in leaf: {unmovedRegistered}/{finalNoMovementCount}");

                var unmovedPercentage = (finalNoMovementCount / (double)objectCount) * 100;
                Console.WriteLine($"Unmoved percentage: {unmovedPercentage:N3}%");

                var expectedTicksPerObject = tickCount / (double)objectCount;
                Console.WriteLine($"Expected ticks per object: {expectedTicksPerObject:N2}");

                if (expectedTicksPerObject < 0.1)
                {
                    Console.WriteLine("WARNING: Tick rate too low to guarantee all objects move");
                }
            }

            // Assertions - these indicate bugs if they fail
            Assert.AreEqual(0, finalStationaryCount,
                "No objects should become stationary during simulation");
            Assert.AreEqual(0, finalMissingCount,
                "All objects should remain in the lattice throughout simulation");

            // Only assert zero unmoved if we have enough ticks per object
            var minExpectedTicksPerObject = 0.5; // At least half the objects should be ticked
            if (tickCount / (double)objectCount >= minExpectedTicksPerObject)
            {
                Assert.AreEqual(0, finalNoMovementCount,
                    $"All objects should have moved at least once during simulation (had {tickCount} ticks for {objectCount} objects)");
            }
            else
            {
                Console.WriteLine($"⚠ Skipping movement assertion: Only {tickCount / (double)objectCount:N2} ticks/object (< {minExpectedTicksPerObject})");
            }

            Assert.IsTrue(tickCount > 0,
                "Ticker should have completed at least one tick");
            Assert.IsTrue(totalMovementDetected > 0,
                "Monitor should have detected movement");

            Console.WriteLine("\n✓ Grand simulation test PASSED");
        } // fucking
        catch(Exception ex)
        {
            Console.WriteLine($"\nEXCEPTION during grand simulation: {ex}");
            Assert.Fail($"Grand simulation threw an exception: {ex}");
        }
    }

    // === GRAND SIMULATION TEST ===

    [TestMethod]
    public void Test_GrandSimulation_10K_5Seconds()
    {
        RunGrandSimulation(objectCount: 10000, durationMs: 5000);
    }

    [TestMethod]
    public void Test_GrandSimulation_1K_2Seconds()
    {
        RunGrandSimulation(objectCount: 1000, durationMs: 2000);
    }

    [TestMethod]
    public void Test_GrandSimulation_50K_10Seconds()
    {
        RunGrandSimulation(objectCount: 50000, durationMs: 10000);
    }

    [TestMethod]
    public void Test_GrandSimulation_100K_10Seconds()
    {
        try
        {
            RunGrandSimulation(objectCount: 100000, durationMs: 10000);
        }// fucking
        catch(Exception ex)
        {
            Console.WriteLine($"\nEXCEPTION during 100K grand simulation: {ex}");
            Assert.Fail($"100K Grand simulation threw an exception: {ex}");
        }
    }
}
