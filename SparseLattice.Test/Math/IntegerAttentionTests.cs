using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Test.Math;

[TestClass]
public sealed class IntegerAttentionTests
{
    private const int FracBits = IntegerTranscendentals.DefaultFracBits;

    [TestMethod]
    public void Unit_FixedExp_Zero_ReturnsOne()
    {
        long result = IntegerTranscendentals.FixedExp(0, FracBits);
        long one = 1L << FracBits;
        double relError = System.Math.Abs(result - one) / (double)one;
        Assert.IsTrue(relError < 1e-6, $"exp(0) should be 1.0, got {IntegerTranscendentals.FixedToDouble(result)}");
    }

    [TestMethod]
    public void Unit_FixedExp_One_MatchesE()
    {
        long one = 1L << FracBits;
        long result = IntegerTranscendentals.FixedExp(one, FracBits);
        double actual = IntegerTranscendentals.FixedToDouble(result, FracBits);
        double expected = System.Math.E;
        double relError = System.Math.Abs(actual - expected) / expected;
        Console.WriteLine($"[E4-3] exp(1.0) = {actual:F8} (expected {expected:F8}, relErr={relError:E3})");
        Assert.IsTrue(relError < 1e-4, $"exp(1.0) relative error {relError:E3} exceeds 1e-4");
    }

    [TestMethod]
    public void Unit_FixedExp_NegativeValues_MatchFloat()
    {
        double[] testValues = [-0.5, -1.0, -2.0, -5.0, -10.0];

        foreach (double val in testValues)
        {
            long input = IntegerTranscendentals.FixedFromDouble(val, FracBits);
            long result = IntegerTranscendentals.FixedExp(input, FracBits);
            double actual = IntegerTranscendentals.FixedToDouble(result, FracBits);
            double expected = System.Math.Exp(val);
            double relError = expected > 1e-10 ? System.Math.Abs(actual - expected) / expected : System.Math.Abs(actual - expected);
            Assert.IsTrue(relError < 0.01,
                $"exp({val}) = {actual:G6} (expected {expected:G6}, relErr={relError:E3})");
        }
    }

    [TestMethod]
    public void Unit_FixedExp_LargeNegative_ReturnsZero()
    {
        long input = IntegerTranscendentals.FixedFromDouble(-50.0, FracBits);
        long result = IntegerTranscendentals.FixedExp(input, FracBits);
        Assert.AreEqual(0L, result);
    }

    [TestMethod]
    public void Unit_FixedSigmoid_Zero_ReturnsHalf()
    {
        long result = IntegerTranscendentals.FixedSigmoid(0, FracBits);
        double actual = IntegerTranscendentals.FixedToDouble(result, FracBits);
        Assert.IsTrue(System.Math.Abs(actual - 0.5) < 0.01,
            $"sigmoid(0) should be 0.5, got {actual:F6}");
    }

    [TestMethod]
    public void Unit_FixedSigmoid_LargePositive_ReturnsOne()
    {
        long input = IntegerTranscendentals.FixedFromDouble(10.0, FracBits);
        long result = IntegerTranscendentals.FixedSigmoid(input, FracBits);
        double actual = IntegerTranscendentals.FixedToDouble(result, FracBits);
        Assert.IsTrue(actual > 0.999, $"sigmoid(10) should be ≈1.0, got {actual:F6}");
    }

    [TestMethod]
    public void Unit_FixedSigmoid_LargeNegative_ReturnsZero()
    {
        long input = IntegerTranscendentals.FixedFromDouble(-10.0, FracBits);
        long result = IntegerTranscendentals.FixedSigmoid(input, FracBits);
        double actual = IntegerTranscendentals.FixedToDouble(result, FracBits);
        Assert.IsTrue(actual < 0.001, $"sigmoid(-10) should be ≈0.0, got {actual:F6}");
    }

    [TestMethod]
    public void Unit_FixedSiLU_MatchesFloat()
    {
        double[] testValues = [-2.0, -1.0, -0.5, 0.0, 0.5, 1.0, 2.0];
        foreach (double val in testValues)
        {
            long input = IntegerTranscendentals.FixedFromDouble(val, FracBits);
            long result = IntegerTranscendentals.FixedSiLU(input, FracBits);
            double actual = IntegerTranscendentals.FixedToDouble(result, FracBits);
            double expected = val / (1.0 + System.Math.Exp(-val));
            double absError = System.Math.Abs(actual - expected);
            Assert.IsTrue(absError < 0.02,
                $"SiLU({val}) = {actual:F6} (expected {expected:F6}, absErr={absError:E3})");
        }
    }

