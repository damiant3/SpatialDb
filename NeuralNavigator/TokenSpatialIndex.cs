using System.Numerics;
///////////////////////////////////////////////
namespace NeuralNavigator;

static class TokenSpatialIndex
{
    // brute-force ray query — fast enough for 10-50K visible tokens at interactive rates
    public static int FindNearestToRay(Vector3[] projected, int count, Vector3 rayOrigin, Vector3 rayDirection, float maxDistance = 2.0f)
    {
        Vector3 dir = Vector3.Normalize(rayDirection);
        float bestDist = maxDistance;
        int bestIdx = -1;

        for (int i = 0; i < count; i++)
        {
            Vector3 toPoint = projected[i] - rayOrigin;
            float along = Vector3.Dot(toPoint, dir);
            if (along < 0) continue;

            float dist = Vector3.Distance(projected[i], rayOrigin + dir * along);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
            }
        }

        return bestIdx;
    }
}
