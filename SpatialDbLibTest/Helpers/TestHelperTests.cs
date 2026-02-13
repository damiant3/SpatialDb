using System.Collections.Concurrent;
using System.Diagnostics;

namespace SpatialDbLibTest.Helpers;

[TestClass]
public class TestHelperTests
{
    private const int WARMUP_ITERATIONS = 1000;
    private const int TEST_ITERATIONS = 10000;
    private const int DICT_SIZE = 1000;
    private const int CONCURRENT_THREADS = 8;

   // [TestMethod]
    public void TestConcurrentDictionaryExtensions_Performance()
    {
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("CONCURRENTDICTIONARY EXTENSIONS PERFORMANCE TEST");
        Console.WriteLine("=".PadRight(80, '='));

        // Test 1: TryTakeRandomFast - Single Threaded Performance
        Console.WriteLine("\n--- Test 1: TryTakeRandomFast - Single Thread Speed ---");
        var fastResult = BenchmarkTryTakeRandomFast();
        Console.WriteLine($"CURRENT - Avg time per operation: {fastResult.AvgTimeNs:F2} ns");
        Console.WriteLine($"CURRENT - Success rate: {fastResult.SuccessRate:P2}");
        Console.WriteLine($"CURRENT - Operations per second: {fastResult.OpsPerSecond:N0}");
        
        var fastOptResult = BenchmarkTryTakeRandomFastOptimized();
        Console.WriteLine($"OPTIMIZED - Avg time per operation: {fastOptResult.AvgTimeNs:F2} ns");
        Console.WriteLine($"OPTIMIZED - Success rate: {fastOptResult.SuccessRate:P2}");
        Console.WriteLine($"OPTIMIZED - Operations per second: {fastOptResult.OpsPerSecond:N0}");
        Console.WriteLine($"⚡ SPEEDUP: {fastResult.AvgTimeNs / fastOptResult.AvgTimeNs:F2}x faster, {fastOptResult.SuccessRate / fastResult.SuccessRate:F2}x more reliable");

        // Test 2: TryTakeRandom - Single Threaded Performance
        Console.WriteLine("\n--- Test 2: TryTakeRandom - Single Thread Speed ---");
        var accurateResult = BenchmarkTryTakeRandom();
        Console.WriteLine($"CURRENT - Avg time per operation: {accurateResult.AvgTimeNs:F2} ns");
        Console.WriteLine($"CURRENT - Success rate: {accurateResult.SuccessRate:P2}");
        Console.WriteLine($"CURRENT - Operations per second: {accurateResult.OpsPerSecond:N0}");
        
        var accurateOptResult = BenchmarkTryTakeRandomOptimized();
        Console.WriteLine($"OPTIMIZED - Avg time per operation: {accurateOptResult.AvgTimeNs:F2} ns");
        Console.WriteLine($"OPTIMIZED - Success rate: {accurateOptResult.SuccessRate:P2}");
        Console.WriteLine($"OPTIMIZED - Operations per second: {accurateOptResult.OpsPerSecond:N0}");
        Console.WriteLine($"⚡ SPEEDUP: {accurateResult.AvgTimeNs / accurateOptResult.AvgTimeNs:F2}x faster, {accurateOptResult.SuccessRate / accurateResult.SuccessRate:F2}x more reliable");

        // Test 3: Multi-threaded Contention Test
        Console.WriteLine("\n--- Test 3: Multi-threaded Contention (8 threads) ---");
        var contentionFast = BenchmarkTryTakeRandomFastContention();
        Console.WriteLine($"CURRENT TryTakeRandomFast:");
        Console.WriteLine($"  Success rate under contention: {contentionFast.SuccessRate:P2}");
        Console.WriteLine($"  Total time: {contentionFast.TotalTimeMs:F2} ms");
        Console.WriteLine($"  Throughput: {contentionFast.OpsPerSecond:N0} ops/sec");

        var contentionFastOpt = BenchmarkTryTakeRandomFastOptimizedContention();
        Console.WriteLine($"OPTIMIZED TryTakeRandomFast:");
        Console.WriteLine($"  Success rate under contention: {contentionFastOpt.SuccessRate:P2}");
        Console.WriteLine($"  Total time: {contentionFastOpt.TotalTimeMs:F2} ms");
        Console.WriteLine($"  Throughput: {contentionFastOpt.OpsPerSecond:N0} ops/sec");
        Console.WriteLine($"  ⚡ IMPROVEMENT: {contentionFastOpt.OpsPerSecond / contentionFast.OpsPerSecond:F2}x throughput, {contentionFastOpt.SuccessRate / contentionFast.SuccessRate:F2}x reliability");

        var contentionAccurate = BenchmarkTryTakeRandomContention();
        Console.WriteLine($"CURRENT TryTakeRandom:");
        Console.WriteLine($"  Success rate under contention: {contentionAccurate.SuccessRate:P2}");
        Console.WriteLine($"  Total time: {contentionAccurate.TotalTimeMs:F2} ms");
        Console.WriteLine($"  Throughput: {contentionAccurate.OpsPerSecond:N0} ops/sec");

        var contentionAccurateOpt = BenchmarkTryTakeRandomOptimizedContention();
        Console.WriteLine($"OPTIMIZED TryTakeRandom:");
        Console.WriteLine($"  Success rate under contention: {contentionAccurateOpt.SuccessRate:P2}");
        Console.WriteLine($"  Total time: {contentionAccurateOpt.TotalTimeMs:F2} ms");
        Console.WriteLine($"  Throughput: {contentionAccurateOpt.OpsPerSecond:N0} ops/sec");
        Console.WriteLine($"  ⚡ IMPROVEMENT: {contentionAccurateOpt.OpsPerSecond / contentionAccurate.OpsPerSecond:F2}x throughput, {contentionAccurateOpt.SuccessRate / contentionAccurate.SuccessRate:F2}x reliability");

        // Test 4: Empty Dictionary Edge Case
        Console.WriteLine("\n--- Test 4: Edge Cases ---");
        TestEdgeCases();

        Console.WriteLine("\n" + "=".PadRight(80, '='));
        Console.WriteLine("SUMMARY - KEY IMPROVEMENTS");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine($"✓ TryTakeRandomFast: {fastResult.AvgTimeNs / fastOptResult.AvgTimeNs:F2}x faster, {(fastOptResult.SuccessRate - fastResult.SuccessRate) * 100:F2}% better success rate");
        Console.WriteLine($"✓ TryTakeRandom: {accurateResult.AvgTimeNs / accurateOptResult.AvgTimeNs:F2}x faster, {(accurateOptResult.SuccessRate - accurateResult.SuccessRate) * 100:F2}% better success rate");
        Console.WriteLine($"✓ Contention Fast: {contentionFastOpt.OpsPerSecond / contentionFast.OpsPerSecond:F2}x better throughput");
        Console.WriteLine($"✓ Contention Accurate: {contentionAccurateOpt.OpsPerSecond / contentionAccurate.OpsPerSecond:F2}x better throughput");
    }

