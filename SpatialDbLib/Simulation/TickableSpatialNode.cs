using SpatialDbLib.Lattice;
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

    protected ITickableChildNode GetTickableChild(int index)
    {
        return (ITickableChildNode)base.Children[index];
    }

    public virtual void Tick()
    {
        for (int i = 0; i < 8; i++)
            GetTickableChild(i).Tick();
    }
}

public class TickableOctetBranchNode
    : TickableOctetParentNode,
      IChildNode<OctetParentNode>, // Implement IChildNode<OctetParentNode> for compatibility
      IChildNode<TickableOctetParentNode>
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
      IChildNode<OctetParentNode>
{
    private List<ITickableObject> m_tickableObjects = [];

    public new TickableOctetParentNode Parent { get; } = parent;
    OctetParentNode IChildNode<OctetParentNode>.Parent => Parent;

    public void RegisterForTicks(ITickableObject obj)
    {
        if (!m_tickableObjects.Contains(obj))
            m_tickableObjects.Add(obj);
    }

    public void Tick()
    {
        foreach (var obj in m_tickableObjects.ToList())  // Copy to avoid modification during iteration
        {
            var result = obj.Tick();
            if (result.HasValue)
            {
                // Handle movement/removal requests
                HandleTickResult(result.Value);
            }
        }
    }

    private void HandleTickResult(TickResult result)
    {
        switch (result.Action)
        {
            case TickAction.Move:
                // Queue movement for later processing
                break;
            case TickAction.Remove:
                // Remove object
                break;
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
            : new TickableSubLatticeBranchNode(tickableLeaf.Bounds, tickableParent, (byte)(latticeDepth + 1), occupantsSnapshot);
    }

    public override VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax)
        => new TickableVenueLeafNode(new Region(childMin, childMax), this);

    public VenueLeafNode? ResolveLeafFromOuterLattice(SpatialObject obj)
    {
        throw new NotImplementedException();
    }
}
public class TickableRootNode :
    TickableRootNode<TickableOctetParentNode, TickableOctetBranchNode, TickableVenueLeafNode, TickableRootNode>,
    IRootNode<OctetParentNode, OctetBranchNode, VenueLeafNode, TickableRootNode>
{
    public TickableRootNode(Region bounds) : base(bounds) { }
}
public class TickableSpatialLattice(Region outerBounds, byte latticeDepth)
    : SpatialLattice<TickableRootNode>(outerBounds, latticeDepth),
      ITickableSpatialNode
{
    public TickableSpatialLattice()
        :this(LatticeUniverse.RootRegion, 0) { }
    protected override TickableRootNode CreateRoot(Region bounds, byte depth)
    {
        return new TickableRootNode(bounds);
    }

    public void Tick()
    {
        m_root.Tick();
    }
}

public class TickableSubLatticeBranchNode
    : SubLatticeBranchNode<TickableSpatialLattice>,
      IChildNode<OctetParentNode>,
      ITickableChildNode
{
    public TickableOctetParentNode Parent { get; }
    OctetParentNode IChildNode<OctetParentNode>.Parent => Parent;

    public TickableSubLatticeBranchNode(
        Region bounds,
        TickableOctetParentNode parent,
        byte latticeDepth,
        IList<SpatialObject> migrants)
        : base(bounds, parent)
    {
        Parent = parent;
        Sublattice = new TickableSpatialLattice(bounds, latticeDepth);
        Sublattice.AdmitMigrants(migrants);
    }

    public void Tick()
    {
        Sublattice.Tick();
    }
}
