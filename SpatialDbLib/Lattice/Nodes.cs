///////////////////////////////
using SpatialDbLib.Synchronize;
using System.Buffers;

namespace SpatialDbLib.Lattice;

public interface ISpatialNode
{
    AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition, byte latticeDepth);
    AdmitResult Admit(IReadOnlyList<SpatialObject> objs, byte latticeDepth);
    void AdmitMigrants(IList<SpatialObject> obj);
    IDisposable LockAndSnapshotForMigration();
}

public interface IParentNode : ISpatialNode
{
}

public interface IChildNode : ISpatialNode
{
}

public abstract class SpatialNode(Region bounds)
    : ISync
{
    public Region Bounds { get; } = bounds;

    protected readonly ReaderWriterLockSlim Sync  = new(LockRecursionPolicy.SupportsRecursion);
    ReaderWriterLockSlim ISync.Sync => Sync;

    public abstract AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition, byte latticeDepth);
    public abstract void AdmitMigrants(IList<SpatialObject> obj);
    public abstract IDisposable LockAndSnapshotForMigration();

    public virtual AdmitResult Admit(IReadOnlyList<SpatialObject> objs, byte latticeDepth)
    {
        // Fallback: scalar path, preserves semantics
        List<AdmitResult> results = [];
        foreach (var obj in objs)
        {
            var result = Admit(obj, obj.LocalPosition, latticeDepth);
            results.Add(result);

            if (result is not AdmitResult.Created)
            {
                Rollback(results);
                return result;
            }
        }

        return new AdmitResult.BulkCreated(
            [.. results
                .OfType<AdmitResult.Created>()
                .Select(r => r.Proxy)]);
    }

    protected static void Rollback(IEnumerable<AdmitResult> results)
    {
        foreach (var r in results)
            if (r is AdmitResult.Created c)
                c.Proxy.Rollback();
    }

    protected static void Commit(IEnumerable<AdmitResult> results)
    {
        foreach (var r in results)
            if (r is AdmitResult.Created c)
                c.Proxy.Commit();
    }
}

