#if !DIAGNOSTIC
using SpatialDbLib.Synchronize;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public partial class TickableVenueLeafNode
{
    // Production build: Tick implementation without diagnostic hooks.
    public partial void Tick()
    {
        List<ITickableObject> tickables;
        using (var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "TickableVenueLeaf: Tick"))
        {
            tickables = [.. m_tickableObjects];
        }

        foreach (var obj in tickables)
        {
            var result = obj.Tick();
            if (result.HasValue)
                HandleTickResult(result.Value);
        }
    }
}
#endif