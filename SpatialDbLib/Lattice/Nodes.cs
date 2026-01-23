///////////////////////////////
namespace SpatialDbLib.Lattice;

public abstract class SpatialNode(Region bounds)
{
    public readonly ReaderWriterLockSlim m_dependantsSync = new(LockRecursionPolicy.SupportsRecursion);
    public ReaderWriterLockSlim DependantsSync => m_dependantsSync;
    public Region Bounds { get; } = bounds;
    public abstract AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition);
    public abstract void AdmitMigrants(IList<SpatialObject> obj);
    public abstract bool HasAnyOccupants();
    public abstract bool Contains(SpatialObject obj);
}

public abstract class OctetParentNode
    : ParentNode
{
    public OctetParentNode(Region localBounds)
        : base(localBounds)
    {
        CreateChildLeafNodes();
    }

    public virtual SpatialNode[] Children { get; } = new SpatialNode[8];

    public void CreateChildLeafNodes()
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

    public SelectChildResult? SelectChild(LongVector3 pos)
    {
        if (!Bounds.Contains(pos)) return null;

        var mid = Bounds.Mid;
        byte index = (byte)(
            ((pos.X >= mid.X) ? 4 : 0) |
            ((pos.Y >= mid.Y) ? 2 : 0) |
            ((pos.Z >= mid.Z) ? 1 : 0));

        return new SelectChildResult(index, Children[index]);
    }

    public override MultiObjectScope<SpatialNode> LockAllChildren()
    {
        var locksHeld = new List<SlimSyncer>();
        var objectsLocked = new List<SpatialNode>();
        try
        {
            foreach (var child in Children)
            {
                var childLock = new SlimSyncer(child.m_dependantsSync, SlimSyncer.LockMode.Write);
                locksHeld.Add(childLock);
                objectsLocked.Add(child);
            }

            return new (objectsLocked, locksHeld);
        }
        catch
        {
            foreach (var l in locksHeld)
                l.Dispose();
            throw;
        }
    }

    public void ReplaceChildAt(byte index, SpatialNode newChild)
    {
        Children[index] = newChild;
    }

    public override bool HasAnyOccupants() => Children.Any(c => c.HasAnyOccupants());

    public override bool Contains(SpatialObject obj) => Children.Any(c => c.Contains(obj));

    public override void AdmitMigrants(IList<SpatialObject> objs)
    {
        if (objs.Count == 0)
            throw new InvalidOperationException("Concurrency Violation, should be a non-zero length list of migrants.");

        List<KeyValuePair<SpatialNode, List<SpatialObject>>> migrantsByTargetChild = [];
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
                migrantsByTargetChild.Add(new KeyValuePair<SpatialNode, List<SpatialObject>>(selectChildResult.ChildNode, [obj]));
        }

        foreach (var kvp in migrantsByTargetChild)
            kvp.Key.AdmitMigrants(kvp.Value);
    }

    struct AdmitFrame
    {
        public OctetParentNode Parent;
        public byte ChildIndex;
        public SpatialNode Child;
    }

    public override AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition)
    {
        if (!Bounds.Contains(proposedPosition))
            return AdmitResult.RequestEscalate();

        SpatialNode current = this;
        AdmitFrame frame = new() { Parent = this, ChildIndex = 0, Child = this };
        while (true)
        {
            if (current is OctetParentNode parent)
            {
                if (parent.SelectChild(proposedPosition) is not SelectChildResult selectChildResult)
                    throw new InvalidOperationException("Containment invariant violated");

                frame.Parent = parent;
                frame.ChildIndex = selectChildResult.IndexInParent;
                frame.Child = selectChildResult.ChildNode;

                current = frame.Child;
                continue;
            }
            
            var admitResult = current.Admit(obj, proposedPosition);

            switch (admitResult.Response)
            {
                case AdmitResult.AdmitResponse.Created:
                case AdmitResult.AdmitResponse.Rejected:
                case AdmitResult.AdmitResponse.Escalate:
                    return admitResult;
                case AdmitResult.AdmitResponse.Retry:
                    current = frame.Parent;
                    continue;
                case AdmitResult.AdmitResponse.Subdivide:
                case AdmitResult.AdmitResponse.Delegate:
                {
                    var subdividingleaf = frame.Child as OccupantLeafNode;
#if DEBUG
                    if (subdividingleaf == null)
                        throw new InvalidOperationException("Subdivision requested by non-leaf");
#endif
                    using var s = new SlimSyncer(frame.Parent.m_dependantsSync, SlimSyncer.LockMode.Write);
                    using var s1 = new SlimSyncer(subdividingleaf.m_dependantsSync, SlimSyncer.LockMode.Write);

                    if (subdividingleaf.IsRetired)
                    {
                        current = frame.Parent;
                        continue;
                    }

                    using var occupantScope = subdividingleaf.LockAndSnapshotOccupants();
                    var occupantsSnapshot = occupantScope.Objects;
#if DEBUG
                    if (occupantsSnapshot.Count != 16)
                        throw new InvalidOperationException("Subdivision requested on non full leaf: " + occupantsSnapshot.Count);
#endif
                    ParentNode newBranch = admitResult.Response == AdmitResult.AdmitResponse.Subdivide
                        ? new OctetBranchNode(subdividingleaf.Bounds, frame.Parent, occupantsSnapshot)
                        : new SubLatticeBranchNode(
                            subdividingleaf.Bounds,
                            frame.Parent,
                            (byte)(occupantsSnapshot[0].PositionStackDepth),
                            occupantsSnapshot);
                    using var s2 = new SlimSyncer(newBranch.m_dependantsSync, SlimSyncer.LockMode.Write);
                    using var s3 = newBranch.LockAllChildren();
                    frame.Parent.Children[frame.ChildIndex] = newBranch;
                    subdividingleaf.RetireAfterPromotion();
                    current = frame.Parent;
                    continue;
                }
                default:
                    throw new InvalidOperationException("Unknown AdmitResponse");
            }
        }
    }
}

