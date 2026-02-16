using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
using System.Numerics;
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
        using var s = PushLatticeDepth(LatticeDepth);
        List<AdmitResult> results = [];
        foreach (var obj in objs)
        {
            var admitResult = Admit(obj, obj.LocalPosition);
            if (admitResult is AdmitResult.Created created)
                results.Add(admitResult);
            else
            {
                foreach (var result in results)
                    if (result is AdmitResult.Created createdRollback)
                        createdRollback.Proxy.Rollback();
                return admitResult;
            }
        }
        foreach (var result in results)
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
        using var s = PushLatticeDepth(LatticeDepth);
        var admitResult = Admit(objs);
        if (admitResult is AdmitResult.BulkCreated created)
            foreach (var proxy in created.Proxies)
            {
                if (proxy.IsCommitted) throw new InvalidOperationException("Proxy is already committed during bulk insert commit.");
                proxy.Commit();
            }
        return admitResult;
    }
    public AdmitResult Insert(ISpatialObject obj)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        using var s2 = new SlimSyncer(obj.Sync, SlimSyncer.LockMode.Write, "SpatialLattice.Insert: Object");
        var admitResult = Admit(obj, obj.GetPositionAtDepth(LatticeDepthContext.CurrentDepth));
        if (admitResult is AdmitResult.Created created)
            created.Proxy.Commit();
        return admitResult;
    }

    public void Remove(ISpatialObject obj)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        using var s2 = new SlimSyncer(obj.Sync, SlimSyncer.LockMode.Write, "SpatialLattice.Remove: Object");
        while (true)
        {
            var leaf = m_root.ResolveOccupyingLeaf(obj);
            if (leaf == null) return;
            leaf.Vacate(obj);
            if (SpatialLattice.EnablePruning) leaf.Parent.PruneIfEmpty();
            return;
        }
    }

    public void Migrate(IList<ISpatialObject> objs)
    {
        foreach (var obj in objs)
        {
            var inner = BoundsTransform.OuterToInnerInsertion(obj.LocalPosition, obj.Guid);
            obj.AppendPosition(inner);
        }
        m_root.Migrate(objs);
    }

    public AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        return m_root.Admit(obj, proposedPosition);
    }

    public AdmitResult Admit(Span<ISpatialObject> buffer)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        return m_root.Admit(buffer);
    }

    public VenueLeafNode? ResolveLeafFromOuterLattice(ISpatialObject obj)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        return m_root.ResolveLeafFromOuterLattice(obj);
    }

    public AdmitResult AdmitForInsert(Span<ISpatialObject> buffer)
    {
        foreach (var obj in buffer)
            // the following sequence seems like it should move down to the sublattice.  why this branch cares about this?
            if (!obj.HasPositionAtDepth(LatticeDepth))
            {
                var proposedPosition = obj.GetPositionAtDepth(LatticeDepth - 1);
                var framedPosition = BoundsTransform.OuterToInnerInsertion(proposedPosition, obj.Guid);
                obj.AppendPosition(framedPosition);
            }
        return Admit(buffer);
    }
    public VenueLeafNode? ResolveOccupyingLeaf(ISpatialObject obj)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        return m_root.ResolveOccupyingLeaf(obj);
    }
    public VenueLeafNode? ResolveLeaf(ISpatialObject obj)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        return m_root.ResolveLeaf(obj);
    }
    public IEnumerable<ISpatialObject> QueryWithinDistance(LongVector3 center, ulong radius)
    {
        var results = new List<ISpatialObject>();
        SpatialLattice<TRoot>.QueryWithinDistanceRecursive(m_root, center, radius, results);
        return results;
    }
    private static void QueryWithinDistanceRecursive(ISpatialNode node, LongVector3 center, ulong radius, List<ISpatialObject> results)
    {
        if (!node.Bounds.IntersectsSphere_SimpleImpl(center, radius)) return;

        switch (node)
        {
            case VenueLeafNode leaf:
            {
                using var s = new SlimSyncer(((ISync)node).Sync, SlimSyncer.LockMode.Read, "QueryWithinDistance: VenueLeafNode");
                foreach (var obj in leaf.Occupants)
                {
                    var distSq = (obj.LocalPosition - center).MagnitudeSquaredBig;
                    if (distSq <= (BigInteger)radius * radius) results.Add(obj);
                }
                break;
            }
            case OctetParentNode parent:
                foreach (var child in parent.Children)
                    if (child.Bounds.IntersectsSphere_SimpleImpl(center, radius))
                        SpatialLattice<TRoot>.QueryWithinDistanceRecursive(child, center, radius, results);
                break;
            case SubLatticeBranchNode sub:
                var innerCenter = sub.Sublattice.BoundsTransform.OuterToInnerCanonical(center);
                var innerSize = LatticeUniverse.RootRegion.Size;
                var outerSize = sub.Sublattice.BoundsTransform.OuterLatticeBounds.Size;
                var scale = innerSize.X / (double)outerSize.X;
                var innerRadius = (ulong)(radius * scale);
                var subResults = sub.Sublattice.QueryWithinDistance(innerCenter, innerRadius);
                results.AddRange(subResults);
                break;
        }
    }
}