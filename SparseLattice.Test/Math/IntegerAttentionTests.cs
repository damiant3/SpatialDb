using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Test.Math;

/// <summary>
/// Tests for <see cref="IntegerTranscendentals"/>, <see cref="IntegerAttention"/>,
/// and <see cref="IntegerFFN"/> — E4-3 and E4-4.
/// </summary>
[TestClass]
public sealed class IntegerAttentionTests
{
    private const int s_fracBits = IntegerTranscendentals.DefaultFracBits;

    // -----------------------------------------------------------------------
    // Transcendentals: exp
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_FixedExp_Zero_ReturnsOne()
    {
        long result = IntegerTranscendentals.FixedExp(0, s_fracBits);
        long one = 1L << s_fracBits;
        double relError = System.Math.Abs(result - one) / (double)one;
        Assert.IsTrue(relError < 1e-6, $"exp(0) should be 1.0, got {IntegerTranscendentals.FixedToDouble(result)}");
    }

    [TestMethod]
    public void Unit_FixedExp_One_MatchesE()
    {
        long one = 1L << s_fracBits;
        long result = IntegerTranscendentals.FixedExp(one, s_fracBits);
        double actual = IntegerTranscendentals.FixedToDouble(result, s_fracBits);
        double expected = System.Math.E;
        double relError = System.Math.Abs(actual - expected) / expected;
        Console.WriteLine($"[E4-3] exp(1.0) = {actual:F8} (expected {expected:F8}, relErr={relError:E3})");
        Assert.IsTrue(relError < 1e-4, $"exp(1.0) relative error {relError:E3} exceeds 1e-4");
    }

    [TestMethod]
    public void Unit_FixedExp_NegativeValues_MatchFloat()
    {
        // Softmax inputs are typically negative (shifted by max)
        double[] testValues = [-0.5, -1.0, -2.0, -5.0, -10.0];

        foreach (double val in testValues)
        {
            long input = IntegerTranscendentals.FixedFromDouble(val, s_fracBits);
            long result = IntegerTranscendentals.FixedExp(input, s_fracBits);
            double actual = IntegerTranscendentals.FixedToDouble(result, s_fracBits);
            double expected = System.Math.Exp(val);
            double relError = expected > 1e-10 ? System.Math.Abs(actual - expected) / expected : System.Math.Abs(actual - expected);
            Assert.IsTrue(relError < 0.01,
                $"exp({val}) = {actual:G6} (expected {expected:G6}, relErr={relError:E3})");
        }
    }

    [TestMethod]
    public void Unit_FixedExp_LargeNegative_ReturnsZero()
    {
        long input = IntegerTranscendentals.FixedFromDouble(-50.0, s_fracBits);
        long result = IntegerTranscendentals.FixedExp(input, s_fracBits);
        Assert.AreEqual(0L, result, "exp(-50) should underflow to 0 in fixed-point.");
    }

    // -----------------------------------------------------------------------
    // Transcendentals: sigmoid
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_FixedSigmoid_Zero_ReturnsHalf()
    {
        long result = IntegerTranscendentals.FixedSigmoid(0, s_fracBits);
        double actual = IntegerTranscendentals.FixedToDouble(result, s_fracBits);
        Assert.IsTrue(System.Math.Abs(actual - 0.5) < 0.01,
            $"sigmoid(0) should be 0.5, got {actual:F6}");
    }

    [TestMethod]
    public void Unit_FixedSigmoid_LargePositive_ReturnsOne()
    {
        long input = IntegerTranscendentals.FixedFromDouble(10.0, s_fracBits);
        long result = IntegerTranscendentals.FixedSigmoid(input, s_fracBits);
        double actual = IntegerTranscendentals.FixedToDouble(result, s_fracBits);
        Assert.IsTrue(actual > 0.999, $"sigmoid(10) should be ≈1.0, got {actual:F6}");
    }

    [TestMethod]
    public void Unit_FixedSigmoid_LargeNegative_ReturnsZero()
    {
        long input = IntegerTranscendentals.FixedFromDouble(-10.0, s_fracBits);
        long result = IntegerTranscendentals.FixedSigmoid(input, s_fracBits);
        double actual = IntegerTranscendentals.FixedToDouble(result, s_fracBits);
        Assert.IsTrue(actual < 0.001, $"sigmoid(-10) should be ≈0.0, got {actual:F6}");
    }

