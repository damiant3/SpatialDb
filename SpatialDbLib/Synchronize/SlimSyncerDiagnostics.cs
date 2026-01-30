using System.Collections.Concurrent;
using System.Diagnostics;
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

    private static readonly ConcurrentDictionary<int, ThreadLogState> _perThread = new();

    private static void Log(EventKind kind, SlimSyncer.LockMode mode, string resource)
    {
        if (!Enabled) return;

        int tid = Environment.CurrentManagedThreadId;
        var state = _perThread.GetOrAdd(tid, _ => new ThreadLogState());

        // first event for this thread
        if (state.Count == 0)
        {
            state.Kind = kind;
            state.Mode = mode;
            state.Resource = resource;
            state.Count = 1;
            PrintFirst(tid, state);
            return;
        }

        // same as previous -> just accumulate
        if (state.Kind == kind &&
            state.Mode == mode &&
            state.Resource == resource)
        {
            state.Count++;
            return;
        }

        // different -> flush previous, start new run
        Flush(tid, state);

        state.Kind = kind;
        state.Mode = mode;
        state.Resource = resource;
        state.Count = 1;
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

        Debug.WriteLine(
            $"{prefix} T{tid} {state.Mode} {state.Resource}");

    }
    private static void Flush(int tid, ThreadLogState state)
    {
        if (state.Count == 0) return;
        if (state.Count == 1)
        {
            PrintFirst(tid, state);
            state.Count = 0;
            return;
        }

        var prefix = state.Kind switch
        {
            EventKind.Enter => "ENTER",
            EventKind.Exit => "EXIT ",
            EventKind.Upgrade => "UPGRD",
            _ => "?????"
        };

        Debug.WriteLine(
            $"{prefix} T{tid} {state.Mode} {state.Resource} ({state.Count})");

        state.Count = 0;
    }

    /// Call this at the end of a test/run to emit the last buffered lines.
    public static void FlushAll()
    {
        foreach (var kv in _perThread)
        {
            Flush(kv.Key, kv.Value);
        }
    }

    public static void OnEnter(ReaderWriterLockSlim @lock, string resourceName, SlimSyncer.LockMode mode)
            => Log(EventKind.Enter, mode, resourceName);

    public static void OnExit(ReaderWriterLockSlim @lock, string resourceName, SlimSyncer.LockMode mode)
        => Log(EventKind.Exit, mode, resourceName);

    public static void OnUpgrade(ReaderWriterLockSlim @lock, string resourceName)
        => Log(EventKind.Upgrade, SlimSyncer.LockMode.Write, resourceName);
}


//public static class SlimSyncerDiagnostics
//{
//    // Toggle at runtime
//    public static bool Enabled { get; set; } = false;

//    // Stub hooks. Replace bodies with real logging/analysis as needed.
//    public static void OnEnter(ReaderWriterLockSlim @lock, string resourceName, SlimSyncer.LockMode mode)
//    {
//        if (!Enabled) return;
//        // e.g. record thread id, timestamp, mode, resourceName
//        // Thread.CurrentThread.ManagedThreadId
//    }

//    public static void OnExit(ReaderWriterLockSlim @lock, string resourceName, SlimSyncer.LockMode mode)
//    {
//        if (!Enabled) return;
//        // e.g. clear tracking record for this lock/thread
//    }

//    public static void OnUpgrade(ReaderWriterLockSlim @lock, string resourceName)
//    {
//        if (!Enabled) return;
//        // e.g. record write upgrade attempt/acquire
//    }
//}

//public class SlimSyncer : IDisposable
//{
//    public enum LockMode
//    {
//        Read,
//        Write,
//        UpgradableRead,
//    }

//    private readonly ReaderWriterLockSlim m_lock;
//    private readonly LockMode m_mode;
//    private readonly bool m_isNoOp;
//    private bool m_writeUpgraded;
//    private bool m_disposed;
//    private readonly bool m_trackLocks = true;
//    private readonly string m_resourceName;

//    public SlimSyncer(ReaderWriterLockSlim @lock, LockMode mode, string resourceName)
//    {
//        m_lock = @lock;
//        m_mode = mode;

//        m_resourceName = resourceName ?? m_lock.GetHashCode().ToString();

//        m_isNoOp = m_lock.IsWriteLockHeld
//            || (mode == LockMode.UpgradableRead && m_lock.IsUpgradeableReadLockHeld)
//            || (mode == LockMode.Read && (m_lock.IsReadLockHeld || m_lock.IsUpgradeableReadLockHeld));

//        if (m_isNoOp)
//            return;

//        switch (mode)
//        {
//            case LockMode.Read:
//                m_lock.EnterReadLock();
//                break;

//            case LockMode.Write:
//                m_lock.EnterWriteLock();
//                break;

//            case LockMode.UpgradableRead:
//                m_lock.EnterUpgradeableReadLock();
//                break;

//            default:
//                throw new InvalidOperationException("Unknown SyncMode");
//        }
//    }
//    public void UpgradeToWriteLock()
//    {
//        if (m_isNoOp)
//            return;

//        if (m_mode != LockMode.UpgradableRead)
//            return;

//        if (m_writeUpgraded)
//            return;

//        m_lock.EnterWriteLock();
//        m_writeUpgraded = true;
//    }

//    public void Dispose()
//    {
//        if (m_disposed) return;
//        m_disposed = true;

//        if (m_isNoOp)
//            return;

//        if (m_writeUpgraded)
//        {
//            m_lock.ExitWriteLock();
//        }

//        switch (m_mode)
//        {
//            case LockMode.Write:
//                m_lock.ExitWriteLock();
//                break;

//            case LockMode.UpgradableRead:
//                m_lock.ExitUpgradeableReadLock();
//                break;

//            case LockMode.Read:
//                m_lock.ExitReadLock();
//                break;
//        }

//        GC.SuppressFinalize(this);
//    }
//}

//public class MultiObjectScope<T>(List<T> objects, List<SlimSyncer> locks)
//    : IDisposable
//{
//    readonly List<SlimSyncer> m_locks = locks;
//    bool m_disposed;
//    public List<T> Objects { get; } = objects;

//    public void Dispose()
//    {
//        if (m_disposed) return;
//        m_disposed = true;
//        foreach (var l in m_locks.Reverse<SlimSyncer>())
//        {
//            try { l.Dispose(); } catch { }
//        }
//        GC.SuppressFinalize(this);
//    }
//}

