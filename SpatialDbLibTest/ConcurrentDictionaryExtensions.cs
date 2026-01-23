////////////////////////////////////
using System.Collections.Concurrent;
using System.Security.Cryptography;
namespace SpatialDbLibTest;

public static class ConcurrentDictionaryTestExtensions
{
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

        int index = RandomNumberGenerator.GetInt32(snapshot.Length);
        var candidate = snapshot[index];

         if (!dict.TryRemove(candidate.Key, out TValue? value))
            return false;

        taken = new KeyValuePair<TKey, TValue>(candidate.Key, value);
        return true;
    }
}
