#if !DIAGNOSTIC
using Common.Core.Sync;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public partial class TickableVenueLeafNode
{
    public partial void Tick()
    {
        List<ITickableObject> tickables;
        using (SlimSyncer s = new(Sync, SlimSyncer.LockMode.Read, "TickableVenueLeaf: Tick"))
        {
            tickables = [.. m_tickableObjects];
        }

        foreach (ITickableObject obj in tickables)
        {
            TickResult? result = obj.Tick();
            if (result.HasValue)
                HandleTickResult(result.Value);
        }
    }
}
#endif