public abstract class ParentNode(Region localBounds)
    : SpatialNode(localBounds)
{
    public abstract IDisposable LockAllChildren();
}

public class RootNode(Region localBounds) : OctetParentNode(localBounds){}

public class OctetBranchNode : OctetParentNode
{
    public OctetBranchNode(Region bounds, ParentNode parent, IList<SpatialObject> migrants)
    : base(bounds)
    {
        Parent = parent;
        AdmitMigrants(migrants);
    }

    public ParentNode Parent { get; }

}

public abstract class LeafNode(Region bounds, ParentNode parent)
    : SpatialNode(bounds)
{
    public virtual ParentNode Parent { get; } = parent;

    public virtual bool CanSubdivide()
    {
        return Bounds.Size.X > 1 && Bounds.Size.Y > 1 && Bounds.Size.Z > 1;
    }
}

public abstract class OccupantLeafNode(Region bounds, ParentNode parent)
    : LeafNode(bounds, parent)
{
    public IList<SpatialObject> Occupants { get; } = [];
    private volatile bool m_isRetired = false;
    public bool IsRetired => m_isRetired;

    public override bool Contains(SpatialObject obj)
    {
        using var s = new SlimSyncer(m_dependantsSync, SlimSyncer.LockMode.Read);
        return Occupants.Contains(obj);
    }
    public override bool HasAnyOccupants()
    {
        using var s = new SlimSyncer(m_dependantsSync, SlimSyncer.LockMode.Read);
        return Occupants.Count > 0;
    }

    public void Leave(SpatialObject obj)
    {
        using var s = new SlimSyncer(m_dependantsSync, SlimSyncer.LockMode.Write);
        Occupants.Remove(obj);
    }

    public void Occupy(SpatialObject obj)
    {
        using var s = new SlimSyncer(m_dependantsSync, SlimSyncer.LockMode.UpgradableRead);
        if (!Occupants.Contains(obj))
        {
            s.UpgradeToWriteLock();
            Occupants.Add(obj);
        }
    }

    public void RetireAfterPromotion()
    {
        using var s = new SlimSyncer(m_dependantsSync, SlimSyncer.LockMode.Write);
        m_isRetired = true;
        Occupants.Clear();
    }

    public void Replace(SpatialObjectProxy proxy)
    {
#if DEBUG
        if (!Parent.m_dependantsSync.IsWriteLockHeld)
            throw new InvalidOperationException(
                "Replace called without parent write lock");

        if (!m_dependantsSync.IsWriteLockHeld)
            throw new InvalidOperationException(
                "Replace called without leaf write lock");
#endif
        int index = Occupants.IndexOf(proxy);
        if (index == -1)
            throw new InvalidOperationException($"Proxy not found during Replace. Occupant Count: {Occupants.Count}");
        Occupants[index] = ((SpatialObjectProxy)Occupants[index]).OriginalObject;  // this is intended to hard fail if the occupant is not the proxy
    }

    public virtual int Capacity => 8;
    protected virtual bool IsUnderPressure()
    {
        return Occupants.Count >= Capacity;
    }

    public override void AdmitMigrants(IList<SpatialObject> objs)
    {
        using var s = new SlimSyncer(m_dependantsSync, SlimSyncer.LockMode.Write);
#if DEBUG
        if(Occupants.Any())
            throw new InvalidOperationException("Cannot admit migrants to non-empty leaf");
#endif
        foreach (var obj in objs)
        {
            using var s2 = new SlimSyncer(obj.m_positionLock, SlimSyncer.LockMode.Write);
            if (obj is SpatialObjectProxy proxy)
                proxy.TargetLeaf = this;
            Occupants.Add(obj);
        }
    }

    public override AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition)
    {
        if (!Bounds.Contains(proposedPosition))
            return AdmitResult.RequestEscalate();

        using var s = new SlimSyncer(m_dependantsSync, SlimSyncer.LockMode.Write);
        if (IsUnderPressure())
        {
            if (CanSubdivide())
                return AdmitResult.RequestSubdivide();
            else
                return AdmitResult.RequestDelegate();
        }

        if(IsRetired)
            return AdmitResult.RequestRetry();

        var proxy = new SpatialObjectProxy(obj, this, proposedPosition);
        Occupy(proxy);
        return AdmitResult.Create(this, proxy);
    }

    public MultiObjectScope<SpatialObject> LockAndSnapshotOccupants()
    {
        var snapshot = new List<SpatialObject>();
        var locksHeld = new List<SlimSyncer>();

        var leafLock = new SlimSyncer(m_dependantsSync, SlimSyncer.LockMode.Write);
        locksHeld.Add(leafLock);
        try
        {
            for (int i = 0; i < Occupants.Count; i++)
            {
                SlimSyncer oLock = null!;
                try
                {
                DijkstrasNemesis:
                    var obj = Occupants[i];
                    oLock = new SlimSyncer(obj.m_positionLock, SlimSyncer.LockMode.Write);

                    if (Occupants[i] != obj)
                    {
                        oLock.Dispose();
                        if (obj is SpatialObjectProxy proxy && proxy.OriginalObject == Occupants[i])
                            goto DijkstrasNemesis;
                        else
                            throw new Exception("Concurrency Violation: Occupant changed while acquiring lock, not proxy.");
                    }

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

public class LargeLeafNode(Region bounds, ParentNode parent)
    : OccupantLeafNode(bounds, parent)
{
    public override int Capacity => 16;
}

public class SubLatticeBranchNode
    : ParentNode
{
    public readonly SpatialLattice m_subLattice;
    public ParentNode Parent { get; }

    public SubLatticeBranchNode(Region bounds, ParentNode parent, byte latticeDepth, IList<SpatialObject> migrants)
        : base(bounds)
    {
        Parent = parent;
        m_subLattice = new(bounds, latticeDepth);
        m_subLattice.AdmitMigrants(migrants);
    }

    public override bool HasAnyOccupants()
        => m_subLattice.HasAnyOccupants();

    public override bool Contains(SpatialObject obj)
        => m_subLattice.Contains(obj);

    public override void AdmitMigrants(IList<SpatialObject> objs)
    {
        m_subLattice.AdmitMigrants(objs);
    }

    public override AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition)
    {
        if (!Bounds.Contains(proposedPosition))
            return AdmitResult.RequestEscalate();

#if DEBUG
        if (obj.LocalPosition != proposedPosition)
            throw new InvalidOperationException("Object moved before admit to sublattice.");
#endif

        var sublatticeFramedPosition = m_subLattice.BoundsTransform.OuterToInnerInsertion(proposedPosition, obj.GetDiscriminator());
        obj.SetPositionAtDepth(m_subLattice.LatticeDepth, sublatticeFramedPosition);
        return m_subLattice.Admit(obj, obj.LocalPosition);
    }

    public override MultiObjectScope<SpatialNode> LockAllChildren()
    {
        var locksHeld = new List<SlimSyncer>();
        var objectsLocked = new List<SpatialNode>();
        try
        {
            locksHeld.Add(new(m_subLattice.m_dependantsSync, SlimSyncer.LockMode.Write));
            foreach (var child in m_subLattice.Children)
            {
                var childLock = new SlimSyncer(child.m_dependantsSync, SlimSyncer.LockMode.Write);
                locksHeld.Add(childLock);
                objectsLocked.Add(child);
            }

            return new (objectsLocked, locksHeld);
        }
        catch
        {
            foreach (var l in locksHeld.Reverse<SlimSyncer>())
                l.Dispose();
            throw;
        }
    }
}

