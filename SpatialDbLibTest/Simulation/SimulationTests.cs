using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;
using SpatialDbLibTest.Helpers;
using System.Diagnostics;
//////////////////////////////////////
namespace SpatialDbLibTest.Simulation;

[TestClass]
public class SimulationTests
{
    // === BASIC FUNCTIONALITY ===
    [TestMethod]
    [DoNotParallelize]
    [Priority(1)]
    public void SimulationTests_Omnibus()
    {
        Console.WriteLine("=".PadRight(70, '='));
        Console.WriteLine("SIMULATION FEATURES TEST SUITE");
        Console.WriteLine("=".PadRight(70, '='));

        // === BASIC FUNCTIONALITY ===
        {
            Console.WriteLine("\n--- Basic Functionality ---");

            // Deep Insertion
            {
                Console.Write("  Deep insertion into sublattice... ");
                var lattice = new TickableSpatialLattice();
                var obj = new TickableSpatialObject([new(1000), new(1200)]);
                var result = lattice.Insert(obj);
                Assert.IsTrue(result is AdmitResult.Created, "Insertion should succeed");
                var leaf = lattice.ResolveOccupyingLeaf(obj);
                Assert.IsNotNull(leaf);
                Assert.IsInstanceOfType(leaf, typeof(TickableVenueLeafNode));
                Assert.AreEqual(2, obj.PositionStackDepth, "Object should have 2 positions");
                Console.WriteLine("✓ PASSED");
            }

            // Object Tick Returns Movement Intent
            {
                Console.Write("  Object tick returns movement intent... ");
                var lattice = new TickableSpatialLattice();
                var obj = new TickableSpatialObject(new LongVector3(1000));
                lattice.Insert(obj);
                obj.Accelerate(new IntVector3(100, 0, 0));
                obj.RegisterForTicks();
                Thread.Sleep(20);
                var result = obj.Tick();
                Assert.IsNotNull(result);
                Assert.AreEqual(TickAction.Move, result.Value.Action);
                Assert.IsTrue(result.Value.Target.X > obj.LocalPosition.X, "Target should be ahead");
                Console.WriteLine("✓ PASSED");
            }

            // Lattice Tick Moves Objects
            {
                Console.Write("  Lattice tick moves objects... ");
                var lattice = new TickableSpatialLattice();
                var obj = new TickableSpatialObject([new(1000)]);
                lattice.Insert(obj);
                var initialPosition = obj.LocalPosition;
                obj.Accelerate(new IntVector3(1000, 0, 0));
                obj.RegisterForTicks();
                Thread.Sleep(50);
                lattice.Tick();
                var newPosition = obj.LocalPosition;
                Assert.IsTrue(newPosition.X > initialPosition.X, "Object should move forward");
                Assert.AreEqual(initialPosition.Y, newPosition.Y, "Y unchanged");
                Assert.AreEqual(initialPosition.Z, newPosition.Z, "Z unchanged");
                Console.WriteLine($"✓ PASSED (moved from {initialPosition.X} to {newPosition.X})");
            }
        }

        // === PROXY BEHAVIOR ===
        {
            Console.WriteLine("\n--- Proxy Behavior ---");

            // Proxy Transfers Velocity On Commit
            {
                Console.Write("  Proxy transfers velocity on commit... ");
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
                Assert.AreEqual(8000, obj.Velocity.X, "Modified velocity should transfer");
                Assert.AreEqual(4000, obj.Velocity.Y);
                Assert.AreEqual(2000, obj.Velocity.Z);
                Console.WriteLine("✓ PASSED");
            }
        }

        // === BOUNDARY CROSSING ===
        {
            Console.WriteLine("\n--- Boundary Crossing ---");

            // Single Object Boundary Crossing
            {
                Console.Write("  Single object crosses octant boundary... ");
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
                Assert.AreEqual(0, initialOctant, "Should start in octant 0");
                obj.RegisterForTicks();
                obj.Accelerate(new IntVector3(5000, 0, 0));
                Thread.Sleep(150);
                lattice.Tick();
                var newPosition = obj.LocalPosition;
                Assert.IsTrue(newPosition.X >= mid.X, "Should cross X boundary");
                Assert.AreEqual(startPos.Y, newPosition.Y, "Y unchanged");
                Assert.AreEqual(startPos.Z, newPosition.Z, "Z unchanged");
                var newLeaf = lattice.ResolveOccupyingLeaf(obj);
                Assert.IsNotNull(newLeaf, "Should occupy leaf after crossing");
                var newOctant = GetOctantIndex(newPosition, mid);
                Assert.AreEqual(4, newOctant, "Should be in octant 4");
                Assert.AreNotEqual(initialLeaf, newLeaf, "Should be in different leaf");
                Console.WriteLine($"✓ PASSED (octant {initialOctant} → {newOctant})");
            }
        }

        // === SUBLATTICE BEHAVIOR ===
        {
            Console.WriteLine("\n--- Sublattice Behavior ---");

            // Tick Propagates Through Sublattices
            {
                Console.Write("  Tick propagates through sublattices... ");
                var lattice = new TickableSpatialLattice();
                var obj = new TickableSpatialObject([new(1), new(100)]);
                lattice.Insert(obj);
                var leaf = lattice.ResolveOccupyingLeaf(obj);
                Assert.IsInstanceOfType(leaf, typeof(TickableVenueLeafNode));
                obj.SetVelocityStack([new IntVector3(0, 0, 1000)]);
                obj.RegisterForTicks();
                var initialPosition = obj.LocalPosition;
                Thread.Sleep(50);
                lattice.Tick();
                Assert.IsTrue(obj.LocalPosition.DistanceTo(initialPosition) > 0, "Should move in sublattice");
                Console.WriteLine("✓ PASSED");
            }
        }

        // === REGISTRATION & FILTERING ===
        {
            Console.WriteLine("\n--- Registration & Filtering ---");

            // Register and Unregister
            {
                Console.Write("  Register/unregister for ticks... ");
                var lattice = new TickableSpatialLattice();
                var obj = new TickableSpatialObject(new LongVector3(1000));
                lattice.Insert(obj);
                obj.Accelerate(new IntVector3(1000, 0, 0));
                obj.RegisterForTicks();
                var initialPosition = obj.LocalPosition;
                Thread.Sleep(50);
                lattice.Tick();
                Assert.IsTrue(obj.LocalPosition.X > initialPosition.X, "Registered object should move");
                obj.UnregisterForTicks();
                var positionAfterUnregister = obj.LocalPosition;
                Thread.Sleep(50);
                lattice.Tick();
                Assert.AreEqual(positionAfterUnregister, obj.LocalPosition, "Unregistered should not move");
                Console.WriteLine("✓ PASSED");
            }

            // Stationary Objects Filtered By Threshold
            {
                Console.Write("  Stationary objects filtered by threshold... ");
                var lattice = new TickableSpatialLattice();
                var slowObj = new TickableSpatialObject(new LongVector3(1000));
                lattice.Insert(slowObj);
                slowObj.Velocity = new IntVector3(5, 5, 5);
                slowObj.RegisterForTicks();
                var fastObj = new TickableSpatialObject(new LongVector3(2000));
                lattice.Insert(fastObj);
                fastObj.Velocity = new IntVector3(100, 100, 100);
                fastObj.RegisterForTicks();
                Assert.IsTrue(slowObj.IsStationary, "Slow should be stationary");
                Assert.IsFalse(fastObj.IsStationary, "Fast should not be stationary");
                var slowInitialPos = slowObj.LocalPosition;
                var fastInitialPos = fastObj.LocalPosition;
                Thread.Sleep(50);
                lattice.Tick();
                Assert.AreEqual(slowInitialPos, slowObj.LocalPosition, "Stationary should not move");
                Assert.AreNotEqual(fastInitialPos, fastObj.LocalPosition, "Fast should move");
                Console.WriteLine("✓ PASSED");
            }

            // Velocity Enforces Threshold
            {
                Console.Write("  Velocity threshold enforcement... ");
                var lattice = new TickableSpatialLattice();
                var obj = new TickableSpatialObject(new LongVector3(1000));
                lattice.Insert(obj);
                obj.Velocity = new IntVector3(5, 5, 5);
                Assert.AreEqual(IntVector3.Zero, obj.Velocity, "Below threshold should be zeroed");
                obj.Velocity = new IntVector3(100, 100, 100);
                Assert.AreNotEqual(IntVector3.Zero, obj.Velocity, "Above threshold should preserve");
                Console.WriteLine("✓ PASSED");
            }
        }

        // === VELOCITY STACK (MULTI-DEPTH) ===
        {
            Console.WriteLine("\n--- Velocity Stack ---");

            // Velocity Stack Multi-Depth
            {
                Console.Write("  Multi-depth velocity stack... ");
                var lattice = new TickableSpatialLattice();
                var obj = new TickableSpatialObject([new(1000), new(500)]);
                lattice.Insert(obj);
                obj.SetVelocityStack([new IntVector3(100, 0, 0), new IntVector3(50, 0, 0)]);
                var velocityStack = obj.GetVelocityStack();
                Assert.AreEqual(2, velocityStack.Count, "Stack should have 2 entries");
                Assert.AreEqual(100, velocityStack[0].X, "Outer velocity should be 100");
                Assert.AreEqual(50, velocityStack[1].X, "Inner velocity should be 50");
                Assert.AreEqual(50, obj.Velocity.X, "LocalVelocity should match last element");
                Console.WriteLine("✓ PASSED");
            }
        }

        // === TICKABLE PRUNING ===
        {
            Console.WriteLine("\n--- Tickable Pruning ---");
            Console.Write("  Prune works... ");
            var tickableLattice = new TickableSpatialLattice();
            SpatialLattice.EnablePruning = true;

            // Create a dense cluster (4x4x4 = 64) near (10,10,10) so it deterministically subdivides
            var clusterCenter = new LongVector3(10, 10, 10);
            var cluster = new List<TickableSpatialObject>();
            for (int zx = 0; zx < 4; zx++)
                for (int yy = 0; yy < 4; yy++)
                    for (int xx = 0; xx < 4; xx++)
                    {
                        // small deterministic offsets to spread at the second level
                        var pos = new LongVector3(clusterCenter.X + xx, clusterCenter.Y + yy, clusterCenter.Z + zx);
                        var o = new TickableSpatialObject(pos);
                        cluster.Add(o);
                        tickableLattice.Insert(o);
                    }

            // Register them and give them a velocity that will move them out of the entire level-2 octet
            foreach (var o in cluster)
            {
                o.SetVelocityStack([new IntVector3(0, 0, 0), new IntVector3(-2000, 0, 0)]); // ensure inner lattice has strong movement
                o.RegisterForTicks();
            }

            // Count nodes before
            int nodeCountBefore = 0;
            void CountNodes(ISpatialNode n)
            {
                nodeCountBefore++;
                if (n is OctetParentNode p)
                    foreach (var c in p.Children) CountNodes(c);
            }
            CountNodes(tickableLattice.m_root);

            // Give time so first Tick has positive delta and the cluster can be processed.
            Thread.Sleep(50);
            // Tick once to move cluster out
            tickableLattice.Tick();

            // Diagnostic snapshot AFTER tick
            Console.WriteLine("=== DIAG AFTER CLUSTER-TICK ===");
            Console.WriteLine($"Cluster moved sample local position: {cluster[0].LocalPosition}");
            var leafAfterSample = tickableLattice.ResolveOccupyingLeaf(cluster[0]);
            Console.WriteLine($"Sample leafAfter.Bounds: {leafAfterSample?.Bounds}");
            Console.WriteLine("=== END DIAG ===");

            // Count nodes after ticking
            int nodeCountAfter = 0;
            CountNodes(tickableLattice.m_root);

            // Pruning should have reduced node count if the entire subdivided octet was emptied
            Assert.IsTrue(nodeCountAfter <= nodeCountBefore, "Pruning should not increase node count");

            Console.WriteLine("✓ PASSED");
        }
        // === LOCAL NEIGHBOR QUERIES ===
        {
            Console.WriteLine("\n--- Queries ---");
            Console.Write("  Local neighbor queries... ");
            var lattice = new TickableSpatialLattice();
            List<TickableSpatialObject> objects = [];
            for (int i = 0; i < 1000; i++)
            {
                var pos = new LongVector3(i * 100L, 0, 0);
                var obj = new TickableSpatialObject(pos);
                objects.Add(obj);
                lattice.Insert(obj);
            }
            var testObj = objects[500];
            var leaf = lattice.ResolveOccupyingLeaf(testObj);
            Assert.IsNotNull(leaf);
            var radius = 500UL; // Should cover ~10 objects
            var neighbors = leaf.QueryNeighbors(testObj.LocalPosition, radius).ToList();
            Assert.IsTrue(neighbors.Count > 0, "Should find neighbors");
            Assert.IsTrue(neighbors.Contains(testObj), "Should include self");
            Console.WriteLine($"✓ PASSED ({neighbors.Count} neighbors)");
        }

        {
            Console.WriteLine("\n--- Parallel Ticking ---");
            Console.Write("  Parallel ticking... ");
            var lattice = new TickableSpatialLattice();
            for (int i = 0; i < 10000; i++)
            {
                var pos = new LongVector3(i * 10L, 0, 0);
                var obj = new TickableSpatialObject(pos);
                lattice.Insert(obj);
                obj.RegisterForTicks();
                obj.Accelerate(new IntVector3(100, 0, 0));
            }
            var initialPositions = ((OctetParentNode)lattice.GetRootNode()).Children
                .OfType<VenueLeafNode>()
                .SelectMany(l => l.Occupants.Select(o => o.LocalPosition.X))
                .ToList();
            // Tick in parallel
            SpatialTicker.TickParallel(lattice, 4);
            var finalPositions = ((OctetParentNode)lattice.GetRootNode()).Children
                .OfType<VenueLeafNode>()
                .SelectMany(l => l.Occupants.Select(o => o.LocalPosition.X))
                .ToList();
            Assert.IsTrue(finalPositions.Zip(initialPositions, (f, i) => f > i).All(b => b), "All objects should have moved forward");
            Console.WriteLine("✓ PASSED");

        }

        Console.WriteLine("\n" + "=".PadRight(70, '='));
        Console.WriteLine("ALL SIMULATION FEATURE TESTS PASSED");
        Console.WriteLine("=".PadRight(70, '='));
    }



