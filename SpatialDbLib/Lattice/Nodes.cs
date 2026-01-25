///////////////////////////////
namespace SpatialDbLib.Lattice;

public interface IChild
{
   IParent Parent { get; }
}

public interface IParent
{
    IChild[] Children { get; }
}

public interface INode
{
    Region Bounds { get; }
    ReaderWriterLockSlim Sync { get; }
    AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition, byte latticeDepth);
    void AdmitMigrants(IList<SpatialObject> obj);
    IDisposable LockAndSnapshotForMigration();
}

public interface IParentNode
    : IParent,
      INode
{
    void ReplaceChildAt(byte index, IChildNode newChild);
    SelectChildResult? SelectChild(LongVector3 pos);
}

public interface IChildNode
    : IChild,
      INode { }

public interface IBranchNode
    : IParentNode,
      IChild { }

public interface IVenueParent
{
    IList<SpatialObject> Occupants { get; }
    bool HasAnyOccupants();
    bool IsAtCapacity();
    bool Contains(SpatialObject obj);
    void Vacate(SpatialObject obj);
    void Occupy(SpatialObject obj);
    void Replace(SpatialObjectProxy proxy);
}

public interface ILeafNode
    : IChildNode,
      IVenueParent
{
    bool CanSubdivide();
    bool IsRetired { get; }
    void Retire();

}
public interface ISeed
{
    SpatialLattice Sublattice { get; }
}

public interface ISeedNode
    : ISeed,
      IChildNode
{
}

public abstract class SpatialNode(Region bounds)
    : INode
{
    public Region Bounds { get; } = bounds;
    public ReaderWriterLockSlim Sync { get; } = new(LockRecursionPolicy.SupportsRecursion);
    public abstract AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition, byte latticeDepth);
    public abstract void AdmitMigrants(IList<SpatialObject> obj);
    public abstract IDisposable LockAndSnapshotForMigration();
}

public abstract class ParentNode(Region bounds)
    : SpatialNode(bounds),
      IParentNode
{
    public abstract IChild[] Children { get; }
    public abstract void ReplaceChildAt(byte index, IChildNode newChild);
    public abstract SelectChildResult? SelectChild(LongVector3 pos);
}

