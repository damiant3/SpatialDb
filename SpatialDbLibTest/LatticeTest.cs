using SpatialDbLib.Lattice;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace SpatialDbLibTest
{
    public class TestSpatialLattice
        : SpatialLattice,
          ITestCatalog
    {
        public void TestInsert(SpatialObject obj)
        {
            AdmitResult ret;
            try
            {
                ret = base.Insert(obj);
                Assert.IsNotNull(ret);
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
                base.Remove(obj);
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
                sb.AppendLine($"Object {kvp.Key}: {kvp.Value}");
            return sb.ToString();
        }

        public void Cleanup() => CreateChildLeafNodes();
    }

    public class TestConcurrentDictionary
        : ConcurrentDictionary<Guid, SpatialObject>,
          ITestCatalog
    {
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

    public class LatticeParallelTest(int iterations = 1, int taskCount = 1)
        : ParallelContainerTestDiagnostic<TestSpatialLattice>(iterations, taskCount) { };

    public class ConcurrentDictionaryParallelTest(int iterations = 1, int taskCount = 1)
    : ParallelContainerTestDiagnostic<TestConcurrentDictionary>(iterations, taskCount) { };

    public class ParallelContainerTestDiagnostic<T>(int iterations, int taskCount = 1)
        : ContainerTestDiagnostics<T>(iterations)
        where T :  class,
                   ITestCatalog,
                   new ()
    {
        public int TaskCount = taskCount;

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

        public void InsertRandomItems()
        {
            var tasks = new List<Task>();

            var swInsert = Stopwatch.StartNew();
            for (int j = 0; j < TaskCount; j++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var pos = new LongVector3(
                            Random.Next(-SpaceRange, SpaceRange),
                            Random.Next(-SpaceRange, SpaceRange),
                            Random.Next(-SpaceRange, SpaceRange)
                        );

                        var obj = new SpatialObject(pos);
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

            int removeCount = Random.Next(1, (int)Math.Log2( InsertedObjects!.Count));
            var tasks = new List<Task>();
            var swRemove = Stopwatch.StartNew();
            for (int r = 0; r < removeCount; r++)
            {
                tasks.Add(Task.Run(() =>
                {
                    if (InsertedObjects.TryTakeRandom(out var kvp))
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
            Task.WaitAll(tasks.ToArray());

            swRemove.Stop();
            TotalRemoveTicks = swRemove.ElapsedTicks;
        }
    }

    public class ContainerTestDiagnostics<T>
        where T : class, ITestCatalog, new()
    {
        public Random Random = Random.Shared;
        public int Iterations;
        public int SpaceRange = 1000;
        public T? Container;
        public ConcurrentDictionary<Guid, SpatialObject>? InsertedObjects = [];

        public int Inserts;
        public int FailedInserts;
        public int Removes;
        public int FailedRemoves;

        public long MemBefore;
        public long MemAfter;
        public long MemAfterCleanup;

        public long TotalTestTicks;
        public long TotalInsertTicks;
        public long TotalRemoveTicks;
        public Stopwatch TotalTestsWatch = new();

        public ContainerTestDiagnostics(int iterations = 1)
        {
            Iterations = iterations;
            Initialize();
        }

        bool  needsCleanup = false;
        public void Initialize()
        {
            if (needsCleanup) return;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            MemBefore = GC.GetTotalMemory(true);
            TotalTestsWatch.Start();
            Container = new T();
            needsCleanup = true;
        }
        public void CleanupAndGatherDiagnostics()
        {
            TotalTestsWatch.Stop();
            TotalTestTicks = TotalTestsWatch.ElapsedTicks;

            if (InsertedObjects != null)
            {
                InsertedObjects.Clear();
                InsertedObjects = null!;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            MemAfter = GC.GetTotalMemory(true);
            Container!.Cleanup();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            MemAfterCleanup = GC.GetTotalMemory(true);
        }

        public string InsertTimePerOperation => Inserts > 0
            ? (TimeSpan.FromTicks(TotalInsertTicks).TotalMilliseconds / Inserts).ToString("F4") + " ms/op"
            : "n/a";
        public string InsertTimeTotal => TimeSpan.FromTicks(TotalInsertTicks).TotalSeconds.ToString("F3") + " s";
        public string MemoryUsageAfter => FormatBytes(MemAfter);
        public string MemoryUsageAfterCleanup => FormatBytes(MemAfterCleanup);

        public string MemoryUsageBefore => FormatBytes(MemBefore);
        public string MemoryUsageDelta => FormatBytes(MemAfter - MemBefore);
        public string MemoryUsageCleanupDelta => FormatBytes(MemAfter - MemAfterCleanup);
        public string RemoveTimePerOperation => Removes > 0
                ? (TimeSpan.FromTicks(TotalRemoveTicks).TotalMilliseconds / Removes).ToString("F4") + " ms/op"
                : "n/a";
        public string RemoveTimeTotal => TimeSpan.FromTicks(TotalRemoveTicks).TotalSeconds.ToString("F3") + " s";
        public string TestTimeTotal => TimeSpan.FromTicks(TotalTestTicks).TotalSeconds.ToString("F3") + " s";
        public static string FormatBytes(long bytes)
        {
            const long KB = 1024;
            const long MB = 1024 * KB;
            const long GB = 1024 * MB;

            if (bytes >= GB)
                return $"{(bytes / (double)GB):F2} GB";
            if (bytes >= MB)
                return $"{(bytes / (double)MB):F2} MB";
            if (bytes >= KB)
                return $"{(bytes / (double)KB):F2} KB";

            return $"{bytes} B";
        }

        public virtual string GenerateReportString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Test complete after {Iterations} iterations.");
            sb.Append($"Inserts: {Inserts} (failed: {FailedInserts}), ");
            sb.AppendLine($"Removes: {Removes} (failed: {FailedRemoves})");
            sb.AppendLine($"Total Time: {TestTimeTotal}");
            sb.AppendLine($"Insert time total: {InsertTimeTotal}, {InsertTimePerOperation}");
            sb.AppendLine($"Remove time total: {RemoveTimeTotal}, {RemoveTimePerOperation}");
            sb.AppendLine($"Memory Before: {MemoryUsageBefore}, After: {MemoryUsageAfter}, Container Memory: {MemoryUsageDelta}");
            sb.AppendLine($"Memory Before Cleanup: {MemoryUsageAfter}, After Cleanup: {MemoryUsageAfterCleanup}, Container Memory Disposed: {MemoryUsageCleanupDelta}");
            sb.AppendLine(Container!.GenerateExceptionReport());
            return sb.ToString();
        }
    }


}