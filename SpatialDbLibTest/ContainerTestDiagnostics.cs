using SpatialDbLib.Lattice;
using SpatialDbLib.Synchronize;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace SpatialDbLibTest;

public class ContainerTestDiagnostics<T>
    where T : class, ITestCatalog, new()
{
    public Random Random = Random.Shared;
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

    public ContainerTestDiagnostics(bool benchmarkTest)
    {
        SlimSyncerDiagnostics.Enabled = !benchmarkTest;
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
        sb.Append($"Container Type: {typeof(T).Name}");
        //sb.Append($" Thread Count: {TaskCount}");
        //sb.AppendLine($" Batch Size: {BatchSize}");

        if (Inserts > 0) sb.Append($"Inserts: {Inserts}");
        if (FailedInserts > 0) sb.Append($" (failed: {FailedInserts})");
        if (Inserts > 0 || FailedInserts > 0) sb.AppendLine();
        if (Removes > 0) sb.Append($"Removes: {Removes}");
        if (FailedRemoves > 0) sb.Append($" (failed: {FailedRemoves})");
        if (Removes > 0 || FailedRemoves > 0) sb.AppendLine();
        sb.Append($"Test Time: {TestTimeTotal}");
        if (Inserts > 0) sb.Append($" Insert time: {InsertTimeTotal}, {InsertTimePerOperation}");
        if (Removes > 0) sb.Append($" Remove time: {RemoveTimeTotal}, {RemoveTimePerOperation}");
        sb.AppendLine();
        sb.AppendLine($"Memory Before: {MemoryUsageBefore}, After: {MemoryUsageAfter}, Container Memory: {MemoryUsageDelta}");
        sb.AppendLine($"Memory Before Cleanup: {MemoryUsageAfter}, After Cleanup: {MemoryUsageAfterCleanup}, Container Memory Disposed: {MemoryUsageCleanupDelta}");
        sb.AppendLine(Container!.GenerateExceptionReport());
        return sb.ToString();
    }
}

public class LatticeParallelTest(int taskCount = 1, int batchSize = 1, bool benchmarkTest = false)
    : ParallelContainerTestDiagnostic<TestSpatialLattice>(taskCount, batchSize, benchmarkTest)
{ };

public class ConcurrentDictionaryParallelTest(int taskCount = 1, int batchSize = 1, bool benchmarkTest = false)
    : ParallelContainerTestDiagnostic<TestConcurrentDictionary>(taskCount, batchSize, benchmarkTest)
{ };

