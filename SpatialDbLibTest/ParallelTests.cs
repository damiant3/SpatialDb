namespace SpatialDbLibTest;

[TestClass]
public class ParallelTests
{
    [TestMethod]
    public void ParallelTests_InsertStress_ConcurrentDictionary()
    {
        const int ITERATIONS = 1024;
        const int TASKS_PER_ITERATION = 512;
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
        const int ITERATIONS = 1024;
        const int TASKS_PER_ITERATION = 512;
        var test = new LatticeParallelTest(ITERATIONS, TASKS_PER_ITERATION);
        for (int iter = 0; iter < test.Iterations; iter++)
        {
            test.InsertOrRemoveRandomItems();
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine(test.GenerateReportString());
    }
}

