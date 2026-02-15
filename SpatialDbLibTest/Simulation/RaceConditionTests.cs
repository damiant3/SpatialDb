using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;
using System.Diagnostics;
//////////////////////////////////////
namespace SpatialDbLibTest.Simulation;
[TestClass]
public class RaceConditionTests
{
    [TestMethod]
    public void RaceMigrationExceptionTest()
    {

        // new, more-sensitive test here.

        Console.Write("  Proxy/migration race reproducer... ");
        // This test amplifies concurrency and timing sensitivity so the "Migrant has no home"
        // failure reproduces reliably even when run alone. Changes:
        //  - larger population to force more subdivision & work
        //  - ticker runs continuously with more threads (Environment.ProcessorCount)
        //  - multiple committer threads perform many admits/commits with tiny jitter
        //  - coordinated simultaneous start to maximize interleaving
        //  - limited GC/pressure actions left commented (can be enabled for more stress)

        var raceLattice = new TickableSpatialLattice();
        var objs = new List<TickableSpatialObject>();
        // Populate lattice to cause subdivision and work (increased size)
        for (int i = 0; i < 4000; i++)
        {
            var pos = new LongVector3(i * 16L, (i % 10) * 8L, (i % 7) * 5L);
            var o = new TickableSpatialObject(pos);
            o.RegisterForTicks();
            o.Accelerate(new IntVector3(200, 0, 0));
            objs.Add(o);
            raceLattice.Insert(o);
        }

        // Choose a candidate object that will be the migrating subject
        var migrant = objs[objs.Count / 2];
        migrant.Accelerate(new IntVector3(2000, 0, 0));
        migrant.RegisterForTicks();

        bool stop = false;
        Exception? captured = null;
        object capLock = new();

        var startSignal = new ManualResetEventSlim(false);

        // Ticker worker: busy-parks, runs parallel ticks rapidly using available cores
        var tickerThread = new Thread(() =>
        {
            try
            {
                // Wait for coordinated start
                startSignal.Wait();
                while (!stop)
                {
                    SpatialTicker.TickParallel(raceLattice, Math.Max(2, Environment.ProcessorCount));
                    // tiny pause to yield scheduler but keep high throughput
                    Thread.SpinWait(20);
                }
            }
            catch (Exception ex)
            {
                lock (capLock) { captured ??= ex; }
                stop = true;
            }
        })
        { IsBackground = true };

        // Multiple committers to increase chance of racing with tick threads
        int committerCount = Math.Max(2, Environment.ProcessorCount / 2);
        var committerThreads = new List<Thread>(committerCount);
        for (int t = 0; t < committerCount; t++)
        {
            var threadIndex = t;
            var committerThread = new Thread(() =>
            {
                // Per-thread deterministic-ish RNG
                var rnd = new Random(1234 + threadIndex);
                try
                {
                    // Wait for coordinated start
                    startSignal.Wait();

                    // before the per-thread loop:
                    var moverTemplate = migrant; // or build a template position

                    // Each thread performs many admits/commits
                    for (int iter = 0; iter < 8000 && !stop; iter++)
                    {
                        var rootNode = raceLattice.GetRootNode();
                        // pick target offset with larger range
                        var targetOffset = new LongVector3(rnd.Next(200, 8000), rnd.Next(-20, 20), rnd.Next(-20, 20));

                        // inside thread:
                        var threadMover = new TickableSpatialObject(moverTemplate.LocalPosition) { /* set state */ };
                        //raceLattice.Insert(threadMover);
                        var admit = rootNode.Admit(threadMover, threadMover.LocalPosition + targetOffset);
                        if (admit is AdmitResult.Created created && created.Proxy is TickableSpatialObjectProxy proxy)
                        {
                            // nudge proxy so its tick will produce movement
                            proxy.LocalVelocity = new IntVector3(rnd.Next(500, 5000), 0, 0);
                            // very small jitter to provoke interleavings; use SpinWait for finer granularity
                            Thread.SpinWait(rnd.Next(0, 40));
                            try
                            {
                                proxy.Commit();
                            }
                            catch (Exception ex)
                            {
                                lock (capLock) { captured ??= ex; }
                                stop = true;
                                break;
                            }
                        }
                        // occasional tiny pause to avoid starving other threads but keep pressure high
                        if ((iter & 0x7) == 0)
                            Thread.Sleep(rnd.Next(0, 2));
                    }
                }
                catch (Exception ex)
                {
                    lock (capLock) { captured ??= ex; }
                    stop = true;
                }
                finally
                {
                    stop = true;
                }
            })
            { IsBackground = true };
            committerThreads.Add(committerThread);
        }

        // start all threads at once
        tickerThread.Start();
        foreach (var ct in committerThreads) ct.Start();
        Thread.Sleep(50); // let threads initialize
        startSignal.Set();

        // join with a larger timeout to give repro time
        var maxWaitMs = 15000;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < maxWaitMs && !stop)
            Thread.Sleep(10);

        stop = true;

        // join threads
        tickerThread.Join(2000);
        foreach (var ct in committerThreads) ct.Join(2000);

        // optional extra GC pressure to provoke resource-related races when debugging locally
        // GC.Collect();
        // GC.WaitForPendingFinalizers();

