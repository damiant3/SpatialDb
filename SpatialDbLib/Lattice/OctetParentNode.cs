using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
using System.Buffers;
///////////////////////////////
namespace SpatialDbLib.Lattice;

public abstract partial class OctetParentNode
    : ParentNode,
      IParentNode
{
    partial void Subdivide_Impl(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice);
    partial void Migrate_Impl(IList<ISpatialObject> objs);
    protected static ArrayRentalContract<T> RentArray<T>(int length, out T[] array)
    {
        array = ArrayPool<T>.Shared.Rent(length);
        for (int i = 0; i < length; i++) array[i] = default!;
        return new ArrayRentalContract<T>(array);
    }
    public void Subdivide(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    => Subdivide_Impl(parent, subdividingleaf, latticeDepth, childIndex, branchOrSublattice);
    public override void Migrate(IList<ISpatialObject> objs)
        => Migrate_Impl(objs);

    public OctetParentNode(Region bounds)
        : base(bounds)
    {
        CreateChildLeafNodes();
    }

    public override IInternalChildNode[] Children { get; } = new IInternalChildNode[8];

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
                (i & 1) != 0 ? mid.Z : min.Z);
            var childMax = new LongVector3(
                (i & 4) != 0 ? max.X : mid.X,
                (i & 2) != 0 ? max.Y : mid.Y,
                (i & 1) != 0 ? max.Z : mid.Z);
            Children[i] = CreateNewVenueNode(i, childMin, childMax);
        }
    }
    public virtual VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax)
        => LeafPool<LargeLeafNode>.Rent(new(childMin, childMax), this, (bounds, parent) => new LargeLeafNode(bounds, parent));
    // => new LargeLeafNode(new (childMin, childMax), this);

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
    public struct AdmitFrame(OctetParentNode parent, IInternalChildNode childNode, byte childIndex)
    {
        public OctetParentNode Parent = parent;
        public IInternalChildNode ChildNode = childNode;
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
                    if (frame.ChildNode is not VenueLeafNode subdividingLeaf) throw new InvalidOperationException("Subdivide/Delgate request from non-venue leaf.");
                    if (!subdividingLeaf.IsRetired)
                        Subdivide(
                            frame.Parent,
                            subdividingLeaf,
                            SpatialLattice.CurrentThreadLatticeDepth,
                            frame.ChildIndex,
                            admitResult is AdmitResult.Subdivide);
                    current = frame.Parent;
                    continue;
                }
                default:
                    throw new InvalidOperationException("Unknown AdmitResponse");
            }
        }
    }
    public virtual IInternalChildNode CreateBranchNodeWithLeafs(
        OctetParentNode parent,
        VenueLeafNode subdividingleaf,
        byte latticeDepth,
        bool branchOrSublattice,
        List<ISpatialObject> occupantsSnapshot)
        => branchOrSublattice
            ? new OctetBranchNode(subdividingleaf.Bounds, parent, occupantsSnapshot)
            : new SubLatticeBranchNode(subdividingleaf.Bounds, parent, (byte)(latticeDepth + 1), occupantsSnapshot);
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
            using var s = RentArray<byte>(span.Length, out var octants);
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
                            Subdivide(
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
    public override void PruneIfEmpty()
    {
        if (Children.All(IsChildEmpty))
            if (this is IChildNode<OctetParentNode> child)
            {
                var parent = child.Parent;
                int index = Array.IndexOf(parent.Children, this);
                if (index >= 0)
                {
                    using var s = new SlimSyncer(parent.Sync, SlimSyncer.LockMode.Write, "PruneIfEmpty: Parent");
                    parent.Children[index] = parent.CreateNewVenueNode((byte)index, Bounds.Min, Bounds.Max);
                    parent.PruneIfEmpty();
                }
            }
    }
    public void PruneChild(int index)  /// who calls this?
    {
        var child = Children[index];
        if (IsChildEmpty(child))
        {
            using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "OctetParentNode.PruneChild");
            Children[index] = CreateNewVenueNode(index, child.Bounds.Min, child.Bounds.Max);
        }
    }
    private bool IsChildEmpty(IChildNode<OctetParentNode> child)
    {
        return child switch
        {
            VenueLeafNode leaf => leaf.Occupants.Count == 0,
            OctetParentNode parent => parent.Children.All(IsChildEmpty),
            SubLatticeBranchNode sub => GetTotalOccupantCount(sub.Sublattice.GetRootNode()) == 0,
            _ => false
        };
    }
    protected static int GetTotalOccupantCount(ISpatialNode node)
    {
        return node switch
        {
            VenueLeafNode leaf => leaf.Occupants.Count,
            OctetParentNode parent => parent.Children.Sum(GetTotalOccupantCount),
            SubLatticeBranchNode sub => GetTotalOccupantCount(sub.Sublattice.GetRootNode()),
            _ => 0
        };
    }
}
