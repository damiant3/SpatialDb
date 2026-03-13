using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public interface ITickableSpatialNode : ISpatialNode
{
    void Tick();
}
public interface ITickableChildNode
    : IChildNode<TickableOctetParentNode>, ITickableSpatialNode
{ }
public class TickableOctetParentNode(Region bounds)
    : OctetParentNode(bounds),
      ITickableSpatialNode
{
    public override VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax)
        => new TickableVenueLeafNode(new Region(childMin, childMax), this);
    public override IInternalChildNode CreateBranchNodeWithLeafs(
        OctetParentNode parent,
        VenueLeafNode subdividingLeaf,
        byte latticeDepth,
        bool branchOrSublattice,
        List<ISpatialObject> occupantsSnapshot)
    {
        TickableOctetParentNode tickableParent = (TickableOctetParentNode)parent;
        TickableVenueLeafNode tickableLeaf = (TickableVenueLeafNode)subdividingLeaf;
        return branchOrSublattice
            ? new TickableOctetBranchNode(tickableLeaf.Bounds, tickableParent, occupantsSnapshot)
            : new TickableSubLatticeBranchNode(tickableLeaf.Bounds, tickableParent, latticeDepth, occupantsSnapshot);
    }
    protected ITickableChildNode GetTickableChild(int index)
        => (ITickableChildNode)base.Children[index];

    public virtual void Tick()
    {
        for (int i = 0; i < 8; i++)
            GetTickableChild(i).Tick();

        if (SpatialLattice.EnablePruning)
            PruneIfEmpty();
    }
}
public class TickableOctetBranchNode
    : TickableOctetParentNode,
      IChildNode<OctetParentNode>,
      ITickableChildNode,
      IInternalChildNode
{
    public TickableOctetBranchNode(Region bounds, TickableOctetParentNode parent, IList<ISpatialObject> migrants)
        : base(bounds)
    {
        Parent = parent;
        Migrate(migrants);
    }

    public TickableOctetParentNode Parent { get; }
    TickableOctetParentNode IChildNode<TickableOctetParentNode>.Parent => Parent;
    OctetParentNode IChildNode<OctetParentNode>.Parent => Parent;

    void IInternalChildNode.MigrateInternal(IList<ISpatialObject> objs) => Migrate(objs);
}
public partial class TickableVenueLeafNode(Region bounds, TickableOctetParentNode parent)
    : VenueLeafNode(bounds, parent),
      ITickableSpatialNode,
      ITickableChildNode
{
    public partial void Tick();
    public override int Capacity => 64;
    internal List<ITickableObject> m_tickableObjects = [];
    TickableOctetParentNode IChildNode<TickableOctetParentNode>.Parent => (TickableOctetParentNode)Parent;
    protected override ISpatialObjectProxy CreateProxy<T>(T obj, LongVector3 proposedPosition)
    {
        if (obj is TickableSpatialObject tickable)
            return new TickableSpatialObjectProxy(tickable, this, proposedPosition, tickable.GetOccupyingLeaf());
        return base.CreateProxy(obj, proposedPosition);
    }
    public override void Migrate(IList<ISpatialObject> objs)
    {
        base.Migrate(objs);
        foreach (ISpatialObject obj in objs)
        {
            if (obj is TickableSpatialObjectBase tickable)
            {
                tickable.SetOccupyingLeaf(this);
                RegisterForTicks(tickable);
            }
        }
    }
    public override void Replace(ISpatialObjectProxy proxy)
    {
        if (proxy is ITickableObject tickableProxy)
            UnregisterForTicks(tickableProxy);

        if (proxy.OriginalObject is TickableSpatialObjectBase tickableOriginal)
        {
            tickableOriginal.SetOccupyingLeaf(this);
            RegisterForTicks(tickableOriginal);
        }
        base.Replace(proxy);
    }
    public void RegisterForTicks(ITickableObject obj)
    {
        using SlimSyncer s = new(Sync, SlimSyncer.LockMode.Write, "TickableSpatialNode: RegisterForTicks");
        if (!m_tickableObjects.Contains(obj))
            m_tickableObjects.Add(obj);
    }
    public void UnregisterForTicks(ITickableObject obj)
    {
        using SlimSyncer s = new(Sync, SlimSyncer.LockMode.Write, "TickableSpatialNode: UnregisterForTicks");
        m_tickableObjects.Remove(obj);
    }
    private void HandleTickResult(TickResult result)
    {
        switch (result.Action)
        {
            case TickAction.Move:
                if (Bounds.Contains(result.Target))
                    result.Object.SetLocalPosition(result.Target);
                else
                    HandleBoundaryCrossing(result.Object, result.Target);
                break;
            case TickAction.Remove:
                Vacate(result.Object);
                break;
        }
    }
    private void HandleBoundaryCrossing(SpatialObject obj, LongVector3 newPosition)
    {
        TickableSpatialObjectBase? tickableObj = obj as TickableSpatialObjectBase;
        if (tickableObj != null) UnregisterForTicks(tickableObj);
        OctetParentNode currentParent = Parent;
        AdmitResult admitResult;
        while (true)
        {
            admitResult = currentParent.Admit(obj, newPosition);
            if (admitResult is AdmitResult.Escalate)
            {
                if (currentParent is IChildNode<TickableOctetParentNode> child)
                {
                    currentParent = child.Parent;
                    continue;
                }
                else
                {
                    if (tickableObj != null)
                        RegisterForTicks(tickableObj);
                    return;
                }
            }
            break;
        }

        switch (admitResult)
        {
            case AdmitResult.Created created:
            {
                TickableVenueLeafNode sourceLeaf = this;
                VenueLeafNode rawTarget = created.Proxy.TargetLeaf;

                if (rawTarget is not TickableVenueLeafNode targetLeaf || ReferenceEquals(targetLeaf, sourceLeaf))
                {
                    created.Proxy.Commit();
                    if (!ReferenceEquals(rawTarget, sourceLeaf))
                        Vacate(obj);
                    break;
                }

                VenueLeafNode first = sourceLeaf;
                VenueLeafNode second = targetLeaf;
                if (LongVector3.CompareLexicographic(sourceLeaf.Bounds.Min, targetLeaf.Bounds.Min) > 0)
                {
                    first = targetLeaf;
                    second = sourceLeaf;
                }
                while (true)
                {

                    using SlimSyncer l1 = new(((ISync)first).Sync, SlimSyncer.LockMode.Write, "HandleBoundaryCrossing: move-first");
                    using SlimSyncer l2 = new(((ISync)second).Sync, SlimSyncer.LockMode.Write, "HandleBoundaryCrossing: move-second");
                    if (sourceLeaf.IsRetired || targetLeaf.IsRetired)
                    {
                        Thread.Yield();
                        continue;
                    }
                    created.Proxy.Commit();
                    Vacate(obj);
                    break;
                }
            }
            break;
            case AdmitResult.Rejected:
                if (tickableObj != null)
                    RegisterForTicks(tickableObj);
                break;

            case AdmitResult.Escalate:
                throw new InvalidOperationException("Escalate should have been handled in escalation loop");

            default:
                throw new InvalidOperationException($"Unexpected AdmitResult type: {admitResult.GetType().Name}");
        }
    }

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

        if (proxy is SpatialObjectProxy sp && obj is TickableSpatialObjectBase tickableOriginal)
            sp.SourceLeaf = tickableOriginal.GetOccupyingLeaf();

        Occupy(proxy);
        return AdmitResult.Create(proxy);
    }
}