        if (captured is not null)
        {
            // If the known "Migrant has no home" appears, fail with details (important diagnostic)
            if (captured.Message != null && captured.Message.Contains("Migrant has no home", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail($"Reproducer detected 'Migrant has no home' exception: {captured}");
            }
            else
            {
                // Other exceptions should also fail the test so they can be investigated
                Assert.Fail($"Reproducer threw an unexpected exception: {captured}");
            }
        }

        Console.WriteLine("✓ PASSED (no migrant exception after racing)");
    }

    [TestMethod]
    public void ProxyVacateRaceTest()
    {
        Console.Write("  Proxy/vacate race targeted reproducer... ");

        var lattice = new TickableSpatialLattice();
        var root = (TickableOctetParentNode)lattice.GetRootNode();

        // Choose two adjacent sibling leaves (0 and 1 differ by the low bit)
        var sourceLeaf = (TickableVenueLeafNode)root.Children[0];
        var targetLeaf = (TickableVenueLeafNode)root.Children[1];

        // 1) Insert 63 occupants into the source leaf
        var occupants = new List<TickableSpatialObject>();
        var basePos = sourceLeaf.Bounds.Mid;
        for (int i = 0; i < 63; i++)
        {
            var o = new TickableSpatialObject(new LongVector3(basePos.X + i, basePos.Y, basePos.Z));
            o.RegisterForTicks();
            occupants.Add(o);
        }
        lattice.Insert(occupants.Cast<ISpatialObject>().ToList());

        // 2) Insert a mover into the same source leaf
        var mover = new TickableSpatialObject(sourceLeaf.Bounds.Mid);
        mover.Accelerate(new IntVector3(1000, 0, 0));
        lattice.Insert(mover);
        mover.RegisterForTicks();

        // Coordination primitives
        var proxyCreated = new ManualResetEventSlim(false);
        var allowCommit = new ManualResetEventSlim(false);
        Exception? captured = null;
        object capLock = new();

        // 3) Thread that will perform the Admit for mover -> target and delay Commit
        var moverThread = new Thread(() =>
        {
            try
            {
                // Admit the mover to the target (returns Created with a proxy, but we won't commit yet)
                var admit = root.Admit(mover, targetLeaf.Bounds.Mid);
                if (admit is AdmitResult.Created created && created.Proxy is TickableSpatialObjectProxy proxy)
                {
                    // signal that proxy exists in target leaf
                    proxyCreated.Set();

                    // wait until allowed to commit (this delays the Replace/Vacate step)
                    allowCommit.Wait();

                    // commit the proxy back into the original object
                    proxy.Commit();
                }
            }
            catch (Exception ex)
            {
                lock (capLock) { captured ??= ex; }
            }
        })
        { IsBackground = true };

        // 4) Thread that will tick the target leaf while the proxy is outstanding
        var tickerThread = new Thread(() =>
        {
            try
            {
                // Wait for proxy to be created in target
                proxyCreated.Wait();
                // Run a few ticks on the target leaf to simulate concurrent activity
                for (int i = 0; i < 20 && !proxyCreated.IsSet; i++) { /* noop */ }
                for (int i = 0; i < 50; i++)
                {
                    targetLeaf.Tick(); // tick only the target leaf
                    Thread.SpinWait(50);
                }
            }
            catch (Exception ex)
            {
                lock (capLock) { captured ??= ex; }
            }
        })
        { IsBackground = true };

        // 5) Thread that will insert a fresh object into the target before the proxy commits
        var inserterThread = new Thread(() =>
        {
            try
            {
                // wait for proxy to exist in target
                proxyCreated.Wait();

                // create a new object intended to occupy the target leaf
                var lateObj = new TickableSpatialObject(targetLeaf.Bounds.Mid + new LongVector3(1, 0, 0));
                lateObj.Accelerate(new IntVector3(0, 0, 0));
                // Use root.Admit so the lattice resolves insertion as normal (and commit immediately if created)
                var admit = root.Admit(lateObj, lateObj.LocalPosition);
                if (admit is AdmitResult.Created created && created.Proxy is TickableSpatialObjectProxy proxy)
                {
                    created.Proxy.Commit();
                }
            }
            catch (Exception ex)
            {
                lock (capLock) { captured ??= ex; }
            }
        })
        { IsBackground = true };

        // Start threads in order
        moverThread.Start();
        // give mover thread a small moment to reach Admit
        Thread.Sleep(5);
        tickerThread.Start();
        inserterThread.Start();

        // Let inserter run shortly so it likely inserts before we allow commit
        Thread.Sleep(50);

        // Allow mover to finish commit (this is the critical window)
        allowCommit.Set();

        // Wait for threads to finish
        moverThread.Join(2000);
        inserterThread.Join(2000);
        tickerThread.Join(2000);

        if (captured is not null)
        {
            if (captured.Message != null && captured.Message.Contains("Migrant has no home", StringComparison.OrdinalIgnoreCase))
                Assert.Fail($"Reproducer detected 'Migrant has no home' exception: {captured}");
            else
                Assert.Fail($"Reproducer threw unexpected exception: {captured}");
        }

        Console.WriteLine("✓ PASSED (proxy/vacate scenario exercised)");
    }
}
