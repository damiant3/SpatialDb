namespace SpatialDbLibTest;

[TestClass]
public class ParallelTests
{
    [TestMethod]
    public void ParallelTests_InsertStress_ConcurrentDictionary()
    {
        const int ITERATIONS = 2048;
        const int TASKS_PER_ITERATION = 2048;
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
        const int ITERATIONS = 512;
        const int TASKS_PER_ITERATION = 128;
        var test = new LatticeParallelTest(ITERATIONS, TASKS_PER_ITERATION);
        for (int iter = 0; iter < test.Iterations; iter++)
        {
            test.InsertOrRemoveRandomItems();
        }
        test.CleanupAndGatherDiagnostics();
        Console.WriteLine(test.GenerateReportString());
    }
}

