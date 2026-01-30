///////////////////////////////////
namespace SpatialDbLib.Synchronize;

public class SlimSyncer : IDisposable
{
    public enum LockMode
    {
        Read,
        Write,
        UpgradableRead,
    }

    private readonly ReaderWriterLockSlim m_lock;
    private readonly LockMode m_mode;
    private readonly bool m_isNoOp;
    private bool m_writeUpgraded;
    private bool m_disposed;
    private readonly string m_resourceName;

    public SlimSyncer(ReaderWriterLockSlim @lock, LockMode mode, string resourceName)
    {
        m_lock = @lock;
        m_mode = mode;
        m_resourceName = resourceName ?? m_lock.GetHashCode().ToString();

        m_isNoOp =
            m_lock.IsWriteLockHeld
            || (mode == LockMode.UpgradableRead && m_lock.IsUpgradeableReadLockHeld)
            || (mode == LockMode.Read && (m_lock.IsReadLockHeld || m_lock.IsUpgradeableReadLockHeld));

        if (m_isNoOp)
            return;

        switch (mode)
        {
            case LockMode.Read:
                m_lock.EnterReadLock();
                SlimSyncerDiagnostics.OnEnter(m_lock, m_resourceName, mode);
                break;

            case LockMode.Write:
                m_lock.EnterWriteLock();
                SlimSyncerDiagnostics.OnEnter(m_lock, m_resourceName, mode);
                break;

            case LockMode.UpgradableRead:
                m_lock.EnterUpgradeableReadLock();
                SlimSyncerDiagnostics.OnEnter(m_lock, m_resourceName, mode);
                break;

            default:
                throw new InvalidOperationException("Unknown SyncMode");
        }
        if (SpatialLatticeOptions.TrackLocks)
            LockTracker.TrackLockEnter(m_lock, m_resourceName);
    }

    public void UpgradeToWriteLock()
    {
        if (m_isNoOp)
            return;

        if (m_mode != LockMode.UpgradableRead)
            return;

        if (m_writeUpgraded)
            return;

        m_lock.EnterWriteLock();
        m_writeUpgraded = true;
        SlimSyncerDiagnostics.OnUpgrade(m_lock, m_resourceName);
    }

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        if (m_isNoOp)
            return;

        if (m_writeUpgraded)
        {
            SlimSyncerDiagnostics.OnExit(m_lock, m_resourceName, LockMode.Write);
            m_lock.ExitWriteLock();
        }

        switch (m_mode)
        {
            case LockMode.Write:
                SlimSyncerDiagnostics.OnExit(m_lock, m_resourceName, LockMode.Write);
                m_lock.ExitWriteLock();
                break;

            case LockMode.UpgradableRead:
                SlimSyncerDiagnostics.OnExit(m_lock, m_resourceName, LockMode.UpgradableRead);
                m_lock.ExitUpgradeableReadLock();
                break;

            case LockMode.Read:
                SlimSyncerDiagnostics.OnExit(m_lock, m_resourceName, LockMode.Read);
                m_lock.ExitReadLock();
                break;
        }

        GC.SuppressFinalize(this);
    }
}

public class MultiObjectScope<T>(List<T> objects, List<SlimSyncer> locks)
    : IDisposable
{
    readonly List<SlimSyncer> m_locks = locks;
    bool m_disposed;

    public List<T> Objects { get; } = objects;

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        // reverse order release
        for (int i = m_locks.Count - 1; i >= 0; i--)
        {
            try { m_locks[i].Dispose(); }
            catch { }
        }

        GC.SuppressFinalize(this);
    }
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

