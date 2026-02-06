using SpatialDbLib.Lattice;
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
        obj.Velocity = new IntVector3(10, 0, 0);  // Set movement (units per second)

        lattice.Insert(obj);

        var leaf = lattice.ResolveOccupyingLeaf(obj);
        Assert.IsNotNull(leaf);
        Assert.IsInstanceOfType(leaf, typeof(TickableVenueLeafNode));
        obj.Accelerate(new IntVector3(100, 0, 0));  // Moving east at 100 units/tick

        var tickableLeaf = (TickableVenueLeafNode)leaf;
        tickableLeaf.RegisterForTicks(obj);

        // Store initial position
        var initialPosition = obj.LocalPosition;
        Thread.Sleep(20); // 20ms = 0.02 seconds
        // 3. Tick the lattice
        lattice.Tick();
 
        // 4. Verify tick behavior occurred
        var newPosition = obj.LocalPosition;

        // With 20ms delay and velocity of 10 units/sec, should move roughly 0.2 units in X
        Assert.IsTrue(newPosition.X > initialPosition.X, "Object should have moved forward");
        Assert.AreEqual(initialPosition.Y, newPosition.Y, "Y should remain unchanged");
        Assert.AreEqual(initialPosition.Z, newPosition.Z, "Z should remain unchanged");

        Console.WriteLine($"Object moved from {initialPosition} to {newPosition}");
        Console.WriteLine($"Velocity: {obj.Velocity}, Delta: {newPosition - initialPosition}");
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
