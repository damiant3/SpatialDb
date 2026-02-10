using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLibTest.Helpers;
///////////////////////////
namespace SpatialDbLibTest.BaseTests;

[TestClass]
public partial class ParallelTests
{
    const int ITERATIONS = 2;
    const int TASKS_PER_ITERATION = 16;
    const int BATCH_SIZE = 10000;

    public static void RunInsertBulk(Dictionary<int, List<ISpatialObject>> objectsToInsert)
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

    public static void RunInsertAsOne(Dictionary<int, List<ISpatialObject>> objectsToInsert)
    {
        var test = new LatticeParallelTest(TASKS_PER_ITERATION, BATCH_SIZE, benchmarkTest: true);
        for (int iter = 0; iter < ITERATIONS; iter++)
        {
            test.InsertAsOne(objectsToInsert);
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine($"Test complete after {ITERATIONS} iterations.");
        Console.WriteLine(test.GenerateReportString());
    }

    // === ACTIVE STRESS TESTS ===

    [TestMethod]
    public void InsertStress_Singleton()
    {
        var test = new LatticeParallelTest(TASKS_PER_ITERATION, BATCH_SIZE, benchmarkTest: true);
        for (int iter = 0; iter < ITERATIONS; iter++)
        {
            test.InsertItems(TestData.GetUniformObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000));
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine($"Test complete after {ITERATIONS} iterations.");
        Console.WriteLine(test.GenerateReportString());
    }

    [TestMethod]
    public void InsertStress_Bulk()
    {
        Console.WriteLine("=== Bimodal Distribution ===");
        RunInsertBulk(TestData.GetBimodalObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000));

        Console.WriteLine("=== Single Path Distribution ===");
        RunInsertBulk(TestData.GetSinglePathObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000 ));

        Console.WriteLine("=== Skewed Distribution ===");
        RunInsertBulk(TestData.GetSkewedObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000));

        Console.WriteLine("=== Uniform Distribution ===");
        RunInsertBulk(TestData.GetUniformObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000));

        Console.WriteLine("=== Clustered Distribution ===");
        RunInsertBulk(TestData.GetClusteredObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000));
    }

    // === COMPARATIVE BENCHMARKS (disabled by default) ===
    // These tests compare InsertAsOne vs BulkInsert performance.
    // Enable manually for performance analysis.

    //[TestMethod]
    public void Benchmark_InsertAsOne_Distributions()
    {
        Console.WriteLine("=== Single Path Distribution ===");
        RunInsertAsOne(TestData.GetSinglePathObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000));
        Console.WriteLine("=== Skewed Distribution ===");
        RunInsertAsOne(TestData.GetSkewedObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000));
        Console.WriteLine("=== Uniform Distribution ===");
        RunInsertAsOne(TestData.GetUniformObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000));
        Console.WriteLine("=== Bimodal Distribution ===");
        RunInsertAsOne(TestData.GetBimodalObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000));
        Console.WriteLine("=== Clustered Distribution ===");
        RunInsertAsOne(TestData.GetClusteredObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000));
    }

    //[TestMethod]
    public void Benchmark_InsertAsOneVsBulk_TinyBatches()
    {
        Console.WriteLine("=== InsertAsOne: Tiny Clustered Distribution (batch size = 5) ===");
        RunInsertAsOne(TestData.GetTinyClusteredObjects(TASKS_PER_ITERATION, 100000));
        Console.WriteLine("=== BulkInsert: Tiny Clustered Distribution (batch size = 5) ===");
        RunInsertBulk(TestData.GetTinyClusteredObjects(TASKS_PER_ITERATION, 100000));

        Console.WriteLine("=== InsertAsOne: Tiny Dispersed Distribution (batch size = 5) ===");
        RunInsertAsOne(TestData.GetTinyDispersedObjects(TASKS_PER_ITERATION, 100000));
        Console.WriteLine("=== BulkInsert: Tiny Dispersed Distribution (batch size = 5) ===");
        RunInsertBulk(TestData.GetTinyDispersedObjects(TASKS_PER_ITERATION, 100000));
    }

    //[TestMethod]
    public void Benchmark_SpatialLattice_vs_ConcurrentDictionary()
    {
        var test = new ConcurrentDictionaryParallelTest(TASKS_PER_ITERATION, BATCH_SIZE, benchmarkTest: true);
        for (int iter = 0; iter < ITERATIONS; iter++)
        {
            test.InsertItems(TestData.GetUniformObjects(TASKS_PER_ITERATION, BATCH_SIZE, 100000));
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine($"Test complete after {ITERATIONS} iterations.");
        Console.WriteLine(test.GenerateReportString());
    }
}

