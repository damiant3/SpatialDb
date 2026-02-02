using SpatialDbLib.Lattice;
///////////////////////////
namespace SpatialDbLibTest;

[TestClass]
public partial class ParallelTests
{
    const int ITERATIONS = 2;
    const int TASKS_PER_ITERATION = 16;
    const int BATCH_SIZE = 10000;

    public void RunInsertBulk(Dictionary<int, List<SpatialObject>> objectsToInsert)
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

    public void RunInsertAsOne(Dictionary<int, List<SpatialObject>> objectsToInsert)
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
    
    [TestMethod]
    public void InsertAsOneVsBulk_TinyClusters()
    {
        Console.WriteLine("=== InsertAsOne: Tiny Clustered Distribution (ignores batch size config, sets = 5) ===");
        RunInsertAsOne(GetTinyClusteredObjects());
        Console.WriteLine("=== BulkInsert: Tiny Clustered Distribution (ignores batch size config, sets = 5) ===");
        RunInsertBulk(GetTinyClusteredObjects());
    }

    [TestMethod]
    public void InsertAsOneVsBulk_TinyDispersed()
    {
        Console.WriteLine("=== InsertAsOne: Tiny Dispersed Distribution (ignores batch size config, sets = 5) ===");
        RunInsertAsOne(GetTinyDispersedObjects());
        Console.WriteLine("=== BulkInsert: Tiny Dispersed Distribution (ignores batch size config, sets = 5) ===");
        RunInsertBulk(GetTinyDispersedObjects());
    }

    [TestMethod]
    public void BulkInsertStress()
    {
        Console.WriteLine("=== Single Path Distribution ===");
        RunInsertBulk(GetSinglePathObjects());

        Console.WriteLine("=== Skewed Distribution ===");
        RunInsertBulk(GetSkewedObjects());
        Console.WriteLine("=== Uniform Distribution ===");
        RunInsertBulk(GetUniformObjects());
        Console.WriteLine("=== Bimodal Distribution ===");
        RunInsertBulk(GetBimodalObjects());
        Console.WriteLine("=== Clustered Distribution ===");
        RunInsertBulk(GetClusteredObjects());

    }

    [TestMethod]
    public void InsertAsOneStress()
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

    [TestMethod]
    public void InsertStress_ConcurrentDictionary()
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
    public void InsertStress()
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

