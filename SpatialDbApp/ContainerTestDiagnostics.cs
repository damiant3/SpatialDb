using SpatialDbLib.Lattice;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace SpatialDbLibTest;

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