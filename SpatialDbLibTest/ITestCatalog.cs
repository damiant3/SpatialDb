using SpatialDbLib.Lattice;

namespace SpatialDbLibTest
{
    public interface ITestCatalog
    {
        void TestBulkInsert(List<ISpatialObject> objs);
        void TestInsertAsOne(List<ISpatialObject> objs);
        void TestInsert(ISpatialObject obj);
        void TestRemove(ISpatialObject obj);
        string GenerateExceptionReport();
        void Cleanup();
    }
}