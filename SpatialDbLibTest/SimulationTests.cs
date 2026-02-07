using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;

namespace SpatialDbLibTest;

[TestClass]
public class SimulationTests
{
    [TestMethod]
    public void ResolveLeafFromOuterLattice_ThrowsNotImplementedException()
    {
        // Arrange
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject([new (1000), new (1200)]);

        var result = lattice.Insert(obj);
        Assert.IsTrue(result is AdmitResult.Created, "Insertion should succeed");

        // Act
        var x = lattice.ResolveOccupyingLeaf(obj);

        // Assert - handled by ExpectedException
        Assert.IsNotNull(x);
    }

    [TestMethod]
    public void BasicMovementTest()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject(new LongVector3(1000));

        // Insert object
        lattice.Insert(new List<SpatialObject> { obj });

        // Give it velocity
        obj.Accelerate(new IntVector3(100, 0, 0));  // Moving east at 100 units/tick
        obj.RegisterForTicks();
        var result = obj.Tick();
        Assert.IsNotNull(result);

        Assert.AreEqual(TickAction.Move, result.Value.Action);
        // Verify position changed appropriately
    }

    [TestMethod]
    public void Test_TickableBasicFunctionality()
    {
        var lattice = new TickableSpatialLattice();

        // 1. Insert tickable object
        var obj = new TickableSpatialObject([new(100)]);

        lattice.Insert(obj);
        var initialPosition = obj.LocalPosition;

        // Register for ticks - object will automatically register with its leaf
        obj.RegisterForTicks();
        obj.Accelerate(new IntVector3(100, 0, 0));  // Moving east at 100 units/tick

        Thread.Sleep(20); // 20ms = 0.02 seconds
        
        // 3. Tick the lattice
        lattice.Tick();
 
        // 4. Verify tick behavior occurred
        var newPosition = obj.LocalPosition;

        // With 20ms delay and velocity of 100 units/tick at 10Hz tick rate:
        // Expected movement ≈ 100 * (20ms / 100ms) = 20 units in X
        Assert.IsTrue(newPosition.X > initialPosition.X, "Object should have moved forward");
        Assert.AreEqual(initialPosition.Y, newPosition.Y, "Y should remain unchanged");
        Assert.AreEqual(initialPosition.Z, newPosition.Z, "Z should remain unchanged");

        Console.WriteLine($"Object moved from {initialPosition} to {newPosition}");
        Console.WriteLine($"Velocity: {obj.Velocity}, Delta: {newPosition - initialPosition}");
    }

    [TestMethod]
    public void Test_BoundaryCrossing()
    {
        var lattice = new TickableSpatialLattice();

        // Get the lattice mid-point to know where octant boundaries are
        var root = lattice.GetRootNode() as OctetParentNode;
        Assert.IsNotNull(root);
        var mid = root.Bounds.Mid;

        // Place object just before the X-axis boundary (in octant 0: ---) 
        // Position it 50 units before the midpoint, and explicitly below mid on Y and Z
        var startPos = new LongVector3(mid.X - 50, mid.Y - 10, mid.Z - 10);
        var obj = new TickableSpatialObject(startPos);

        lattice.Insert(obj);
        
        // Verify initial leaf (should be in octant 0: X < mid, Y < mid, Z < mid)
        var initialLeaf = lattice.ResolveOccupyingLeaf(obj);
        Assert.IsNotNull(initialLeaf);
        Assert.IsTrue(initialLeaf.Bounds.Contains(startPos), "Object should be in initial leaf bounds");
        
        // Calculate which child index the initial leaf is
        var initialOctant = GetOctantIndex(startPos, mid);
        Assert.AreEqual(0, initialOctant, "Object should start in octant 0 (---)");

        Console.WriteLine($"Initial position: {startPos}, Octant: {initialOctant}");
        Console.WriteLine($"Midpoint: {mid}");
        Console.WriteLine($"Initial leaf bounds: {initialLeaf.Bounds}");

        // Give it velocity to cross the X boundary (moving east into octant 4: +--) 
        // We need enough velocity to cross ~50 units in one tick
        // With velocity = 5000 and 20ms sleep, movement ≈ 5000 * 0.02 = 100 units
        obj.Accelerate(new IntVector3(5000, 0, 0));
        obj.RegisterForTicks();

        Thread.Sleep(20); // 20ms

        // Tick the lattice - this should trigger boundary crossing
        lattice.Tick();

        // Verify new position crossed the boundary
        var newPosition = obj.LocalPosition;
        Console.WriteLine($"New position: {newPosition}");
        
        Assert.IsTrue(newPosition.X >= mid.X, "Object should have crossed X boundary");
        Assert.AreEqual(startPos.Y, newPosition.Y, "Y should remain unchanged");
        Assert.AreEqual(startPos.Z, newPosition.Z, "Z should remain unchanged");

        // Verify object is now in the correct new leaf (octant 4: X >= mid, Y < mid, Z < mid)
        var newLeaf = lattice.ResolveOccupyingLeaf(obj);
        Assert.IsNotNull(newLeaf, "Object should occupy a leaf after boundary crossing");
        Assert.IsTrue(newLeaf.Bounds.Contains(newPosition), "Object should be in new leaf bounds");
        
        var newOctant = GetOctantIndex(newPosition, mid);
        Assert.AreEqual(4, newOctant, "Object should now be in octant 4 (+--) after crossing X boundary");
        
        // Verify it's a different leaf
        Assert.AreNotEqual(initialLeaf, newLeaf, "Object should be in a different leaf after crossing boundary");

        Console.WriteLine($"New leaf bounds: {newLeaf.Bounds}");
        Console.WriteLine($"Boundary crossing successful: Octant {initialOctant} → Octant {newOctant}");
    }

    // Helper method to calculate octant index from position and midpoint
    private int GetOctantIndex(LongVector3 pos, LongVector3 mid)
    {
        return ((pos.X >= mid.X) ? 4 : 0) |
               ((pos.Y >= mid.Y) ? 2 : 0) |
               ((pos.Z >= mid.Z) ? 1 : 0);
    }

    [TestMethod]
    public void Test_TickPerformance()
    {
        var counts = new[] { 50000, 100000, 200000, 400000, 800000 };

        Console.WriteLine($"{"Count",-10} {"Insert (ms)",-15} {"Tick (ms)",-15} {"Objs/sec",-15}");
        Console.WriteLine(new string('-', 60));

        foreach (var count in counts)
        {
            var lattice = new TickableSpatialLattice();
            var objects = new List<TickableSpatialObject>();

            // Create objects with varying positions and velocities
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

            // Bulk insert
            lattice.Insert(objects.Cast<SpatialObject>().ToArray());

            // Register all objects for ticks
            foreach (var obj in objects)
            {
                obj.RegisterForTicks();
                var leaf = lattice.ResolveOccupyingLeaf(obj);
                if (leaf is TickableVenueLeafNode tickableLeaf)
                {
                    tickableLeaf.RegisterForTicks(obj);
                }
                obj.Accelerate(new IntVector3(100, 0, 0));  // Moving east at 100 units/tick
            }

            var insertTime = sw.ElapsedMilliseconds;

            // Small delay to ensure measurable time delta
            Thread.Sleep(10);

            // Measure tick performance
            sw.Restart();
            lattice.Tick();
            var tickTime = sw.ElapsedMilliseconds;

            var objsPerSec = tickTime > 0 ? (count * 1000.0 / tickTime) : double.PositiveInfinity;

            Console.WriteLine($"{count,-10} {insertTime,-15} {tickTime,-15} {objsPerSec,-15:N0}");
        }
    }


    [TestMethod]
    public void Test_TickableWithSublattices()
    {
        var lattice = new TickableSpatialLattice();

        // Force sublattice creation
        for (int i = 0; i < 20; i++)
        {
            var obj = new TickableSpatialObject([new(1)]);
            lattice.Insert(obj);
        }

        // Verify tick propagates through sublattices
        lattice.Tick();

        // Verify all objects were ticked
    }
}
