///////////////////////////////////
namespace SpatialDbLib.Synchronize;

internal interface ISync
{
    ReaderWriterLockSlim Sync { get; }
}

