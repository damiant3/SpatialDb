using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
///////////////////////////////
namespace SpatialDbLib.Lattice;

public class OctetRootNode(Region bounds, byte latticeDepth)
    : RootNode<OctetParentNode, OctetBranchNode, VenueLeafNode, OctetRootNode>(bounds, latticeDepth) { }

public class SpatialLattice: SpatialLattice<OctetRootNode>
{
    public SpatialLattice() : base() { }
    public SpatialLattice(Region outerBounds, byte latticeDepth) : base(outerBounds, latticeDepth) { }
}

public interface ISpatialLattice : ISpatialNode
{
    ISpatialNode GetRootNode();
    byte LatticeDepth { get; }
    VenueLeafNode? ResolveLeafFromOuterLattice(SpatialObject obj);
    AdmitResult AdmitForInsert(Span<SpatialObject> buffer);
    ParentToSubLatticeTransform BoundsTransform { get; }
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
    {
        // Default creates SpatialRootNode, but can be overridden
        return (TRoot)(IRootNode<OctetParentNode, OctetBranchNode, VenueLeafNode, TRoot>) new OctetRootNode(bounds, depth);
    }

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
        {
            LatticeDepthContext.CurrentDepth = m_previousDepth;
        }
    }

    public AdmitResult InsertAsOne(List<SpatialObject> objs)
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
                {
                    if (result is AdmitResult.Created createdRollback)
                    {
                        createdRollback.Proxy.Rollback();
                    }
                }
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
    public AdmitResult Insert(List<SpatialObject> objs)
    {
        return Insert(objs.ToArray());
    }

    public AdmitResult Insert(SpatialObject[] objs)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        var admitResult = Admit(objs);
        if (admitResult is AdmitResult.BulkCreated created)
        {
            foreach (var proxy in created.Proxies)
            {
                if (proxy.IsCommitted) throw new InvalidOperationException("Proxy is already committed during bulk insert commit.");
                proxy.Commit();
            }
        }
        return admitResult;
    }

    public AdmitResult Insert(SpatialObject obj)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        using var s2 = new SlimSyncer(((ISync)obj).Sync, SlimSyncer.LockMode.Write, "SpatialLattice.Insert: Object");
        var admitResult = Admit(obj, obj.GetPositionAtDepth(LatticeDepthContext.CurrentDepth));
        if (admitResult is AdmitResult.Created created)
            created.Proxy.Commit();
        return admitResult;
    }

    public void Remove(SpatialObject obj)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        using var s2 = new SlimSyncer(((ISync)obj).Sync, SlimSyncer.LockMode.Write, "SpatialLattice.Remove: Object");
        while (true)
        {
            var leaf = m_root.ResolveOccupyingLeaf(obj);
            if (leaf == null) return;
            leaf.Vacate(obj);
            return;
        }
    }
    public void AdmitMigrants(IList<SpatialObject> objs)
    {
        foreach (var obj in objs)
        {
            var inner = BoundsTransform.OuterToInnerInsertion(obj.LocalPosition, obj.Guid);
            obj.AppendPosition(inner);
        }
        m_root.AdmitMigrants(objs);
    }

    public AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        return m_root.Admit(obj, proposedPosition);
    }

    public AdmitResult Admit(Span<SpatialObject> buffer)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        return m_root.Admit(buffer);
    }

    public VenueLeafNode? ResolveLeafFromOuterLattice(SpatialObject obj)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        return m_root.ResolveLeafFromOuterLattice(obj);
    }

    public AdmitResult AdmitForInsert(Span<SpatialObject> buffer)
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

    internal VenueLeafNode? ResolveOccupyingLeaf(SpatialObject obj)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        return m_root.ResolveOccupyingLeaf(obj);
    }

    internal VenueLeafNode? ResolveLeaf(SpatialObject obj)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        return m_root.ResolveLeaf(obj);
    }
}