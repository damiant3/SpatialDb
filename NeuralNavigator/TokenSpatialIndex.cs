using System.Numerics;
///////////////////////////////////////////////
namespace NeuralNavigator;

/// <summary>
/// Brute-force nearest-point-to-ray query over projected 3D token positions.
/// For 10-50K visible tokens this is fast enough at interactive rates.
/// </summary>
static class TokenSpatialIndex
{
    /// <summary>
    /// Finds the token index whose projected 3D position is closest to the given ray.
    /// Returns -1 if no token is within <paramref name="maxDistance"/>.
    /// </summary>
    public static int FindNearestToRay(
        Vector3[] projected, int count,
        Vector3 rayOrigin, Vector3 rayDirection,
        float maxDistance = 2.0f)
    {
        Vector3 dir = Vector3.Normalize(rayDirection);
        float bestDist = maxDistance;
        int bestIdx = -1;

        for (int i = 0; i < count; i++)
        {
            Vector3 toPoint = projected[i] - rayOrigin;
            float along = Vector3.Dot(toPoint, dir);
            if (along < 0) continue;

            Vector3 closest = rayOrigin + dir * along;
            float dist = Vector3.Distance(projected[i], closest);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
            }
        }

        return bestIdx;
    }
}
