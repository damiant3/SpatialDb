using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using SpatialDbLib.Lattice;
using SpatialDbLib.Synchronize;
//////////////////////////
namespace SpatialDbLibTest
{
    public class TestSpatialLattice
        : SpatialLattice,
          ITestCatalog
    {

        public void TestBulkInsert(IEnumerable<SpatialObject> objs)
        {
            AdmitResult ret;
            try
            {
                ret = Insert(objs);
                if (ret is not AdmitResult.BulkCreated)
                {
                    Debugger.Break();
                }
            }
            catch (Exception ex)
            {
                Exceptions[Guid.NewGuid()] = ex;
                Debugger.Break();
                throw;
            }
        }
        public void TestInsert(SpatialObject obj)
        {
            AdmitResult ret;
            try
            {
                ret = Insert(obj);
                if (ret is not AdmitResult.Created)
                {
                    Debugger.Break();
                }
            }
            catch (Exception ex)
            {
                Exceptions[obj.Guid] = ex;
                Debugger.Break();
                throw;
            }
        }

        public void TestRemove(SpatialObject obj)
        {
            try
            {
                Remove(obj);
            }
            catch (Exception ex)
            {
                Exceptions[obj.Guid] = ex;
                Debugger.Break();
                throw;
            }
        }

        readonly ConcurrentDictionary<Guid, Exception> Exceptions = [];
        public string GenerateExceptionReport()
        {
            if (Exceptions.IsEmpty)
                return "No exceptions tracked.";
            var sb = new StringBuilder();
            sb.AppendLine($"Exceptions tracked: {Exceptions.Count}");
            foreach (var kvp in Exceptions)
                sb.AppendLine($"Object {kvp.Key}: {kvp.Value}");
            return sb.ToString();
        }

        public void Cleanup() => CreateChildLeafNodes();
    }

    public class TestConcurrentDictionary
        : ConcurrentDictionary<Guid, SpatialObject>,
          ITestCatalog
    {
        public void TestBulkInsert(IEnumerable<SpatialObject> objs)
        {
            SpatialObject? obj = null;
            try
            {
                for(var i = 0; i < objs.Count(); i++)
                {
                    obj = objs.ElementAt(i);
                    this[obj.Guid] = obj;
                }
            }
            catch (Exception ex)
            {
                if(obj != null)
                    Exceptions[obj.Guid] = ex;
                Debugger.Break();
                throw;
            }
        }

        public void TestInsert(SpatialObject obj)
        {
            try
            {
                this[obj.Guid] = obj;
            }
            catch (Exception ex)
            {
                Exceptions[obj.Guid] = ex;
                Debugger.Break();
                throw;
            }
        }

        public void TestRemove(SpatialObject obj)
        {
            try
            {
                TryRemove(obj.Guid, out _);
            }
            catch (Exception ex)
            {
                Exceptions[obj.Guid] = ex;
                Debugger.Break();
                throw;
            }
        }

        ConcurrentDictionary<Guid, Exception> Exceptions = [];
        public string GenerateExceptionReport()
        {
            if (Exceptions.IsEmpty)
                return "No exceptions tracked.";
            var sb = new StringBuilder();
            sb.AppendLine($"Exceptions tracked: {Exceptions.Count}");
            foreach (var kvp in Exceptions)
            {
                sb.AppendLine($"Object {kvp.Key}: {kvp.Value}");
            }
            return sb.ToString();
        }

        public void Cleanup() => Clear();
    }

    public class LatticeParallelTest(int iterations = 1, int taskCount = 1, int batchSize = 1)
        : ParallelContainerTestDiagnostic<TestSpatialLattice>(iterations, taskCount, batchSize) { };

    public class ConcurrentDictionaryParallelTest(int iterations = 1, int taskCount = 1, int batchSize = 1)
    : ParallelContainerTestDiagnostic<TestConcurrentDictionary>(iterations, taskCount, batchSize) { };

