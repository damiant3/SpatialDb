using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using SpatialDbLib.Synchronize;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public interface ITickable
{
    TickResult? Tick();
    void RegisterForTicks();
    void UnregisterForTicks();

}

public interface IMoveable
    : ISpatialObject,
      ITickable
{
    IntVector3 Velocity { get; set; }

    void Accelerate(IntVector3 intVector3);
    IList<IntVector3> GetVelocityStack();
    void SetVelocityStack(IList<IntVector3> newStack);
}

public interface ITickableMoveableObjectProxy
    : IMoveable,
      ISpatialObjectProxy
{
    new IMoveable OriginalObject { get; }
}

public abstract class TickableSpatialObjectBase(IList<LongVector3> position)
    : SpatialObject(position),
      IMoveable
{
    private IList<IntVector3> m_velocityStack = [new(0)];
    private long m_lastTick = 0;
    private TickableVenueLeafNode? m_occupyingLeaf;

    public bool IsStationary => !SimulationPolicy.MeetsMovementThreshold(LocalVelocity);

    public IntVector3 LocalVelocity
    {
        get
        {
            return m_velocityStack[^1];
        }
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
        m_lastTick = DateTime.Now.Ticks;
        m_occupyingLeaf?.RegisterForTicks(this);
    }

    public void UnregisterForTicks()
    {
        m_lastTick = 0;

        m_occupyingLeaf?.UnregisterForTicks(this);
    }

    const long ExpectedTicksPerTick = TimeSpan.TicksPerSecond / 10;

    public virtual TickResult? Tick()
    {
        if (LocalVelocity.IsZero) return null;

        var deltaTicks = (int)(DateTime.Now.Ticks - m_lastTick);
        m_lastTick += deltaTicks;

        var scaledVelocity = LocalVelocity * deltaTicks;

        var fractionalMovement = new ShortVector3(
            (short)(scaledVelocity.X / ExpectedTicksPerTick),
            (short)(scaledVelocity.Y / ExpectedTicksPerTick),
            (short)(scaledVelocity.Z / ExpectedTicksPerTick)
        );

        LongVector3 target = LocalPosition + fractionalMovement;
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
    IMoveable ITickableMoveableObjectProxy.OriginalObject => OriginalObject;
    ISpatialObject ISpatialObjectProxy.OriginalObject => OriginalObject;
    public VenueLeafNode TargetLeaf { get; set; }

    public TickableSpatialObjectProxy(
        TickableSpatialObject originalObj,
        VenueLeafNode targetLeaf,
        LongVector3 proposedPosition)
        : base([.. originalObj.GetPositionStack()])
    {
#if DEBUG
        if (originalObj.PositionStackDepth == 0)
            throw new InvalidOperationException("Original object has no position.");
        if (targetLeaf.IsRetired)
            throw new InvalidOperationException("Target leaf is retired.");
#endif
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