public abstract class OctetParentNode
    : ParentNode
{
    public OctetParentNode(Region bounds)
        : base(bounds)
    {
        CreateChildLeafNodes();
    }

    public override IChildNode[] Children { get; } = new IChildNode[8];

    internal void CreateChildLeafNodes()
    {
        var min = Bounds.Min;
        var max = Bounds.Max;
        var mid = Bounds.Mid;

        for (int i = 0; i < 8; i++)
        {
            var childMin = new LongVector3(
                (i & 4) != 0 ? mid.X : min.X,
                (i & 2) != 0 ? mid.Y : min.Y,
                (i & 1) != 0 ? mid.Z : min.Z
            );

            var childMax = new LongVector3(
                (i & 4) != 0 ? max.X : mid.X,
                (i & 2) != 0 ? max.Y : mid.Y,
                (i & 1) != 0 ? max.Z : mid.Z
            );

            Children[i] = new LargeLeafNode(new(childMin, childMax), this);
        }
    }

    public override SelectChildResult? SelectChild(LongVector3 pos)
    {
        if (!Bounds.Contains(pos)) return null;

        var mid = Bounds.Mid;
        byte index = (byte)(
            ((pos.X >= mid.X) ? 4 : 0) |
            ((pos.Y >= mid.Y) ? 2 : 0) |
            ((pos.Z >= mid.Z) ? 1 : 0));

        return new SelectChildResult(index, Children[index]);
    }

    public override MultiObjectScope<IChildNode> LockAndSnapshotForMigration()
    {
        var locksHeld = new List<SlimSyncer>();
        var nodesLocked = new List<IChildNode>();
        try
        {
            foreach (var child in Children)
            {
                var childLock = new SlimSyncer(child.Sync, SlimSyncer.LockMode.Write);
                locksHeld.Add(childLock);
                nodesLocked.Add(child);
            }

            return new (nodesLocked, locksHeld);
        }
        catch
        {
            foreach (var l in locksHeld)
                l.Dispose();
            throw;
        }
    }

    public override void ReplaceChildAt(byte index, IChildNode newChild)
    {
        Children[index] = newChild;
    }

    public override void AdmitMigrants(IList<SpatialObject> objs)
    {
        List<KeyValuePair<IChildNode, List<SpatialObject>>> migrantsByTargetChild = [];
        foreach (var obj in objs)
        {
            if (!Bounds.Contains(obj.LocalPosition))
                throw new InvalidOperationException("Migrant has no home.");

            if (SelectChild(obj.LocalPosition) is not SelectChildResult selectChildResult)
                throw new InvalidOperationException("Containment invariant violated");

            var migrantSubGroup = migrantsByTargetChild.Find(kvp => kvp.Key == selectChildResult.ChildNode);
            if (migrantSubGroup.Key != null && migrantSubGroup.Value != null)
                migrantSubGroup.Value.Add(obj);
            else
                migrantsByTargetChild.Add(new KeyValuePair<IChildNode, List<SpatialObject>>(selectChildResult.ChildNode, [obj]));
        }

        foreach (var kvp in migrantsByTargetChild)
            kvp.Key.AdmitMigrants(kvp.Value);
    }

    struct AdmitFrame
    {
        public IParentNode Parent;
        public byte ChildIndex;
        public INode ChildINode;
        public byte LatticeDepth;
    }

    public override AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition, byte latticeDepth)
    {
        if (!Bounds.Contains(proposedPosition))
            return AdmitResult.EscalateRequest();

        INode current = this;
        AdmitFrame frame = new() { Parent = this, ChildIndex = 0, ChildINode = this, LatticeDepth = latticeDepth };
        while (true)
        {
            if (current is IParentNode parent)
            {
                if (parent.SelectChild(proposedPosition) is not SelectChildResult selectChildResult)
                    throw new InvalidOperationException("Containment invariant violated");

                frame.Parent = parent;
                frame.ChildIndex = selectChildResult.IndexInParent;
                frame.ChildINode = selectChildResult.ChildNode;

                current = frame.ChildINode;
                continue;
            }

            var admitResult = current.Admit(obj, proposedPosition, frame.LatticeDepth);

            switch (admitResult)
            {
                case AdmitResult.Created:
                case AdmitResult.Rejected:
                case AdmitResult.Escalate:
                    return admitResult;
                case AdmitResult.Retry:
                    current = frame.Parent;
                    continue;
                case AdmitResult.Subdivide:
                case AdmitResult.Delegate:
                {
                    var subdividingleaf = frame.ChildINode as VenueLeafNode;
#if DEBUG
                    if (subdividingleaf == null)
                        throw new InvalidOperationException("Subdivision requested by non-venueleaf");
#endif
                    using var s = new SlimSyncer(frame.Parent.Sync, SlimSyncer.LockMode.Write);
                    using var s1 = new SlimSyncer(subdividingleaf.Sync, SlimSyncer.LockMode.Write);

                    if (subdividingleaf.IsRetired)
                    {
                        current = frame.Parent;
                        continue;
                    }

                    using var occupantScope = subdividingleaf.LockAndSnapshotForMigration();
                    var occupantsSnapshot = occupantScope.Objects;
                    IChildNode newBranch = admitResult is AdmitResult.Subdivide
                        ? new OctetBranchNode(subdividingleaf.Bounds, frame.Parent, occupantsSnapshot)
                        : new SubLatticeBranchNode(
                            subdividingleaf.Bounds,
                            frame.Parent,
                            latticeDepth,
                            occupantsSnapshot);
                    using var s2 = new SlimSyncer(newBranch.Sync, SlimSyncer.LockMode.Write);
                    using var s3 = newBranch.LockAndSnapshotForMigration();
                    subdividingleaf.Retire();
                    frame.Parent.Children[frame.ChildIndex] = newBranch;
                    current = frame.Parent;
                    continue;
                }
                default:
                    throw new InvalidOperationException("Unknown AdmitResponse");
            }
        }
    }
}
public class OctetRootNode(Region localBounds)
    : OctetParentNode(localBounds)
{ }