    public class ParallelContainerTestDiagnostic<T>(int iterations, int taskCount = 1, int batchSize = 1)
        : ContainerTestDiagnostics<T>(iterations)
        where T :  class,
                   ITestCatalog,
                   new ()
    {
        public int TaskCount = taskCount;
        public int BatchSize = batchSize;

        int insertHeadStart = 5;
        public void InsertOrRemoveRandomItems()
        {
            if (insertHeadStart-- > 0)
            {
                InsertRandomItems();
            }
            else
            {
                if (insertHeadStart % 2 == 0)
                    InsertRandomItems();
                else
                    RemoveRandomItems();
            }
        }

        public void InsertBulkRandomItems()
        {
            var tasks = new List<Task>();

            SlimSyncerDiagnostics.Enabled = true;
            var objsToInsert = new Dictionary<int, List<SpatialObject>>();
            for (int i = 0; i < TaskCount; i++)
            {
                objsToInsert.Add(i, []);
                for (int j = 0; j < BatchSize; j++)
                {
                    objsToInsert[i].Add(new SpatialObject([new LongVector3(
                        FastRandom.NextInt(-SpaceRange, SpaceRange),
                        FastRandom.NextInt(-SpaceRange, SpaceRange),
                        FastRandom.NextInt(-SpaceRange, SpaceRange)
                    )]));
                }
            }
            Task monitor;  // we need to find a way to stop this task cleanly from the outside.
            var cts = new CancellationTokenSource();


            if (SlimSyncerDiagnostics.Enabled)
                monitor = Task.Run(() =>
                {
                    var sw = Stopwatch.StartNew();

                    while (sw.ElapsedMilliseconds < 2000)
                        Thread.Sleep(2000);

                    while (true)
                    {
                        var snapshot = tasks.ToArray();
                        if (snapshot.Length == TaskCount && snapshot.All(t => t.IsCompleted))
                            break;

                        Debug.WriteLine(LockTracker.DumpHeldLocks());
                        Thread.Sleep(1000);
                        if(cts.Token.IsCancellationRequested)
                            break;
                    }
                }, cts.Token);

            var swInsert = Stopwatch.StartNew();
            for (int j = 0; j < TaskCount; j++)
            {
                var k = j; // Capture loop variable, i think necessary, but not sure.
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        Container!.TestBulkInsert(objsToInsert[k]);
                        Interlocked.Add(ref Inserts, BatchSize);
                    }
                    catch
                    {
                        Interlocked.Add(ref FailedInserts, BatchSize);
                    }
                }));
            }

            Task.WaitAll([.. tasks]);
            // stop the monitor
            cts.Cancel();
            SlimSyncerDiagnostics.FlushAll();
            swInsert.Stop();
            TotalInsertTicks += swInsert.ElapsedTicks;
        }

        public void InsertRandomItems()
        {
            var tasks = new List<Task>();
            SlimSyncerDiagnostics.Enabled = true;
            var swInsert = Stopwatch.StartNew();
            for (int j = 0; j < TaskCount; j++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var pos = new LongVector3(
                            FastRandom.NextInt(-SpaceRange, SpaceRange),
                            FastRandom.NextInt(-SpaceRange, SpaceRange),
                            FastRandom.NextInt(-SpaceRange, SpaceRange)
                        );

                        var obj = new SpatialObject([pos]);
                        Container!.TestInsert(obj);
                        InsertedObjects![obj.Guid] = obj;

                        Interlocked.Increment(ref Inserts);
                    }
                    catch
                    {
                        Interlocked.Increment(ref FailedInserts);
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
            swInsert.Stop();
            TotalInsertTicks += swInsert.ElapsedTicks;
        }

        public void RemoveRandomItems()
        {
            if(InsertedObjects!.Count < 8) return;

            int removeCount = Random.Next(1, (int)Math.Log2(InsertedObjects!.Count));
            var tasks = new List<Task>();
            var swRemove = Stopwatch.StartNew();
            for (int r = 0; r < removeCount; r++)
            {
                tasks.Add(Task.Run(() =>
                {
                    if (InsertedObjects.TryTakeRandomFast(out var kvp))
                    {
                        var objToRemove = kvp.Value;
                        try
                        {
                            Container!.TestRemove(objToRemove);
                            Interlocked.Increment(ref Removes);
                        }
                        catch
                        {
                            Interlocked.Increment(ref FailedRemoves);
                        }
                    }
                }));
            }
            Task.WaitAll([.. tasks]);

            swRemove.Stop();
            TotalRemoveTicks = swRemove.ElapsedTicks;
        }
    }


}