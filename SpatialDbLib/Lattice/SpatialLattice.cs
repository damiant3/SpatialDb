using System.Numerics;
using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
///////////////////////////////
namespace SpatialDbLib.Lattice;

public class OctetRootNode(Region bounds, byte latticeDepth)
    : RootNode<OctetParentNode, OctetBranchNode, VenueLeafNode, OctetRootNode>(bounds, latticeDepth)
{ }
public class SpatialLattice : SpatialLattice<OctetRootNode>
{
    public SpatialLattice() : base() { }
    public SpatialLattice(Region outerBounds, byte latticeDepth) : base(outerBounds, latticeDepth) { }
    public static bool EnablePruning { get; set; } = false;
}
public interface ISpatialLattice : ISpatialNode
{
    ISpatialNode GetRootNode();
    byte LatticeDepth { get; }
    VenueLeafNode? ResolveLeafFromOuterLattice(ISpatialObject obj);
    AdmitResult AdmitForInsert(Span<ISpatialObject> buffer);
    ParentToSubLatticeTransform BoundsTransform { get; }
    IEnumerable<ISpatialObject> QueryWithinDistance(LongVector3 center, ulong radius);
}
internal static class LatticeDepthContext
{
    [ThreadStatic] private static byte t_latticeDepth;

    public static byte CurrentDepth
    {
        get => t_latticeDepth;
        set => t_latticeDepth = value;
    }
}
public class SpatialLattice<TRoot>
    : ISpatialLattice,
      ISpatialNode
    where TRoot : IRootNode<OctetParentNode, OctetBranchNode, VenueLeafNode, TRoot>
{
    public static byte CurrentThreadLatticeDepth => LatticeDepthContext.CurrentDepth;

    public ISpatialNode GetRootNode() => m_root;
    internal protected readonly TRoot m_root;
    public Region Bounds => m_root.Bounds;
    public ParentToSubLatticeTransform BoundsTransform { get; protected set; }

    public SpatialLattice() // for the rootiest of roots
        : this(LatticeUniverse.RootRegion, 0) { }

    public SpatialLattice(Region outerBounds, byte latticeDepth)
    {
        m_root = CreateRoot(LatticeUniverse.RootRegion, latticeDepth);
        m_root.OwningLattice = this;
        BoundsTransform = new ParentToSubLatticeTransform(outerBounds);
    }
    protected virtual TRoot CreateRoot(Region bounds, byte depth)
        => (TRoot)(IRootNode<OctetParentNode, OctetBranchNode, VenueLeafNode, TRoot>)new OctetRootNode(bounds, depth);

    public byte LatticeDepth => m_root.LatticeDepth;
    public static IDisposable PushLatticeDepth(byte depth)
    {
        return new LatticeDepthScope(depth);
    }
    class LatticeDepthScope : IDisposable
    {
        private readonly byte m_previousDepth;
        public LatticeDepthScope(byte depth)
        {
            m_previousDepth = LatticeDepthContext.CurrentDepth;
            LatticeDepthContext.CurrentDepth = depth;
        }
        public void Dispose()
            => LatticeDepthContext.CurrentDepth = m_previousDepth;
    }

    public AdmitResult InsertAsOne(List<ISpatialObject> objs)
    {
        using IDisposable s = PushLatticeDepth(LatticeDepth);
        List<AdmitResult> results = [];
        foreach (ISpatialObject obj in objs)
        {
            AdmitResult admitResult = Admit(obj, obj.LocalPosition);
            if (admitResult is AdmitResult.Created created)
                results.Add(admitResult);
            else
            {
                foreach (AdmitResult result in results)
                    if (result is AdmitResult.Created createdRollback)
                        createdRollback.Proxy.Rollback();
                return admitResult;
            }
        }
        foreach (AdmitResult result in results)
        {
            if (result is AdmitResult.Created created)
                created.Proxy.Commit();
        }
        return AdmitResult.BulkCreate([.. results.Cast<AdmitResult.Created>().Select(r => r.Proxy)]);
    }
    public AdmitResult Insert(List<ISpatialObject> objs)
        => Insert(objs.ToArray());

    public AdmitResult Insert(ISpatialObject[] objs)
    {
        using IDisposable s = PushLatticeDepth(LatticeDepth);
        AdmitResult admitResult = Admit(objs);
        if (admitResult is AdmitResult.BulkCreated created)
            foreach (ISpatialObjectProxy proxy in created.Proxies)
            {
                if (proxy.IsCommitted) throw new InvalidOperationException("Proxy is already committed during bulk insert commit.");
                proxy.Commit();
            }
        return admitResult;
    }
    public AdmitResult Insert(ISpatialObject obj)
    {
        using IDisposable s = PushLatticeDepth(LatticeDepth);
        using SlimSyncer s2 = new(obj.Sync, SlimSyncer.LockMode.Write, "SpatialLattice.Insert: Object");
        AdmitResult admitResult = Admit(obj, obj.GetPositionAtDepth(LatticeDepthContext.CurrentDepth));
        if (admitResult is AdmitResult.Created created)
            created.Proxy.Commit();
        return admitResult;
    }

    public void Remove(ISpatialObject obj)
    {
        using IDisposable s = PushLatticeDepth(LatticeDepth);
        using SlimSyncer s2 = new(obj.Sync, SlimSyncer.LockMode.Write, "SpatialLattice.Remove: Object");
        while (true)
        {
            VenueLeafNode? leaf = m_root.ResolveOccupyingLeaf(obj);
            if (leaf == null) return;
            leaf.Vacate(obj);
            if (SpatialLattice.EnablePruning) leaf.Parent.PruneIfEmpty();
            return;
        }
    }

    public void Migrate(IList<ISpatialObject> objs)
    {
        foreach (ISpatialObject obj in objs)
        {
            LongVector3 inner = BoundsTransform.OuterToInnerInsertion(obj.LocalPosition, obj.Guid);
            obj.AppendPosition(inner);
        }
        m_root.Migrate(objs);
    }

    public AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition)
    {
        using IDisposable s = PushLatticeDepth(LatticeDepth);
        return m_root.Admit(obj, proposedPosition);
    }

    public AdmitResult Admit(Span<ISpatialObject> buffer)
    {
        using IDisposable s = PushLatticeDepth(LatticeDepth);
        return m_root.Admit(buffer);
    }

    public VenueLeafNode? ResolveLeafFromOuterLattice(ISpatialObject obj)
    {
        using IDisposable s = PushLatticeDepth(LatticeDepth);
        return m_root.ResolveLeafFromOuterLattice(obj);
    }

    public AdmitResult AdmitForInsert(Span<ISpatialObject> buffer)
    {
        foreach (ISpatialObject obj in buffer)
            if (!obj.HasPositionAtDepth(LatticeDepth))
            {
                LongVector3 proposedPosition = obj.GetPositionAtDepth(LatticeDepth - 1);
                LongVector3 framedPosition = BoundsTransform.OuterToInnerInsertion(proposedPosition, obj.Guid);
                obj.AppendPosition(framedPosition);
            }
        return Admit(buffer);
    }
    public VenueLeafNode? ResolveOccupyingLeaf(ISpatialObject obj)
    {
        using IDisposable s = PushLatticeDepth(LatticeDepth);
        return m_root.ResolveOccupyingLeaf(obj);
    }
    public VenueLeafNode? ResolveLeaf(ISpatialObject obj)
    {
        using IDisposable s = PushLatticeDepth(LatticeDepth);
        return m_root.ResolveLeaf(obj);
    }
    public IEnumerable<ISpatialObject> QueryWithinDistance(LongVector3 center, ulong radius)
    {
        List<ISpatialObject> results = new();
        SpatialQuery.CollectWithinSphere(m_root, center, radius, results, acquireLeafLock: true);
        return results;
    }
}
