using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;

namespace SpatialDbLibTest;

[TestClass]
public class SimulationTests
{
    [TestMethod]
    public void ResolveLeafFromOuterLattice_DeepInsert()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject([new(1000), new(1200)]);

        var result = lattice.Insert(obj);
        Assert.IsTrue(result is AdmitResult.Created, "Insertion should succeed");

        var leaf = lattice.ResolveOccupyingLeaf(obj);
        Assert.IsNotNull(leaf);
        Assert.AreEqual(2, obj.PositionStackDepth, "Object should have 2 positions (deep insert)");
    }

    [TestMethod]
    public void BasicMovementTest()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject(new LongVector3(1000));

        lattice.Insert(obj);

        obj.Accelerate(new IntVector3(100, 0, 0));
        obj.RegisterForTicks();
        var result = obj.Tick();
        
        Assert.IsNotNull(result);
        Assert.AreEqual(TickAction.Move, result.Value.Action);
    }

    [TestMethod]
    public void Test_TickableBasicFunctionality()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject([new(100)]);

        lattice.Insert(obj);
        var initialPosition = obj.LocalPosition;

        obj.RegisterForTicks();
        obj.Accelerate(new IntVector3(100, 0, 0));

        Thread.Sleep(20);
        lattice.Tick();
 
        var newPosition = obj.LocalPosition;

        Assert.IsTrue(newPosition.X > initialPosition.X, "Object should have moved forward");
        Assert.AreEqual(initialPosition.Y, newPosition.Y, "Y should remain unchanged");
        Assert.AreEqual(initialPosition.Z, newPosition.Z, "Z should remain unchanged");

        Console.WriteLine($"Object moved from {initialPosition} to {newPosition}");
        Console.WriteLine($"Velocity: {obj.Velocity}, Delta: {newPosition - initialPosition}");
    }

    [TestMethod]
    public void Test_ProxyTicksWhileUncommitted()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject(new LongVector3(1000));
        
        lattice.Insert(obj);
        obj.Accelerate(new IntVector3(5000, 0, 0));
        obj.RegisterForTicks();
        
        var initialPosition = obj.LocalPosition;
        Thread.Sleep(20);
        
        var admitResult = lattice.GetRootNode().Admit(obj, new LongVector3(2000));
        Assert.IsTrue(admitResult is AdmitResult.Created);
        
        var proxy = ((AdmitResult.Created)admitResult).Proxy;
        Assert.IsTrue(proxy is TickableSpatialObjectProxy, "Should create tickable proxy");
        
        var proxyInitialPos = proxy.LocalPosition;
        Thread.Sleep(20);
        
        lattice.Tick();
        
        var proxyNewPos = proxy.LocalPosition;
        Assert.IsTrue(proxyNewPos.X > proxyInitialPos.X, "Proxy should tick while uncommitted");
        
        proxy.Commit();
        
        var finalPos = obj.LocalPosition;
        Assert.AreEqual(proxyNewPos.X, finalPos.X, "Position should transfer on commit");
    }

    [TestMethod]
    public void Test_VelocityTransfersOnCommit()
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
    }

    [TestMethod]
    public void Test_BoundaryCrossing()
    {
        var lattice = new TickableSpatialLattice();
        var root = lattice.GetRootNode() as OctetParentNode;
        Assert.IsNotNull(root);
        var mid = root.Bounds.Mid;

        var startPos = new LongVector3(mid.X - 50, mid.Y - 10, mid.Z - 10);
        var obj = new TickableSpatialObject(startPos);

        lattice.Insert(obj);
        
        var initialLeaf = lattice.ResolveOccupyingLeaf(obj);
        Assert.IsNotNull(initialLeaf);
        
        var initialOctant = GetOctantIndex(startPos, mid);
        Assert.AreEqual(0, initialOctant, "Object should start in octant 0 (---)");

        Console.WriteLine($"Initial position: {startPos}, Octant: {initialOctant}");
        Console.WriteLine($"Midpoint: {mid}");

        obj.Accelerate(new IntVector3(5000, 0, 0));
        obj.RegisterForTicks();

        Thread.Sleep(20);
        lattice.Tick();

        var newPosition = obj.LocalPosition;
        Console.WriteLine($"New position: {newPosition}");
        
        Assert.IsTrue(newPosition.X >= mid.X, "Object should have crossed X boundary");
        Assert.AreEqual(startPos.Y, newPosition.Y, "Y should remain unchanged");
        Assert.AreEqual(startPos.Z, newPosition.Z, "Z should remain unchanged");

        var newLeaf = lattice.ResolveOccupyingLeaf(obj);
        Assert.IsNotNull(newLeaf, "Object should occupy a leaf after boundary crossing");
        
        var newOctant = GetOctantIndex(newPosition, mid);
        Assert.AreEqual(4, newOctant, "Object should now be in octant 4 (+--) after crossing X boundary");
        Assert.AreNotEqual(initialLeaf, newLeaf, "Object should be in a different leaf after crossing boundary");

        Console.WriteLine($"Boundary crossing successful: Octant {initialOctant} → Octant {newOctant}");
    }

    [TestMethod]
    public void Test_VelocityStackMaintained()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject([new(1000), new(2000)]);
        
        lattice.Insert(obj);
        
        Assert.AreEqual(2, obj.PositionStackDepth);
        
        obj.Accelerate(new IntVector3(100, 200, 300));
        
        var velocityStack = obj.GetVelocityStack();
        Assert.AreEqual(2, velocityStack.Count, "Velocity stack should match position stack depth");
        Assert.AreEqual(100, velocityStack[1].X, "Local velocity should be set");
    }

    [TestMethod]
    public void Test_TickableWithSublattices()
    {
        var lattice = new TickableSpatialLattice();

        var objects = new List<TickableSpatialObject>();
        for (int i = 0; i < 20; i++)
        {
            var obj = new TickableSpatialObject([new(1), new(i * 100)]);
            lattice.Insert(obj);

            // For a size=1 outer lattice, set velocity stack explicitly:
            // [0] = outer velocity (will cause it to escape sublattice)
            // [1] = inner velocity (movement within sublattice)
            obj.SetVelocityStack([new IntVector3(50, 0, 0), new IntVector3(50, 0, 0)]);
            obj.RegisterForTicks();
            objects.Add(obj);
        }

        var initialPositions = objects.Select(o => o.LocalPosition).ToList();

        Thread.Sleep(20);
        lattice.Tick();

        for (int i = 0; i < objects.Count; i++)
        {
            var newPos = objects[i].LocalPosition;
            Assert.IsTrue(newPos.X > initialPositions[i].X, 
                $"Object {i} in sublattice should have moved");
        }
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

            lattice.Insert(objects.Cast<SpatialObject>().ToArray());

            foreach (var obj in objects)
            {
                obj.RegisterForTicks();
                obj.Accelerate(new IntVector3(100, 0, 0));
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

    private int GetOctantIndex(LongVector3 pos, LongVector3 mid)
    {
        return ((pos.X >= mid.X) ? 4 : 0) |
               ((pos.Y >= mid.Y) ? 2 : 0) |
               ((pos.Z >= mid.Z) ? 1 : 0);
    }
}
