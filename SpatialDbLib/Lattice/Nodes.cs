using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
using System.Buffers;
///////////////////////////////
namespace SpatialDbLib.Lattice;

public interface ISpatialNode
{
    AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition);
    AdmitResult Admit(Span<ISpatialObject> buffer);
    void AdmitMigrants(IList<ISpatialObject> obj);
}

public abstract class SpatialNode(Region bounds)
    : ISync
{
    public Region Bounds { get; } = bounds;
    protected readonly ReaderWriterLockSlim Sync  = new(LockRecursionPolicy.SupportsRecursion);
    ReaderWriterLockSlim ISync.Sync => Sync;
    public abstract void AdmitMigrants(IList<ISpatialObject> obj);
    public abstract AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition);
    public abstract AdmitResult Admit(Span<ISpatialObject> buffer);
}

public interface IParentNode : ISpatialNode
{
    IChildNode<OctetParentNode> CreateBranchNodeWithLeafs(
        OctetParentNode parent,
        VenueLeafNode subdividingleaf,
        byte latticeDepth,
        bool branchOrSublattice,
        List<ISpatialObject> occupantsSnapshot);

    VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax);
    public abstract VenueLeafNode? ResolveOccupyingLeaf(ISpatialObject obj);
    public abstract VenueLeafNode? ResolveLeaf(ISpatialObject obj);
}

public interface IChildNode<TParent> : ISpatialNode
    where TParent : OctetParentNode
{
    public TParent Parent { get; }
}

public interface IRootNode<TParent, TBranch, TVenue, TSelf>
    : IParentNode
    where TParent : OctetParentNode
    where TBranch : OctetParentNode, IChildNode<TParent>
    where TVenue : VenueLeafNode
    where TSelf : IRootNode<TParent, TBranch, TVenue, TSelf>
{
    byte LatticeDepth { get; }
    VenueLeafNode? ResolveLeafFromOuterLattice(ISpatialObject obj);
    ISpatialLattice? OwningLattice { get; set; }
}
public class RootNode<TParent, TBranch, TVenue, TSelf>(Region bounds, byte latticeDepth)
    : OctetParentNode(bounds),
    IRootNode<TParent, TBranch, TVenue, TSelf>
    where TParent : OctetParentNode
    where TBranch : OctetParentNode, IChildNode<TParent>
    where TVenue : VenueLeafNode
    where TSelf : RootNode<TParent, TBranch, TVenue, TSelf>
{
    protected RootNode(Region bounds)
        : this(bounds, 0) { }

    public ISpatialLattice? OwningLattice { get; set; }

    public byte LatticeDepth { get; } = latticeDepth;

    public override void AdmitMigrants(IList<ISpatialObject> objs)
        => BucketAndDispatchMigrants(objs);

    public VenueLeafNode? ResolveLeafFromOuterLattice(ISpatialObject obj)
    {
#if DEBUG
        if (obj.PositionStackDepth <= LatticeDepth)
            throw new InvalidOperationException("Object position stack depth is less than or equal to lattice depth during outer lattice leaf resolution.");
#endif
        return ResolveLeaf(obj);
    }
}

public abstract class ParentNode(Region bounds)
    : SpatialNode(bounds)
{
    public abstract IChildNode<OctetParentNode>[] Children { get; }
}

