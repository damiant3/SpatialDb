using SpatialDbLib.Lattice;
namespace SpatialDbLib.Simulation;

public enum TickAction
{
    Move,
    Remove
}
//public readonly struct TickResult(TickableSpatialObject obj, TickAction action, LongVector3 target)
//{
//    public TickableSpatialObject Object { get; } = obj;
//    public TickAction Action { get; } = action;

//    public LongVector3 Target { get; } = target;
//    public static TickResult Move(TickableSpatialObject obj, LongVector3 targetCoordinate)
//        => new(obj, TickAction.Move, targetCoordinate);
//    public static TickResult Remove(TickableSpatialObject obj)
//        => new(obj, TickAction.Remove, default);
//}

