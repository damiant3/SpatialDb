using SpatialDbLib.Lattice;
///////////////////////////
namespace SpatialDbLibTest;

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
            test.InsertItems(GetUniformObjects());
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine($"Test complete after {ITERATIONS} iterations.");
        Console.WriteLine(test.GenerateReportString());
    }

    [TestMethod]
    public void InsertStress_Bulk()
    {
        Console.WriteLine("=== Bimodal Distribution ===");
        RunInsertBulk(GetBimodalObjects());

        Console.WriteLine("=== Single Path Distribution ===");
        RunInsertBulk(GetSinglePathObjects());

        Console.WriteLine("=== Skewed Distribution ===");
        RunInsertBulk(GetSkewedObjects());

        Console.WriteLine("=== Uniform Distribution ===");
        RunInsertBulk(GetUniformObjects());

        Console.WriteLine("=== Clustered Distribution ===");
        RunInsertBulk(GetClusteredObjects());
    }

    // === COMPARATIVE BENCHMARKS (disabled by default) ===
    // These tests compare InsertAsOne vs BulkInsert performance.
    // Enable manually for performance analysis.

    //[TestMethod]
    public void Benchmark_InsertAsOne_Distributions()
    {
        Console.WriteLine("=== Single Path Distribution ===");
        RunInsertAsOne(GetSinglePathObjects());
        Console.WriteLine("=== Skewed Distribution ===");
        RunInsertAsOne(GetSkewedObjects());
        Console.WriteLine("=== Uniform Distribution ===");
        RunInsertAsOne(GetUniformObjects());
        Console.WriteLine("=== Bimodal Distribution ===");
        RunInsertAsOne(GetBimodalObjects());
        Console.WriteLine("=== Clustered Distribution ===");
        RunInsertAsOne(GetClusteredObjects());
    }

    //[TestMethod]
    public void Benchmark_InsertAsOneVsBulk_TinyBatches()
    {
        Console.WriteLine("=== InsertAsOne: Tiny Clustered Distribution (batch size = 5) ===");
        RunInsertAsOne(GetTinyClusteredObjects());
        Console.WriteLine("=== BulkInsert: Tiny Clustered Distribution (batch size = 5) ===");
        RunInsertBulk(GetTinyClusteredObjects());

        Console.WriteLine("=== InsertAsOne: Tiny Dispersed Distribution (batch size = 5) ===");
        RunInsertAsOne(GetTinyDispersedObjects());
        Console.WriteLine("=== BulkInsert: Tiny Dispersed Distribution (batch size = 5) ===");
        RunInsertBulk(GetTinyDispersedObjects());
    }

    //[TestMethod]
    public void Benchmark_SpatialLattice_vs_ConcurrentDictionary()
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
}

