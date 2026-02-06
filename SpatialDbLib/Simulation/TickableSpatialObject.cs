using SpatialDbLib.Lattice;

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

    public void RegisterForTicks()
    {
        m_lastTick = DateTime.Now.Ticks;
    }

    public void UnregisterForTicks()
    {
        m_lastTick = 0;
    }
    // Expected ticks per second = 10, so ticks per tick = TimeSpan.TicksPerSecond / 10
    const long expectedTicksPerTick = TimeSpan.TicksPerSecond / 10;

    public virtual TickResult? Tick()
    {
        if (m_velocity.IsZero) return null;

        var deltaTicks = (int)(DateTime.Now.Ticks - m_lastTick);
        m_lastTick += deltaTicks;

        var scaledVelocity = m_velocity * deltaTicks;
        
        // Add to remainder (converting from long to short precision)
        // scaledVelocity is in units of (velocity * ticks), divide by expectedTicksPerTick to get actual movement
        var fractionalMovement = new ShortVector3(
            (short)(scaledVelocity.X / expectedTicksPerTick),
            (short)(scaledVelocity.Y / expectedTicksPerTick),
            (short)(scaledVelocity.Z / expectedTicksPerTick)
        );
        


        LongVector3 target = LocalPosition + fractionalMovement;
        return TickResult.Move(this, target);
    }
}