    [TestMethod]
    public void Unit_FixedSoftmax_UniformScores_UniformProbabilities()
    {
        long[] scores = [1L << FracBits, 1L << FracBits, 1L << FracBits, 1L << FracBits];

        IntegerTranscendentals.FixedSoftmax(scores, 0, 4, FracBits);

        for (int i = 0; i < 4; i++)
        {
            double prob = IntegerTranscendentals.FixedToDouble(scores[i], FracBits);
            Assert.IsTrue(System.Math.Abs(prob - 0.25) < 0.01,
                $"scores[{i}] = {prob:F4}, expected ≈0.25");
        }
    }

    [TestMethod]
    public void Unit_FixedSoftmax_SumsToOne()
    {
        long[] scores = [
            IntegerTranscendentals.FixedFromDouble(2.0),
            IntegerTranscendentals.FixedFromDouble(1.0),
            IntegerTranscendentals.FixedFromDouble(-1.0),
            IntegerTranscendentals.FixedFromDouble(0.5)
        ];

        IntegerTranscendentals.FixedSoftmax(scores, 0, 4, FracBits);

        long sum = 0;
        for (int i = 0; i < 4; i++) sum += scores[i];
        double total = IntegerTranscendentals.FixedToDouble(sum, FracBits);
        Assert.IsTrue(System.Math.Abs(total - 1.0) < 0.01,
            $"Softmax probabilities should sum to 1.0, got {total:F6}");
    }

    [TestMethod]
    public void Unit_FixedSoftmax_MatchesFloat()
    {
        float[] floatScores = [2.0f, 1.0f, -1.0f, 0.5f];

        float max = floatScores.Max();
        float[] expScores = floatScores.Select(s => MathF.Exp(s - max)).ToArray();
        float expSum = expScores.Sum();
        float[] expected = expScores.Select(e => e / expSum).ToArray();

        long[] intScores = floatScores.Select(s => IntegerTranscendentals.FixedFromDouble(s)).ToArray();
        IntegerTranscendentals.FixedSoftmax(intScores, 0, 4, FracBits);

        double maxRelError = 0;
        for (int i = 0; i < 4; i++)
        {
            double actual = IntegerTranscendentals.FixedToDouble(intScores[i], FracBits);
            double relErr = System.Math.Abs(actual - expected[i]) / expected[i];
            if (relErr > maxRelError) maxRelError = relErr;
        }

        Console.WriteLine($"[E4-3] Softmax fidelity: max relative error = {maxRelError:E4}");
        Assert.IsTrue(maxRelError < 0.02,
            $"Softmax max relative error {maxRelError:E4} exceeds 2%.");
    }

    [TestMethod]
    public void Unit_RoPE_PreservesNorm()
    {
        int embd = 64;
        int nHeads = 2;
        int headDim = embd / nHeads;
        int seqLen = 1;

        IntegerAttention.IntegerRoPECache cache = new(16, headDim, 10000f, FracBits);

        Random rng = new(42);
        long[] x = new long[seqLen * embd];
        for (int i = 0; i < x.Length; i++)
            x[i] = (long)(rng.NextDouble() * 200 - 100) * (1L << 20);

        System.Numerics.BigInteger normBefore = 0;
        for (int i = 0; i < embd; i++)
            normBefore += (System.Numerics.BigInteger)x[i] * x[i];

        IntegerAttention.ApplyRoPE(x, seqLen, embd, nHeads, cache);

        System.Numerics.BigInteger normAfter = 0;
        for (int i = 0; i < embd; i++)
            normAfter += (System.Numerics.BigInteger)x[i] * x[i];

        double before = (double)normBefore;
        double after = (double)normAfter;
        double relChange = System.Math.Abs(after - before) / before;

        Console.WriteLine($"[E4-3] RoPE norm: before={before:E4}, after={after:E4}, relChange={relChange:E4}");
        Assert.IsTrue(relChange < 0.01,
            $"RoPE changed norm by {relChange:P2}.");
    }

