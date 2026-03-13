////////////////////////////
namespace Common.Core.Sync;

public interface ISync
{
    ReaderWriterLockSlim Sync { get; }
}
