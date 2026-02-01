using SpatialDbLib.Lattice;
using static System.Net.Mime.MediaTypeNames;

namespace SpatialDbLibTest;

[TestClass]
public partial class ParallelTests
{
    const int ITERATIONS = 2;
    const int TASKS_PER_ITERATION = 16;
    const int BATCH_SIZE = 10000;

    public void RunTest(Dictionary<int, List<SpatialObject>> objectsToInsert)
    {
        var test = new LatticeParallelTest(TASKS_PER_ITERATION, BATCH_SIZE, benchmarkTest: true);
        for (int iter = 0; iter < ITERATIONS; iter++)
        {
            test.InsertBulkItems(objectsToInsert);
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine($"Test complete after {ITERATIONS} iterations.");
        Console.WriteLine(test.GenerateReportString());
    }
    [TestMethod]
    public void ParallelTests_BulkInsertStress()
    {
        Console.WriteLine("=== Skewed Distribution ===");
        RunTest(GetSkewedObjects());
        Console.WriteLine("=== Uniform Distribution ===");
        RunTest(GetUniformObjects());
        Console.WriteLine("=== Bimodal Distribution ===");
        RunTest(GetBimodalObjects());
        Console.WriteLine("=== Clustered Distribution ===");
        RunTest(GetClusteredObjects());
        Console.WriteLine("=== Single Path Distribution ===");
        RunTest(GetSinglePathObjects());
    }

    [TestMethod]
    public void ParallelTests_InsertStress_ConcurrentDictionary()
    {
        var test = new ConcurrentDictionaryParallelTest(TASKS_PER_ITERATION, BATCH_SIZE, benchmarkTest: true);
        for (int iter = 0; iter < ITERATIONS; iter++)
        {
            test.InsertItems(GetUniformObjects());
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine($"Test complete after {ITERATIONS} iterations.");
        Console.WriteLine(test.GenerateReportString());
    }

    [TestMethod]
    public void ParallelTests_InsertStress()
    {
        var test = new LatticeParallelTest(TASKS_PER_ITERATION, BATCH_SIZE, benchmarkTest: true);
        for (int iter = 0; iter < ITERATIONS; iter++)
        {
            test.InsertItems(GetUniformObjects());
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine($"Test complete after {ITERATIONS} iterations.");
        Console.WriteLine(test.GenerateReportString());
    }


}