    [TestMethod]
    public void Unit_MultiHeadAttention_SmallMatrix_DoesNotCrash()
    {
        int seqLen = 3;
        int embd = 8;
        int nHeads = 2;
        int scaleBits = 20;

        Random rng = new(42);
        long[] q = RandomLongs(rng, seqLen * embd, scaleBits);
        long[] k = RandomLongs(rng, seqLen * embd, scaleBits);
        long[] v = RandomLongs(rng, seqLen * embd, scaleBits);

        long[] output = IntegerAttention.MultiHeadAttention(q, k, v, seqLen, embd, nHeads, -scaleBits);

        Assert.AreEqual(seqLen * embd, output.Length);
        Assert.IsTrue(output.Any(val => val != 0));
    }

    [TestMethod]
    public void Unit_MultiHeadAttention_Deterministic()
    {
        int seqLen = 3;
        int embd = 8;
        int nHeads = 2;
        int scaleBits = 20;

        Random rng = new(42);
        long[] q = RandomLongs(rng, seqLen * embd, scaleBits);
        long[] k = RandomLongs(rng, seqLen * embd, scaleBits);
        long[] v = RandomLongs(rng, seqLen * embd, scaleBits);

        long[] ref1 = IntegerAttention.MultiHeadAttention(q, k, v, seqLen, embd, nHeads, -scaleBits);
        long[] ref2 = IntegerAttention.MultiHeadAttention(q, k, v, seqLen, embd, nHeads, -scaleBits);

        CollectionAssert.AreEqual(ref1, ref2);
    }

    [TestMethod]
    public void Unit_GroupedQueryAttention_SmallMatrix_DoesNotCrash()
    {
        int seqLen = 3;
        int qEmbd = 8;
        int nHeads = 4;
        int nKvHeads = 2;
        int headDim = qEmbd / nHeads;
        int kvDim = nKvHeads * headDim;
        int scaleBits = 20;

        Random rng = new(42);
        long[] q = RandomLongs(rng, seqLen * qEmbd, scaleBits);
        long[] k = RandomLongs(rng, seqLen * kvDim, scaleBits);
        long[] v = RandomLongs(rng, seqLen * kvDim, scaleBits);

        long[] output = IntegerAttention.GroupedQueryAttention(
            q, k, v, seqLen, qEmbd, kvDim, nHeads, nKvHeads, -scaleBits);

        Assert.AreEqual(seqLen * qEmbd, output.Length);
        Assert.IsTrue(output.Any(val => val != 0));
    }

    [TestMethod]
    public void Unit_GroupedQueryAttention_Deterministic()
    {
        int seqLen = 3;
        int qEmbd = 8;
        int nHeads = 4;
        int nKvHeads = 2;
        int kvDim = nKvHeads * (qEmbd / nHeads);
        int scaleBits = 20;

        Random rng = new(42);
        long[] q = RandomLongs(rng, seqLen * qEmbd, scaleBits);
        long[] k = RandomLongs(rng, seqLen * kvDim, scaleBits);
        long[] v = RandomLongs(rng, seqLen * kvDim, scaleBits);

        long[] ref1 = IntegerAttention.GroupedQueryAttention(
            q, k, v, seqLen, qEmbd, kvDim, nHeads, nKvHeads, -scaleBits);
        long[] ref2 = IntegerAttention.GroupedQueryAttention(
            q, k, v, seqLen, qEmbd, kvDim, nHeads, nKvHeads, -scaleBits);

        CollectionAssert.AreEqual(ref1, ref2);
    }

    [TestMethod]
    public void Integration_Softmax_768Dim_Distribution()
    {
        const int seqLen = 12;
        Random rng = new(42);

        float[] floatScores = new float[seqLen];
        for (int i = 0; i < seqLen; i++)
            floatScores[i] = (float)(rng.NextDouble() * 6.0 - 3.0);

        float max = floatScores.Max();
        float[] floatProbs = floatScores.Select(s => MathF.Exp(s - max)).ToArray();
        float probSum = floatProbs.Sum();
        for (int i = 0; i < seqLen; i++) floatProbs[i] /= probSum;

        long[] intScores = floatScores.Select(s => IntegerTranscendentals.FixedFromDouble(s)).ToArray();
        IntegerTranscendentals.FixedSoftmax(intScores, 0, seqLen, FracBits);

        double maxRelError = 0;
        for (int i = 0; i < seqLen; i++)
        {
            double actual = IntegerTranscendentals.FixedToDouble(intScores[i], FracBits);
            if (floatProbs[i] > 1e-6)
            {
                double rel = System.Math.Abs(actual - floatProbs[i]) / floatProbs[i];
                if (rel > maxRelError) maxRelError = rel;
            }
        }

        Console.WriteLine($"[E4-3] Softmax (n=12) max relative error: {maxRelError:E4}");
        Assert.IsTrue(maxRelError < 0.05,
            $"Softmax max relative error {maxRelError:E4} exceeds 5%.");
    }