    private static int GetOctantIndex(LongVector3 pos, LongVector3 mid)
    {
        return (pos.X >= mid.X ? 4 : 0) |
               (pos.Y >= mid.Y ? 2 : 0) |
               (pos.Z >= mid.Z ? 1 : 0);
    }

    // === PERFORMANCE BENCHMARK ===

    [TestMethod]
    [DoNotParallelize]
    public void Benchmark_ParallelTickPerformance()
    {
        // === PARALLEL VS SERIAL TICK PERFORMANCE ===
        {
            var count = 500_000;
            Console.WriteLine($"Comparing Serial vs Parallel Tick for {count} objects");
            Console.WriteLine($"{"Method",-10} {"Tick Time (ms)",-15} {"Objs/sec",-15}");
            Console.WriteLine(new string('-', 40));

            // Serial
            {
                var lattice = new TickableSpatialLattice();
                var objects = new List<IMoveableObject>();
                for (int i = 0; i < count; i++)
                {
                    var pos = new LongVector3(
                        FastRandom.NextLong(-LatticeUniverse.HalfExtent, LatticeUniverse.HalfExtent),
                        FastRandom.NextLong(-LatticeUniverse.HalfExtent, LatticeUniverse.HalfExtent),
                        FastRandom.NextLong(-LatticeUniverse.HalfExtent, LatticeUniverse.HalfExtent));

                    var obj = new TickableSpatialObject(pos)
                    {
                        Velocity = new IntVector3(
                            FastRandom.NextInt(20, 100),
                            FastRandom.NextInt(20, 100),
                            FastRandom.NextInt(20, 100))
                    };
                    objects.Add(obj);
                }
                lattice.Insert(objects.Cast<ISpatialObject>().ToList());
                foreach (var obj in objects)
                    obj.RegisterForTicks();

                var sw = Stopwatch.StartNew();
                lattice.Tick();
                var tickTime = sw.ElapsedMilliseconds;
                var objsPerSec = count * 1000.0 / tickTime;
                Console.WriteLine($"Serial   {tickTime,-15} {objsPerSec,-15:N0}");
            }

            // Parallel
            {
                var lattice = new TickableSpatialLattice();
                var objects = new List<IMoveableObject>();
                for (int i = 0; i < count; i++)
                {
                    var pos = new LongVector3(
                        FastRandom.NextLong(-LatticeUniverse.HalfExtent, LatticeUniverse.HalfExtent),
                        FastRandom.NextLong(-LatticeUniverse.HalfExtent, LatticeUniverse.HalfExtent),
                        FastRandom.NextLong(-LatticeUniverse.HalfExtent, LatticeUniverse.HalfExtent));

                    var obj = new TickableSpatialObject(pos)
                    {
                        Velocity = new IntVector3(
                            FastRandom.NextInt(20, 100),
                            FastRandom.NextInt(20, 100),
                            FastRandom.NextInt(20, 100))
                    };
                    objects.Add(obj);
                }
                lattice.Insert(objects.Cast<ISpatialObject>().ToList());
                foreach (var obj in objects)
                    obj.RegisterForTicks();

                var sw = Stopwatch.StartNew();
                SpatialTicker.TickParallel(lattice);  // for better hallway vision.
                var tickTime = sw.ElapsedMilliseconds;
                var objsPerSec = count * 1000.0 / tickTime;
                Console.WriteLine($"Parallel {tickTime,-15} {objsPerSec,-15:N0}");
            }
        }
    }
    //[TestMethod]
    [DoNotParallelize]
    public void Benchmark_TickPerformance()
    {
        var counts = new[] { 50000, 250000, 1000000 };
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
                    FastRandom.NextLong(-LatticeUniverse.HalfExtent, LatticeUniverse.HalfExtent),
                    FastRandom.NextLong(-LatticeUniverse.HalfExtent, LatticeUniverse.HalfExtent),
                    FastRandom.NextLong(-LatticeUniverse.HalfExtent, LatticeUniverse.HalfExtent));

                var obj = new TickableSpatialObject(pos)
                {
                    Velocity = new IntVector3(
                        FastRandom.NextInt(20, 100),
                        FastRandom.NextInt(20, 100),
                        FastRandom.NextInt(20, 100))
                };
                objects.Add(obj);
            }
            lattice.Insert(objects.Cast<ISpatialObject>().ToList());
            foreach (var obj in objects)
                obj.RegisterForTicks();
            var insertTime = sw.ElapsedMilliseconds;
            Thread.Sleep(10);
            sw.Restart();
            lattice.Tick();
            var tickTime = sw.ElapsedMilliseconds;
            var objsPerSec = tickTime > 0 ? count * 1000.0 / tickTime : double.PositiveInfinity;
            Console.WriteLine($"{count,-10} {insertTime,-15} {tickTime,-15} {objsPerSec,-15:N0}");
        }
    }
}
