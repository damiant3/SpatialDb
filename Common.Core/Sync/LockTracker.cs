using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
////////////////////////////
namespace Common.Core.Sync;

public static class LockTracker
{
    private class LockInfo
    {
        public int ThreadId;
        public DateTime AcquiredAt;
        public string ResourceName = "";
    }

    private static readonly ConcurrentDictionary<ReaderWriterLockSlim, LockInfo> s_heldLocks = new();

    public static void TrackLockEnter(ReaderWriterLockSlim sync, string resourceName)
    {
        int threadId = Environment.CurrentManagedThreadId;
        bool acquired = false;
        while (!acquired)
        {
            acquired = Monitor.TryEnter(sync, TimeSpan.FromSeconds(5));
            if (!acquired)
                Debug.WriteLine($"Thread {threadId} waiting >5s on {resourceName}...");
        }

        s_heldLocks[sync] = new LockInfo { ThreadId = threadId, AcquiredAt = DateTime.UtcNow, ResourceName = resourceName };
    }

    public static void TrackLockExit(ReaderWriterLockSlim sync)
    {
        s_heldLocks.TryRemove(sync, out _);
        Monitor.Exit(sync);
    }

    public static string DumpHeldLocks()
    {
        if (s_heldLocks.IsEmpty)
            return "";
        StringBuilder sb = new();
        sb.AppendLine("=== Currently held locks ===");
        foreach (KeyValuePair<ReaderWriterLockSlim, LockInfo> kv in s_heldLocks)
            sb.AppendLine($"Thread {kv.Value.ThreadId} holds {kv.Value.ResourceName} since {kv.Value.AcquiredAt:HH:mm:ss.fff}");
        sb.AppendLine("===========================");
        return sb.ToString();
    }
}
