using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
////////////////////////////
namespace Common.Core.Sync;

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

    private static readonly ConcurrentDictionary<int, ThreadLogState> s_perThread = new();
    private static readonly ConcurrentDictionary<int, List<LockEvent>> s_threadHistory = new();

    private static void Log(EventKind kind, SlimSyncer.LockMode mode, string resource)
    {
        if (!Enabled) return;
        int tid = Environment.CurrentManagedThreadId;
        ThreadLogState state = s_perThread.GetOrAdd(tid, _ => new ThreadLogState());

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
        List<LockEvent> histList = s_threadHistory.GetOrAdd(tid, _ => new List<LockEvent>());
        lock (histList)
        {
            LockEvent? last = histList.Count > 0 ? histList[histList.Count - 1] : null;
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

        string prefix = state.Kind switch
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

        string prefix = state.Kind switch
        {
            EventKind.Enter => "ENTER",
            EventKind.Exit => "EXIT ",
            EventKind.Upgrade => "UPGRD",
            _ => "?????"
        };

        string suffix = state.Count > 1 ? $" ({state.Count})" : "";
        Debug.WriteLine($"{prefix} T{tid} {state.Mode} {state.Resource}{suffix}");
        state.Count = 0;
    }
    public static void FlushAll()
    {
        foreach (KeyValuePair<int, ThreadLogState> kv in s_perThread)
            Flush(kv.Key, kv.Value);
    }
    public static string DumpHistory()
    {
        StringBuilder sb = new();
        foreach (KeyValuePair<int, List<LockEvent>> kv in s_threadHistory)
        {
            int tid = kv.Key;
            List<LockEvent> list = kv.Value;
            lock (list)
            {
                foreach (LockEvent ev in list)
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
