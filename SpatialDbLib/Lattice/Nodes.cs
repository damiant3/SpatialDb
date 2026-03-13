using Common.Core.Sync;
using SpatialDbLib.Math;
///////////////////////////////
namespace SpatialDbLib.Lattice;
public interface ISpatialNode
{
    AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition);
    AdmitResult Admit(Span<ISpatialObject> buffer);
    void Migrate(IList<ISpatialObject> objects);
    Region Bounds { get; }
}
public abstract class SpatialNode(Region bounds)
    : ISync
{
    public Region Bounds { get; protected set; } = bounds;
    protected readonly ReaderWriterLockSlim Sync = new(LockRecursionPolicy.SupportsRecursion);
    ReaderWriterLockSlim ISync.Sync => Sync;
    public abstract void Migrate(IList<ISpatialObject> obj);
    public abstract AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition);
    public abstract AdmitResult Admit(Span<ISpatialObject> buffer);
}
public interface IParentNode : ISpatialNode
{
    IInternalChildNode CreateBranchNodeWithLeafs(
        OctetParentNode parent,
        VenueLeafNode subdividingleaf,
        byte latticeDepth,
        bool branchOrSublattice,
        List<ISpatialObject> occupantsSnapshot);

    VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax);
    public abstract VenueLeafNode? ResolveOccupyingLeaf(ISpatialObject obj);
    public abstract VenueLeafNode? ResolveLeaf(ISpatialObject obj);
}
public interface IInternalChildNode : IChildNode<OctetParentNode>
{
    void MigrateInternal(IList<ISpatialObject> objs);
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

