using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Test.Math;

[TestClass]
public sealed class IntegerLayerNormTests
{
    [TestMethod]
    public void Unit_ISqrt64_PerfectSquares()
    {
        Assert.AreEqual(0L, IntegerLayerNorm.ISqrt64(0));
        Assert.AreEqual(1L, IntegerLayerNorm.ISqrt64(1));
        Assert.AreEqual(2L, IntegerLayerNorm.ISqrt64(4));
        Assert.AreEqual(3L, IntegerLayerNorm.ISqrt64(9));
        Assert.AreEqual(10L, IntegerLayerNorm.ISqrt64(100));
        Assert.AreEqual(1000L, IntegerLayerNorm.ISqrt64(1_000_000));
        Assert.AreEqual(1_000_000L, IntegerLayerNorm.ISqrt64(1_000_000_000_000L));
    }

    [TestMethod]
    public void Unit_ISqrt64_NonPerfectSquares_ReturnsFloor()
    {
        Assert.AreEqual(1L, IntegerLayerNorm.ISqrt64(2));
        Assert.AreEqual(1L, IntegerLayerNorm.ISqrt64(3));
        Assert.AreEqual(2L, IntegerLayerNorm.ISqrt64(5));
        Assert.AreEqual(2L, IntegerLayerNorm.ISqrt64(8));
        Assert.AreEqual(3L, IntegerLayerNorm.ISqrt64(10));
    }

    [TestMethod]
    public void Unit_ISqrt64_LargeValues()
    {
        Assert.AreEqual(1L << 30, IntegerLayerNorm.ISqrt64(1L << 60));

        long result = IntegerLayerNorm.ISqrt64(long.MaxValue);
        // Int128 comparisons: result² overflows long at this scale
        Assert.IsTrue((Int128)result * result <= long.MaxValue,
            $"isqrt({long.MaxValue})={result}: result²={(Int128)result * result} > {long.MaxValue}");
        Assert.IsTrue((Int128)(result + 1) * (result + 1) > long.MaxValue,
            $"isqrt({long.MaxValue})={result}: (result+1)²={(Int128)(result + 1) * (result + 1)} ≤ {long.MaxValue}");
    }

    [TestMethod]
    public void Unit_ISqrt64_FloorInvariant_Range()
    {
        long[] testValues = [0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 99, 100, 101,
                             999_999, 1_000_000, 1_000_001,
                             (1L << 50) - 1, 1L << 50, (1L << 50) + 1];

        foreach (long n in testValues)
        {
            long x = IntegerLayerNorm.ISqrt64(n);
            Assert.IsTrue(x >= 0, $"ISqrt64({n}) returned negative: {x}");
            Assert.IsTrue(x * x <= n, $"ISqrt64({n})={x}: x²={x * x} > {n}");
            if (x < (1L << 31))
                Assert.IsTrue((x + 1) * (x + 1) > n, $"ISqrt64({n})={x}: (x+1)²={(x + 1) * (x + 1)} ≤ {n}");
        }
    }

    [TestMethod]
    public void Unit_ISqrt128_PerfectSquares()
    {
        Assert.AreEqual(0L, IntegerLayerNorm.ISqrt128(0));
        Assert.AreEqual(1L, IntegerLayerNorm.ISqrt128(1));
        Assert.AreEqual(10L, IntegerLayerNorm.ISqrt128(100));
        Assert.AreEqual(1_000_000L, IntegerLayerNorm.ISqrt128((Int128)1_000_000_000_000));
    }

    [TestMethod]
    public void Unit_ISqrt128_LargeValues_BeyondLong()
    {
        // 768 × 2^60 exceeds long range — realistic variance bound
        Int128 bigVariance = (Int128)768 * ((Int128)1 << 60);
        long result = IntegerLayerNorm.ISqrt128(bigVariance);

        Int128 sq = (Int128)result * result;
        Int128 sqPlus = (Int128)(result + 1) * (result + 1);
        Assert.IsTrue(sq <= bigVariance, $"ISqrt128: result²={sq} > value={bigVariance}");
        Assert.IsTrue(sqPlus > bigVariance, $"ISqrt128: (result+1)²={sqPlus} ≤ value={bigVariance}");
    }

    [TestMethod]
    public void Unit_LayerNorm_UniformInput_ProducesBiasOnly()
    {
        long[] x = [100, 100, 100, 100];
        long[] weight = [1L << 30, 1L << 30, 1L << 30, 1L << 30];
        long[] bias = [10, 20, 30, 40];

        IntegerLayerNorm.NormalizeRow(x, 0, 4, weight, bias, -30);

        CollectionAssert.AreEqual(new long[] { 10, 20, 30, 40 }, x);
    }

