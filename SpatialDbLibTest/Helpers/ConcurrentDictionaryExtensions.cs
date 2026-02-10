using System.Collections.Concurrent;
///////////////////////////
namespace SpatialDbLibTest.Helpers;
public static class ConcurrentDictionaryTestExtensions
{
    public static bool TryTakeRandomFast<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dict,
        out KeyValuePair<TKey, TValue> taken,
        int maxProbes = 8)
    where TKey : notnull
    {
        taken = default;

        if (dict.IsEmpty)
            return false;

        int probes = 0;

        foreach (var kvp in dict)
        {
            if (probes++ >= maxProbes)
                break;

            if (FastRandom.NextInt(2) == 0 &&
                dict.TryRemove(kvp.Key, out var value))
            {
                taken = new(kvp.Key, value);
                return true;
            }
        }

        return false;
    }

    public static bool TryTakeRandom<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dict,
        out KeyValuePair<TKey, TValue> taken)
        where TKey : notnull
    {
        taken = default;

        if (dict.IsEmpty)
            return false;

        var snapshot = dict.ToArray();
        if (snapshot.Length == 0)
            return false;

        int index = FastRandom.NextInt(snapshot.Length);
        var candidate = snapshot[index];

         if (!dict.TryRemove(candidate.Key, out TValue? value))
            return false;

        taken = new KeyValuePair<TKey, TValue>(candidate.Key, value);
        return true;
    }
}