public abstract class OctetBranchBase (Region bounds)
    : OctetParentNode(bounds),
      IChildNode
{
    public abstract IParent Parent { get; }
}

public class OctetBranchNode
    : OctetBranchBase,
      IChildNode
{
    public OctetBranchNode(Region bounds, IParentNode parent, IList<SpatialObject> migrants)
    : base(bounds)
    {
        Parent = parent;
        AdmitMigrants(migrants);
    }

    public override IParentNode Parent { get; }

}

public abstract class ChildNode(Region bounds)
    : SpatialNode(bounds),
      IChildNode
{
    public abstract IParent Parent { get; }

    public virtual bool CanSubdivide()
    {
        return Bounds.Size.X > 1 && Bounds.Size.Y > 1 && Bounds.Size.Z > 1;
    }
}

public abstract class LeafNode(Region bounds, IParentNode parent)
    : ChildNode(bounds),
      ILeafNode
{
    public override IParentNode Parent { get; } = parent;
    public abstract IList<SpatialObject> Occupants { get; }
    public abstract bool IsRetired { get; }
    public abstract bool Contains(SpatialObject obj);
    public abstract bool HasAnyOccupants();
    public abstract void Vacate(SpatialObject obj);
    public abstract void Occupy(SpatialObject obj);
    public abstract void Replace(SpatialObjectProxy proxy);
    public abstract void Retire();
    public abstract int Capacity { get; }
    public abstract bool IsAtCapacity();
}

public abstract class VenueLeafNode(Region bounds, IParentNode parent)
    : LeafNode(bounds, parent)
{
    public override IList<SpatialObject> Occupants { get; } = [];
    private volatile bool m_isRetired = false;
    public override bool IsRetired => m_isRetired;

    public override bool Contains(SpatialObject obj)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read);
        return Occupants.Contains(obj);
    }
    public override bool HasAnyOccupants()
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read);
        return Occupants.Count > 0;
    }

    public override void Vacate(SpatialObject obj)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write);
        Occupants.Remove(obj);
    }

    public override void Occupy(SpatialObject obj)
    {

#if DEBUG
        if (!Sync.IsWriteLockHeld)
            throw new InvalidOperationException("Occupy called without leaf write lock");
#endif
        Occupants.Add(obj);
    }

    public override void Retire()
    {
#if DEBUG
        if (!Parent.Sync.IsWriteLockHeld)
            throw new InvalidOperationException("Leaf retired without parent write lock");
#endif
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write);
        m_isRetired = true;
        Occupants.Clear();
    }

    public override void Replace(SpatialObjectProxy proxy)
    {
#if DEBUG
        if (!Parent.Sync.IsWriteLockHeld)
            throw new InvalidOperationException(
                "Replace called without parent write lock");

        if (!Sync.IsWriteLockHeld)
            throw new InvalidOperationException(
                "Replace called without leaf write lock");
#endif
        int index = Occupants.IndexOf(proxy);
        if (index == -1)
            throw new InvalidOperationException($"Proxy not found during Replace. Occupant Count: {Occupants.Count}");
        Occupants[index] = ((SpatialObjectProxy)Occupants[index]).OriginalObject;  // this is intended to hard fail if the occupant is not the proxy
    }

    public override int Capacity => 8;
    public override bool IsAtCapacity()
    {
        return Occupants.Count >= Capacity;
    }

    public override void AdmitMigrants(IList<SpatialObject> objs)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write);
        foreach (var obj in objs)
        {
            using var s2 = new SlimSyncer(obj.m_positionLock, SlimSyncer.LockMode.Write);
            if (obj is SpatialObjectProxy proxy)
                proxy.TargetLeaf = this;
            Occupants.Add(obj);
        }
    }

    public override AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition, byte latticeDepth)
    {
        if (!Bounds.Contains(proposedPosition))
            return AdmitResult.EscalateRequest();

        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write);
        if (IsAtCapacity() || obj.PositionStackDepth > latticeDepth + 1)
        {
            if (CanSubdivide())
                return AdmitResult.SubdivideRequest(this);
            else
                return AdmitResult.DelegateRequest(this);
        }

        if(IsRetired)
            return AdmitResult.RetryRequest();

        var proxy = new SpatialObjectProxy(obj, this, proposedPosition);
        Occupy(proxy);
        return AdmitResult.Create(proxy, this);
    }

    public override MultiObjectScope<SpatialObject> LockAndSnapshotForMigration()
    {
        var snapshot = new List<SpatialObject>();
        var locksHeld = new List<SlimSyncer>();

        var leafLock = new SlimSyncer(Sync, SlimSyncer.LockMode.Write);
        locksHeld.Add(leafLock);
        try
        {
            for (int i = 0; i < Occupants.Count; i++)
            {
                SlimSyncer oLock = null!;
                try
                {
                    var obj = Occupants[i];
                    oLock = new SlimSyncer(obj.m_positionLock, SlimSyncer.LockMode.Write);
                    snapshot.Add(obj);
                    locksHeld.Add(oLock);
                }
                catch
                {
                    try { oLock?.Dispose(); } catch { }
                    throw;
                }
            }

            return new (snapshot, locksHeld);
        }
        catch
        {
            foreach (var l in locksHeld.Reverse<SlimSyncer>())
                try { l.Dispose(); } catch { }
            throw;
        }
    }
}

