🏗️ Developer Guide: Extending the Spatial Lattice System
Architecture Overview
The spatial lattice uses a type family pattern with generic base classes to support polymorphic node hierarchies. This allows you to create parallel hierarchies (e.g., standard vs. tickable) without code duplication.
Core Hierarchy
SpatialLattice (facade/coordinator)
  └─ RootNode<TParent, TBranch, TVenue, TSelf> (generic root with type family)
       └─ OctetParentNode (parent node logic)
            └─ OctetBranchNode (parent with parent)
            └─ LeafNode (abstract leaf)
                 └─ VenueLeafNode (leaf with occupants)
                 └─ SubLatticeBranchNode (nested lattice)
Key Principles

Type Families Travel Together: Branch, Venue, and Root types must be compatible
ThreadStatic Depth Tracking: SpatialLattice.CurrentThreadLatticeDepth tracks context
Position Stacks: Objects have position stacks; use GetPositionAtDepth(depth) for navigation
Factories in Root: Root node knows concrete types via abstract factory methods


🎯 How to Add New Capabilities (e.g., Tick)
Step 1: Define Your Interfaces
Create interfaces for the new behavior at each level:
csharp// For nodes that can be ticked
public interface ITickableSpatialNode : ISpatialNode
{
    void Tick();
}

// For objects that participate in ticking
public interface ITickableObject
{
    TickResult? Tick();  // Returns movement/action request
}

// For child nodes in tickable hierarchy
public interface ITickableChildNode : IChildNode<TickableOctetParentNode>, ITickableSpatialNode
{
    // Combines child behavior with tickable behavior
}
Step 2: Create Tickable Base Classes
Extend the core hierarchy with tickable versions:
csharp// Tickable parent node
public class TickableOctetParentNode : OctetParentNode, ITickableSpatialNode
{
    public TickableOctetParentNode(Region bounds) : base(bounds) { }
    
    // Override Children to return tickable children
    public new ITickableChildNode[] Children => 
        base.Children.Cast<ITickableChildNode>().ToArray();
    
    // Implement tick behavior
    public virtual void Tick()
    {
        foreach (var child in Children)
            child.Tick();
    }
}

// Tickable branch
public class TickableOctetBranchNode 
    : TickableOctetParentNode, 
      ITickableChildNode
{
    public TickableOctetBranchNode(Region bounds, OctetParentNode parent, IList<SpatialObject> migrants)
        : base(bounds)
    {
        Parent = (TickableOctetParentNode)parent;
        AdmitMigrants(migrants);
    }
    
    public new TickableOctetParentNode Parent { get; }
    OctetParentNode IChildNode<OctetParentNode>.Parent => Parent;
}

