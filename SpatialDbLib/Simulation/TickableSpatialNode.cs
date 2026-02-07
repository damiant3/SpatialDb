using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public interface ITickableSpatialNode : ISpatialNode
{
    void Tick();
}

public interface ITickableChildNode
    : IChildNode<TickableOctetParentNode>, ITickableSpatialNode { }

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
        List<SpatialObject> occupantsSnapshot)
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
    public TickableOctetBranchNode(Region bounds, TickableOctetParentNode parent, IList<SpatialObject> migrants)
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
    private List<ITickableObject> m_tickableObjects = [];

    public new TickableOctetParentNode Parent { get; } = parent;

    public override void AdmitMigrants(IList<SpatialObject> objs)
    {
        base.AdmitMigrants(objs);
        foreach (var obj in objs)
            if (obj is TickableSpatialObject tickable)
                tickable.SetOccupyingLeaf(this);
    }

    internal override void Replace(SpatialObjectProxy proxy)
    {
        base.Replace(proxy);
        if (proxy.OriginalObject is TickableSpatialObject tickable)
            tickable.SetOccupyingLeaf(this);
    }

    public void RegisterForTicks(ITickableObject obj)
    {
        if (!m_tickableObjects.Contains(obj))
            m_tickableObjects.Add(obj);
    }

    public void UnregisterForTicks(ITickableObject obj)
        => m_tickableObjects.Remove(obj);


    public void Tick()
    {
        foreach (var obj in m_tickableObjects.ToList())  // Copy to avoid modification during iteration
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
        var tickableObj = obj as TickableSpatialObject;
        if (tickableObj != null) UnregisterForTicks(tickableObj);
        var admitResult = Parent.Admit(obj, newPosition);
        if (admitResult is AdmitResult.Created created)
        {
            created.Proxy.Commit();
            Vacate(obj);
            tickableObj?.RegisterForTicks();
        }
        else
        {
            // Movement failed - object stays in current leaf
            // Could log this or handle differently
            // this wont happen until there is policy reasons, like a walled off part of the lattice for dev playground or something.
        }
    }
}

public class TickableRootNode<TParent, TBranch, TVenue, TSelf>(Region bounds, byte latticeDepth)
    : TickableOctetParentNode(bounds),
      IRootNode<TickableOctetParentNode, TickableOctetBranchNode, TickableVenueLeafNode, TickableRootNode<TParent, TBranch, TVenue, TSelf>>,
      ITickableSpatialNode
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
        List<SpatialObject> occupantsSnapshot)
    {
        var tickableParent = (TickableOctetParentNode)parent;
        var tickableLeaf = (TickableVenueLeafNode)subdividingLeaf;

        return branchOrSublattice
            ? new TickableOctetBranchNode(tickableLeaf.Bounds, tickableParent, occupantsSnapshot)
            : new TickableSubLatticeBranchNode(tickableLeaf.Bounds, tickableParent, latticeDepth, occupantsSnapshot);
    }

    public override VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax)
        => new TickableVenueLeafNode(new Region(childMin, childMax), this);

    public VenueLeafNode? ResolveLeafFromOuterLattice(SpatialObject obj)
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
        :this(LatticeUniverse.RootRegion, 0) { }
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
    public new TickableOctetParentNode Parent { get; }  // not proud of this new
    OctetParentNode IChildNode<OctetParentNode>.Parent => Parent;

    public TickableSubLatticeBranchNode(
        Region bounds,
        TickableOctetParentNode parent,
        byte latticeDepth,
        IList<SpatialObject> migrants)
        : base(bounds, parent)
    {
        Parent = parent;
        Sublattice = new TickableSpatialLattice(bounds, (byte)(latticeDepth+1));
        Sublattice.AdmitMigrants(migrants);
    }

    public void Tick()
        => Sublattice.Tick();
}
