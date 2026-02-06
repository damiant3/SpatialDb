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

public class TickableSpatialObject(LongVector3 position)
    : SpatialObject([position]),
      ITickableObject
{

    private IntVector3 m_velocity = new(0);
    private ShortVector3 m_remainder = new(0);

    private long m_lastTick = DateTime.Now.Ticks;
    public bool IsStationary => !SimulationPolicy.MeetsMovementThreshold(Velocity);

    public IntVector3 Velocity
    {
        get => m_velocity;
        internal set
        {
            var enforced = SimulationPolicy.EnforceMovementThreshold(value);
            bool wasMoving = !m_velocity.IsZero;
            m_velocity = enforced;

            if (wasMoving && enforced.IsZero)
            {
                m_remainder = ShortVector3.Zero;  // Kill accumulator too
            }
        }
    }

    public void Accelerate(IntVector3 acceleration)
    {
        Velocity += acceleration;
    }

    public virtual TickResult? Tick()
    {
        if (m_velocity.IsZero) return null;
        var deltaTicks = DateTime.Now.Ticks - m_lastTick;
        m_lastTick+= deltaTicks;

        // Extract whole units (short can hold ±32K, plenty for per-tick movement)
        var movement = m_remainder.ToInt();
        if (movement.IsZero) return null;

        m_remainder = ShortVector3.Zero;  // Reset after extracting movement

        LongVector3 target = LocalPosition + movement;
        return TickResult.Move(this, target);
    }
}

