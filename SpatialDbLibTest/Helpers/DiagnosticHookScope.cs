#if DIAGNOSTIC
using SpatialDbLib.Lattice;
///////////////////////////////////
namespace SpatialDbLibTest.Helpers;
internal sealed class DiagnosticHookScope : IDisposable
{
    private readonly ManualResetEventSlim? _prevSignalBeforeBucketAndDispatch;
    private readonly ManualResetEventSlim? _prevBlockBeforeBucketAndDispatch;
    private readonly ManualResetEventSlim? _prevSignalAfterLeafLock;
    private readonly ManualResetEventSlim? _prevBlockAfterLeafLock;
    private readonly ManualResetEventSlim? _prevSignalSubdivideStart;
    private readonly ManualResetEventSlim? _prevWaitSubdivideProceed;
    private readonly ManualResetEventSlim? _prevSignalTickerStart;
    private readonly ManualResetEventSlim? _prevWaitTickerProceed;

    public DiagnosticHookScope()
    {
        // save previous
        _prevSignalBeforeBucketAndDispatch = OctetParentNode.DiagnosticHooks.SignalBeforeBucketAndDispatch;
        _prevBlockBeforeBucketAndDispatch = OctetParentNode.DiagnosticHooks.BlockBeforeBucketAndDispatch;
        _prevSignalAfterLeafLock = OctetParentNode.DiagnosticHooks.SignalAfterLeafLock;
        _prevBlockAfterLeafLock = OctetParentNode.DiagnosticHooks.BlockAfterLeafLock;
        _prevSignalSubdivideStart = OctetParentNode.DiagnosticHooks.SignalSubdivideStart;
        _prevWaitSubdivideProceed = OctetParentNode.DiagnosticHooks.WaitSubdivideProceed;
        _prevSignalTickerStart = OctetParentNode.DiagnosticHooks.SignalTickerStart;
        _prevWaitTickerProceed = OctetParentNode.DiagnosticHooks.WaitTickerProceed;

        // install fresh instances owned by this scope
        OctetParentNode.DiagnosticHooks.SignalBeforeBucketAndDispatch = new ManualResetEventSlim(false);
        OctetParentNode.DiagnosticHooks.BlockBeforeBucketAndDispatch = new ManualResetEventSlim(false);
        OctetParentNode.DiagnosticHooks.SignalAfterLeafLock = new ManualResetEventSlim(false);
        OctetParentNode.DiagnosticHooks.BlockAfterLeafLock = new ManualResetEventSlim(false);
        OctetParentNode.DiagnosticHooks.SignalSubdivideStart = new ManualResetEventSlim(false);
        OctetParentNode.DiagnosticHooks.WaitSubdivideProceed = new ManualResetEventSlim(false);
        OctetParentNode.DiagnosticHooks.SignalTickerStart = new ManualResetEventSlim(false);
        OctetParentNode.DiagnosticHooks.WaitTickerProceed = new ManualResetEventSlim(false);
    }

    public void Dispose()
    {
        // dispose our instances (if any), then restore previous hooks
        try { OctetParentNode.DiagnosticHooks.SignalBeforeBucketAndDispatch?.Dispose(); } catch { }
        try { OctetParentNode.DiagnosticHooks.BlockBeforeBucketAndDispatch?.Dispose(); } catch { }
        try { OctetParentNode.DiagnosticHooks.SignalAfterLeafLock?.Dispose(); } catch { }
        try { OctetParentNode.DiagnosticHooks.BlockAfterLeafLock?.Dispose(); } catch { }
        try { OctetParentNode.DiagnosticHooks.SignalSubdivideStart?.Dispose(); } catch { }
        try { OctetParentNode.DiagnosticHooks.WaitSubdivideProceed?.Dispose(); } catch { }
        try { OctetParentNode.DiagnosticHooks.SignalTickerStart?.Dispose(); } catch { }
        try { OctetParentNode.DiagnosticHooks.WaitTickerProceed?.Dispose(); } catch { }

        OctetParentNode.DiagnosticHooks.SignalBeforeBucketAndDispatch = _prevSignalBeforeBucketAndDispatch;
        OctetParentNode.DiagnosticHooks.BlockBeforeBucketAndDispatch = _prevBlockBeforeBucketAndDispatch;
        OctetParentNode.DiagnosticHooks.SignalAfterLeafLock = _prevSignalAfterLeafLock;
        OctetParentNode.DiagnosticHooks.BlockAfterLeafLock = _prevBlockAfterLeafLock;
        OctetParentNode.DiagnosticHooks.SignalSubdivideStart = _prevSignalSubdivideStart;
        OctetParentNode.DiagnosticHooks.WaitSubdivideProceed = _prevWaitSubdivideProceed;
        OctetParentNode.DiagnosticHooks.SignalTickerStart = _prevSignalTickerStart;
        OctetParentNode.DiagnosticHooks.WaitTickerProceed = _prevWaitTickerProceed;
    }
}
#endif