public class LargeLeafNode(Region bounds, OctetParentNode parent)
    : VenueLeafNode(bounds, parent)
{
    public override int Capacity => 16;
}

public class SubLatticeBranchNode
    : ChildNode,
      ISeedNode
{
    public SpatialLattice Sublattice { get; }
    public override IParentNode Parent { get; }
    public SubLatticeBranchNode(Region bounds, IParentNode parent, byte latticeDepth, IList<SpatialObject> migrants)
        : base(bounds)
    {
        Parent = parent;
        Sublattice = new(bounds, latticeDepth);
        Sublattice.AdmitMigrants(migrants);
    }
    public override void AdmitMigrants(IList<SpatialObject> objs)
    {
        Sublattice.AdmitMigrants(objs);
    }
    public override AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition, byte latticeDepth)
    {
        if (!Bounds.Contains(proposedPosition))
            return AdmitResult.EscalateRequest();

        if (!obj.HasPositionAtDepth(Sublattice.LatticeDepth))
        {
            var sublatticeFramedPosition = Sublattice.BoundsTransform.OuterToInnerInsertion(proposedPosition, obj.GetDiscriminator());
            obj.AppendPosition(sublatticeFramedPosition);
        }
        var subFramePosition = obj.GetPositionAtDepth(Sublattice.LatticeDepth);
        return Sublattice.Admit(obj, subFramePosition, (byte)(latticeDepth+1));
    }

    public override MultiObjectScope<IChildNode> LockAndSnapshotForMigration()
    {
        var locksHeld = new List<SlimSyncer>();
        var nodesLocked = new List<IChildNode>();
        try
        {
            locksHeld.Add(new(Sublattice.Sync, SlimSyncer.LockMode.Write));
            foreach (var child in Sublattice.Children)
            {
                var childLock = new SlimSyncer(child.Sync, SlimSyncer.LockMode.Write);
                locksHeld.Add(childLock);
                nodesLocked.Add(child);
            }

            return new (nodesLocked, locksHeld);
        }
        catch
        {
            foreach (var l in locksHeld.Reverse<SlimSyncer>())
                l.Dispose();
            throw;
        }
    }
}

