using SpatialDbLib.Lattice;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace SpatialDbLibTest
{
    public class TestSpatialLattice
        : SpatialLattice,
          ITestCatalog
    {
        public void TestInsert(SpatialObject obj)
        {
            AdmitResult ret;
            try
            {
                ret = base.Insert(obj);

                if (ret is not AdmitResult.Created)
                {
                    Debugger.Break();
                }
            }
            catch (Exception ex)
            {
                Exceptions[obj.Guid] = ex;
                Debugger.Break();
                throw;
            }
        }

        public void TestRemove(SpatialObject obj)
        {
            try
            {
                base.Remove(obj);
            }
            catch (Exception ex)
            {
                Exceptions[obj.Guid] = ex;
                Debugger.Break();
                throw;
            }
        }

        ConcurrentDictionary<Guid, Exception> Exceptions = [];
        public string GenerateExceptionReport()
        {
            if (Exceptions.IsEmpty)
                return "No exceptions tracked.";
            var sb = new StringBuilder();
            sb.AppendLine($"Exceptions tracked: {Exceptions.Count}");
            foreach (var kvp in Exceptions)
                sb.AppendLine($"Object {kvp.Key}: {kvp.Value}");
            return sb.ToString();
        }
        public void Cleanup() => m_root.CreateChildLeafNodes();
    }

    public class TestConcurrentDictionary
        : ConcurrentDictionary<Guid, SpatialObject>,
          ITestCatalog
    {
        public void TestInsert(SpatialObject obj)
        {
            try
            {
                this[obj.Guid] = obj;
            }
            catch (Exception ex)
            {
                Exceptions[obj.Guid] = ex;
                Debugger.Break();
                throw;
            }
}
        public void TestRemove(SpatialObject obj)
        {
            try
            {
                TryRemove(obj.Guid, out _);
            }
            catch (Exception ex)
            {
                Exceptions[obj.Guid] = ex;
                Debugger.Break();
                throw;
            }
        }

        ConcurrentDictionary<Guid, Exception> Exceptions = [];
        public string GenerateExceptionReport()
        {
            if (Exceptions.IsEmpty)
                return "No exceptions tracked.";
            var sb = new StringBuilder();
            sb.AppendLine($"Exceptions tracked: {Exceptions.Count}");
            foreach (var kvp in Exceptions)
            {
                sb.AppendLine($"Object {kvp.Key}: {kvp.Value}");
            }
            return sb.ToString();
        }

        public void Cleanup() => Clear();
    }

    public class LatticeParallelTest(int iterations = 1, int taskCount = 1)
        : ParallelContainerTestDiagnostic<TestSpatialLattice>(iterations, taskCount) { };

    public class ConcurrentDictionaryParallelTest(int iterations = 1, int taskCount = 1)
    : ParallelContainerTestDiagnostic<TestConcurrentDictionary>(iterations, taskCount) { };

    public class ParallelContainerTestDiagnostic<T>(int iterations, int taskCount = 1)
        : ContainerTestDiagnostics<T>(iterations)
        where T :  class,
                   ITestCatalog,
                   new ()
    {
        public int TaskCount = taskCount;

        int insertHeadStart = 5;
        public void InsertOrRemoveRandomItems()
        {
            if (insertHeadStart-- > 0)
            {
                InsertRandomItems();
            }
            else
            {
                if (insertHeadStart % 2 == 0)
                    InsertRandomItems();
                else
                    RemoveRandomItems();
            }

        }

        public void InsertRandomItems()
        {
            var tasks = new List<Task>();

            var swInsert = Stopwatch.StartNew();
            for (int j = 0; j < TaskCount; j++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var pos = new LongVector3(
                            Random.Next(-SpaceRange, SpaceRange),
                            Random.Next(-SpaceRange, SpaceRange),
                            Random.Next(-SpaceRange, SpaceRange)
                        );

                        var obj = new SpatialObject([pos]);
                        Container!.TestInsert(obj);
                        InsertedObjects![obj.Guid] = obj;

                        Interlocked.Increment(ref Inserts);
                    }
                    catch
                    {
                        Interlocked.Increment(ref FailedInserts);
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
            swInsert.Stop();
            TotalInsertTicks += swInsert.ElapsedTicks;
        }

        public void RemoveRandomItems()
        {
            if(InsertedObjects!.Count < 8) return;

            int removeCount = Random.Next(1, (int)Math.Log2( InsertedObjects!.Count));
            var tasks = new List<Task>();
            var swRemove = Stopwatch.StartNew();
            for (int r = 0; r < removeCount; r++)
            {
                tasks.Add(Task.Run(() =>
                {
                    if (InsertedObjects.TryTakeRandomFast(out var kvp))
                    {
                        var objToRemove = kvp.Value;
                        try
                        {
                            Container!.TestRemove(objToRemove);
                            Interlocked.Increment(ref Removes);
                        }
                        catch
                        {
                            Interlocked.Increment(ref FailedRemoves);
                        }
                    }
                }));
            }
            Task.WaitAll(tasks.ToArray());

            swRemove.Stop();
            TotalRemoveTicks = swRemove.ElapsedTicks;
        }
    }


}