public class TickableRootNode<TParent, TBranch, TVenue, TSelf>(Region bounds, byte latticeDepth)
    : TickableOctetParentNode(bounds),
      IRootNode<TickableOctetParentNode, TickableOctetBranchNode, TickableVenueLeafNode, TickableRootNode<TParent, TBranch, TVenue, TSelf>>,
      ITickableSpatialNode
    where TParent : TickableOctetParentNode
    where TBranch : TickableOctetBranchNode
    where TVenue : TickableVenueLeafNode
    where TSelf : TickableRootNode<TParent, TBranch, TVenue, TSelf>
{
    protected TickableRootNode(Region bounds)
    : this(bounds, 0) { }

    public byte LatticeDepth { get; } = latticeDepth;

    public ISpatialLattice? OwningLattice { get; set; }

    public override VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax)
        => new TickableVenueLeafNode(new Region(childMin, childMax), this);

    public VenueLeafNode? ResolveLeafFromOuterLattice(ISpatialObject obj)
        => ResolveLeaf(obj);
}

public class TickableRootNode :
    TickableRootNode<TickableOctetParentNode, TickableOctetBranchNode, TickableVenueLeafNode, TickableRootNode>,
    IRootNode<OctetParentNode, OctetBranchNode, VenueLeafNode, TickableRootNode>
{
    public TickableRootNode(Region bounds) : base(bounds) { }
    public TickableRootNode(Region bound, byte latticeDepth) : base(bound, latticeDepth) { }
}

public class TickableSpatialLattice(Region outerBounds, byte latticeDepth)
    : SpatialLattice<TickableRootNode>(outerBounds, latticeDepth),
      ITickableSpatialNode
{
    public TickableSpatialLattice()
        : this(LatticeUniverse.RootRegion, 0) { }

    protected override TickableRootNode CreateRoot(Region bounds, byte latticeDepth)
        => new(bounds, latticeDepth);

    public void Tick() => m_root.Tick();
}

public class TickableSubLatticeBranchNode
    : SubLatticeBranchNode<TickableSpatialLattice>,
      IChildNode<OctetParentNode>,
      ITickableChildNode
{
    public new TickableOctetParentNode Parent { get; }
    OctetParentNode IChildNode<OctetParentNode>.Parent => Parent;

    public TickableSubLatticeBranchNode(
        Region bounds,
        TickableOctetParentNode parent,
        byte latticeDepth,
        IList<ISpatialObject> migrants)
        : base(bounds, parent)
    {
        Parent = parent;
        Sublattice = new TickableSpatialLattice(bounds, (byte)(latticeDepth + 1));
        Sublattice.Migrate(migrants);
    }
    public void Tick() => Sublattice.Tick();
}