    [TestMethod]
    public void Unit_LayerNorm_SymmetricInput_MeanIsZero()
    {
        long scale = 1L << 20;
        long[] x = [-100 * scale, 100 * scale];
        long[] weight = [scale, scale];
        long[] bias = [0, 0];

        IntegerLayerNorm.NormalizeRow(x, 0, 2, weight, bias, -20);

        Assert.IsTrue(x[0] < 0, $"x[0]={x[0]} should be negative");
        Assert.IsTrue(x[1] > 0, $"x[1]={x[1]} should be positive");
        Assert.AreEqual(-x[0], x[1], $"x[0]={x[0]} and x[1]={x[1]} should be equal magnitude");
    }

    [TestMethod]
    public void Integration_LayerNorm_768Dim_MatchesFloat()
    {
        const int embd = 768;
        const int scaleBits = 30;
        Random rng = new(42);

        float[] xFloat = RandomFloats(rng, embd, -0.1f, 0.1f);
        float[] wFloat = RandomFloats(rng, embd, 0.9f, 1.1f);
        float[] bFloat = RandomFloats(rng, embd, -0.01f, 0.01f);

        float[] xFloatRef = (float[])xFloat.Clone();
        FloatLayerNorm(xFloatRef, embd, wFloat, bFloat);

        ScaledTensor xInt = IntegerMatMul.QuantizeFromFloat(xFloat, scaleBits);
        ScaledTensor wInt = IntegerMatMul.QuantizeFromFloat(wFloat, scaleBits);
        ScaledTensor bInt = IntegerMatMul.QuantizeFromFloat(bFloat, scaleBits);

        IntegerLayerNorm.NormalizeRow(xInt.Data, 0, embd, wInt.Data, bInt.Data, xInt.ScaleExponent);

        float[] xIntDeq = IntegerMatMul.DequantizeToFloat(xInt);

        double maxAbsError = 0;
        double maxRelError = 0;
        double sumRelError = 0;
        int count = 0;

        for (int d = 0; d < embd; d++)
        {
            double absErr = System.Math.Abs(xFloatRef[d] - xIntDeq[d]);
            if (absErr > maxAbsError) maxAbsError = absErr;

            double abs = System.Math.Abs(xFloatRef[d]);
            if (abs > 1e-6)
            {
                double rel = absErr / abs;
                if (rel > maxRelError) maxRelError = rel;
                sumRelError += rel;
                count++;
            }
        }

        double meanRelError = count > 0 ? sumRelError / count : 0;

        Console.WriteLine($"[E4-2] 768-dim LayerNorm fidelity:");
        Console.WriteLine($"[E4-2]   Max absolute error:  {maxAbsError:E4}");
        Console.WriteLine($"[E4-2]   Max relative error:  {maxRelError:E4}");
        Console.WriteLine($"[E4-2]   Mean relative error: {meanRelError:E4}");
        Console.WriteLine($"[E4-2]   Elements compared:   {count}/{embd}");

        Assert.IsTrue(maxRelError < 0.05,
            $"Max relative error {maxRelError:E4} exceeds 5% threshold.");
    }

    [TestMethod]
    public void Integration_LayerNorm_MultiRow_MatchesFloat()
    {
        const int embd = 768;
        const int seqLen = 4;
        const int scaleBits = 30;
        Random rng = new(7);

        float[] xFloat = RandomFloats(rng, seqLen * embd, -0.1f, 0.1f);
        float[] wFloat = RandomFloats(rng, embd, 0.9f, 1.1f);
        float[] bFloat = RandomFloats(rng, embd, -0.01f, 0.01f);

        float[] xFloatRef = (float[])xFloat.Clone();
        for (int t = 0; t < seqLen; t++)
            FloatLayerNormRow(xFloatRef, t * embd, embd, wFloat, bFloat);

        ScaledTensor xInt = IntegerMatMul.QuantizeFromFloat(xFloat, scaleBits);
        ScaledTensor wInt = IntegerMatMul.QuantizeFromFloat(wFloat, scaleBits);
        ScaledTensor bInt = IntegerMatMul.QuantizeFromFloat(bFloat, scaleBits);

        IntegerLayerNorm.ApplyInPlace(xInt.Data, seqLen, embd, wInt.Data, bInt.Data, xInt.ScaleExponent);

        float[] xIntDeq = IntegerMatMul.DequantizeToFloat(xInt);

        double maxRelError = 0;
        for (int i = 0; i < xFloatRef.Length; i++)
        {
            double abs = System.Math.Abs(xFloatRef[i]);
            if (abs < 1e-6) continue;
            double rel = System.Math.Abs(xFloatRef[i] - xIntDeq[i]) / abs;
            if (rel > maxRelError) maxRelError = rel;
        }

        Console.WriteLine($"[E4-2] Multi-row ({seqLen}×{embd}) max relative error: {maxRelError:E4}");

        Assert.IsTrue(maxRelError < 0.05,
            $"Multi-row LayerNorm max relative error {maxRelError:E4} exceeds 5%.");
    }

