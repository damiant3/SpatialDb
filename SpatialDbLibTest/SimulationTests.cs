using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;

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
        var counts = new[] { 50000, 100000, 200000, 400000, 800000 };

        Console.WriteLine($"{"Count",-10} {"Insert (ms)",-15} {"Tick (ms)",-15} {"Objs/sec",-15}");
        Console.WriteLine(new string('-', 60));

        foreach (var count in counts)
        {
            var lattice = new TickableSpatialLattice();
            var objects = new List<IMoveable>();

            var sw = System.Diagnostics.Stopwatch.StartNew();

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
}
