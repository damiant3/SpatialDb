///////////////////////////////
using SpatialDbLib.Synchronize;
using System.Buffers;

namespace SpatialDbLib.Lattice;

public interface ISpatialNode
{
    AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition);
    AdmitResult Admit(Span<SpatialObject> buffer);
    void AdmitMigrants(IList<SpatialObject> obj);
    IDisposable LockAndSnapshotForMigration();
}

public abstract class SpatialNode(Region bounds)
    : ISync
{
    public Region Bounds { get; } = bounds;

    protected readonly ReaderWriterLockSlim Sync  = new(LockRecursionPolicy.SupportsRecursion);
    ReaderWriterLockSlim ISync.Sync => Sync;

    public abstract void AdmitMigrants(IList<SpatialObject> obj);
    public abstract IDisposable LockAndSnapshotForMigration();

    public abstract AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition);
    public abstract AdmitResult Admit(Span<SpatialObject> buffer);
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

public interface IParentNode : ISpatialNode { }
public interface IChildNode : ISpatialNode { }

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
            if (!Bounds.Contains(obj.LocalPosition)) throw new InvalidOperationException("Migrant has no home.");
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

    public struct AdmitFrame(OctetParentNode parent, IChildNode childNode, byte childIndex)
    {
        public OctetParentNode Parent = parent;
        public IChildNode ChildNode = childNode;
        public byte ChildIndex = childIndex;
    }

    public override AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition)
    {
        if (!Bounds.Contains(proposedPosition)) return AdmitResult.EscalateRequest();

        ISpatialNode current = this;
        AdmitFrame frame = new(this, Children[0], 0);
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

            if (current is not LeafNode leaf) throw new InvalidOperationException("Current node is not a LeafNode during Admit");
            var admitResult = current is VenueLeafNode venue && obj.PositionStackDepth > ((SpatialLattice)this).LatticeDepth + 1 // deep insert
                ? venue.CanSubdivide() ? AdmitResult.SubdivideRequest(venue) : AdmitResult.DelegateRequest(venue)
                : leaf.Admit(obj, proposedPosition);

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
                    var subdividingleaf = frame.ChildNode as VenueLeafNode;
#if DEBUG
                    if (subdividingleaf == null)
                        throw new InvalidOperationException("Subdivision requested by non-venueleaf");
                    if (frame.Parent == null)
                        throw new InvalidOperationException("Subdivision requested at root node");
#endif
                    if (!subdividingleaf.IsRetired)
                        SubdivideAndMigrate(frame.Parent, subdividingleaf, ((SpatialLattice)this).LatticeDepth, frame.ChildIndex, admitResult is AdmitResult.Subdivide);

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
        if(subdividingleaf.IsRetired) return;
        var migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        var occupantsSnapshot = migrationSnapshot.Objects;
        IChildNode newBranch = branchOrSublattice
            ? new OctetBranchNode(subdividingleaf.Bounds, parent, occupantsSnapshot)
            : new SubLatticeBranchNode(subdividingleaf.Bounds, parent, (byte)(latticeDepth + 1), occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }

    internal sealed class BufferSlice(int start, int length)
    {
        public int Start { get; } = start;
        public int Length { get; } = length;
        public Span<SpatialObject> GetSpan(Span<SpatialObject> rootBuffer) => rootBuffer.Slice(Start, Length);
    }

    internal sealed class AdmitWorkFrame
    {
        public OctetParentNode Parent;
        public BufferSlice WorkingSlice;
        public byte BucketCount;
        public byte NextBucket;
        public BufferSlice[] Buckets;
        public byte[] BucketChildIndex;
        public List<SpatialObjectProxy> Proxies;

        public AdmitWorkFrame(OctetParentNode parent, Span<SpatialObject> rootBuffer, BufferSlice workingSlice, byte latticeDepth)
        {
            Parent = parent;
            WorkingSlice = workingSlice;
            NextBucket = 0;
            BucketCount = 0;
            Buckets = new BufferSlice[8];
            BucketChildIndex = new byte[8];
            Proxies = [];
            PartitionInPlace(workingSlice.GetSpan(rootBuffer), latticeDepth);
        }

        void PartitionInPlace(Span<SpatialObject> span, byte latticeDepth)
        {
            var mid = Parent.Bounds.Mid;
            var octants = ArrayPool<byte>.Shared.Rent(span.Length);
            try
            {
                for (int i = 0; i < span.Length; i++)
                {
                    var pos = span[i].GetPositionAtDepth(latticeDepth);
                    octants[i] = (byte)(
                        ((pos.X >= mid.X) ? 4 : 0) |
                        ((pos.Y >= mid.Y) ? 2 : 0) |
                        ((pos.Z >= mid.Z) ? 1 : 0));
                }
                Span<int> counts = stackalloc int[8];
                for (int i = 0; i < span.Length; i++) counts[octants[i]]++;

                int runningOffset = 0;
                byte bucketIdx = 0;
                for (byte i = 0; i < 8; i++)
                {
                    if (counts[i] == 0) continue;
                    Buckets[bucketIdx] = new (WorkingSlice.Start + runningOffset, counts[i]);
                    BucketChildIndex[bucketIdx] = i;
                    bucketIdx++;
                    runningOffset += counts[i];
                }
                BucketCount = bucketIdx;

                Span<int> bucketStart = stackalloc int[BucketCount];
                Span<int> bucketEnd = stackalloc int[BucketCount];
                for (byte i = 0; i < BucketCount; i++)
                {
                    bucketStart[i] = Buckets[i].Start - WorkingSlice.Start;
                    bucketEnd[i] = bucketStart[i] + Buckets[i].Length;
                }

                Span<int> octantToBucket = stackalloc int[8];
                octantToBucket.Fill(-1);
                for (byte i = 0; i < BucketCount; i++) octantToBucket[BucketChildIndex[i]] = i;
                for (byte b = 0; b < BucketCount; b++)
                {
                    int pos = bucketStart[b];
                    byte octant = BucketChildIndex[b];
                    while (pos < bucketEnd[b])
                    {
                        byte targetOctant = octants[pos];
                        if (targetOctant == octant) { pos++; continue; }
                        int targetBucket = octantToBucket[targetOctant];
                        int swapPos = bucketStart[targetBucket]++;
                        (span[pos], span[swapPos]) = (span[swapPos], span[pos]);
                        (octants[pos], octants[swapPos]) = (octants[swapPos], octants[pos]);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(octants, clearArray: false);
            }
        }
    }

    public override AdmitResult Admit(Span<SpatialObject> buffer)
    {
        var stack = new Stack<AdmitWorkFrame>();
        stack.Push(new AdmitWorkFrame(this, buffer, new BufferSlice(0, buffer.Length), ((SpatialLattice)this).LatticeDepth));
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

                var bucket = frame.Buckets[frame.NextBucket];
                if (bucket.Length == 0) { frame.NextBucket++; continue; }

                var child = frame.Parent.Children[frame.BucketChildIndex[frame.NextBucket]];
                if (child is OctetParentNode parent)
                {
                    stack.Push(new AdmitWorkFrame(parent, buffer, bucket, ((SpatialLattice)this).LatticeDepth));
                    frame.NextBucket++;
                    continue;
                }

                var leaf = (LeafNode)child;
                var slice = bucket.GetSpan(buffer);
                var result = leaf is VenueLeafNode venue && NeedsDeeper(slice) // deep insert
                    ? venue.CanSubdivide() ? AdmitResult.SubdivideRequest(venue) : AdmitResult.DelegateRequest(venue)
                    : leaf.Admit(slice);

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
                                ((SpatialLattice)this).LatticeDepth,
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
                foreach (var proxy in frame.Proxies)
                    proxy.Rollback();
            throw;
        }

        bool NeedsDeeper(Span<SpatialObject> slice)
        {
            foreach (var obj in slice)
                if (obj.PositionStackDepth > ((SpatialLattice)this).LatticeDepth + 1)
                    return true;
            return false;
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
    public bool IsRetired => m_isRetired;

    public bool Contains(SpatialObject obj)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "Venue.Contains: Leaf");
        return Occupants.Contains(obj);
    }
    public bool HasAnyOccupants()
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

    public virtual int Capacity => 8;
    public bool IsAtCapacity(int toAdd = 1)
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

    public override AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition)
    {
        if (!Bounds.Contains(proposedPosition))
            return AdmitResult.EscalateRequest();

        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "Venue.Admit: Leaf");
        if (IsRetired)
            return AdmitResult.RetryRequest();

        if (IsAtCapacity(1))
        {
            return CanSubdivide()
                ? AdmitResult.SubdivideRequest(this)
                : AdmitResult.DelegateRequest(this);
        }

        var proxy = new SpatialObjectProxy(obj, this, proposedPosition);
        Occupy(proxy);
        return AdmitResult.Create(proxy);
    }

    public override AdmitResult Admit(Span<SpatialObject> buffer)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "Venue.AdmitList: Leaf");
        if (IsRetired)
            return AdmitResult.RetryRequest();


        if (IsAtCapacity(buffer.Length))
        {
            return CanSubdivide()
                ? AdmitResult.SubdivideRequest(this)
                : AdmitResult.DelegateRequest(this);
        }

        var outProxies = new List<SpatialObjectProxy>();

        foreach (var obj in buffer)
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
    : LeafNode
{
    public SpatialLattice Sublattice { get; }
    public SubLatticeBranchNode(Region bounds, IParentNode parent, byte latticeDepth, IList<SpatialObject> migrants)
        : base(bounds, parent)
    {
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
    public override AdmitResult Admit(SpatialObject obj, LongVector3 proposedPosition)
    {
        if (!Bounds.Contains(proposedPosition))
            return AdmitResult.EscalateRequest();

        if (!obj.HasPositionAtDepth(Sublattice.LatticeDepth))
        {
            var sublatticeFramedPosition = Sublattice.BoundsTransform.OuterToInnerInsertion(proposedPosition, obj.Guid);
            obj.AppendPosition(sublatticeFramedPosition);
        }
        var subFramePosition = obj.GetPositionAtDepth(Sublattice.LatticeDepth);
        return Sublattice.Admit(obj, subFramePosition);
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

    public override AdmitResult Admit(Span<SpatialObject> buffer)
    {
#if DEBUG
        foreach (var obj in buffer)
        {
            var proposedPosition = obj.GetPositionAtDepth(Sublattice.LatticeDepth - 1);
            if (!Bounds.Contains(proposedPosition))
                throw new InvalidOperationException($"Object outside SublatticeBranch bounds: {proposedPosition}");
        }
#endif

        foreach (var obj in buffer)
            if (!obj.HasPositionAtDepth(Sublattice.LatticeDepth))
            {
                var proposedPosition = obj.GetPositionAtDepth(Sublattice.LatticeDepth -1);
                var framedPosition = Sublattice.BoundsTransform.OuterToInnerInsertion(proposedPosition, obj.Guid);
                obj.AppendPosition(framedPosition);
            }

        return Sublattice.Admit(buffer);
    }
}

