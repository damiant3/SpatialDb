namespace SpatialDbLib.Lattice;

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

    public SlimSyncer(ReaderWriterLockSlim @lock, LockMode mode)
    {
        m_lock = @lock;
        m_mode = mode;

        m_isNoOp = m_lock.IsWriteLockHeld
            || (mode == LockMode.UpgradableRead && m_lock.IsUpgradeableReadLockHeld)
            || (mode == LockMode.Read && (m_lock.IsReadLockHeld || m_lock.IsUpgradeableReadLockHeld));

        if (m_isNoOp)
            return;

        switch (mode)
        {
            case LockMode.Read:
                m_lock.EnterReadLock();
                break;

            case LockMode.Write:
                m_lock.EnterWriteLock();
                break;

            case LockMode.UpgradableRead:
                m_lock.EnterUpgradeableReadLock();
                break;

            default:
                throw new InvalidOperationException("Unknown SyncMode");
        }
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
    }

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        if (m_isNoOp)
            return;

        if (m_writeUpgraded)
        {
            m_lock.ExitWriteLock();
        }

        switch (m_mode)
        {
            case LockMode.Write:
                m_lock.ExitWriteLock();
                break;

            case LockMode.UpgradableRead:
                m_lock.ExitUpgradeableReadLock();
                break;

            case LockMode.Read:
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
        foreach (var l in m_locks.Reverse<SlimSyncer>())
        {
            try { l.Dispose(); } catch { }
        }
        GC.SuppressFinalize(this);
    }
}

