#if !DIAGNOSTIC
using SpatialDbLib.Synchronize;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public partial class TickableVenueLeafNode
{
    // Production build: Tick implementation without diagnostic hooks.
    public partial void Tick()
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.UpgradableRead, "tick");
        foreach (var obj in m_tickableObjects.ToList())
        {
            var result = obj.Tick();
            if (result.HasValue)
                HandleTickResult(result.Value);
        }
    }
}
#endif