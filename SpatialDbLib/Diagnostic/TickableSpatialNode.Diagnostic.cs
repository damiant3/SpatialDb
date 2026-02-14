#if DIAGNOSTIC
using SpatialDbLib.Lattice;
using SpatialDbLib.Synchronize;
using SpatialDbLib.Diagnostic;
using System.Collections.Generic;
using System.Threading;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public partial class TickableVenueLeafNode
{
    public static int CurrentTickerThreadId { get; set; }
    public partial void Tick()
    {
        //diagnostic
        HookSet.Instance["SignalTickerStart"].Set();
        HookSet.Instance["WaitTickerProceed"].Wait();
        CurrentTickerThreadId = Environment.CurrentManagedThreadId;

        if (OctetParentNode.SleepThreadId == Environment.CurrentManagedThreadId)
        {
            if (OctetParentNode.UseYield) Thread.Yield();
            else Thread.Sleep(System.Math.Max(1, OctetParentNode.SleepMs));
        }

        // prod
        List<ITickableObject> tickables;
        using (var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "tick"))
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