    [TestMethod]
    public void Unit_LayerNorm_Deterministic_100Runs()
    {
        const int embd = 128;
        Random rng = new(42);
        float[] xFloat = RandomFloats(rng, embd, -0.1f, 0.1f);
        float[] wFloat = RandomFloats(rng, embd, 0.9f, 1.1f);
        float[] bFloat = RandomFloats(rng, embd, -0.01f, 0.01f);

        ScaledTensor wInt = IntegerMatMul.QuantizeFromFloat(wFloat, 30);
        ScaledTensor bInt = IntegerMatMul.QuantizeFromFloat(bFloat, 30);

        ScaledTensor xRef = IntegerMatMul.QuantizeFromFloat(xFloat, 30);
        IntegerLayerNorm.NormalizeRow(xRef.Data, 0, embd, wInt.Data, bInt.Data, xRef.ScaleExponent);

        for (int run = 0; run < 100; run++)
        {
            ScaledTensor xTest = IntegerMatMul.QuantizeFromFloat(xFloat, 30);
            IntegerLayerNorm.NormalizeRow(xTest.Data, 0, embd, wInt.Data, bInt.Data, xTest.ScaleExponent);

            for (int d = 0; d < embd; d++)
                Assert.AreEqual(xRef.Data[d], xTest.Data[d],
                    $"Run {run}, dim {d}: not bit-identical.");
        }
    }

    [TestMethod]
    public void Unit_LayerNorm_SingleElement()
    {
        long[] x = [500];
        long[] weight = [1L << 30];
        long[] bias = [42];

        IntegerLayerNorm.NormalizeRow(x, 0, 1, weight, bias, -30);

        Assert.AreEqual(42L, x[0]);
    }

    [TestMethod]
    public void Unit_LayerNorm_TwoElements_Normalized()
    {
        long scale = 1L << 20;
        long[] x = [3 * scale, 1 * scale];
        long[] weight = [scale, scale];
        long[] bias = [0, 0];

        IntegerLayerNorm.NormalizeRow(x, 0, 2, weight, bias, -20);

        Assert.IsTrue(System.Math.Abs(x[0] - scale) < scale / 100,
            $"x[0]={x[0]}, expected ≈{scale}");
        Assert.IsTrue(System.Math.Abs(x[1] + scale) < scale / 100,
            $"x[1]={x[1]}, expected ≈{-scale}");
    }

    [TestMethod]
    public void Unit_LayerNorm_NearZeroVariance_DoesNotCrash()
    {
        long[] x = [1000000, 1000001, 1000000, 1000001];
        long[] weight = [1L << 30, 1L << 30, 1L << 30, 1L << 30];
        long[] bias = [0, 0, 0, 0];

        IntegerLayerNorm.NormalizeRow(x, 0, 4, weight, bias, -30);

        for (int d = 0; d < 4; d++)
            Assert.IsTrue(x[d] >= long.MinValue && x[d] <= long.MaxValue,
                $"x[{d}]={x[d]} should be a valid long.");
    }

    private static void FloatLayerNorm(float[] x, int embd, float[] weight, float[] bias)
        => FloatLayerNormRow(x, 0, embd, weight, bias);

    private static void FloatLayerNormRow(float[] x, int rowBase, int embd, float[] weight, float[] bias)
    {
        const float eps = 1e-12f;

        float mean = 0f;
        for (int d = 0; d < embd; d++)
            mean += x[rowBase + d];
        mean /= embd;

        float variance = 0f;
        for (int d = 0; d < embd; d++)
        {
            float delta = x[rowBase + d] - mean;
            variance += delta * delta;
        }
        variance /= embd;

        float invStd = 1.0f / MathF.Sqrt(variance + eps);

        for (int d = 0; d < embd; d++)
            x[rowBase + d] = (x[rowBase + d] - mean) * invStd * weight[d] + bias[d];
    }

    private static float[] RandomFloats(Random rng, int count, float min, float max)
    {
        float[] result = new float[count];
        float range = max - min;
        for (int i = 0; i < count; i++)
            result[i] = (float)(rng.NextDouble() * range + min);
        return result;
    }
}
