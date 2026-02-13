#if DIAGNOSTIC
using SpatialDbLib.Lattice;
using SpatialDbLib.Synchronize;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public partial class TickableVenueLeafNode
{
    // Diagnostic build: Tick implementation with diagnostic hooks present.
    public partial void Tick()
    {
        // Publish ticker thread id so a test can learn it before the tick acquires the leaf lock.
        try
        {
            OctetParentNode.DiagnosticHooks.CurrentTickerThreadId = Thread.CurrentThread.ManagedThreadId;
            OctetParentNode.DiagnosticHooks.SignalTickerStart?.Set();
            // allow the test to set SleepThreadId or other controls before we proceed
            OctetParentNode.DiagnosticHooks.WaitTickerProceed?.Wait();
        }
        catch { /* test-only */ }

        // TEST-CHEAT: optional deterministic delay for this thread before taking the venue lock.
        if (OctetParentNode.DiagnosticHooks.SleepThreadId.HasValue && OctetParentNode.DiagnosticHooks.SleepThreadId.Value == Thread.CurrentThread.ManagedThreadId)
        {
            if (OctetParentNode.DiagnosticHooks.UseYield)
                Thread.Yield();
            else
                Thread.Sleep(System.Math.Max(1, OctetParentNode.DiagnosticHooks.SleepMs));
        }

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