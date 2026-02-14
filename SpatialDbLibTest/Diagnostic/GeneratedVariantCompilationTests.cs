#if DIAGNOSTIC

namespace SpatialDbLibTest.Diagnostic
{
    [TestClass]
    public class GeneratedVariantCompilationTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void Injected_LockOrderWrong_Variant_Stability_25Runs()
        {
            // locate variant source file relative to test build output (deterministic)
            var buildDir = System.IO.Path.GetDirectoryName(typeof(GeneratedVariantCompilationTests).Assembly.Location) ?? System.AppContext.BaseDirectory;
            var repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(buildDir, "..", "..", "..", ".."));
            var variantPath = System.IO.Path.Combine(repoRoot, "SpatialDbLibTest", "Diagnostic", "Variants", "VariantImpl.LockOrderWrong.cs");

            if (!System.IO.File.Exists(variantPath))
                Assert.Fail($"Variant file not found at '{variantPath}' (place the small variant file at that path).");

            var source = System.IO.File.ReadAllText(variantPath);

            // compile & register the variant impl for the LockOrderWrong enum value
            using var handle = VariantTestHarness.CompileAndRegisterSubdivideImpl(source, SpatialDbLib.Lattice.OctetParentNode.SubdivideVariant.LockOrderWrong);

            // run repeated trials for stability
            const int TrialsPerPermutation = 25;
            var permutations = new[]
            {
                (delaySubdivider: false, delayMigration: false),
                (delaySubdivider: true,  delayMigration: false),
                (delaySubdivider: false, delayMigration: true),
                (delaySubdivider: true,  delayMigration: true)
            };

            foreach (var (delaySubdivider, delayMigration) in permutations)
            {
                SpatialDbLib.Lattice.OctetParentNode.SelectedSubdivideVariant =
                    SpatialDbLib.Lattice.OctetParentNode.SubdivideVariant.LockOrderWrong;

                int detected = 0;
                int notDetected = 0;
                int errors = 0;

                for (int i = 0; i < TrialsPerPermutation; i++)
                {
                    var result = PublicVariantRunner.RunVariant("LockOrderWrong", delaySubdivider, delayMigration);
                    TestContext.WriteLine($"Run #{i+1} perm(dSub={delaySubdivider},dMig={delayMigration}) -> {result}");

                    if (result.StartsWith("DETECTED", System.StringComparison.OrdinalIgnoreCase)) detected++;
                    else if (result.StartsWith("NOT_DETECTED", System.StringComparison.OrdinalIgnoreCase)) notDetected++;
                    else errors++;
                }

                TestContext.WriteLine($"Summary perm(dSub={delaySubdivider},dMig={delayMigration}): detected={detected}, notDetected={notDetected}, errors={errors}");

                // Expectation: this injected, obviously-wrong variant should be reliably detected.
                if (detected == 0)
                    Assert.Fail($"Injected variant was NOT detected in any of {TrialsPerPermutation} runs for permutation (dSub={delaySubdivider}, dMig={delayMigration}). See logs for details.");

                // If there are intermittent not-detected runs, fail to make the flakiness visible.
                if (notDetected > 0 || errors > 0)
                    Assert.Fail($"Injected variant produced inconsistent results for permutation (dSub={delaySubdivider}, dMig={delayMigration}). detected={detected}, notDetected={notDetected}, errors={errors}");
            }
        }
    }
}
#endif