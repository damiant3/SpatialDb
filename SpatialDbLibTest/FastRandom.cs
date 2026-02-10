using System.Runtime.CompilerServices;

namespace SpatialDbLibTest
{
    internal static class FastRandom
    {
        private static readonly ThreadLocal<Random> s_rng = new(() => new Random(Environment.TickCount ^ Environment.CurrentManagedThreadId));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NextInt(int max) => s_rng.Value!.Next(max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NextInt(int min, int max) => s_rng.Value!.Next(min, max);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long NextLong(long min, long max) => s_rng.Value!.NextInt64(min, max);
    }
}