    [TestMethod]
    public void ConcurrentDictionaryExtensions_Correctness()
    {
        Console.WriteLine("\n--- Correctness Tests ---");

        // Test that optimized methods actually remove items
        var dict = CreatePopulatedDictionary(100);
        int initialCount = dict.Count;
        
        bool success = dict.TryTakeRandomFast(out var taken);
        Assert.IsTrue(success, "TryTakeRandomFast should succeed on non-empty dictionary");
        Assert.AreEqual(initialCount - 1, dict.Count, "Item should be removed");
        Assert.IsFalse(dict.ContainsKey(taken.Key), "Removed key should not exist");

        success = dict.TryTakeRandom(out taken);
        Assert.IsTrue(success, "TryTakeRandom should succeed on non-empty dictionary");
        Assert.AreEqual(initialCount - 2, dict.Count, "Item should be removed");
        Assert.IsFalse(dict.ContainsKey(taken.Key), "Removed key should not exist");

        // Test empty dictionary
        dict.Clear();
        success = dict.TryTakeRandomFast(out _);
        Assert.IsFalse(success, "Should fail on empty dictionary");

        success = dict.TryTakeRandom(out _);
        Assert.IsFalse(success, "Should fail on empty dictionary");

        // Test deprecated methods for comparison
        dict = CreatePopulatedDictionary(100);
#pragma warning disable CS0618 // Type or member is obsolete
        success = dict.TryTakeRandomFastDeprecated(out taken);
#pragma warning restore CS0618
        // Deprecated version may fail randomly, so just log the result
        Console.WriteLine($"  Deprecated TryTakeRandomFast success: {success}");

#pragma warning disable CS0618 // Type or member is obsolete
        success = dict.TryTakeRandomDeprecated(out taken);
#pragma warning restore CS0618
        Console.WriteLine($"  Deprecated TryTakeRandom success: {success}");

        Console.WriteLine("✓ All correctness tests passed");
    }

    private record BenchmarkResult(
        double AvgTimeNs,
        double SuccessRate,
        double OpsPerSecond,
        double TotalTimeMs);

