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
        get
        {
            using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "TickableSpatialObjectBase.LocalVelocity");
            return m_velocityStack[^1];
        }
        set
        {
            using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "TickableSpatialObjectBase.LocalVelocity");
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
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Read, "TickableSpatialObjectBase.LocalVelocity");
        return [.. m_velocityStack];
    }

    public void SetVelocityStack(IList<IntVector3> newStack)
    {
        using var s = new SlimSyncer(Sync, SlimSyncer.LockMode.Write, "TickableSpatialObjectBase.LocalVelocity");
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
        // Avoid int overflow by performing scaling in 64-bit and producing a LongVector3 target.
        if (LocalVelocity.IsZero) return null;
        var currentMs = MillisecondTicks32;
        var deltaMs = currentMs - m_lastMsTick;
        // Guard against zero or negative deltas (including wrap) — treat as no-op
        if (deltaMs <= 0) return null;
        m_lastMsTick = currentMs;

        // Scale using 64-bit arithmetic to avoid overflow when velocity * deltaMs exceeds Int32 range
        long dx = (long)LocalVelocity.X * (long)deltaMs;
        long dy = (long)LocalVelocity.Y * (long)deltaMs;
        long dz = (long)LocalVelocity.Z * (long)deltaMs;

        var pos = LocalPosition;
        var target = new LongVector3(pos.X + dx, pos.Y + dy, pos.Z + dz);
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
    private readonly ProxyCommitCoordinator<TickableSpatialObject, TickableSpatialObjectProxy> m_coordinator;

    public bool IsCommitted => m_coordinator.IsCommitted;
    public TickableSpatialObject OriginalObject => m_coordinator.OriginalObject;
    IMoveableObject ITickableMoveableObjectProxy.OriginalObject => OriginalObject;
    ISpatialObject ISpatialObjectProxy.OriginalObject => OriginalObject;
    
    public VenueLeafNode TargetLeaf
    { 
        get => m_coordinator.TargetLeaf;
        set => m_coordinator.TargetLeaf = value;
    }

    public TickableSpatialObjectProxy(
        TickableSpatialObject originalObj,
        VenueLeafNode targetLeaf,
        LongVector3 proposedPosition)
        : base([.. originalObj.GetPositionStack()])
    {
        m_coordinator = new ProxyCommitCoordinator<TickableSpatialObject, TickableSpatialObjectProxy>(
            originalObj, 
            targetLeaf);
        SetLocalPosition(proposedPosition);
        SetVelocityStack(originalObj.GetVelocityStack());
    }

    public virtual void Commit()
        => m_coordinator.Commit(
            transferState: original =>
            {
                original.SetPositionStack(GetPositionStack());
                original.SetVelocityStack(GetVelocityStack());
            },
            clearProxyState: () => SetPositionStack([]),
            proxy: this);


    public void Rollback()
        => m_coordinator.Rollback(this);
}