using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using SpatialDbLib.Lattice;
//////////////////////////
namespace SpatialDbLibTest;

public class TestSpatialLattice
    : SpatialLattice,
      ITestCatalog
{
    public void TestBulkInsert(List<SpatialObject> objs)
    {
        AdmitResult ret;
        try
        {
            ret = Insert(objs);
            if (ret is not AdmitResult.BulkCreated)
            {
                Debugger.Break();
            }
        }
        catch (Exception ex)
        {
            Exceptions[Guid.NewGuid()] = ex;
            Debugger.Break();
            throw;
        }
    }
    public void TestInsert(SpatialObject obj)
    {
        AdmitResult ret;
        try
        {
            ret = Insert(obj);
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
            Remove(obj);
        }
        catch (Exception ex)
        {
            Exceptions[obj.Guid] = ex;
            Debugger.Break();
            throw;
        }
    }

    readonly ConcurrentDictionary<Guid, Exception> Exceptions = [];
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

    public void Cleanup() => CreateChildLeafNodes();
}

public class TestConcurrentDictionary
    : ConcurrentDictionary<Guid, SpatialObject>,
      ITestCatalog
{
    public void TestBulkInsert(List<SpatialObject> objs)
    {
        try
        {
            foreach (var obj in objs)
            {
                this[obj.Guid] = obj;
            }
        }
        catch (Exception ex)
        {
            Exceptions[Guid.NewGuid()] = ex;
            Debugger.Break();
            throw;
        }
    }

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