#if DIAGNOSTIC
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Globalization;
using System.Text;
/////////////////////////////////////
namespace SpatialDbLibTest.Diagnostic;

[TestClass]
public class GeneratedVariantIntegrationTests
{
    public TestContext TestContext { get; set; }

    // This test decompiles the runtime type, mutates the target method,
    // replaces the original source method body, compiles the full project sources
    // into an isolated assembly and runs the VariantEntry.RunVariant wrapper in that assembly.
    [TestMethod]
    public void GenerateMutatedVariants_FromAssembly_CompileAndRunWrappers()
    {
     
    }

}
#endif