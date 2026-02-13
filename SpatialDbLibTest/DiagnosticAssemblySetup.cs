using Microsoft.VisualStudio.TestTools.UnitTesting;
using SpatialDbLib.Lattice;
///////////////////////////
namespace SpatialDbLibTest;

[TestClass]
public class DiagnosticAssemblySetup
{
    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
#if DIAGNOSTIC
        // Best-effort: ensure library-side waits used by diagnostics are not left blocking
        // when tests don't explicitly set them. Don't touch the "Signal" events tests wait on
        // to observe library progress; only open the waits/blocks the library could hang on.
        try
        {
            OctetParentNode.DiagnosticHooks.WaitSubdivideProceed.Set();
            OctetParentNode.DiagnosticHooks.WaitTickerProceed.Set();
            OctetParentNode.DiagnosticHooks.BlockAfterLeafLock.Set();
            OctetParentNode.DiagnosticHooks.BlockBeforeBucketAndDispatch.Set();
        }
        catch
        {
            // swallow — diagnostics are best-effort during assembly init
        }
#endif
    }
}