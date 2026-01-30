namespace SpatialDbLibTest;

[TestClass]
public class ParallelTests
{
    const int ITERATIONS = 5;
    const int TASKS_PER_ITERATION = 1024;
    const int BATCH_SIZE = 1;
    [TestMethod]
    public void ParallelTests_InsertStress_ConcurrentDictionary()
    {
        var test = new ConcurrentDictionaryParallelTest(ITERATIONS, TASKS_PER_ITERATION);
        for (int iter = 0; iter < test.Iterations; iter++)
        {
            test.InsertOrRemoveRandomItems();
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine(test.GenerateReportString());
    }

    [TestMethod]
    public void ParallelTests_InsertStress()
    {
        var test = new LatticeParallelTest(ITERATIONS, TASKS_PER_ITERATION);
        for (int iter = 0; iter < test.Iterations; iter++)
        {
            test.InsertOrRemoveRandomItems();
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine(test.GenerateReportString());
    }

    [TestMethod]
    public void ParallelTests_BulkInsertStress()
    {
        var test = new LatticeParallelTest(ITERATIONS, TASKS_PER_ITERATION, BATCH_SIZE);
        for (int iter = 0; iter < test.Iterations; iter++)
        {
            test.InsertBulkRandomItems();
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine(test.GenerateReportString());
    }
}

