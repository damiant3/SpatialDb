namespace SpatialDbLibTest;

[TestClass]
public class ParallelTests
{
    const int ITERATIONS = 128;
    const int TASKS_PER_ITERATION = 2048;
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
}

