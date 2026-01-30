using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
///////////////////////////////////
namespace SpatialDbLib.Synchronize;

public static class SlimSyncerDiagnostics
{
    public static bool Enabled { get; set; } = false;

    private enum EventKind
    {
        Enter,
        Exit,
        Upgrade
    }

    private sealed class ThreadLogState
    {
        public EventKind Kind;
        public SlimSyncer.LockMode Mode;
        public string Resource = "";
        public int Count;
    }

    private sealed class LockEvent
    {
        public string ResourceName = "";
        public SlimSyncer.LockMode Mode;
        public EventKind Kind;
        public long TimestampTicks;
        public int Count = 1;
    }

    // For per-thread compression & last-seen tracking
    private static readonly ConcurrentDictionary<int, ThreadLogState> _perThread = new();

    // Optional historical log for later analysis / debugging
    private static readonly ConcurrentDictionary<int, List<LockEvent>> _threadHistory = new();

    private static void Log(EventKind kind, SlimSyncer.LockMode mode, string resource)
    {
        if (!Enabled) return;

        int tid = Environment.CurrentManagedThreadId;

        // --- compress repeated events per-thread ---
        var state = _perThread.GetOrAdd(tid, _ => new ThreadLogState());

        if (state.Count == 0)
        {
            state.Kind = kind;
            state.Mode = mode;
            state.Resource = resource;
            state.Count = 1;
            PrintFirst(tid, state);
        }
        else if (state.Kind == kind && state.Mode == mode && state.Resource == resource)
        {
            state.Count++;
        }
        else
        {
            Flush(tid, state);
            state.Kind = kind;
            state.Mode = mode;
            state.Resource = resource;
            state.Count = 1;
            PrintFirst(tid, state);
        }

        var histList = _threadHistory.GetOrAdd(tid, _ => new List<LockEvent>());
        lock (histList)
        {
            var last = histList.LastOrDefault();
            if (last != null &&
                last.Kind == kind &&
                last.Mode == mode &&
                last.ResourceName == resource)
            {
                last.Count++;
            }
            else
            {
                histList.Add(new LockEvent
                {
                    Kind = kind,
                    Mode = mode,
                    ResourceName = resource,
                    TimestampTicks = Stopwatch.GetTimestamp(),
                    Count = 1
                });
            }
        }
    }

    private static void PrintFirst(int tid, ThreadLogState state)
    {
        if (state.Count != 1) return;

        var prefix = state.Kind switch
        {
            EventKind.Enter => "ENTER",
            EventKind.Exit => "EXIT ",
            EventKind.Upgrade => "UPGRD",
            _ => "?????"
        };

        Debug.WriteLine($"{prefix} T{tid} {state.Mode} {state.Resource}");
    }

    private static void Flush(int tid, ThreadLogState state)
    {
        if (state.Count < 2) return;

        var prefix = state.Kind switch
        {
            EventKind.Enter => "ENTER",
            EventKind.Exit => "EXIT ",
            EventKind.Upgrade => "UPGRD",
            _ => "?????"
        };

        var suffix = state.Count > 1 ? $" ({state.Count})" : "";
        Debug.WriteLine($"{prefix} T{tid} {state.Mode} {state.Resource}{suffix}");
        state.Count = 0;
    }

    public static void FlushAll()
    {
        foreach (var kv in _perThread)
            Flush(kv.Key, kv.Value);
    }

   public static string DumpHistory()
    {
        var sb = new StringBuilder();
        foreach (var kv in _threadHistory)
        {
            int tid = kv.Key;
            var list = kv.Value;
            lock (list)
            {
                foreach (var ev in list)
                {
                    sb.AppendLine(
                        $"T{tid} {ev.Kind} {ev.Mode} {ev.ResourceName} ({ev.Count}) @ {ev.TimestampTicks}"
                    );
                }
            }
        }
        return sb.ToString();
    }


    public static void OnEnter(ReaderWriterLockSlim @lock, string resourceName, SlimSyncer.LockMode mode)
        => Log(EventKind.Enter, mode, resourceName);

    public static void OnExit(ReaderWriterLockSlim @lock, string resourceName, SlimSyncer.LockMode mode)
        => Log(EventKind.Exit, mode, resourceName);

    public static void OnUpgrade(ReaderWriterLockSlim @lock, string resourceName)
        => Log(EventKind.Upgrade, SlimSyncer.LockMode.Write, resourceName);
}