public abstract class OctetParentNode
    : ParentNode,
      IParentNode
{
    public OctetParentNode(Region bounds)
        : base(bounds)
    {
        CreateChildLeafNodes();
    }

    public override IChildNode<OctetParentNode>[] Children { get; } = new IChildNode<OctetParentNode>[8];

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
            Children[i] = CreateNewVenueNode(i, childMin, childMax);
        }
    }

    public virtual VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax)
        => new LargeLeafNode(new(childMin, childMax), this);

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

    public MultiObjectScope<IChildNode<OctetParentNode>> LockAndSnapshotForMigration()
    {
        var locksAcquired = new List<SlimSyncer>();
        var nodesLocked = new List<IChildNode<OctetParentNode>>();
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

    public VenueLeafNode? ResolveOccupyingLeaf(ISpatialObject obj)
    {
        var resolveleaf = ResolveLeaf(obj);
        if (resolveleaf == null) return null;
        if (!resolveleaf.Contains(obj))
            return null;
        return resolveleaf;
    }

    public VenueLeafNode? ResolveLeaf(ISpatialObject obj)
    {
        LongVector3 pos = obj.GetPositionStack()[LatticeDepthContext.CurrentDepth];
        ISpatialNode current = this;
        while (current != null)
        {
            switch (current)
            {
                case ISubLatticeBranch sublatticebranch:
                    return sublatticebranch.GetSublattice().ResolveLeafFromOuterLattice(obj);

                case OctetParentNode parent:
                {
                    var result = parent.SelectChild(pos)
                        ?? throw new InvalidOperationException("Failed to select child during occupation resolution");
                    current = result.ChildNode;
                    break;
                }
                case VenueLeafNode venue:
                {
                    using var s3 = new SlimSyncer(((ISync)venue).Sync, SlimSyncer.LockMode.Read, "SpatialLattice.ResolveLeaf: venue");
                    return venue;
                }
                default:
                    throw new InvalidOperationException("Unknown node type during occupation resolution");
            }
        }
        return null;
    }

    protected void BucketAndDispatchMigrants(IList<ISpatialObject> objs)
    {
        var buckets = new List<ISpatialObject>[8];

        foreach (var obj in objs)
        {
            if (!Bounds.Contains(obj.LocalPosition))
                throw new InvalidOperationException("Migrant has no home.");
            if (SelectChild(obj.LocalPosition) is not SelectChildResult result)
                throw new InvalidOperationException("Containment invariant violated");

            buckets[result.IndexInParent] ??= [];
            buckets[result.IndexInParent].Add(obj);
        }

        for (byte i = 0; i < 8; i++)
        {
            if (buckets[i] == null) continue;
            Children[i].AdmitMigrants(buckets[i]);
        }
    }

    public override void AdmitMigrants(IList<ISpatialObject> objs)
        => BucketAndDispatchMigrants(objs);


    public struct AdmitFrame(OctetParentNode parent, IChildNode<OctetParentNode> childNode, byte childIndex)
    {
        public OctetParentNode Parent = parent;
        public IChildNode<OctetParentNode> ChildNode = childNode;
        public byte ChildIndex = childIndex;
    }

    public override AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition)
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
            var admitResult = current is VenueLeafNode venue && obj.PositionStackDepth > SpatialLattice.CurrentThreadLatticeDepth + 1 // deep insert
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
                        SubdivideAndMigrate(frame.Parent, subdividingleaf, SpatialLattice.CurrentThreadLatticeDepth, frame.ChildIndex, admitResult is AdmitResult.Subdivide);

                    current = frame.Parent;
                    continue;
                }
                default:
                    throw new InvalidOperationException("Unknown AdmitResponse");
            }
        }
    }

    public virtual IChildNode<OctetParentNode> CreateBranchNodeWithLeafs(
        OctetParentNode parent,
        VenueLeafNode subdividingleaf,
        byte latticeDepth,
        bool branchOrSublattice,
        List<ISpatialObject> occupantsSnapshot)
        => branchOrSublattice
            ? new OctetBranchNode(subdividingleaf.Bounds, parent, occupantsSnapshot)
            : new SubLatticeBranchNode(subdividingleaf.Bounds, parent, (byte)(latticeDepth + 1), occupantsSnapshot);

    public void SubdivideAndMigrate(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        using var parentLock = new SlimSyncer(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent");
        using var leafLock = new SlimSyncer(((ISync)subdividingleaf).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf");
        if (subdividingleaf.IsRetired) return;
        var migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        var occupantsSnapshot = migrationSnapshot.Objects;
        IChildNode<OctetParentNode> newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }

    internal sealed class AdmitWorkFrame
    {
        public OctetParentNode Parent;
        public BufferSlice WorkingSlice;
        public byte BucketCount;
        public byte NextBucket;
        public BufferSlice[] Buckets;
        public byte[] BucketChildIndex;
        public List<ISpatialObjectProxy> Proxies;

        public AdmitWorkFrame(OctetParentNode parent, Span<ISpatialObject> rootBuffer, BufferSlice workingSlice, byte latticeDepth)
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
        void PartitionInPlace(Span<ISpatialObject> span, byte latticeDepth)
        {
            var mid = Parent.Bounds.Mid;
            using var s = RentArray(span.Length, out var octants);
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
                Buckets[bucketIdx] = new(WorkingSlice.Start + runningOffset, counts[i]);
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

        private static ArrayRentalContract RentArray(int length, out byte[] array)
        {
            array = ArrayPool<byte>.Shared.Rent(length);
            return new ArrayRentalContract(array);
        }
    }
    public override AdmitResult Admit(Span<ISpatialObject> buffer)
    {
        var stack = new Stack<AdmitWorkFrame>();
        stack.Push(new AdmitWorkFrame(this, buffer, new BufferSlice(0, buffer.Length), SpatialLattice.CurrentThreadLatticeDepth));
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
                    stack.Push(new AdmitWorkFrame(parent, buffer, bucket, SpatialLattice.CurrentThreadLatticeDepth));
                    frame.NextBucket++;
                    continue;
                }
                
                var slice = bucket.GetSpan(buffer);
                var leaf = (LeafNode)child ?? throw new InvalidOperationException("Child is not a LeafNode");
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
                        var venue2 = leaf as VenueLeafNode ?? throw new InvalidOperationException("Subdivision requested by non-venueleaf");
                        if (!venue2.IsRetired)
                            SubdivideAndMigrate(
                                frame.Parent,
                                venue2,
                                SpatialLattice.CurrentThreadLatticeDepth,
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

        bool NeedsDeeper(Span<ISpatialObject> slice)
        {
            foreach (var obj in slice)
                if (obj.PositionStackDepth > SpatialLattice.CurrentThreadLatticeDepth + 1)
                    return true;
            return false;
        }
    }
}
public class OctetBranchNode
    : OctetParentNode,
      IChildNode<OctetParentNode>
{
    public OctetBranchNode(Region bounds, OctetParentNode parent, IList<ISpatialObject> migrants)
        : base(bounds)
    {
        Parent = parent;
        AdmitMigrants(migrants);
    }

    public OctetParentNode Parent { get; }
}

public abstract class LeafNode(Region bounds, OctetParentNode parent)
    : SpatialNode(bounds),
      IChildNode<OctetParentNode>
{
    public OctetParentNode Parent { get; } = parent;

    public virtual bool CanSubdivide()
        => Bounds.Size.X > 1 && Bounds.Size.Y > 1 && Bounds.Size.Z > 1;
}

public abstract class VenueLeafNode(Region bounds, OctetParentNode parent)
    : LeafNode(bounds, parent)
{
    internal IList<ISpatialObject> Occupants { get; } = [];

    protected virtual ISpatialObjectProxy CreateProxy(
        ISpatialObject obj,
        LongVector3 proposedPosition)
    {
        return new SpatialObjectProxy((SpatialObject)obj, this, proposedPosition);
    }

    internal bool m_isRetired = false;
    public bool IsRetired => m_isRetired;
    public bool Contains(ISpatialObject obj)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "Venue.Contains: Leaf");
        return Occupants.Contains(obj);
    }
    public bool HasAnyOccupants()
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "Venue.HasAnyOccupants: Leaf");
        return Occupants.Count > 0;
    }

    internal void Vacate(ISpatialObject obj)
        => Occupants.Remove(obj);

    protected void Occupy(ISpatialObject obj)
        => Occupants.Add(obj); // private, assumes write lock held and is not retired

    internal void Retire()
    {
        Occupants.Clear();
        m_isRetired = true;
    }

    public virtual void Replace(ISpatialObjectProxy proxy)  // Changed signature
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "VenueLeafNode.Replace");

        var originalObject = proxy.OriginalObject;
        int index = Occupants.IndexOf(proxy);

        if (index == -1)
            throw new InvalidOperationException("Proxy not found in occupants list.");

        Occupants[index] = originalObject;
    }

    public virtual int Capacity => 8;
    public bool IsAtCapacity(int toAdd = 1)
        => Occupants.Count + toAdd > Capacity;

    public override void AdmitMigrants(IList<ISpatialObject> objs)
    {
        foreach (var obj in objs)
        {
            if (obj is SpatialObjectProxy proxy)
                proxy.TargetLeaf = this;
            Occupants.Add(obj);
        }
    }

    public override AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition)
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

        var proxy = CreateProxy(obj, proposedPosition);
        Occupy(proxy);
        return AdmitResult.Create(proxy);
    }

    public override AdmitResult Admit(Span<ISpatialObject> buffer)
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

        var outProxies = new List<ISpatialObjectProxy>();

        foreach (var obj in buffer)
        {
            var proxy = CreateProxy(obj, obj.LocalPosition);
            Occupy(proxy);
            outProxies.Add(proxy);
        }

        return AdmitResult.BulkCreate(outProxies);
    }

    public MultiObjectScope<ISpatialObject> LockAndSnapshotForMigration()
    {
        var snapshot = new List<ISpatialObject>();
        var locksAcquired = new List<SlimSyncer>();
        try
        {
            for (int i = 0; i < Occupants.Count; i++)
            {
                SlimSyncer syncer = null!;
                try
                {
                    var occupant = Occupants[i];
                    if (!occupant.Sync.IsWriteLockHeld)
                    {
                        syncer = new SlimSyncer(occupant.Sync, SlimSyncer.LockMode.Write, "Venue.LockAndSnap: Occupant");
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

public interface ISubLatticeBranch
{
    ISpatialLattice GetSublattice();
}

public abstract class SubLatticeBranchNode<TLattice>
    : LeafNode,
      ISubLatticeBranch
    where TLattice : ISpatialLattice
{
    internal TLattice Sublattice { get; set; }
    public ISpatialLattice GetSublattice() => Sublattice;

    protected SubLatticeBranchNode(Region bounds, OctetParentNode parent)
        : base(bounds, parent)
    {
        Sublattice = default!;  // to be initialized by subclass constructor
    }

    public override void AdmitMigrants(IList<ISpatialObject> objs)
        => Sublattice.AdmitMigrants(objs);

    public override AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition)
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

    public VenueLeafNode? ResolveLeafFromOuterLattice(ISpatialObject obj)
        => Sublattice.ResolveLeafFromOuterLattice(obj);

    public override AdmitResult Admit(Span<ISpatialObject> buffer)
    {
#if DEBUG
        foreach (var obj in buffer)
        {
            var proposedPosition = obj.GetPositionAtDepth(Sublattice.LatticeDepth - 1);
            if (!Bounds.Contains(proposedPosition))
                throw new InvalidOperationException($"Object outside SublatticeBranch bounds: {proposedPosition}");
        }
#endif
        return Sublattice.AdmitForInsert(buffer);
    }
}


public class SubLatticeBranchNode
    : SubLatticeBranchNode<ISpatialLattice>
{
    public SubLatticeBranchNode(Region bounds, OctetParentNode parent, byte latticeDepth, IList<ISpatialObject> migrants)
        : base(bounds, parent)
    {
        Sublattice = new SpatialLattice(bounds, latticeDepth);
#if DEBUG
        if (migrants.Any(a => a.PositionStackDepth > latticeDepth + 1))
        {
            throw new InvalidOperationException($"Occupant has depth > lattice");
        }
#endif
        Sublattice.AdmitMigrants(migrants);
    }
}