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
    {
        return new TickableVenueLeafNode(new Region(childMin, childMax), this);
    }

    public override IChildNode<OctetParentNode> CreateBranchNodeWithLeafs(
        OctetParentNode parent,
        VenueLeafNode subdividingLeaf,
        byte latticeDepth,
        bool branchOrSublattice,
        List<ISpatialObject> occupantsSnapshot)
    {
        var tickableParent = (TickableOctetParentNode)parent;
        var tickableLeaf = (TickableVenueLeafNode)subdividingLeaf;

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
    }
}

public class TickableOctetBranchNode
    : TickableOctetParentNode,
      IChildNode<OctetParentNode>,
      ITickableChildNode
{
    public TickableOctetBranchNode(Region bounds, TickableOctetParentNode parent, IList<ISpatialObject> migrants)
        : base(bounds)
    {
        Parent = parent;
        AdmitMigrants(migrants);
    }

    public TickableOctetParentNode Parent { get; }
    TickableOctetParentNode IChildNode<TickableOctetParentNode>.Parent => Parent;
    OctetParentNode IChildNode<OctetParentNode>.Parent => Parent;
}

public class TickableVenueLeafNode(Region bounds, TickableOctetParentNode parent)
    : VenueLeafNode(bounds, parent),
      ITickableSpatialNode,
      ITickableChildNode
{
    private List<ITickable> m_tickableObjects = [];
    protected override ISpatialObjectProxy CreateProxy(
        ISpatialObject obj,
        LongVector3 proposedPosition)
    {
        if (obj is TickableSpatialObject tickable)
        {
            var proxy = new TickableSpatialObjectProxy(tickable, this, proposedPosition);
            RegisterForTicks(proxy);
            return proxy;
        }

        return base.CreateProxy(obj, proposedPosition);
    }

    public new TickableOctetParentNode Parent { get; } = parent; // not proud of this

    public override AdmitResult Admit(ISpatialObject obj, LongVector3 proposedPosition)
    {
        if (!Bounds.Contains(proposedPosition))
            return AdmitResult.EscalateRequest();

        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "TickableVenue.Admit: Leaf");
        if (IsRetired)
            return AdmitResult.RetryRequest();

        if (IsAtCapacity(1))
        {
            return CanSubdivide()
                ? AdmitResult.SubdivideRequest(this)
                : AdmitResult.DelegateRequest(this);
        }

        ISpatialObjectProxy proxy = obj is TickableSpatialObject tickable
            ? new TickableSpatialObjectProxy(tickable, this, proposedPosition)
            : new SpatialObjectProxy(obj, this, proposedPosition);

        if (proxy is ITickable tickableProxy)
            RegisterForTicks(tickableProxy);

        Occupy(proxy);
        return AdmitResult.Create(proxy);
    }

    public override void AdmitMigrants(IList<ISpatialObject> objs)
    {
        base.AdmitMigrants(objs);
        foreach (var obj in objs)
        {
            if (obj is TickableSpatialObjectBase tickable)
            {
                tickable.SetOccupyingLeaf(this);

                if (tickable.Velocity != IntVector3.Zero)
                    RegisterForTicks(tickable);
            }
        }
    }

    public override void Replace(ISpatialObjectProxy proxy)
    {
        base.Replace(proxy);

        if (proxy is ITickable tickableProxy)
            UnregisterForTicks(tickableProxy);

        if (proxy.OriginalObject is TickableSpatialObjectBase tickable)
        {
            tickable.SetOccupyingLeaf(this);
            if (tickable.Velocity != IntVector3.Zero)
                RegisterForTicks(tickable);
        }
    }

    public void RegisterForTicks(ITickable obj)
    {
        if (!m_tickableObjects.Contains(obj))
            m_tickableObjects.Add(obj);
    }

    public void UnregisterForTicks(ITickable obj)
        => m_tickableObjects.Remove(obj);

    public void Tick()
    {
        foreach (var obj in m_tickableObjects.ToList())
        {
            var result = obj.Tick();
            if (result.HasValue)
                HandleTickResult(result.Value);
        }
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
        var tickableObj = obj as TickableSpatialObjectBase;
        if (tickableObj != null) UnregisterForTicks(tickableObj);

        var admitResult = Parent.Admit(obj, newPosition);
        if (admitResult is AdmitResult.Created created)
        {
            created.Proxy.Commit();
            Vacate(obj);
            tickableObj?.RegisterForTicks();
        }
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

    public override IChildNode<OctetParentNode> CreateBranchNodeWithLeafs(
        OctetParentNode parent,
        VenueLeafNode subdividingLeaf,
        byte latticeDepth,
        bool branchOrSublattice,
        List<ISpatialObject> occupantsSnapshot)
    {
        var tickableParent = (TickableOctetParentNode)parent;
        var tickableLeaf = (TickableVenueLeafNode)subdividingLeaf;

        return branchOrSublattice
            ? new TickableOctetBranchNode(tickableLeaf.Bounds, tickableParent, occupantsSnapshot)
            : new TickableSubLatticeBranchNode(tickableLeaf.Bounds, tickableParent, latticeDepth, occupantsSnapshot);
    }

    public override VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax)
        => new TickableVenueLeafNode(new Region(childMin, childMax), this);

    public VenueLeafNode? ResolveLeafFromOuterLattice(ISpatialObject obj)
    {
#if DEBUG
        if (obj.PositionStackDepth <= LatticeDepth)
            throw new InvalidOperationException("Object position stack depth is less than or equal to lattice depth during outer lattice leaf resolution.");
#endif
        return ResolveLeaf(obj);
    }
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

    public void Tick()
        => m_root.Tick();
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
        Sublattice.AdmitMigrants(migrants);
    }

    public void Tick()
        => Sublattice.Tick();
}