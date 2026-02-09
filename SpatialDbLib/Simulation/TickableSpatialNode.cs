using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
using System.Collections.Concurrent;
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
    internal List<ITickableObject> m_tickableObjects = [];
    
#if DEBUG
    // 🔍 DEBUG: Track registration changes (only in DEBUG builds)
    private static readonly ConcurrentDictionary<Guid, string> s_registrationLog = new();
#endif
    
    protected override ISpatialObjectProxy CreateProxy(
        ISpatialObject obj,
        LongVector3 proposedPosition)
    {
         return obj is TickableSpatialObject tickable
            ? new TickableSpatialObjectProxy(tickable, this, proposedPosition)
            : base.CreateProxy(obj, proposedPosition);
    }

    public new TickableOctetParentNode Parent { get; } = parent;

    public override void AdmitMigrants(IList<ISpatialObject> objs)
    {
        base.AdmitMigrants(objs);
        foreach (var obj in objs)
        {
            if (obj is TickableSpatialObjectBase tickable)
            {
                tickable.SetOccupyingLeaf(this);
                
#if DEBUG
                // 🔍 DEBUG: Log migration registration
                var stackTrace = new System.Diagnostics.StackTrace(1, true);
                s_registrationLog[tickable.Guid] = $"REGISTERED via AdmitMigrants in leaf {GetHashCode()} at {DateTime.Now:HH:mm:ss.fff}\nStack: {stackTrace}";
#endif
                
                RegisterForTicks(tickable);
            }
        }
    }

    public override void Replace(ISpatialObjectProxy proxy)
    {
        if (proxy is ITickableObject tickableProxy)
        {
#if DEBUG
            // 🔍 DEBUG: Log proxy unregistration
            if (proxy.OriginalObject is TickableSpatialObjectBase tickable)
            {
                var stackTrace = new System.Diagnostics.StackTrace(1, true);
                s_registrationLog[tickable.Guid] = $"UNREGISTERED proxy via Replace in leaf {GetHashCode()} at {DateTime.Now:HH:mm:ss.fff}\nStack: {stackTrace}";
            }
#endif
            UnregisterForTicks(tickableProxy);
        }

        if (proxy.OriginalObject is TickableSpatialObjectBase tickableOriginal)
        {
            tickableOriginal.SetOccupyingLeaf(this);
            
#if DEBUG
            // 🔍 DEBUG: Log replacement registration
            var stackTrace = new System.Diagnostics.StackTrace(1, true);
            s_registrationLog[tickableOriginal.Guid] = $"REGISTERED via Replace in leaf {GetHashCode()} at {DateTime.Now:HH:mm:ss.fff}\nStack: {stackTrace}";
#endif
            
            RegisterForTicks(tickableOriginal);
        }
        base.Replace(proxy);
    }

    public void RegisterForTicks(ITickableObject obj)
    {
        if (!m_tickableObjects.Contains(obj))
        {
            m_tickableObjects.Add(obj);
            
#if DEBUG
            // 🔍 DEBUG: Log direct registration
            if (obj is TickableSpatialObjectBase tickable)
            {
                var stackTrace = new System.Diagnostics.StackTrace(1, true);
                s_registrationLog[tickable.Guid] = $"ADDED to m_tickableObjects in leaf {GetHashCode()} (count now {m_tickableObjects.Count}) at {DateTime.Now:HH:mm:ss.fff}\nStack: {stackTrace}";
            }
#endif
        }
    }

    public void UnregisterForTicks(ITickableObject obj)
    {
        var wasRemoved = m_tickableObjects.Remove(obj);
        
#if DEBUG
        // 🔍 DEBUG: Log unregistration
        if (wasRemoved && obj is TickableSpatialObjectBase tickable)
        {
            var stackTrace = new System.Diagnostics.StackTrace(1, true);
            s_registrationLog[tickable.Guid] = $"REMOVED from m_tickableObjects in leaf {GetHashCode()} (count now {m_tickableObjects.Count}) at {DateTime.Now:HH:mm:ss.fff}\nStack: {stackTrace}";
        }
#endif
    }

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
        if (tickableObj != null)
        {
            UnregisterForTicks(tickableObj);
        }

        // Start with immediate parent
        var currentParent = Parent;
        AdmitResult admitResult;
        int escalationLevel = 0;
        
        // Keep escalating up until we find a parent that can handle it
        while (true)
        {
            admitResult = currentParent.Admit(obj, newPosition);
            
#if DEBUG
            if (tickableObj != null)
            {
                s_registrationLog[tickableObj.Guid] += $"\n\nAdmit attempt at level {escalationLevel}, result: {admitResult.GetType().Name} at {DateTime.Now:HH:mm:ss.fff}";
            }
#endif
            
            if (admitResult is AdmitResult.Escalate)
            {
                // 🔍 SAFETY: Prevent infinite loops
                if (escalationLevel > 100)
                {
#if DEBUG
                    if (tickableObj != null)
                    {
                        s_registrationLog[tickableObj.Guid] += $"\n⚠ ESCALATION LOOP DETECTED at level {escalationLevel}!";
                    }
#endif
                    if (tickableObj != null) RegisterForTicks(tickableObj);
                    throw new InvalidOperationException($"Escalation loop detected - escalated {escalationLevel} times without resolution!");
                }
                
                // Need to go higher - check if current parent has a parent
                if (currentParent is IChildNode<TickableOctetParentNode> { Parent: not null } child)
                {
                    currentParent = child.Parent;
                    escalationLevel++;
#if DEBUG
                    if (tickableObj != null)
                    {
                        s_registrationLog[tickableObj.Guid] += $"\nEscalating to level {escalationLevel}";
                    }
#endif
                    continue; // Try again at higher level
                }
                else
                {
                    // No more parents - we're at the root and it still can't handle it!
#if DEBUG
                    if (tickableObj != null)
                    {
                        s_registrationLog[tickableObj.Guid] += $"\nESCALATION FAILED - reached root, position outside lattice bounds!";
                        s_registrationLog[tickableObj.Guid] += $"\nRe-registering in current leaf {GetHashCode()}";
                    }
#endif
                    if (tickableObj != null) RegisterForTicks(tickableObj);
                    // Position is outside the entire lattice - reject the move
                    return;
                }
            }
            
            // Not escalate - break out and handle the result
            break;
        }
        
        // Now handle the final result
        switch (admitResult)
        {
            case AdmitResult.Created created:
#if DEBUG
                if (tickableObj != null)
                {
                    s_registrationLog[tickableObj.Guid] += $"\nCommitting proxy in leaf {((VenueLeafNode)created.Proxy.TargetLeaf).GetHashCode()}";
                }
#endif
                created.Proxy.Commit();
                Vacate(obj);
                break;
                
            case AdmitResult.Rejected rejected:
                // Admission was rejected - re-register in current leaf
#if DEBUG
                if (tickableObj != null)
                {
                    s_registrationLog[tickableObj.Guid] += $"\nADMISSION REJECTED - re-registering in current leaf {GetHashCode()}";
                }
#endif
                if (tickableObj != null) RegisterForTicks(tickableObj);
                break;
                
            case AdmitResult.Escalate:
                // Should never get here - we handle escalate in the loop above
                throw new InvalidOperationException("Escalate should have been handled in escalation loop");
                
            default:
                // Unexpected result type
                throw new InvalidOperationException($"Unexpected AdmitResult type: {admitResult.GetType().Name}");
        }
        
#if DEBUG
        // 🔍 DEBUG: Log final state after boundary crossing handled
        if (tickableObj != null)
        {
            // Walk up to find root, then resolve the leaf
            TickableOctetParentNode root = Parent;
            while (root is IChildNode<TickableOctetParentNode> { Parent: not null } child)
            {
                root = child.Parent;
            }
            
            var finalLeaf = root.ResolveLeaf(obj) as TickableVenueLeafNode;
            var isRegistered = finalLeaf?.m_tickableObjects?.Contains(tickableObj) ?? false;
            s_registrationLog[tickableObj.Guid] += $"\n\nFinal state: InLeaf={finalLeaf?.GetHashCode()}, Registered={isRegistered}, Position={obj.LocalPosition}";
        }
#endif
    }
    
#if DEBUG
    // 🔍 DEBUG: Public method to dump registration log for specific object
    public static string GetRegistrationHistory(Guid objectGuid)
    {
        return s_registrationLog.TryGetValue(objectGuid, out var log) ? log : "No registration history found";
    }
#endif
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
        Sublattice.AdmitMigrants(migrants);
    }

    public void Tick() => Sublattice.Tick();
}