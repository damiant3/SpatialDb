using SpatialDbLib.Lattice;

namespace SpatialDbLibTest
{
    public interface ITestCatalog
    {
        void TestBulkInsert(List<SpatialObject> objs);
        void TestInsert(SpatialObject obj);
        void TestRemove(SpatialObject obj);
        string GenerateExceptionReport();
        void Cleanup();
    }
}