// Tickable venue leaf
public class TickableVenueLeafNode 
    : VenueLeafNode, 
      ITickableChildNode
{
    private List<ITickableObject> m_tickableObjects = new();
    
    public TickableVenueLeafNode(Region bounds, OctetParentNode parent) 
        : base(bounds, parent) { }
    
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
Step 3: Create Tickable Root Node
Define the root that declares the tickable type family:
public class TickableSpatialRootNode 
    : RootNode<TickableOctetParentNode, TickableOctetBranchNode, TickableVenueLeafNode, TickableSpatialRootNode>
{
    public TickableSpatialRootNode(Region bounds, byte latticeDepth)
        : base(bounds, latticeDepth) { }
    
    // Root knows how to tick
    public void Tick()
    {
        // Tick all children
        foreach (var child in Children.Cast<ITickableSpatialNode>())
            child.Tick();
    }
}
Step 4: Create Tickable Lattice Facade
The public-facing class that users interact with:
public class TickableSpatialLattice : ISpatialLattice
{
    protected readonly TickableSpatialRootNode m_root;
    public ParentToSubLatticeTransform BoundsTransform { get; protected set; }
    
    public TickableSpatialLattice() 
        : this(LatticeUniverse.RootRegion, 0) { }
    
    public TickableSpatialLattice(Region outerBounds, byte latticeDepth)
    {
        m_root = new TickableSpatialRootNode(LatticeUniverse.RootRegion, latticeDepth)
        {
            OwningLattice = this
        };
        BoundsTransform = new ParentToSubLatticeTransform(outerBounds);
    }
    
    public byte LatticeDepth => m_root.LatticeDepth;
    
    // Delegate all ISpatialLattice methods to m_root
    public AdmitResult Insert(SpatialObject obj)
    {
        using var s = SpatialLattice.PushLatticeDepth(LatticeDepth);
        using var s2 = new SlimSyncer(((ISync)obj).Sync, SlimSyncer.LockMode.Write, "Insert");
        
        var positionAtThisDepth = obj.GetPositionAtDepth(LatticeDepth);
        var admitResult = m_root.Admit(obj, positionAtThisDepth);
        
        if (admitResult is AdmitResult.Created created)
            created.Proxy.Commit();
        
        return admitResult;
    }
    
    // ... other ISpatialLattice methods ...
    
    // New tickable functionality
    public void Tick()
    {
        using var s = SpatialLattice.PushLatticeDepth(LatticeDepth);
        m_root.Tick();
    }
}
Step 5: Handle Sublattices
Create tickable sublattice branch node:
public class TickableSubLatticeBranchNode : SubLatticeBranchNode, ITickableChildNode
{
    internal new TickableSpatialLattice Sublattice { get; set; }
    
    public TickableSubLatticeBranchNode(
        Region bounds, 
        OctetParentNode parent, 
        byte latticeDepth, 
        IList<SpatialObject> migrants)
        : base(bounds, parent)
    {
        Sublattice = new TickableSpatialLattice(bounds, latticeDepth);
        Sublattice.AdmitMigrants(migrants);
        base.Sublattice = Sublattice;  // Set base class property
    }
    
    public void Tick()
    {
        Sublattice.Tick();
    }
}
Step 6: Override Factory Methods in Root
Tell the root how to create tickable nodes:
public class TickableSpatialRootNode : RootNode<...>
{
    // Override factory from OctetParentNode
    public override IChildNode<OctetParentNode> CreateBranchNodeWithLeafs(
        OctetParentNode parent,
        VenueLeafNode subdividingLeaf,
        byte latticeDepth,
        bool branchOrSublattice,
        List<SpatialObject> occupantsSnapshot)
    {
        return branchOrSublattice
            ? new TickableOctetBranchNode(subdividingLeaf.Bounds, parent, occupantsSnapshot)
            : new TickableSubLatticeBranchNode(subdividingLeaf.Bounds, parent, (byte)(latticeDepth + 1), occupantsSnapshot);
    }
    
    public override VenueLeafNode CreateNewVenueNode(int i, LongVector3 childMin, LongVector3 childMax)
    {
        return new TickableVenueLeafNode(new Region(childMin, childMax), this);
    }
}

🧪 Testing Your Extension
Test Template
csharp[TestMethod]
public void Test_TickableBasicFunctionality()
{
    var lattice = new TickableSpatialLattice();
    
    // 1. Insert tickable object
    var obj = new TickableSpatialObject([new(100, 100, 100)]);
    obj.Velocity = new IntVector3(10, 0, 0);  // Set movement
    
    lattice.Insert(obj);
    
    // 2. Verify insertion worked
    var leaf = lattice.ResolveOccupyingLeaf(obj);
    Assert.IsNotNull(leaf);
    Assert.IsInstanceOfType(leaf, typeof(TickableVenueLeafNode));
    
    // 3. Tick the lattice
    lattice.Tick();
    
    // 4. Verify tick behavior occurred
    // (e.g., object moved, state changed, etc.)
}

[TestMethod]
public void Test_TickableWithSublattices()
{
    var lattice = new TickableSpatialLattice();
    
    // Force sublattice creation
    for (int i = 0; i < 20; i++)
    {
        var obj = new TickableSpatialObject([new(1, 1, 1)]);
        lattice.Insert(obj);
    }
    
    // Verify tick propagates through sublattices
    lattice.Tick();
    
    // Verify all objects were ticked
}

⚠️ Common Pitfalls
1. Forgetting PushLatticeDepth
Every entry point from your facade must push depth:
public void Tick()
{
    using var s = SpatialLattice.PushLatticeDepth(LatticeDepth);  // ← Don't forget!
    m_root.Tick();
}
2. Mismatched Type Families
If root creates TickableOctetBranchNode but expects OctetBranchNode, you'll get cast exceptions. Keep types consistent.
3. Not Overriding CreateBranchNodeWithLeafs
The base OctetParentNode.CreateBranchNodeWithLeafs creates non-tickable nodes. Override it in your tickable root!
4. Forgetting Sublattice Variants
Regular sublattices won't tick. Make TickableSubLatticeBranchNode that contains TickableSpatialLattice.

📋 Checklist for New Extension

 Define interfaces for new behavior (ITickableSpatialNode, etc.)
 Create extended parent node class (TickableOctetParentNode)
 Create extended branch node class (TickableOctetBranchNode)
 Create extended venue leaf class (TickableVenueLeafNode)
 Create extended sublattice branch class (TickableSubLatticeBranchNode)
 Create root node declaring type family (TickableSpatialRootNode)
 Create facade class (TickableSpatialLattice)
 Override factory methods in root node
 Add PushLatticeDepth to all facade entry points
 Write unit tests for basic functionality
 Write unit tests for sublattice scenarios
 Write concurrent stress tests


This pattern lets you extend the lattice without touching existing code. The generic RootNode does the heavy lifting, you just declare your type family and implement your new behavior!
Good luck with Tick! 