    private BenchmarkResult BenchmarkTryTakeRandomFast()
    {
        // Warmup
        for (int i = 0; i < WARMUP_ITERATIONS; i++)
        {
            var dict = CreatePopulatedDictionary(DICT_SIZE);
#pragma warning disable CS0618 // Type or member is obsolete
            dict.TryTakeRandomFastDeprecated(out _);
#pragma warning restore CS0618
        }

        // Actual benchmark
        var sw = Stopwatch.StartNew();
        int successes = 0;

        for (int i = 0; i < TEST_ITERATIONS; i++)
        {
            var dict = CreatePopulatedDictionary(DICT_SIZE);
#pragma warning disable CS0618 // Type or member is obsolete
            if (dict.TryTakeRandomFastDeprecated(out _))
#pragma warning restore CS0618
                successes++;
        }

        sw.Stop();
        double avgTimeNs = (sw.Elapsed.TotalNanoseconds / TEST_ITERATIONS);
        double successRate = successes / (double)TEST_ITERATIONS;
        double opsPerSecond = TEST_ITERATIONS / sw.Elapsed.TotalSeconds;

        return new BenchmarkResult(avgTimeNs, successRate, opsPerSecond, sw.Elapsed.TotalMilliseconds);
    }

    private BenchmarkResult BenchmarkTryTakeRandom()
    {
        // Warmup
        for (int i = 0; i < WARMUP_ITERATIONS; i++)
        {
            var dict = CreatePopulatedDictionary(DICT_SIZE);
#pragma warning disable CS0618 // Type or member is obsolete
            dict.TryTakeRandomDeprecated(out _);
#pragma warning restore CS0618
        }

        // Actual benchmark
        var sw = Stopwatch.StartNew();
        int successes = 0;

        for (int i = 0; i < TEST_ITERATIONS; i++)
        {
            var dict = CreatePopulatedDictionary(DICT_SIZE);
#pragma warning disable CS0618 // Type or member is obsolete
            if (dict.TryTakeRandomDeprecated(out _))
#pragma warning restore CS0618
                successes++;
        }

        sw.Stop();
        double avgTimeNs = (sw.Elapsed.TotalNanoseconds / TEST_ITERATIONS);
        double successRate = successes / (double)TEST_ITERATIONS;
        double opsPerSecond = TEST_ITERATIONS / sw.Elapsed.TotalSeconds;

        return new BenchmarkResult(avgTimeNs, successRate, opsPerSecond, sw.Elapsed.TotalMilliseconds);
    }

    private BenchmarkResult BenchmarkTryTakeRandomFastOptimized()
    {
        // Warmup
        for (int i = 0; i < WARMUP_ITERATIONS; i++)
        {
            var dict = CreatePopulatedDictionary(DICT_SIZE);
            dict.TryTakeRandomFast(out _);
        }

        // Actual benchmark
        var sw = Stopwatch.StartNew();
        int successes = 0;

        for (int i = 0; i < TEST_ITERATIONS; i++)
        {
            var dict = CreatePopulatedDictionary(DICT_SIZE);
            if (dict.TryTakeRandomFast(out _))
                successes++;
        }

        sw.Stop();
        double avgTimeNs = (sw.Elapsed.TotalNanoseconds / TEST_ITERATIONS);
        double successRate = successes / (double)TEST_ITERATIONS;
        double opsPerSecond = TEST_ITERATIONS / sw.Elapsed.TotalSeconds;

        return new BenchmarkResult(avgTimeNs, successRate, opsPerSecond, sw.Elapsed.TotalMilliseconds);
    }

    private BenchmarkResult BenchmarkTryTakeRandomOptimized()
    {
        // Warmup
        for (int i = 0; i < WARMUP_ITERATIONS; i++)
        {
            var dict = CreatePopulatedDictionary(DICT_SIZE);
            dict.TryTakeRandom(out _);
        }

        // Actual benchmark
        var sw = Stopwatch.StartNew();
        int successes = 0;

        for (int i = 0; i < TEST_ITERATIONS; i++)
        {
            var dict = CreatePopulatedDictionary(DICT_SIZE);
            if (dict.TryTakeRandom(out _))
                successes++;
        }

        sw.Stop();
        double avgTimeNs = (sw.Elapsed.TotalNanoseconds / TEST_ITERATIONS);
        double successRate = successes / (double)TEST_ITERATIONS;
        double opsPerSecond = TEST_ITERATIONS / sw.Elapsed.TotalSeconds;

        return new BenchmarkResult(avgTimeNs, successRate, opsPerSecond, sw.Elapsed.TotalMilliseconds);
    }

    private BenchmarkResult BenchmarkTryTakeRandomFastContention()
    {
        var dict = CreatePopulatedDictionary(TEST_ITERATIONS);
        int successes = 0;
        int failures = 0;

        var sw = Stopwatch.StartNew();
        var tasks = new List<Task>();

        for (int i = 0; i < CONCURRENT_THREADS; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                while (dict.Count > 0)
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    if (dict.TryTakeRandomFastDeprecated(out _))
#pragma warning restore CS0618
                        Interlocked.Increment(ref successes);
                    else
                        Interlocked.Increment(ref failures);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());
        sw.Stop();

        int totalAttempts = successes + failures;
        double successRate = totalAttempts > 0 ? successes / (double)totalAttempts : 0;
        double opsPerSecond = successes / sw.Elapsed.TotalSeconds;

        return new BenchmarkResult(0, successRate, opsPerSecond, sw.Elapsed.TotalMilliseconds);
    }

