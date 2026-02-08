///////////////////////////////////
namespace SpatialDbLib.Synchronize;

public interface ISync
{
    ReaderWriterLockSlim Sync { get; }
}