    // -----------------------------------------------------------------------
    // Transcendentals: SiLU
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_FixedSiLU_MatchesFloat()
    {
        double[] testValues = [-2.0, -1.0, -0.5, 0.0, 0.5, 1.0, 2.0];
        foreach (double val in testValues)
        {
            long input = IntegerTranscendentals.FixedFromDouble(val, s_fracBits);
            long result = IntegerTranscendentals.FixedSiLU(input, s_fracBits);
            double actual = IntegerTranscendentals.FixedToDouble(result, s_fracBits);
            double expected = val / (1.0 + System.Math.Exp(-val)); // SiLU = x * sigmoid(x)
            double absError = System.Math.Abs(actual - expected);
            Assert.IsTrue(absError < 0.02,
                $"SiLU({val}) = {actual:F6} (expected {expected:F6}, absErr={absError:E3})");
        }
    }

    // -----------------------------------------------------------------------
    // Transcendentals: Softmax
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_FixedSoftmax_UniformScores_UniformProbabilities()
    {
        long[] scores = [1L << s_fracBits, 1L << s_fracBits, 1L << s_fracBits, 1L << s_fracBits]; // all equal

        IntegerTranscendentals.FixedSoftmax(scores, 0, 4, s_fracBits);

        // Each should be ≈ 0.25
        for (int i = 0; i < 4; i++)
        {
            double prob = IntegerTranscendentals.FixedToDouble(scores[i], s_fracBits);
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

        IntegerTranscendentals.FixedSoftmax(scores, 0, 4, s_fracBits);

        long sum = 0;
        for (int i = 0; i < 4; i++) sum += scores[i];
        double total = IntegerTranscendentals.FixedToDouble(sum, s_fracBits);
        Assert.IsTrue(System.Math.Abs(total - 1.0) < 0.01,
            $"Softmax probabilities should sum to 1.0, got {total:F6}");
    }

    [TestMethod]
    public void Unit_FixedSoftmax_MatchesFloat()
    {
        float[] floatScores = [2.0f, 1.0f, -1.0f, 0.5f];

        // Float reference softmax
        float max = floatScores.Max();
        float[] expScores = floatScores.Select(s => MathF.Exp(s - max)).ToArray();
        float expSum = expScores.Sum();
        float[] expected = expScores.Select(e => e / expSum).ToArray();

        // Integer softmax
        long[] intScores = floatScores.Select(s => IntegerTranscendentals.FixedFromDouble(s)).ToArray();
        IntegerTranscendentals.FixedSoftmax(intScores, 0, 4, s_fracBits);

        double maxRelError = 0;
        for (int i = 0; i < 4; i++)
        {
            double actual = IntegerTranscendentals.FixedToDouble(intScores[i], s_fracBits);
            double relErr = System.Math.Abs(actual - expected[i]) / expected[i];
            if (relErr > maxRelError) maxRelError = relErr;
        }

        Console.WriteLine($"[E4-3] Softmax fidelity: max relative error = {maxRelError:E4}");
        Assert.IsTrue(maxRelError < 0.02,
            $"Softmax max relative error {maxRelError:E4} exceeds 2%.");
    }

    // -----------------------------------------------------------------------
    // RoPE
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_RoPE_PreservesNorm()
    {
        // RoPE is a rotation — it should preserve the L2 norm of each head
        int embd = 64;
        int nHeads = 2;
        int headDim = embd / nHeads;
        int seqLen = 1;

        IntegerAttention.IntegerRoPECache cache = new(16, headDim, 10000f, s_fracBits);

        Random rng = new(42);
        long[] x = new long[seqLen * embd];
        for (int i = 0; i < x.Length; i++)
            x[i] = (long)(rng.NextDouble() * 200 - 100) * (1L << 20); // random at scale 20

        // Compute norm before
        System.Numerics.BigInteger normBefore = 0;
        for (int i = 0; i < embd; i++)
            normBefore += (System.Numerics.BigInteger)x[i] * x[i];

        IntegerAttention.ApplyRoPE(x, seqLen, embd, nHeads, cache);

        // Compute norm after
        System.Numerics.BigInteger normAfter = 0;
        for (int i = 0; i < embd; i++)
            normAfter += (System.Numerics.BigInteger)x[i] * x[i];

        // Norm should be approximately preserved (rotation is norm-preserving)
        // Allow some tolerance for integer truncation in the rotation
        double before = (double)normBefore;
        double after = (double)normAfter;
        double relChange = System.Math.Abs(after - before) / before;

        Console.WriteLine($"[E4-3] RoPE norm: before={before:E4}, after={after:E4}, relChange={relChange:E4}");
        Assert.IsTrue(relChange < 0.01,
            $"RoPE changed norm by {relChange:P2} — rotation should be norm-preserving.");
    }

    // -----------------------------------------------------------------------
    // Full attention: small test
    // -----------------------------------------------------------------------

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
        // At least some values should be nonzero (not all-zero degenerate case)
        Assert.IsTrue(output.Any(val => val != 0), "Attention output should not be all zeros.");
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

        CollectionAssert.AreEqual(ref1, ref2, "Attention must be deterministic.");
    }