public class ParallelContainerTestDiagnostic<T>(int taskCount = 1, int batchSize = 1, bool benchmarkTest = false)
    : ContainerTestDiagnostics<T>(benchmarkTest)
    where T : class,
               ITestCatalog,
               new()
{
    public int TaskCount = taskCount;
    public int BatchSize = batchSize;

    public void InsertBulkItems(Dictionary<int, List<SpatialObject>> objsToInsert)
    {
        var tasks = new List<Task>();
        for (int j = 0; j < TaskCount; j++)
        {
            var k = j;  // capture value for closure below
            tasks.Add(new Task(() =>
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

        var cts = StartDiagnosticMonitorTask(tasks);
        var swInsert = Stopwatch.StartNew();
        foreach (var task in tasks)
        {
            task.Start();
        }
        Task.WaitAll([.. tasks]);
        swInsert.Stop();
        cts.Cancel();
        SlimSyncerDiagnostics.FlushAll();
        TotalInsertTicks += swInsert.ElapsedTicks;
    }



    private CancellationTokenSource StartDiagnosticMonitorTask(List<Task> tasks)
    {
        var cts = new CancellationTokenSource();
        Task monitor;
        int tasksPassedInCount = tasks.Count;

        if (tasksPassedInCount > 0 && SlimSyncerDiagnostics.Enabled)
            monitor = Task.Run(() =>
            {
                while (true)
                {
                    var snapshot = tasks.ToArray();
                    if (snapshot.Length == TaskCount && snapshot.All(t => t.IsCompleted))
                        break;

                    Debug.WriteLine(LockTracker.DumpHeldLocks());
                    Thread.Sleep(100);
                    if (cts.Token.IsCancellationRequested)
                        break;
                }
            }, cts.Token);
        return cts;
    }

    public void InsertItems(Dictionary<int, List<SpatialObject>> objsToInsert)
    {
        var tasks = new List<Task>();

        var swInsert = Stopwatch.StartNew();

        for (int i = 0; i < TaskCount; i++)
        {
            var k = i;
            tasks.Add(Task.Run(() =>
            {
                foreach (var obj in objsToInsert[k])
                {
                    try
                    {
                        Container!.TestInsert(obj);
                        Interlocked.Increment(ref Inserts);
                    }
                    catch
                    {
                        Interlocked.Increment(ref FailedInserts);
                    }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());
        swInsert.Stop();
        TotalInsertTicks += swInsert.ElapsedTicks;
    }

    //public void InsertRandomItems()
    //{
    //    var tasks = new List<Task>();

    //    for (int i = 0; i < BatchSize; i++)// for singleton inserts, do BatchSize rounds
    //    {
    //        ConcurrentDictionary<int, SpatialObject> objectsPerTask = [];
    //        for (int j = 0; j < TaskCount; j++)
    //        {
    //            objectsPerTask[j] = new SpatialObject([new LongVector3(
    //                FastRandom.NextInt(-SpaceRange, SpaceRange),
    //                FastRandom.NextInt(-SpaceRange, SpaceRange),
    //                FastRandom.NextInt(-SpaceRange, SpaceRange)
    //            )]);
    //        }

    //        var swInsert = Stopwatch.StartNew();
    //        for (int j = 0; j < TaskCount; j++)
    //        {
    //            var k = j; // Capture loop variable, i think necessary, but not sure.
    //            tasks.Add(Task.Run(() =>
    //            {
    //                var obj = objectsPerTask[k];
    //                try
    //                {
    //                    Container!.TestInsert(obj);
    //                    Interlocked.Increment(ref Inserts);
    //                }
    //                catch
    //                {
    //                    Interlocked.Increment(ref FailedInserts);
    //                }
    //            }));
    //        }

    //        Task.WaitAll([.. tasks]);
    //        swInsert.Stop();
    //        TotalInsertTicks += swInsert.ElapsedTicks;
    //        tasks.Clear();
    //    }
    //}

    public void RemoveRandomItems()
    {
        if (InsertedObjects!.Count < 8) return;

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
    public override string GenerateReportString()
    {
        var sb = new StringBuilder();
        sb.Append($"Container Type: {typeof(T).Name}");
        sb.Append($" Thread Count: {TaskCount}");
        sb.AppendLine($" Batch Size: {BatchSize}");

        if (Inserts > 0) sb.Append($"Inserts: {Inserts}");
        if (FailedInserts > 0) sb.Append($" (failed: {FailedInserts})");
        if(Inserts > 0 || FailedInserts > 0) sb.AppendLine();
        if (Removes > 0) sb.Append($"Removes: {Removes}");
        if (FailedRemoves > 0) sb.Append($" (failed: {FailedRemoves})");
        if(Removes > 0 || FailedRemoves > 0) sb.AppendLine();
        sb.Append($"Test Time: {TestTimeTotal}");
        if (Inserts > 0) sb.Append($" Insert time: {InsertTimeTotal}, {InsertTimePerOperation}");
        if (Removes > 0) sb.Append($" Remove time: {RemoveTimeTotal}, {RemoveTimePerOperation}");
        sb.AppendLine();
        sb.AppendLine($"Memory Before: {MemoryUsageBefore}, After: {MemoryUsageAfter}, Container Memory: {MemoryUsageDelta}");
        sb.AppendLine($"Memory Before Cleanup: {MemoryUsageAfter}, After Cleanup: {MemoryUsageAfterCleanup}, Container Memory Disposed: {MemoryUsageCleanupDelta}");
        sb.AppendLine(Container!.GenerateExceptionReport());
        return sb.ToString();
    }
}
