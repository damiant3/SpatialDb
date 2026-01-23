using SpatialDbLib.Lattice;

namespace SpatialDbLib.Simulation;

public interface ISimulationRegion
{
    ulong TickCount { get; }
    void StartTicking();
    void StopTicking();
    void TickObjects();
}

public abstract class TickableSpatialNode(Region bounds)
    : SpatialNode(bounds),
      ISimulationRegion
{
    protected bool m_isTicking;
    public ulong TickCount { get; protected set; } = 0;

    public void StartTicking()
    {
        m_isTicking = true;
    }

    public void StopTicking()
    {
        m_isTicking = false;
    }

    public abstract void TickObjects();
}
