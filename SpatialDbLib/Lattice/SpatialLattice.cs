///////////////////////////////
using SpatialDbLib.Synchronize;

namespace SpatialDbLib.Lattice;

public class SpatialRootNode
    : RootNode<OctetParentNode, OctetBranchNode, LargeLeafNode, SpatialRootNode>
{
    public SpatialRootNode(Region bounds, byte latticeDepth)
        : base(bounds, latticeDepth)
    {
    }

    protected override OctetBranchNode CreateBranch(OctetParentNode parent, Region bounds)
    {
        throw new NotImplementedException();
    }

    protected override LargeLeafNode CreateVenue(OctetParentNode parent, Region bounds)
    {
        throw new NotImplementedException();
    }
}
public interface ISpatialLattice : ISpatialNode
{
    byte LatticeDepth { get; }
    VenueLeafNode? ResolveLeafFromOuterLattice(SpatialObject obj);
    AdmitResult AdmitForInsert(Span<SpatialObject> buffer);

    ParentToSubLatticeTransform BoundsTransform { get; }

}

public class SpatialLattice
    : ISpatialLattice,
      ISpatialNode
{
    [ThreadStatic] private static byte t_latticeDepth;
    public static byte CurrentThreadLatticeDepth
    => t_latticeDepth;

    internal protected readonly SpatialRootNode m_root;
    public ParentToSubLatticeTransform BoundsTransform { get; protected set; }

    public SpatialLattice() // for the rootiest of roots
        : this(LatticeUniverse.RootRegion, 0)
    {
    }
    
    public SpatialLattice(Region outerBounds, byte latticeDepth)
    {
        m_root = new SpatialRootNode(LatticeUniverse.RootRegion, latticeDepth)
        {
            OwningLattice = this
        };
        BoundsTransform = new ParentToSubLatticeTransform(outerBounds);
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
        var admitResult = Admit(obj, obj.LocalPosition);
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

    public byte LatticeDepth  => m_root.LatticeDepth;

    // i need a class/method that returns an IDisposable, and on Dispose it resets the lattice depth to the previous value.
    // this is for the case where we need to temporarily set the lattice depth for a call, but we want to be sure it gets reset even if an exception is thrown.
    public IDisposable PushLatticeDepth(byte depth)
    {
        return new LatticeDepthScope(depth);
    }
    class LatticeDepthScope : IDisposable
    {
        public LatticeDepthScope(byte depth)
        {
            t_latticeDepth = depth;
        }
        public void Dispose()
        {
            t_latticeDepth--;
        }
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

    // is this useful?  presumably nobody can even target this during construction/migration into it.
    //public IDisposable LockAndSnapshotForMigration()
    //{
    //    return m_root.LockAndSnapshotForMigration(out var snapshot);
    //}

    public VenueLeafNode? ResolveLeafFromOuterLattice(SpatialObject obj)
    {
        using var s = PushLatticeDepth(LatticeDepth);
        return m_root.ResolveLeafFromOuterLattice(obj);
    }

    public AdmitResult AdmitForInsert(Span<SpatialObject> buffer)
    {
        foreach (var obj in buffer)
            // the following sequence seems like it should move down to the sublattice.  why this branch cares about this.
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

    //public static MultiObjectScope<(SpatialObject, IList<LongVector3>)> LockAndSnapshot(IEnumerable<SpatialObject> objects)
    //{
    //    var lockedObjects = new List<(SpatialObject, IList<LongVector3>)>();
    //    var acquiredLocks = new List<SlimSyncer>();

    //    foreach (var obj in objects)
    //    {
    //        var s = new SlimSyncer(((ISync)obj).Sync, SlimSyncer.LockMode.Write, $"SpatialLattice.LockAndSnapshot: Object({obj.Guid})");
    //        acquiredLocks.Add(s);
    //        //lockedObjects.Add(new (obj, obj.GetPositionStack()));
    //    }

    //    return new MultiObjectScope<(SpatialObject, IList<LongVector3>)>(lockedObjects, acquiredLocks);
    //}






}