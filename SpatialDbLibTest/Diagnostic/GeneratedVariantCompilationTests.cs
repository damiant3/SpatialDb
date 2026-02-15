#if DIAGNOSTIC
using SpatialDbLib.Lattice;

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
            var buildDir = Path.GetDirectoryName(typeof(GeneratedVariantCompilationTests).Assembly.Location) ?? AppContext.BaseDirectory;
            var repoRoot = Path.GetFullPath(Path.Combine(buildDir, "..", "..", "..", ".."));
            var variantPath = Path.Combine(repoRoot, "SpatialDbLibTest", "Diagnostic", "Variants", "VariantImpl.LockOrderWrong.cs");

            if (!File.Exists(variantPath))
                Assert.Fail($"Variant file not found at '{variantPath}' (place the small variant file at that path).");

            var source = File.ReadAllText(variantPath);

            // compile & register the variant impl for the LockOrderWrong enum value
            using var handle = VariantTestHarness.CompileAndRegisterSubdivideImpl(source, OctetParentNode.SubdivideVariant.LockOrderWrong);

            // run repeated trials for stability
            const int TrialsPerPermutation = 25;
            var permutations = new[]
            {
                (delaySubdivider: false, delayMigration: false),
                (delaySubdivider: true,  delayMigration: false),
                (delaySubdivider: false, delayMigration: true),
                (delaySubdivider: true,  delayMigration: true)
            };
            TestContext.WriteLine("The purpose of this test is to prove that bad code is bad.  We are going to prove that it is bad");
            TestContext.WriteLine("by injecting into the lattice code (dynamically!), creating the condition that triggers the bad,");
            TestContext.WriteLine("capturing it, and reporting it.  This is more an academic exercise than a practical test.");
            TestContext.WriteLine("And was the result of pushing ChatGPT5-mini to prove the things it was saying.");
            TestContext.WriteLine("I gave it a great deal of my time to let it build this crazy thing.");
            TestContext.WriteLine("Luckily, this did get it to stop hallucinating about deadlocks that don't exist. - Damian Tedrow, Feb 2026.");
            foreach (var (delaySubdivider, delayMigration) in permutations)
            {
                OctetParentNode.SelectedSubdivideVariant = OctetParentNode.SubdivideVariant.LockOrderWrong;

                int detected = 0;
                int notDetected = 0;
                int errors = 0;

                for (int i = 0; i < TrialsPerPermutation; i++)
                {
                    var result = PublicVariantRunner.RunVariant("LockOrderWrong", delaySubdivider, delayMigration);
                    TestContext.WriteLine($"Run #{i+1} perm(dSub={delaySubdivider},dMig={delayMigration}) -> {result.VariantName}");

                    if (result.Detected) detected++;
                    else if (!string.IsNullOrEmpty(result.ErrorMessage)) errors++;
                    else notDetected++;
                }

                TestContext.WriteLine($"Summary of permutation (delaySubdivider={delaySubdivider},delayMigrator={delayMigration}): Test condition detected={detected}, notDetected={notDetected}, errors={errors}");

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