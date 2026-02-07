using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public interface ITickableObject
{
    TickResult? Tick();
}

public static class SimulationPolicy
{
    public const int MinPerAxis = 10;
    public const int MinSum = 20;

    public static bool MeetsMovementThreshold(IntVector3 velocity)
    {
        if (velocity.MaxComponentAbs() < MinPerAxis)
            return false;

        return velocity.SumAbs() >= MinSum;
    }

    public static IntVector3 EnforceMovementThreshold(IntVector3 velocity)
    {
        return MeetsMovementThreshold(velocity)
            ? velocity : IntVector3.Zero;
    }
}

public class TickableSpatialObject(List<LongVector3> position)
    : SpatialObject(position),
      ITickableObject
{
    public TickableSpatialObject(LongVector3 position) : this([position]) { }
    private IntVector3 m_velocity = new(0);

    private long m_lastTick = 0;
    private TickableVenueLeafNode m_occupyingLeaf = null!;  // a promise to set this before registering for ticks

    public bool IsStationary => !SimulationPolicy.MeetsMovementThreshold(Velocity);

    public IntVector3 Velocity
    {
        get => m_velocity;
        internal set
        {
            var enforced = SimulationPolicy.EnforceMovementThreshold(value);
            bool wasMoving = !m_velocity.IsZero;
            m_velocity = enforced;
        }
    }

    public void Accelerate(IntVector3 acceleration)
    {
        Velocity += acceleration;
    }

    public void SetOccupyingLeaf(TickableVenueLeafNode leaf)
    {
        m_occupyingLeaf = leaf;
    }

    public void RegisterForTicks()
    {
        m_lastTick = DateTime.Now.Ticks;
        
        if (m_occupyingLeaf != null)
        {
            m_occupyingLeaf.RegisterForTicks(this);
        }
    }

    public void UnregisterForTicks()
    {
        m_lastTick = 0;
        
        if (m_occupyingLeaf != null)
        {
            m_occupyingLeaf.UnregisterForTicks(this);
        }
    }
    
    // Expected ticks per second = 10, so ticks per tick = TimeSpan.TicksPerSecond / 10
    const long ExpectedTicksPerTick = TimeSpan.TicksPerSecond / 10;

    public virtual TickResult? Tick()
    {
        if (m_velocity.IsZero) return null;

        var deltaTicks = (int)(DateTime.Now.Ticks - m_lastTick);
        m_lastTick += deltaTicks;

        var scaledVelocity = m_velocity * deltaTicks;

        // scaledVelocity is in units of (velocity * ticks), divide by expectedTicksPerTick to get actual movement
        var fractionalMovement = new ShortVector3(
            (short)(scaledVelocity.X / ExpectedTicksPerTick),
            (short)(scaledVelocity.Y / ExpectedTicksPerTick),
            (short)(scaledVelocity.Z / ExpectedTicksPerTick)
        );

        LongVector3 target = LocalPosition + fractionalMovement;
        return TickResult.Move(this, target);
    }
}