    // -----------------------------------------------------------------------
    // FFN
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_IntegerFFN_SmallMatrix_DoesNotCrash()
    {
        int seqLen = 2;
        int embd = 8;
        int nFf = 16;
        int scaleBits = 20;

        Random rng = new(42);
        long[] x = RandomLongs(rng, seqLen * embd, scaleBits);
        long[] wGate = RandomLongs(rng, embd * nFf, scaleBits);
        long[] wUp = RandomLongs(rng, embd * nFf, scaleBits);
        long[] wDown = RandomLongs(rng, nFf * embd, scaleBits);

        long[] result = IntegerFFN.Apply(x, seqLen, embd, wGate, wUp, wDown, nFf, scaleBits);

        Assert.AreEqual(seqLen * embd, result.Length);
    }

    [TestMethod]
    public void Unit_IntegerFFN_Deterministic()
    {
        int seqLen = 2;
        int embd = 8;
        int nFf = 16;
        int scaleBits = 20;

        Random rng = new(42);
        long[] x = RandomLongs(rng, seqLen * embd, scaleBits);
        long[] wGate = RandomLongs(rng, embd * nFf, scaleBits);
        long[] wUp = RandomLongs(rng, embd * nFf, scaleBits);
        long[] wDown = RandomLongs(rng, nFf * embd, scaleBits);

        long[] ref1 = IntegerFFN.Apply(x, seqLen, embd, wGate, wUp, wDown, nFf, scaleBits);
        long[] ref2 = IntegerFFN.Apply(x, seqLen, embd, wGate, wUp, wDown, nFf, scaleBits);

        CollectionAssert.AreEqual(ref1, ref2, "FFN must be deterministic.");
    }

    // -----------------------------------------------------------------------
    // Integration: attention fidelity vs float
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Integration_Softmax_768Dim_Distribution()
    {
        // Simulate a realistic attention score distribution for 12 tokens
        const int seqLen = 12;
        Random rng = new(42);

        // Float reference
        float[] floatScores = new float[seqLen];
        for (int i = 0; i < seqLen; i++)
            floatScores[i] = (float)(rng.NextDouble() * 6.0 - 3.0); // scores in [-3, +3]

        float max = floatScores.Max();
        float[] floatProbs = floatScores.Select(s => MathF.Exp(s - max)).ToArray();
        float probSum = floatProbs.Sum();
        for (int i = 0; i < seqLen; i++) floatProbs[i] /= probSum;

        // Integer
        long[] intScores = floatScores.Select(s => IntegerTranscendentals.FixedFromDouble(s)).ToArray();
        IntegerTranscendentals.FixedSoftmax(intScores, 0, seqLen, s_fracBits);

        double maxRelError = 0;
        for (int i = 0; i < seqLen; i++)
        {
            double actual = IntegerTranscendentals.FixedToDouble(intScores[i], s_fracBits);
            if (floatProbs[i] > 1e-6)
            {
                double rel = System.Math.Abs(actual - floatProbs[i]) / floatProbs[i];
                if (rel > maxRelError) maxRelError = rel;
            }
        }

        Console.WriteLine($"[E4-3] Softmax (n=12) max relative error: {maxRelError:E4}");
        Assert.IsTrue(maxRelError < 0.05,
            $"Softmax max relative error {maxRelError:E4} exceeds 5% for 12-token sequence.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static long[] RandomLongs(Random rng, int count, int scaleBits)
    {
        long[] result = new long[count];
        long maxVal = 1L << (scaleBits - 2); // leave headroom
        for (int i = 0; i < count; i++)
            result[i] = (long)((rng.NextDouble() * 2 - 1) * maxVal);
        return result;
    }
}
