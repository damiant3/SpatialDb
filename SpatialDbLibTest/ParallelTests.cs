namespace SpatialDbLibTest;

[TestClass]
public class ParallelTests
{
    const int ITERATIONS = 4;
    const int TASKS_PER_ITERATION = 8;
    const int BATCH_SIZE = 10000;

    [TestMethod]
    public void ParallelTests_BulkInsertStress()
    {
        var test = new LatticeParallelTest(ITERATIONS, TASKS_PER_ITERATION, BATCH_SIZE, benchmarkTest: true);
        for (int iter = 0; iter < test.Iterations; iter++)
        {
            test.InsertBulkRandomItems();
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine(test.GenerateReportString());
    }

    [TestMethod]
    public void ParallelTests_InsertStress_ConcurrentDictionary()
    {
        var test = new ConcurrentDictionaryParallelTest(ITERATIONS, TASKS_PER_ITERATION, BATCH_SIZE, benchmarkTest: true);
        for (int iter = 0; iter < test.Iterations; iter++)
        {
            test.InsertRandomItems();
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine(test.GenerateReportString());
    }

    [TestMethod]
    public void ParallelTests_InsertStress()
    {
        var test = new LatticeParallelTest(ITERATIONS, TASKS_PER_ITERATION, BATCH_SIZE, benchmarkTest: true);
        for (int iter = 0; iter < test.Iterations; iter++)
        {
            test.InsertRandomItems();
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine(test.GenerateReportString());
    }


}

