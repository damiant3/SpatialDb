using SpatialDbLib.Lattice;
using SpatialDbLib.Math;

namespace SpatialDbLib.Simulation;

public enum TickAction
{
    Move,
    Remove
}

public readonly struct TickResult
{
    public SpatialObject Object { get; init; }
    public LongVector3 Target { get; init; }
    public TickAction Action { get; init; }

    public static TickResult Move(SpatialObject obj, LongVector3 target)
        => new() { Object = obj, Target = target, Action = TickAction.Move };

    public static TickResult Remove(SpatialObject obj)
        => new() { Object = obj, Action = TickAction.Remove };
}