public abstract class OctetParentNode
    : SpatialNode,
      IParentNode
{
    public OctetParentNode(Region bounds)
        : base(bounds)
    {
        CreateChildLeafNodes();
    }

    public IChildNode[] Children { get; } = new IChildNode[8];

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

    public override MultiObjectScope<IChildNode> LockAndSnapshotForMigration()
    {
        var locksAcquired = new List<SlimSyncer>();
        var nodesLocked = new List<IChildNode>();
        try
        {
            foreach (var child in Children)
            {
                var childLock = new SlimSyncer(((ISync)child).Sync, SlimSyncer.LockMode.Write, "Parent.LockAndSnap: Child");
                locksAcquired.Add(childLock);
                nodesLocked.Add(child);
            }

            return new (nodesLocked, locksAcquired);
        }
        catch
        {
            foreach (var l in locksAcquired)
                l.Dispose();
            throw;
        }
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

    public struct AdmitFrame
    {
        public OctetParentNode? Parent;
        public IChildNode? ChildNode;
        public byte ChildIndex;
        public byte LatticeDepth;
    }

    public override AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition, byte latticeDepth)
    {
        if (!Bounds.Contains(proposedPosition))
            return AdmitResult.EscalateRequest();

        ISpatialNode current = this;
        AdmitFrame frame = new() { LatticeDepth = latticeDepth };
        while (true)
        {
            if (current is OctetParentNode parent)
            {
                if (parent.SelectChild(proposedPosition) is not SelectChildResult selectChildResult)
                    throw new InvalidOperationException("Containment invariant violated");

                frame.Parent = parent;
                frame.ChildIndex = selectChildResult.IndexInParent;
                frame.ChildNode = selectChildResult.ChildNode;

                current = frame.ChildNode;
                continue;
            }
#if DEBUG
            if(current == null)
                throw new InvalidOperationException("Current node is null during Admit");
#endif
            var admitResult = current.Admit(obj, proposedPosition, frame.LatticeDepth);

            switch (admitResult)
            {
                case AdmitResult.Created:
                case AdmitResult.Rejected:
                case AdmitResult.Escalate:
                    return admitResult;
                case AdmitResult.Retry:
#if DEBUG
                if(frame.Parent == null)
                    throw new InvalidOperationException("Retry requested at root node");
#endif
                    current = frame.Parent;
                    continue;
                case AdmitResult.Subdivide:
                case AdmitResult.Delegate:
                {
                    var subdividingleaf = frame.ChildNode as VenueLeafNode;
#if DEBUG
                    if (subdividingleaf == null)
                        throw new InvalidOperationException("Subdivision requested by non-venueleaf");
                    if (frame.Parent == null)
                        throw new InvalidOperationException("Subdivision requested at root node");
#endif
                    if (subdividingleaf.IsRetired)
                    {
                        current = frame.Parent;
                        continue;
                    }
                    SubdivideAndMigrate(frame.Parent, subdividingleaf, latticeDepth, frame.ChildIndex, admitResult is AdmitResult.Subdivide);
                    current = frame.Parent;
                    continue;
                }
                default:
                    throw new InvalidOperationException("Unknown AdmitResponse");
            }
        }
    }

    private static void SubdivideAndMigrate(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        using var parentLock = new SlimSyncer(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent");
        using var leafLock = new SlimSyncer(((ISync)subdividingleaf).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf");
        if(subdividingleaf.IsRetired)
            return;
        var migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        var occupantsSnapshot = migrationSnapshot.Objects;
        IChildNode newBranch = branchOrSublattice
            ? new OctetBranchNode(subdividingleaf.Bounds, parent, occupantsSnapshot)
            : new SubLatticeBranchNode(subdividingleaf.Bounds, parent, (byte)(latticeDepth + 1), occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }

    internal sealed class AdmitWorkFrame
    {
        public OctetParentNode Parent;
        public byte LatticeDepth;

        public SpatialObject[] Buffer;
        public int Start;
        public int Length;

        public int BucketCount;
        public int NextBucket;
        public (int Start, int Length)[] Buckets; // length = 8
        public int OriginalStart;
        public int OriginalLength;
        public byte[] BucketChildIndex;

        public List<SpatialObjectProxy> Proxies = [];

        public AdmitWorkFrame(OctetParentNode parent, SpatialObject[] buffer, int start, int length, byte latticeDepth)
        {
            Parent = parent;
            Buffer = buffer;
            Start = start;
            Length = length;
            OriginalStart = start;
            OriginalLength = length;
            LatticeDepth = latticeDepth;
            Buckets = new (int, int)[8];
            BucketChildIndex = new byte[8];
            Partition();
        }

        void Partition()
        {
            Span<int> counts = stackalloc int[8];

            var objectSpan = Buffer.AsSpan(Start, Length);
            var childAssignments = new SelectChildResult[Length];
            for (int i = 0; i < objectSpan.Length; i++)
            {
                childAssignments[i] = Parent.SelectChild(objectSpan[i].GetPositionAtDepth(LatticeDepth)) ?? throw new InvalidOperationException("Containment invariant violated");
                counts[childAssignments[i].IndexInParent]++;
            }

            Span<int> offsets = stackalloc int[8];
            int running = 0;
            int nextIdx = 0;

            for (int i = 0; i < 8; i++)
            {
                int count = counts[i];
                if (count == 0)
                    continue;

                offsets[i] = running;
                Buckets[nextIdx] = (Start + running, count);
                BucketChildIndex[nextIdx] = (byte)i;

                running += count;
                nextIdx++;
            }

            BucketCount = nextIdx;

            var temp = ArrayPool<SpatialObject>.Shared.Rent(Length);
            try
            {
                for (int i = 0; i < objectSpan.Length; i++)
                {
                    temp[offsets[childAssignments[i].IndexInParent]++] = objectSpan[i];
                }

                temp.AsSpan(0, Length).CopyTo(objectSpan);
            }
            finally
            {
                ArrayPool<SpatialObject>.Shared.Return(temp, clearArray: false);
            }
        }
    }

    public override AdmitResult Admit(IReadOnlyList<SpatialObject> objs, byte latticeDepth)
    {
        // Single materialization, once.
        var buffer = objs as SpatialObject[] ?? objs.ToArray();

        var stack = new Stack<AdmitWorkFrame>();
        stack.Push(new AdmitWorkFrame(this, buffer, 0, buffer.Length, latticeDepth));

        AdmitResult.BulkCreated? topResult = null;

        try
        {
            while (stack.Count > 0)
            {
                var frame = stack.Peek();

                if (frame.NextBucket == frame.BucketCount)
                {
                    stack.Pop();

                    if (stack.Count > 0)
                    {
                        var parentFrame = stack.Peek();
                        parentFrame.Proxies.AddRange(frame.Proxies);
                        continue;
                    }

                    topResult = new AdmitResult.BulkCreated(frame.Proxies);
                    break;
                }

                var (start, length) = frame.Buckets[frame.NextBucket];

                if (length == 0)
                {
                    frame.NextBucket++;
                    continue;
                }

                // Determine child *at time of descent*
                var child = frame.Parent.Children[frame.BucketChildIndex[frame.NextBucket]];

                // descend
                if (child is OctetParentNode parent)
                {
                    stack.Push(
                        new AdmitWorkFrame(
                            parent,
                            frame.Buffer,
                            start,
                            length,
                            frame.LatticeDepth));
                    frame.NextBucket++;
                    continue;
                }

                // leaf path
                var leaf = (SpatialNode)child;
                var slice = new ArraySegment<SpatialObject>(frame.Buffer, start, length);

                var result = leaf.Admit(slice, frame.LatticeDepth);

                switch (result)
                {
                    case AdmitResult.BulkCreated bulk:
                        frame.Proxies.AddRange(bulk.Proxies);
                        frame.NextBucket++;
                        break;

                    case AdmitResult.Retry:
                        break;

                    case AdmitResult.Subdivide:
                    case AdmitResult.Delegate:
                    {
                        if (leaf is not VenueLeafNode venueLeaf)
                            throw new InvalidOperationException("Subdivision requested by non-venueleaf");

                        if (!venueLeaf.IsRetired)
                            SubdivideAndMigrate(
                                frame.Parent,
                                venueLeaf,
                                frame.LatticeDepth,
                                frame.BucketChildIndex[frame.NextBucket],
                                result is AdmitResult.Subdivide);

                        break;
                    }

                    default:
                        throw new InvalidOperationException("Unknown AdmitResult");
                }
            }

            return topResult!;
        }
        catch
        {
            foreach (var frame in stack)
            {
                foreach (var proxy in frame.Proxies)
                    proxy.Rollback();
            }
            throw;
        }
    }
}
    public class OctetRootNode(Region localBounds)
    : OctetParentNode(localBounds)
{ }

public class OctetBranchNode
    : OctetParentNode,
      IChildNode
{
    public OctetBranchNode(Region bounds, OctetParentNode parent, IList<SpatialObject> migrants)
    : base(bounds)
    {
        Parent = parent;
        AdmitMigrants(migrants);
    }

    public IParentNode Parent { get; }

}

public abstract class LeafNode(Region bounds, IParentNode parent)
    : SpatialNode(bounds),
      IChildNode
{
    public IParentNode Parent { get; } = parent;
    public abstract bool IsRetired { get; }
    public abstract bool Contains(SpatialObject obj);
    public abstract bool HasAnyOccupants();
    public abstract int Capacity { get; }
    public abstract bool IsAtCapacity(int toAdd);
    public virtual bool CanSubdivide()
    {
        return Bounds.Size.X > 1 && Bounds.Size.Y > 1 && Bounds.Size.Z > 1;
    }
}

public abstract class VenueLeafNode(Region bounds, IParentNode parent)
    : LeafNode(bounds, parent)
{
    internal IList<SpatialObject> Occupants { get; } = [];
    internal bool m_isRetired = false;
    public override bool IsRetired => m_isRetired;

    public override bool Contains(SpatialObject obj)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "Venue.Contains: Leaf");
        return Occupants.Contains(obj);
    }
    public override bool HasAnyOccupants()
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "Venue.HasAnyOccupants: Leaf");
        return Occupants.Count > 0;
    }

    internal void Vacate(SpatialObject obj)
    {
        Occupants.Remove(obj);
    }

    private void Occupy(SpatialObject obj)
    {
        // private, assumes write lock held and is not retired
        Occupants.Add(obj);
    }

    internal void Retire()
    {
        Occupants.Clear();
        m_isRetired = true;
    }

    internal void Replace(SpatialObjectProxy proxy)
    {
        int index = Occupants.IndexOf(proxy);
        if (index == -1)
            throw new InvalidOperationException($"Proxy not found during Replace. Occupant Count: {Occupants.Count}");
        Occupants[index] = ((SpatialObjectProxy)Occupants[index]).OriginalObject;  // this is intended to hard fail if the occupant is not the proxy
    }

    public override int Capacity => 8;
    public override bool IsAtCapacity(int toAdd = 1)
    {
        return Occupants.Count + toAdd > Capacity;
    }

    public override void AdmitMigrants(IList<SpatialObject> objs)
    {
        foreach (var obj in objs)
        {
            if (obj is SpatialObjectProxy proxy)
                proxy.TargetLeaf = this;
            Occupants.Add(obj);
        }
    }

    public override AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition, byte latticeDepth)
    {
        if (!Bounds.Contains(proposedPosition))
            return AdmitResult.EscalateRequest();

        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "Venue.Admit: Leaf");
        if (IsRetired)
            return AdmitResult.RetryRequest();

        if (IsAtCapacity(1) || obj.PositionStackDepth > latticeDepth + 1)
        {
            return CanSubdivide()
                ? AdmitResult.SubdivideRequest(this)
                : AdmitResult.DelegateRequest(this);
        }

        var proxy = new SpatialObjectProxy(obj, this, proposedPosition);
        Occupy(proxy);
        return AdmitResult.Create(proxy);
    }

    public override AdmitResult Admit(IReadOnlyList<SpatialObject> objs, byte latticeDepth)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "Venue.AdmitList: Leaf");
        if (IsRetired)
            return AdmitResult.RetryRequest();

        if (IsAtCapacity(objs.Count) || objs.Any(obj => obj.PositionStackDepth > latticeDepth + 1))
        {
            return CanSubdivide()
                ? AdmitResult.SubdivideRequest(this)
                : AdmitResult.DelegateRequest(this);
        }

        var outProxies = new List<SpatialObjectProxy>();

        foreach (var obj in objs)
        {
            var proxy = new SpatialObjectProxy(obj, this, obj.LocalPosition);
            Occupy(proxy);
            outProxies.Add(proxy);
        }

        return AdmitResult.BulkCreate(outProxies);
    }

    public override MultiObjectScope<SpatialObject> LockAndSnapshotForMigration()
    {
        var snapshot = new List<SpatialObject>();
        var locksAcquired = new List<SlimSyncer>();

        if (!Sync.IsWriteLockHeld)
        {
            var leafLock = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "Venue.LockAndSnap: Leaf");
            locksAcquired.Add(leafLock);
        }
        try
        {
            for (int i = 0; i < Occupants.Count; i++)
            {
                SlimSyncer syncer = null!;
                try
                {
                    var occupant = Occupants[i];
                    if (!((ISync)occupant).Sync.IsWriteLockHeld)
                    {
                        syncer = new SlimSyncer(((ISync)occupant).Sync, SlimSyncer.LockMode.Write, "Venue.LockAndSnap: Occupant");
                        locksAcquired.Add(syncer);
                    }
                    snapshot.Add(occupant);

                }
                catch
                {
                    try { syncer?.Dispose(); } catch { }
                    throw;
                }
            }

            return new (snapshot, locksAcquired);
        }
        catch
        {
            foreach (var l in locksAcquired)
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
    : SpatialNode,
      IChildNode
{
    public SpatialLattice Sublattice { get; }
    public IParentNode Parent { get; }
    public SubLatticeBranchNode(Region bounds, IParentNode parent, byte latticeDepth, IList<SpatialObject> migrants)
        : base(bounds)
    {
        Parent = parent;
        Sublattice = new(bounds, latticeDepth);
#if DEBUG
        if (migrants.Any(a => a.PositionStackDepth > latticeDepth + 1))
        {
            throw new InvalidOperationException($"Occupant has depth > lattice");
        }
#endif
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
            var sublatticeFramedPosition = Sublattice.BoundsTransform.OuterToInnerInsertion(proposedPosition, obj.Guid);
            obj.AppendPosition(sublatticeFramedPosition);
        }
        var subFramePosition = obj.GetPositionAtDepth(Sublattice.LatticeDepth);
        return Sublattice.Admit(obj, subFramePosition, (byte)(latticeDepth+1));
    }

    public override MultiObjectScope<IChildNode> LockAndSnapshotForMigration()
    {
        var locksAcquired = new List<SlimSyncer>();
        var nodesLocked = new List<IChildNode>();
        try
        {
            if (!((ISync)Sublattice).Sync.IsWriteLockHeld)
            {
                locksAcquired.Add(new(((ISync)Sublattice).Sync, SlimSyncer.LockMode.Write, "SublatticeBranch.LockAndSnap: Sublattice"));
            }
            foreach (var child in Sublattice.Children)
            {
                if (!((ISync)child).Sync.IsWriteLockHeld)
                {
                    var childLock = new SlimSyncer(((ISync)child).Sync, SlimSyncer.LockMode.Write, "SublatticeBranch.LockAndSnap: Child");
                    locksAcquired.Add(childLock);
                }
                nodesLocked.Add(child);
            }

            return new (nodesLocked, locksAcquired);
        }
        catch
        {
            foreach (var l in locksAcquired)
                l.Dispose();
            throw;
        }
    }


    public override AdmitResult Admit(IReadOnlyList<SpatialObject> objs, byte latticeDepth)
    {
#if DEBUG
        foreach (var obj in objs)
        {
            var proposedPosition = obj.GetPositionAtDepth(latticeDepth);
            if (!Bounds.Contains(proposedPosition))
                throw new InvalidOperationException($"Object outside SublatticeBranch bounds: {proposedPosition}");
        }
#endif

        foreach (var obj in objs)
            if (!obj.HasPositionAtDepth(Sublattice.LatticeDepth))
            {
                var proposedPosition = obj.GetPositionAtDepth(latticeDepth);
                var framedPosition = Sublattice.BoundsTransform.OuterToInnerInsertion(proposedPosition, obj.Guid);
                obj.AppendPosition(framedPosition);
            }

        return Sublattice.Admit(objs, (byte)(latticeDepth + 1));
    }
}

