using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public interface ITickableObject
{
    TickResult? Tick();
    void RegisterForTicks();
    void UnregisterForTicks();
}

public interface IMoveableObject
    : ISpatialObject,
      ITickableObject
{
    IntVector3 Velocity { get; set; }
    void Accelerate(IntVector3 intVector3);
    IList<IntVector3> GetVelocityStack();
    void SetVelocityStack(IList<IntVector3> newStack);
}

public interface ITickableMoveableObjectProxy
    : IMoveableObject,
      ISpatialObjectProxy
{
    new IMoveableObject OriginalObject { get; }
}

public abstract class TickableSpatialObjectBase(IList<LongVector3> position)
    : SpatialObject(position),
      IMoveableObject
{
    private IList<IntVector3> m_velocityStack = [new(0)];
    private int m_lastMsTick = 0;
    private TickableVenueLeafNode? m_occupyingLeaf;

    public bool IsStationary => !SimulationPolicy.MeetsMovementThreshold(LocalVelocity);

    public IntVector3 LocalVelocity
    {
        get { return m_velocityStack[^1]; }
        set
        {
            var enforced = SimulationPolicy.EnforceMovementThreshold(value);
            m_velocityStack[^1] = enforced;
        }
    }

    public IntVector3 Velocity
    {
        get => LocalVelocity;
        set => LocalVelocity = value;
    }

    public IList<IntVector3> GetVelocityStack()
    {
        return [.. m_velocityStack];
    }

    public void SetVelocityStack(IList<IntVector3> newStack)
    {
        m_velocityStack = newStack;
    }

    public void Accelerate(IntVector3 acceleration)
    {
        LocalVelocity += acceleration;
    }

    public void SetOccupyingLeaf(TickableVenueLeafNode leaf)
    {
        m_occupyingLeaf = leaf;
    }

    public void RegisterForTicks()
    {
        m_lastMsTick = MillisecondTicks32;
        m_occupyingLeaf?.RegisterForTicks(this);
    }

    public void UnregisterForTicks()
    {
        m_lastMsTick = 0;
        m_occupyingLeaf?.UnregisterForTicks(this);
    }

    public static int MillisecondTicks32 => unchecked((int)((DateTime.Now.Ticks * 429497L) >> 32));

    public virtual TickResult? Tick()
    {
        if (LocalVelocity.IsZero) return null; 
        var currentMs = MillisecondTicks32;
        var deltaMs = currentMs - m_lastMsTick;
        if (deltaMs == 0) return null;
        m_lastMsTick = currentMs;
        var scaledVelocity = LocalVelocity * deltaMs;
        LongVector3 target = LocalPosition + scaledVelocity;
        return TickResult.Move(this, target);
    }
}

public class TickableSpatialObject : TickableSpatialObjectBase
{
    public TickableSpatialObject(LongVector3 position) : base([position]) { }
    public TickableSpatialObject(List<LongVector3> position) : base(position) { }
}

public class TickableSpatialObjectProxy
    : TickableSpatialObjectBase,
      ITickableMoveableObjectProxy
{
    public enum ProxyState
    {
        Uncommitted,
        Committed,
        RolledBack
    }

    private ProxyState m_proxyState;
    public bool IsCommitted => ProxyState.Committed == m_proxyState;
    public TickableSpatialObject OriginalObject { get; }
    IMoveableObject ITickableMoveableObjectProxy.OriginalObject => OriginalObject;
    ISpatialObject ISpatialObjectProxy.OriginalObject => OriginalObject;
    public VenueLeafNode TargetLeaf { get; set; }

    public TickableSpatialObjectProxy(
        TickableSpatialObject originalObj,
        VenueLeafNode targetLeaf,
        LongVector3 proposedPosition)
        : base([.. originalObj.GetPositionStack()])
    {
        m_proxyState = ProxyState.Uncommitted;
        OriginalObject = originalObj;
        TargetLeaf = targetLeaf;
        SetLocalPosition(proposedPosition);
        SetVelocityStack(originalObj.GetVelocityStack());
    }

    public virtual void Commit()
    {
        if (IsCommitted) throw new InvalidOperationException("Proxy already committed!");

        while (true)
        {
            var leaf = TargetLeaf;
            using var s = new SlimSyncer(((ISync)leaf).Sync, SlimSyncer.LockMode.Write, "TickableSpatialObjectProxy.Commit: Leaf");
            if (leaf.IsRetired) continue;
            OriginalObject.SetPositionStack(GetPositionStack());
            OriginalObject.SetVelocityStack(GetVelocityStack());
            leaf.Replace(this);
            SetPositionStack([]);
            m_proxyState = ProxyState.Committed;
            break;
        }
    }

    public void Rollback()
    {
        if (m_proxyState != ProxyState.Uncommitted) return;
        TargetLeaf.Vacate(this);
        m_proxyState = ProxyState.RolledBack;
    }
}