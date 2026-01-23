///////////////////////////////
namespace SpatialDbLib.Lattice;

public class SpatialLattice : RootNode
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

    public AdmitResult Insert(SpatialObject obj)
    {
        using var s = new SlimSyncer(obj.m_positionLock, SlimSyncer.LockMode.Write);
        var admitResult = Admit(obj, obj.LocalPosition);
        if (admitResult.Response == AdmitResult.AdmitResponse.Created)
            admitResult.Proxy!.Commit();
        else
            throw new InvalidOperationException("Insertion failed: " + admitResult.Response);
        return admitResult;
    }

    public void Remove(SpatialObject obj)
    {
        using var s = new SlimSyncer(obj.m_positionLock, SlimSyncer.LockMode.Write);
        byte retries = 0;
        while (true)
        {
            var leaf = ResolveOccupyingLeaf(obj);
            if (leaf == null) return;
            var parent = leaf.Parent;
            using var s2 = new SlimSyncer(parent.DependantsSync, SlimSyncer.LockMode.Write);
            using var s3 = new SlimSyncer(leaf.DependantsSync, SlimSyncer.LockMode.Write);
            if (leaf.Parent != parent || !leaf.Contains(obj))
            {
                if (retries-- > 0)
                {
#if DEBUG
                    throw new Exception("Removal retries exhausted.");
#else
                    return;
#endif
                }
                continue;
            }
            leaf.Leave(obj);
            break;
        }
    }

    public OccupantLeafNode? ResolveLeafFromOuterLattice(SpatialObject obj)
    {
#if DEBUG
        if(obj.PositionStackDepth <= LatticeDepth)
        {
            throw new InvalidOperationException("Object position stack depth is less than or equal to lattice depth during outer lattice leaf resolution.");
        }
#endif
        return ResolveLeaf(obj);
    }

    public OccupantLeafNode? ResolveOccupyingLeaf(SpatialObject obj)
    {
        var resolveleaf = ResolveLeaf(obj);
        if (resolveleaf == null) return null;
        if (!resolveleaf.Contains(obj))
        {
            return null;
        }
        return resolveleaf;
    }

    public OccupantLeafNode? ResolveLeaf(SpatialObject obj)
    {
        LongVector3 pos = obj.GetPositionStack()[LatticeDepth];
        SpatialNode current = this;
        while (current != null)
        {
            switch (current)
            {
                case SubLatticeBranchNode sublatticebranch:
                    return sublatticebranch.m_subLattice.ResolveLeafFromOuterLattice(obj);

                case OctetParentNode parent:
                {
                    using var s2 = new SlimSyncer(parent.DependantsSync, SlimSyncer.LockMode.Read);
                    var result = parent.SelectChild(pos)
                        ?? throw new InvalidOperationException("Failed to select child during occupation resolution");
                    current = result.ChildNode;
                    break;
                }
                case OccupantLeafNode leaf:
                {
                    using var s3 = new SlimSyncer(leaf.DependantsSync, SlimSyncer.LockMode.Read);
                    return leaf;
                }
                default:
                    throw new InvalidOperationException("Unknown region type during occupation resolution");
            }
        }
        return null;
    }

    public override void AdmitMigrants(IList<SpatialObject> objs)
    {
        List<KeyValuePair<SpatialNode, List<SpatialObject>>> migrantsByTargetChild = [];
        foreach (var obj in objs)
        {
            obj.SetPositionAtDepth(LatticeDepth, BoundsTransform.OuterToInnerInsertion(obj.LocalPosition, obj.GetDiscriminator()));
            if (!Bounds.Contains(obj.LocalPosition)) throw new InvalidOperationException("Migrant has no home.");
            if (SelectChild(obj.LocalPosition) is not SelectChildResult selectChildResult) throw new InvalidOperationException("Containment invariant violated");
            var migrantSubGroup = migrantsByTargetChild.Find(kvp => kvp.Key == selectChildResult.ChildNode);
            if (migrantSubGroup.Key != null && migrantSubGroup.Value != null)
                migrantSubGroup.Value.Add(obj);
            else
                migrantsByTargetChild.Add(new KeyValuePair<SpatialNode, List<SpatialObject>>(selectChildResult.ChildNode, [obj]));
        }

        foreach (var kvp in migrantsByTargetChild)
            kvp.Key.AdmitMigrants(kvp.Value);
    }
}