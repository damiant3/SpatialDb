using SpatialDbLib.Math;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

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