    public VenueLeafNode? ResolveLeafFromOuterLattice(ISpatialObject obj)
        => ResolveLeaf(obj);
}
public abstract class ParentNode(Region bounds)
    : SpatialNode(bounds)
{
    public abstract IInternalChildNode[] Children { get; }
    public abstract void PruneIfEmpty();
}
public class OctetBranchNode
    : OctetParentNode,
      IChildNode<OctetParentNode>,
      IInternalChildNode
{
    public OctetBranchNode(Region bounds, OctetParentNode parent, IList<ISpatialObject> migrants)
        : base(bounds)
    {
        Parent = parent;
        Migrate(migrants);
    }
    public OctetParentNode Parent { get; }

    void IInternalChildNode.MigrateInternal(IList<ISpatialObject> objs) => Migrate(objs);
}
public abstract class LeafNode(Region bounds, OctetParentNode parent)
    : SpatialNode(bounds),
      IChildNode<OctetParentNode>
{
    public OctetParentNode Parent { get; protected set; } = parent;
    public virtual bool CanSubdivide()
        => Bounds.Size.X > 1 && Bounds.Size.Y > 1 && Bounds.Size.Z > 1;
}
public abstract class VenueLeafNode(Region bounds, OctetParentNode parent)
    : LeafNode(bounds, parent),
      IInternalChildNode
{
    internal IList<ISpatialObject> Occupants { get; } = [];
    protected virtual ISpatialObjectProxy CreateProxy<T>(T obj, LongVector3 proposedPosition)
    where T : SpatialObject, ISpatialObject
        => new SpatialObjectProxy(obj, this, proposedPosition);
    internal bool m_isRetired = false;
    public bool IsRetired => m_isRetired;
    public bool Contains(ISpatialObject obj)
    {
        using SlimSyncer s = new(Sync, SlimSyncer.LockMode.Read, "Venue.Contains: Leaf");
        return Occupants.Contains(obj);
    }
    public bool HasAnyOccupants()
    {
        using SlimSyncer s = new(Sync, SlimSyncer.LockMode.Read, "Venue.HasAnyOccupants: Leaf");
        return Occupants.Count > 0;
    }
    internal void Vacate(ISpatialObject obj)
        => Occupants.Remove(obj);
    protected void Occupy(ISpatialObject obj)
        => Occupants.Add(obj); // private, assumes write lock held and is not retired
    public void Retire()
    {
        Occupants.Clear();
        m_isRetired = true;
        LeafPool<VenueLeafNode>.Return(this);
    }
    internal virtual void Reinitialize(Region bounds, OctetParentNode parent)
    {
        if (!IsRetired) throw new InvalidOperationException("Cannot reinitialize a non-retired leaf node.");
        Bounds = bounds;
        Parent = parent;
        m_isRetired = false;
    }
    public virtual void Replace(ISpatialObjectProxy proxy)
    {
        using SlimSyncer s = new(Sync, SlimSyncer.LockMode.Write, "VenueLeafNode.Replace");
        ISpatialObject originalObject = proxy.OriginalObject;
        int index = Occupants.IndexOf(proxy);

        if (index == -1)
            throw new InvalidOperationException("Proxy not found in occupants list.");
        Occupants[index] = originalObject;
    }
    public virtual int Capacity => 8;
    public bool IsAtCapacity(int toAdd = 1)
        => Occupants.Count + toAdd > Capacity;
    public override void Migrate(IList<ISpatialObject> objs)
    {
        foreach (ISpatialObject obj in objs)
        {
            if (obj is SpatialObjectProxy proxy)
                proxy.TargetLeaf = this;
            Occupants.Add(obj);
        }
    }

    void IInternalChildNode.MigrateInternal(IList<ISpatialObject> objs) => Migrate(objs);

    public override AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition)
    {
        if (!Bounds.Contains(proposedPosition)) return AdmitResult.EscalateRequest();
        using SlimSyncer s = new(Sync, SlimSyncer.LockMode.Write, "Venue.Admit: Leaf");
        if (IsRetired) return AdmitResult.RetryRequest();
        if (IsAtCapacity(1))
        {
            return CanSubdivide()
                ? AdmitResult.SubdivideRequest(this)
                : AdmitResult.DelegateRequest(this);
        }
        ISpatialObjectProxy proxy = CreateProxy((SpatialObject)obj, proposedPosition);
        Occupy(proxy);
        return AdmitResult.Create(proxy);
    }
    public override AdmitResult Admit(Span<ISpatialObject> buffer)
    {
        using SlimSyncer s = new(Sync, SlimSyncer.LockMode.Write, "Venue.AdmitList: Leaf");
        if (IsRetired)
            return AdmitResult.RetryRequest();
        if (IsAtCapacity(buffer.Length))
        {
            return CanSubdivide()
                ? AdmitResult.SubdivideRequest(this)
                : AdmitResult.DelegateRequest(this);
        }
        List<ISpatialObjectProxy> outProxies = new();
        foreach (ISpatialObject obj in buffer)
        {
            ISpatialObjectProxy proxy = CreateProxy((SpatialObject)obj, obj.LocalPosition);
            Occupy(proxy);
            outProxies.Add(proxy);
        }
        return AdmitResult.BulkCreate(outProxies);
    }
    public MultiObjectScope<ISpatialObject> LockAndSnapshotForMigration()
    {
        List<ISpatialObject> snapshot = new();
        List<SlimSyncer> locksAcquired = new();
        try
        {
            for (int i = 0; i < Occupants.Count; i++)
            {
                SlimSyncer syncer = null!;
                try
                {
                    ISpatialObject occupant = Occupants[i];
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
            return new(snapshot, locksAcquired);
        }
        catch
        {
            foreach (SlimSyncer l in locksAcquired)
                try { l.Dispose(); } catch { }
            throw;
        }
    }
    public IEnumerable<ISpatialObject> QueryNeighbors(LongVector3 center, ulong radius)
    {
        List<ISpatialObject> results = [];
        SpatialQuery.CollectWithinSphere(this, center, radius, results, acquireLeafLock: false);
        return results;
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
public abstract class SubLatticeBranchNode<TLattice>(Region bounds, OctetParentNode parent)
    : LeafNode(bounds, parent),
      ISubLatticeBranch,
      IInternalChildNode
    where TLattice : ISpatialLattice
{
    internal TLattice Sublattice { get; set; } = default!;
    public ISpatialLattice GetSublattice() => Sublattice;
    public override void Migrate(IList<ISpatialObject> objs)
        => Sublattice.Migrate(objs);

    void IInternalChildNode.MigrateInternal(IList<ISpatialObject> objs) => Migrate(objs);

    public override AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition)
    {
        if (!Bounds.Contains(proposedPosition)) return AdmitResult.EscalateRequest();
        if (!obj.HasPositionAtDepth(Sublattice.LatticeDepth))
        {
            LongVector3 sublatticeFramedPosition = Sublattice.BoundsTransform.OuterToInnerInsertion(proposedPosition, obj.Guid);
            obj.AppendPosition(sublatticeFramedPosition);
        }
        LongVector3 subFramePosition = obj.GetPositionAtDepth(Sublattice.LatticeDepth);
        return Sublattice.Admit(obj, subFramePosition);
    }
    public VenueLeafNode? ResolveLeafFromOuterLattice(ISpatialObject obj)
        => Sublattice.ResolveLeafFromOuterLattice(obj);
    public override AdmitResult Admit(Span<ISpatialObject> buffer)
        => Sublattice.AdmitForInsert(buffer);
}
public class SubLatticeBranchNode
    : SubLatticeBranchNode<ISpatialLattice>
{
    public SubLatticeBranchNode(Region bounds, OctetParentNode parent, byte latticeDepth, IList<ISpatialObject> migrants)
        : base(bounds, parent)
    {
        Sublattice = new SpatialLattice(bounds, latticeDepth);
        Sublattice.Migrate(migrants);
    }
}
