using SpatialDbLib.Lattice;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public interface ITickableSpatialNode : ISpatialNode
{
    void Tick();
}

public interface ITickableChildNode : IChildNode<TickableOctetParentNode>, ITickableSpatialNode { }

public class TickableOctetParentNode(Region bounds)
    : OctetParentNode(bounds),
      ITickableSpatialNode
{
    public override IChildNode<OctetParentNode>[] Children => (IChildNode<OctetParentNode>[])TickableChildren;
    public ITickableChildNode[] TickableChildren => new ITickableChildNode[8];

    // Implement tick behavior
    public virtual void Tick()
    {
        foreach (var child in TickableChildren)
            child.Tick();
    }
}

public class TickableOctetBranchNode
    : TickableOctetParentNode,
      IChildNode<OctetParentNode>, // Implement IChildNode<OctetParentNode> for compatibility
      IChildNode<TickableOctetParentNode>,
      ITickableSpatialNode
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

    public override void Tick()
    {
        foreach (var child in TickableChildren)
            child.Tick();
    }
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
public class TickableOctetRootNode(Region bounds)
    : TickableRootNode<TickableOctetParentNode, TickableOctetBranchNode, TickableVenueLeafNode, TickableOctetRootNode>(bounds)
{ }

public class TickableRootNode<TParent, TBranch, TVenue, TSelf>(Region bounds, byte latticeDepth)
    : TickableOctetParentNode(bounds),
      IRootNode<TParent, TBranch, TVenue, TSelf>,
      ITickableSpatialNode
    where TParent : TickableOctetParentNode
    where TBranch : TickableOctetParentNode, IChildNode<TParent>
    where TVenue : TickableVenueLeafNode
    where TSelf : TickableRootNode<TParent, TBranch, TVenue, TSelf>
{
    protected TickableRootNode(Region bounds)
    : this(bounds, 0) { }

    public byte LatticeDepth { get; } = latticeDepth;

    public override void Tick()
    {
        foreach (var child in Children)
            child.Tick();
    }

    public IChildNode<OctetParentNode> CreateBranchNodeWithLeafs(
        TickableOctetParentNode parent,
        TickableVenueLeafNode subdividingLeaf,
        byte latticeDepth,
        bool branchOrSublattice,
        List<SpatialObject> occupantsSnapshot)
    {
        return branchOrSublattice
            ? new TickableOctetBranchNode(subdividingLeaf.Bounds, parent, occupantsSnapshot)
            : new TickableSubLatticeBranchNode(subdividingLeaf.Bounds, parent, (byte)(latticeDepth + 1), occupantsSnapshot);
    }

    public override VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax)
        => new TickableVenueLeafNode(new Region(childMin, childMax), this);

    public VenueLeafNode? ResolveLeafFromOuterLattice(SpatialObject obj)
    {
        throw new NotImplementedException();
    }
}

public class TickableSpatialLattice(Region outerBounds, byte latticeDepth)
        : SpatialLattice(outerBounds, latticeDepth),
      ITickableSpatialNode
{
    public TickableSpatialLattice()
        :this(LatticeUniverse.RootRegion, 0)
    {
        
    }
    public void Tick()
    {
        m_root.Tick();
    }
}

public class TickableSubLatticeBranchNode
    : LeafNode,
      ITickableChildNode
{
    internal TickableSpatialLattice Sublattice { get; }

    public TickableOctetParentNode Parent { get; }


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
