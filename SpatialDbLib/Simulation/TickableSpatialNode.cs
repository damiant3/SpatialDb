using SpatialDbLib.Lattice;

namespace SpatialDbLib.Simulation;

public interface ITickableSpatialNode
{
    void Tick();
}

public interface ITickableChildNode : IChildNode<TickableOctetParentNode>, ITickableSpatialNode {}

public class TickableOctetParentNode(Region bounds)
    : OctetParentNode(bounds),
      ITickableSpatialNode
{
    public override ITickableChildNode[] Children { get; } = new ITickableChildNode[8];
    public void Tick()
    {
        foreach (var child in Children)
            child.Tick();
    }

    public override IChildNode<TickableOctetParentNode> CreateBranchNodeWithLeafs(
        OctetParentNode parent,
        VenueLeafNode subdividingleaf,
        byte latticeDepth,
        bool branchOrSublattice,
        List<SpatialObject> occupantsSnapshot)
    {
        return branchOrSublattice
            ? new TickableOctetBranchNode(subdividingleaf.Bounds, parent, occupantsSnapshot)
            : new SubLatticeBranchNode<TickableSpatialLattice>(subdividingleaf.Bounds, parent, (byte)(latticeDepth + 1), occupantsSnapshot);
    }

    public override VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax)
    {
        return new TickableVenueLeafNode(new(childMin, childMax), this);
    }
}

public class TickableOctetBranchNode
    : TickableOctetParentNode,
      IChildNode<TickableOctetParentNode>
{
    public TickableOctetBranchNode(Region bounds, OctetParentNode parent, IList<SpatialObject> migrants)
        :base(bounds)
    {
        Parent = parent;
        AdmitMigrants(migrants);
    }

    public IParentNode Parent { get; }

}

public class TickableVenueLeafNode(Region bounds, TickableOctetParentNode parent)
    : VenueLeafNode(bounds, parent),
      ITickableSpatialNode
{

    private readonly List<ITickableObject> m_tickableObjects = [];

    public void RegisterForTicks(ITickableObject obj)
    {
        m_tickableObjects.Add(obj);
    }

    public void UnregisterForTicks(ITickableObject obj)
    {
        m_tickableObjects.Remove(obj);  // Could be a swap-remove if order doesn't matter
    }

    public void Tick()
    {
        if (m_tickableObjects.Count == 0) return;
        foreach (var obj in m_tickableObjects)
        {
            obj.Tick();
        }
    }
}

public class TickableSubLatticeBranchNode
    : SubLatticeBranchNode<TickableSpatialLattice>,
      ITickableSpatialNode
{
    internal override TickableSpatialLattice Sublattice { get; }

    public TickableSubLatticeBranchNode(Region bounds, TickableOctetParentNode parent, byte latticeDepth, IList<SpatialObject> migrants)
        :base(bounds, parent)
    {
        Sublattice = new TickableSpatialLattice(bounds, latticeDepth);
        Sublattice.AdmitMigrants(migrants);
    }
    public void Tick()
    {
        Sublattice.Tick();
    }
}

public class TickableSpatialLattice
    : TickableOctetParentNode, ISpatialLattice

{
    public TickableSpatialLattice()
    : base(LatticeUniverse.RootRegion)
    {
        BoundsTransform = new ParentToSubLatticeTransform(LatticeUniverse.RootRegion);
    }

    public TickableSpatialLattice(Region outerBounds, byte latticeDepth)
        : base(LatticeUniverse.RootRegion)
    {
        LatticeDepth = latticeDepth;
        BoundsTransform = new ParentToSubLatticeTransform(outerBounds);
    }

    public readonly ParentToSubLatticeTransform BoundsTransform;
    public readonly byte LatticeDepth;

}