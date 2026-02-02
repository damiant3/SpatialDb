///////////////////////////////
using SpatialDbLib.Synchronize;
using System.Diagnostics;

namespace SpatialDbLib.Lattice;

public class SpatialLattice : OctetRootNode
{
    public SpatialLattice()
        : base(LatticeUniverse.RootRegion)
    {
        BoundsTransform = new ParentToSubLatticeTransform(LatticeUniverse.RootRegion);
    }

    public SpatialLattice(Region outerBounds, byte latticeDepth)
        : base(LatticeUniverse.RootRegion)
    {
        LatticeDepth = latticeDepth;
        BoundsTransform = new ParentToSubLatticeTransform(outerBounds);
    }

    public readonly ParentToSubLatticeTransform BoundsTransform;
    public readonly byte LatticeDepth;

    public static MultiObjectScope<(SpatialObject, IList<LongVector3>)> LockAndSnapshot(IEnumerable<SpatialObject> objects)
    {
        var lockedObjects = new List<(SpatialObject, IList<LongVector3>)>();
        var acquiredLocks = new List<SlimSyncer>();

        foreach (var obj in objects)
        {
            var s = new SlimSyncer(((ISync)obj).Sync, SlimSyncer.LockMode.Write, $"SpatialLattice.LockAndSnapshot: Object({obj.Guid})");
            acquiredLocks.Add(s);
            //lockedObjects.Add(new (obj, obj.GetPositionStack()));
        }

        return new MultiObjectScope<(SpatialObject, IList<LongVector3>)>(lockedObjects, acquiredLocks);
    }

    public AdmitResult InsertAsOne(List<SpatialObject> objs)
    {
        List<AdmitResult> results = [];
        foreach (var obj in objs)
        {
            var admitResult = Admit(obj, obj.LocalPosition, LatticeDepth);
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
    readonly object m_batchLocker = new();
    public AdmitResult Insert(List<SpatialObject> objs)
    {
        //lock (m_batchLocker)
        {
            var admitResult = Admit(objs, LatticeDepth);
            if (admitResult is AdmitResult.BulkCreated created)
            {
                foreach (var proxy in created.Proxies)
                {
                    if (proxy.IsCommitted)
                    {
                        throw new InvalidOperationException("Proxy is already committed during bulk insert commit.");
                    }
                    proxy.Commit();
                }
            }
            return admitResult;
        }
    }

    public AdmitResult Insert(SpatialObject obj)
    {
        using var s = new SlimSyncer(((ISync)obj).Sync, SlimSyncer.LockMode.Write, "SpatialLattice.Insert: Object");
        var admitResult = Admit(obj, obj.LocalPosition, LatticeDepth);
        if (admitResult is AdmitResult.Created created)
            created.Proxy.Commit();
        return admitResult;
    }

    public void Remove(SpatialObject obj)
    {
        using var s = new SlimSyncer(((ISync)obj).Sync, SlimSyncer.LockMode.Write, "SpatialLattice.Remove: Object");
        while (true)
        {
            var leaf = ResolveOccupyingLeaf(obj);
            if (leaf == null) return;
            leaf.Vacate(obj);
            return;
        }
    }

    public VenueLeafNode? ResolveLeafFromOuterLattice(SpatialObject obj)
    {
#if DEBUG
        if(obj.PositionStackDepth <= LatticeDepth)
        {
            throw new InvalidOperationException("Object position stack depth is less than or equal to lattice depth during outer lattice leaf resolution.");
        }
#endif
        return ResolveLeaf(obj);
    }

    public VenueLeafNode? ResolveOccupyingLeaf(SpatialObject obj)
    {
        var resolveleaf = ResolveLeaf(obj);
        if (resolveleaf == null) return null;
        if (!resolveleaf.Contains(obj))
        {
            return null;
        }
        return resolveleaf;
    }

    public VenueLeafNode? ResolveLeaf(SpatialObject obj)
    {
        LongVector3 pos = obj.GetPositionStack()[LatticeDepth];
        ISpatialNode current = this;
        while (current != null)
        {
            switch (current)
            {
                case SubLatticeBranchNode sublatticebranch:
                    return sublatticebranch.Sublattice.ResolveLeafFromOuterLattice(obj);

                case OctetParentNode parent:
                {
                    var result = parent.SelectChild(pos)
                        ?? throw new InvalidOperationException("Failed to select child during occupation resolution");
                    current = result.ChildNode;
                    break;
                }
                case VenueLeafNode leaf:
                {
                    using var s3 = new SlimSyncer(((ISync)leaf).Sync, SlimSyncer.LockMode.Read, "SpatialLattice.ResolveLeaf: Leaf");
                    return leaf;
                }
                default:
                    throw new InvalidOperationException("Unknown node type during occupation resolution");
            }
        }
        return null;
    }

    public override void AdmitMigrants(IList<SpatialObject> objs)
    {
        List<KeyValuePair<ISpatialNode, List<SpatialObject>>> migrantsByTargetChild = [];

#if DEBUG
        if (objs.Any(a => a.PositionStackDepth > LatticeDepth + 1))
        {
            Debugger.Break();
            throw new InvalidOperationException($"Occupant has depth > lattice");
        }
#endif

        foreach (var obj in objs)
        {
            var innerPosition = BoundsTransform.OuterToInnerInsertion(obj.LocalPosition, obj.Guid);
            obj.AppendPosition(innerPosition);
            if (!Bounds.Contains(obj.LocalPosition)) throw new InvalidOperationException("Migrant has no home.");
            if (SelectChild(obj.LocalPosition) is not SelectChildResult selectChildResult) throw new InvalidOperationException("Containment invariant violated");
            var migrantSubGroup = migrantsByTargetChild.Find(kvp => kvp.Key == selectChildResult.ChildNode);
            if (migrantSubGroup.Key != null && migrantSubGroup.Value != null)
                migrantSubGroup.Value.Add(obj);
            else
                migrantsByTargetChild.Add(new KeyValuePair<ISpatialNode, List<SpatialObject>>(selectChildResult.ChildNode, [obj]));
        }

        foreach (var kvp in migrantsByTargetChild)
            kvp.Key.AdmitMigrants(kvp.Value);
    }
}