    private static long[] RandomLongs(Random rng, int count, int scaleBits)
    {
        long[] result = new long[count];
        long maxVal = 1L << (scaleBits - 2);
        for (int i = 0; i < count; i++)
            result[i] = (long)((rng.NextDouble() * 2 - 1) * maxVal);
        return result;
    }

    [TestMethod]
    public void Unit_SlidingWindowCausalGQA_MasksOutsideWindow()
    {
        // With a sliding window of 2, position t can only see positions [t-1, t].
        // Position 0 sees only itself. Position 2 sees [1, 2]. Position 3 sees [2, 3].
        // The output at position 3 should differ from full causal (which sees all 0..3).
        int seqLen = 4;
        int qEmbd = 8;
        int nHeads = 2;
        int nKvHeads = 2;
        int headDim = qEmbd / nHeads;
        int kvDim = nKvHeads * headDim;
        int scaleBits = 20;
        int windowSize = 2;

        Random rng = new(42);
        long[] q = RandomLongs(rng, seqLen * qEmbd, scaleBits);
        long[] k = RandomLongs(rng, seqLen * kvDim, scaleBits);
        long[] v = RandomLongs(rng, seqLen * kvDim, scaleBits);

        long[] fullCausal = IntegerAttention.CausalGroupedQueryAttention(
            q, k, v, seqLen, qEmbd, kvDim, nHeads, nKvHeads, -scaleBits);
        long[] slidingWindow = IntegerAttention.SlidingWindowCausalGQA(
            q, k, v, seqLen, qEmbd, kvDim, nHeads, nKvHeads, -scaleBits, windowSize);

        Assert.AreEqual(fullCausal.Length, slidingWindow.Length);

        // Position 0: both should be identical (window=2 covers positions [-1, 0], so just 0)
        for (int d = 0; d < qEmbd; d++)
            Assert.AreEqual(fullCausal[0 * qEmbd + d], slidingWindow[0 * qEmbd + d],
                $"Position 0 dim {d}: sliding should match full causal (only self-attention).");

        // Position 1: both should be identical (window=2 covers [0, 1], same as full causal for t=1)
        for (int d = 0; d < qEmbd; d++)
            Assert.AreEqual(fullCausal[1 * qEmbd + d], slidingWindow[1 * qEmbd + d],
                $"Position 1 dim {d}: sliding should match full causal (window covers [0,1]).");

        // Position 3: full causal sees [0,1,2,3], sliding sees [2,3].
        // These should differ.
        bool anyDiff = false;
        for (int d = 0; d < qEmbd; d++)
        {
            if (fullCausal[3 * qEmbd + d] != slidingWindow[3 * qEmbd + d])
            {
                anyDiff = true;
                break;
            }
        }
        Assert.IsTrue(anyDiff,
            "Position 3: sliding window output should differ from full causal " +
            "(full sees [0,1,2,3], sliding sees [2,3] only).");
    }

    [TestMethod]
    public void Unit_SlidingWindowCausalGQA_LargeWindow_MatchesFullCausal()
    {
        // When window >= seqLen, sliding window should produce identical results to full causal.
        int seqLen = 4;
        int qEmbd = 8;
        int nHeads = 2;
        int nKvHeads = 2;
        int headDim = qEmbd / nHeads;
        int kvDim = nKvHeads * headDim;
        int scaleBits = 20;

        Random rng = new(42);
        long[] q = RandomLongs(rng, seqLen * qEmbd, scaleBits);
        long[] k = RandomLongs(rng, seqLen * kvDim, scaleBits);
        long[] v = RandomLongs(rng, seqLen * kvDim, scaleBits);

        long[] fullCausal = IntegerAttention.CausalGroupedQueryAttention(
            q, k, v, seqLen, qEmbd, kvDim, nHeads, nKvHeads, -scaleBits);
        long[] slidingLargeWindow = IntegerAttention.SlidingWindowCausalGQA(
            q, k, v, seqLen, qEmbd, kvDim, nHeads, nKvHeads, -scaleBits, seqLen + 100);

        CollectionAssert.AreEqual(fullCausal, slidingLargeWindow,
            "Sliding window with window >= seqLen should be identical to full causal.");
    }
}
