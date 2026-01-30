///////////////////////////////////
namespace SpatialDbLib.Synchronize;

public static class SpatialLatticeOptions
{
    // Enable lock tracking for debug/deadlock detection
    public static bool TrackLocks { get; set; } = false;
}
