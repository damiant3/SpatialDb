using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public interface ITickableObject
{
    TickResult? Tick();
}

public class TickableSpatialObject(List<LongVector3> position)
    : SpatialObject(position),
      ITickableObject
{
    public TickableSpatialObject(LongVector3 position) : this([position]) { }
    
    private IList<IntVector3> m_velocityStack = [new(0)];
    private long m_lastTick = 0;
    private TickableVenueLeafNode m_occupyingLeaf = null!;

    public bool IsStationary => !SimulationPolicy.MeetsMovementThreshold(LocalVelocity);

    public IntVector3 LocalVelocity
    {
        get
        {
            EnsureVelocityStackDepth();
            return m_velocityStack[^1];
        }
        set
        {
            EnsureVelocityStackDepth();
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
        EnsureVelocityStackDepth();
        return [.. m_velocityStack];
    }

    public void SetVelocityStack(IList<IntVector3> newStack)
    {
        m_velocityStack = newStack;
    }

    private void EnsureVelocityStackDepth()
    {
        while (m_velocityStack.Count < PositionStackDepth)
            m_velocityStack.Add(IntVector3.Zero);
        
        while (m_velocityStack.Count > PositionStackDepth)
            m_velocityStack.RemoveAt(m_velocityStack.Count - 1);
    }

    public void Accelerate(IntVector3 acceleration)
    {
        LocalVelocity += acceleration;
    }

    public void SetOccupyingLeaf(TickableVenueLeafNode leaf)
    {
        m_occupyingLeaf = leaf;
        EnsureVelocityStackDepth();
    }

    public void RegisterForTicks()
    {
        m_lastTick = DateTime.Now.Ticks;
        
        if (m_occupyingLeaf != null)
            m_occupyingLeaf.RegisterForTicks(this);
    }

    public void UnregisterForTicks()
    {
        m_lastTick = 0;
        
        if (m_occupyingLeaf != null)
            m_occupyingLeaf.UnregisterForTicks(this);
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

public class TickableSpatialObjectProxy : SpatialObjectProxy, ITickableObject
{
    private IList<IntVector3> m_velocityStack;
    private long m_lastTick = 0;

    public TickableSpatialObjectProxy(
        TickableSpatialObject originalObj, 
        VenueLeafNode targetLeaf, 
        LongVector3 proposedPosition)
        : base(originalObj, targetLeaf, proposedPosition)
    {
        m_velocityStack = [.. originalObj.GetVelocityStack()];
        m_lastTick = DateTime.Now.Ticks;
    }

    public new TickableSpatialObject OriginalObject => (TickableSpatialObject)base.OriginalObject;

    public IntVector3 LocalVelocity
    {
        get => m_velocityStack[^1];
        set => m_velocityStack[^1] = value;
    }

    const long ExpectedTicksPerTick = TimeSpan.TicksPerSecond / 10;

    public TickResult? Tick()
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

    public override void Commit()
    {
        base.Commit();
        OriginalObject.SetVelocityStack(m_velocityStack);
    }
}