    private BenchmarkResult BenchmarkTryTakeRandomContention()
    {
        var dict = CreatePopulatedDictionary(TEST_ITERATIONS);
        int successes = 0;
        int failures = 0;

        var sw = Stopwatch.StartNew();
        var tasks = new List<Task>();

        for (int i = 0; i < CONCURRENT_THREADS; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                while (dict.Count > 0)
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    if (dict.TryTakeRandomDeprecated(out _))
#pragma warning restore CS0618
                        Interlocked.Increment(ref successes);
                    else
                        Interlocked.Increment(ref failures);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());
        sw.Stop();

        int totalAttempts = successes + failures;
        double successRate = totalAttempts > 0 ? successes / (double)totalAttempts : 0;
        double opsPerSecond = successes / sw.Elapsed.TotalSeconds;

        return new BenchmarkResult(0, successRate, opsPerSecond, sw.Elapsed.TotalMilliseconds);
    }

    private BenchmarkResult BenchmarkTryTakeRandomFastOptimizedContention()
    {
        var dict = CreatePopulatedDictionary(TEST_ITERATIONS);
        int successes = 0;
        int failures = 0;

        var sw = Stopwatch.StartNew();
        var tasks = new List<Task>();

        for (int i = 0; i < CONCURRENT_THREADS; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                while (dict.Count > 0)
                {
                    if (dict.TryTakeRandomFast(out _))
                        Interlocked.Increment(ref successes);
                    else
                        Interlocked.Increment(ref failures);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());
        sw.Stop();

        int totalAttempts = successes + failures;
        double successRate = totalAttempts > 0 ? successes / (double)totalAttempts : 0;
        double opsPerSecond = successes / sw.Elapsed.TotalSeconds;

        return new BenchmarkResult(0, successRate, opsPerSecond, sw.Elapsed.TotalMilliseconds);
    }

    private BenchmarkResult BenchmarkTryTakeRandomOptimizedContention()
    {
        var dict = CreatePopulatedDictionary(TEST_ITERATIONS);
        int successes = 0;
        int failures = 0;

        var sw = Stopwatch.StartNew();
        var tasks = new List<Task>();

        for (int i = 0; i < CONCURRENT_THREADS; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                while (dict.Count > 0)
                {
                    if (dict.TryTakeRandom(out _))
                        Interlocked.Increment(ref successes);
                    else
                        Interlocked.Increment(ref failures);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());
        sw.Stop();

        int totalAttempts = successes + failures;
        double successRate = totalAttempts > 0 ? successes / (double)totalAttempts : 0;
        double opsPerSecond = successes / sw.Elapsed.TotalSeconds;

        return new BenchmarkResult(0, successRate, opsPerSecond, sw.Elapsed.TotalMilliseconds);
    }

    private void TestEdgeCases()
    {
        // Empty dictionary
        var emptyDict = new ConcurrentDictionary<int, string>();
        Assert.IsFalse(emptyDict.TryTakeRandomFast(out _), "Fast: Should fail on empty");
        Assert.IsFalse(emptyDict.TryTakeRandom(out _), "Accurate: Should fail on empty");

        // Single item test 1 - Optimized version should succeed reliably
        var singleDict1 = new ConcurrentDictionary<int, string>();
        singleDict1.TryAdd(1, "value");
        
        Assert.IsTrue(singleDict1.TryTakeRandomFast(out var taken1), "Fast: Should succeed with single item");
        Assert.AreEqual(1, taken1.Key);
        Assert.AreEqual(0, singleDict1.Count);

        // Single item test 2
        var singleDict2 = new ConcurrentDictionary<int, string>();
        singleDict2.TryAdd(2, "value2");
        Assert.IsTrue(singleDict2.TryTakeRandom(out var taken2), "Accurate: Should succeed with single item");
        Assert.AreEqual(2, taken2.Key);
        Assert.AreEqual(0, singleDict2.Count);

        // Large dictionary with maxProbes
        var largeDict = CreatePopulatedDictionary(10000);
        Assert.IsTrue(largeDict.TryTakeRandomFast(out _, maxProbes: 4), "Should successfully remove from large dictionary");

        Console.WriteLine("✓ All edge cases passed");
    }

    private ConcurrentDictionary<int, string> CreatePopulatedDictionary(int size)
    {
        var dict = new ConcurrentDictionary<int, string>();
        for (int i = 0; i < size; i++)
        {
            dict.TryAdd(i, $"value_{i}");
        }
        return dict;
    }
}
