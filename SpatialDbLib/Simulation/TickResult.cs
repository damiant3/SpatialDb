using SpatialDbLib.Lattice;
using SpatialDbLib.Math;

namespace SpatialDbLib.Simulation;

public enum TickAction
{
    Move,
    Remove
}

public struct TickResult
{
    public SpatialObject Object { get; init; }
    public TickAction Action { get; init; }
    public LongVector3 Target { get; init; }

    public TickResult(SpatialObject obj, TickAction action, LongVector3 target)
    {
        Object = obj;
        Action = action;
        Target = target;
    }

    public static TickResult Move(SpatialObject obj, LongVector3 target)
        => new(obj, TickAction.Move, target);

    public static TickResult Remove(SpatialObject obj)
        => new(obj, TickAction.Remove, LongVector3.Zero);
}