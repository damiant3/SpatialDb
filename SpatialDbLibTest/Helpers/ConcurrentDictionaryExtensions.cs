using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SpatialDbLibTest.Helpers;

public static class ConcurrentDictionaryTestExtensions
{
    // ============================================================================
    // CURRENT IMPLEMENTATION (Optimized)
    // ============================================================================
    
    /// <summary>
    /// Attempts to remove a random item using bounded enumeration.
    /// Fast but may fail when dictionary is nearly empty under high contention.
    /// Optimized: Removed probabilistic coin flip, added AggressiveInlining,
    /// continues searching after failed removal to handle race conditions.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryTakeRandomFast<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dict,
        out KeyValuePair<TKey, TValue> taken,
        int maxProbes = 8)
    where TKey : notnull
    {
        taken = default;

        if (dict.IsEmpty)
            return false;

        // Pick a random starting point within our probe range
        int skipCount = FastRandom.NextInt(maxProbes);
        int currentIndex = 0;
        int attemptsLeft = maxProbes;

        foreach (var kvp in dict)
        {
            // Skip to our random starting position
            if (currentIndex++ < skipCount)
                continue;

            // Attempt removal - no probabilistic failure
            if (dict.TryRemove(kvp.Key, out var value))
            {
                taken = new(kvp.Key, value);
                return true;
            }

            // If removal failed, try next items but limit total attempts
            if (--attemptsLeft <= 0)
                break;
        }

        // If we haven't found anything yet and we skipped items, try from the beginning
        if (skipCount > 0 && attemptsLeft > 0)
        {
            currentIndex = 0;
            foreach (var kvp in dict)
            {
                if (currentIndex++ >= skipCount)
                    break; // Already tried these

                if (dict.TryRemove(kvp.Key, out var value))
                {
                    taken = new(kvp.Key, value);
                    return true;
                }

                if (--attemptsLeft <= 0)
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to remove a truly random item using snapshot approach.
    /// More reliable but allocates memory. Retries multiple times under contention.
    /// Optimized: Added retry loop to handle concurrent modifications,
    /// added AggressiveInlining for better performance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        // Try multiple random indices to handle race conditions
        int maxAttempts = Math.Min(3, snapshot.Length);
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int index = FastRandom.NextInt(snapshot.Length);
            var candidate = snapshot[index];

            if (dict.TryRemove(candidate.Key, out TValue? value))
            {
                taken = new KeyValuePair<TKey, TValue>(candidate.Key, value);
                return true;
            }
        }

        return false;
    }

    // ============================================================================
    // DEPRECATED IMPLEMENTATIONS (for comparison/testing only)
    // ============================================================================
    
    /// <summary>
    /// DEPRECATED: Original implementation with probabilistic failure bug.
    /// Use TryTakeRandomFast instead.
    /// </summary>
    [Obsolete("Use TryTakeRandomFast - this version has a probabilistic failure bug due to coin flip")]
    public static bool TryTakeRandomFastDeprecated<TKey, TValue>(
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

    /// <summary>
    /// DEPRECATED: Original implementation with single retry.
    /// Use TryTakeRandom instead.
    /// </summary>
    [Obsolete("Use TryTakeRandom - this version has lower success rate under contention")]
    public static bool TryTakeRandomDeprecated<TKey, TValue>(
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

    // ============================================================================
    // OPTIMIZED IMPLEMENTATIONS (for testing/comparison)
    // ============================================================================
    
    /// <summary>
    /// TESTING ONLY: Alias for current optimized implementation.
    /// Use TryTakeRandomFast for production code.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryTakeRandomFastOptimized<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dict,
        out KeyValuePair<TKey, TValue> taken,
        int maxProbes = 8)
    where TKey : notnull
        => TryTakeRandomFast(dict, out taken, maxProbes);

    /// <summary>
    /// TESTING ONLY: Alias for current optimized implementation.
    /// Use TryTakeRandom for production code.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryTakeRandomOptimized<TKey, TValue>(
        this ConcurrentDictionary<TKey, TValue> dict,
        out KeyValuePair<TKey, TValue> taken)
        where TKey : notnull
        => TryTakeRandom(dict, out taken);
}
