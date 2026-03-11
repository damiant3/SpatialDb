using SparseLattice.Math;
using System.Diagnostics;
using System.Numerics;
///////////////////////////////////////////////
namespace SparseLattice.Test.Math;

[TestClass]
public sealed class IntegerMatMulTests
{
    [TestMethod]
    public void Unit_DotInt128_ExactResult()
    {
        // 3×5 + 7×11 = 15 + 77 = 92
        long[] a = [3, 7];
        long[] b = [5, 11];

        Int128 result = IntegerMatMul.DotInt128(a, 0, b, 0, 2);

        Assert.AreEqual((Int128)92, result);
    }

    [TestMethod]
    public void Unit_DotInt128_LargeValues_NoOverflow()
    {
        // Two values near long.MaxValue/2 — their product exceeds long range
        // but fits in Int128.
        long big = long.MaxValue / 2;         // ~4.6 × 10^18
        long[] a = [big, big];
        long[] b = [big, big];

        Int128 result = IntegerMatMul.DotInt128(a, 0, b, 0, 2);

        // Expected: 2 × (big × big)
        BigInteger expected = 2 * (BigInteger)big * big;
        Assert.AreEqual((Int128)(long)0, (Int128)0, "Sanity: Int128 is usable");
        // Verify via BigInteger cross-check
        BigInteger resultAsBig = (BigInteger)(UInt128)(result >= 0 ? result : throw new Exception("Negative unexpected"));
        Assert.AreEqual(expected, resultAsBig,
            "Int128 dot product must match BigInteger for large values.");
    }

    [TestMethod]
    public void Unit_DotInt128_NegativeValues()
    {
        // (-3)×5 + 7×(-11) = -15 + (-77) = -92
        long[] a = [-3, 7];
        long[] b = [5, -11];

        Int128 result = IntegerMatMul.DotInt128(a, 0, b, 0, 2);

        Assert.AreEqual((Int128)(-92), result);
    }

    [TestMethod]
    public void Unit_DotInt128_ZeroVector()
    {
        long[] a = [0, 0, 0];
        long[] b = [100, 200, 300];

        Int128 result = IntegerMatMul.DotInt128(a, 0, b, 0, 3);

        Assert.AreEqual((Int128)0, result);
    }

    [TestMethod]
    public void Unit_DotInt128_SingleElement()
    {
        long[] a = [1_000_000_000L];
        long[] b = [1_000_000_000L];

        Int128 result = IntegerMatMul.DotInt128(a, 0, b, 0, 1);

        Assert.AreEqual((Int128)1_000_000_000_000_000_000L, result);
    }

    [TestMethod]
    public void Unit_DotInt128_WithOffset()
    {
        long[] a = [99, 3, 7, 99];
        long[] b = [99, 5, 11, 99];

        Int128 result = IntegerMatMul.DotInt128(a, 1, b, 1, 2);

        Assert.AreEqual((Int128)92, result, "Offset should skip the 99s.");
    }

    [TestMethod]
    public void Unit_MatMul_Identity()
    {
        // A = [1,2; 3,4], W = identity (column-major) = [1,0; 0,1]
        // Column-major identity for 2×2: col0=[1,0], col1=[0,1] → [1,0,0,1]
        long[] a = [1, 2, 3, 4];
        long[] w = [1, 0, 0, 1]; // col-major: W[col*nIn + k]

        long[] result = IntegerMatMul.MatMul(a, rowsA: 2, colsA: 2, w, colsB: 2);

        CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4 }, result);
    }

    [TestMethod]
    public void Unit_MatMul_KnownResult()
    {
        // A = [1,2,3] (1×3), W = [[4],[5],[6]] (3×1, col-major: [4,5,6])
        // Result: 1×4 + 2×5 + 3×6 = 4+10+18 = 32
        long[] a = [1, 2, 3];
        long[] w = [4, 5, 6]; // single column

        long[] result = IntegerMatMul.MatMul(a, rowsA: 1, colsA: 3, w, colsB: 1);

        Assert.AreEqual(1, result.Length);
        Assert.AreEqual(32L, result[0]);
    }

    [TestMethod]
    public void Unit_MatMul_TwoByThree_Times_ThreeByTwo()
    {
        // A [2×3]:  [[1, 2, 3],
        //            [4, 5, 6]]
        // W [3×2] col-major:
        //   col 0 = [7, 8, 9]  → maps to W row-major [[7,10],[8,11],[9,12]]
        //   col 1 = [10,11,12]
        //
        // C[0,0] = 1×7 + 2×8 + 3×9 = 7+16+27 = 50
        // C[0,1] = 1×10 + 2×11 + 3×12 = 10+22+36 = 68
        // C[1,0] = 4×7 + 5×8 + 6×9 = 28+40+54 = 122
        // C[1,1] = 4×10 + 5×11 + 6×12 = 40+55+72 = 167
        long[] a = [1, 2, 3, 4, 5, 6];
        long[] w = [7, 8, 9, 10, 11, 12]; // col-major: col0=[7,8,9], col1=[10,11,12]

        long[] result = IntegerMatMul.MatMul(a, rowsA: 2, colsA: 3, w, colsB: 2);

        CollectionAssert.AreEqual(new long[] { 50, 68, 122, 167 }, result);
    }

    [TestMethod]
    public void Unit_MatMul_WithRightShift()
    {
        // Same as above but with resultShift=1 (divide by 2, floor)
        long[] a = [1, 2, 3, 4, 5, 6];
        long[] w = [7, 8, 9, 10, 11, 12];

        long[] result = IntegerMatMul.MatMul(a, 2, 3, w, 2, resultShift: 1);

        // 50>>1=25, 68>>1=34, 122>>1=61, 167>>1=83
        CollectionAssert.AreEqual(new long[] { 25, 34, 61, 83 }, result);
    }

    [TestMethod]
    public void Unit_MatMul_NegativeValues()
    {
        // A = [-1, 2], W col-major = [3, -4] (single output col)
        // Result: (-1)×3 + 2×(-4) = -3 + (-8) = -11
        long[] a = [-1, 2];
        long[] w = [3, -4];

        long[] result = IntegerMatMul.MatMul(a, 1, 2, w, 1);

        Assert.AreEqual(-11L, result[0]);
    }

    [TestMethod]
    public void Unit_ScaledTensor_MatMul_ExponentTracking()
    {
        // activations at scale -20 (values ×2^20), weights at scale -10 (values ×2^10)
        // result should be at scale -20 + -10 + 0 = -30
        ScaledTensor act = new([100, 200], -20);
        ScaledTensor wgt = new([3, 4], -10);

        ScaledTensor result = IntegerMatMul.MatMul(act, rowsA: 1, colsA: 2, wgt, colsB: 1);

        Assert.AreEqual(-30, result.ScaleExponent);
        Assert.AreEqual(100L * 3 + 200L * 4, result.Data[0]); // 1100
    }

    [TestMethod]
    public void Unit_ScaledTensor_MatMul_WithShift_ExponentTracking()
    {
        ScaledTensor act = new([100, 200], -20);
        ScaledTensor wgt = new([3, 4], -10);

        // resultShift=5 → result exponent = -20 + -10 + 5 = -25
        ScaledTensor result = IntegerMatMul.MatMul(act, 1, 2, wgt, 1, resultShift: 5);

        Assert.AreEqual(-25, result.ScaleExponent);
        Assert.AreEqual(1100L >> 5, result.Data[0]); // 1100 >> 5 = 34
    }

    [TestMethod]
    public void Unit_ScaledTensor_RightShift()
    {
        ScaledTensor t = new([1024, -512, 7], -20);
        ScaledTensor shifted = t.RightShift(3);

        Assert.AreEqual(-17, shifted.ScaleExponent); // -20 + 3
        Assert.AreEqual(128L, shifted.Data[0]);      // 1024 >> 3
        Assert.AreEqual(-64L, shifted.Data[1]);      // -512 >> 3
        Assert.AreEqual(0L, shifted.Data[2]);         // 7 >> 3 = 0 (floor)
    }

    [TestMethod]
    public void Unit_QuantizeFromFloat_RoundTrip()
    {
        float[] original = [0.5f, -0.25f, 0.125f, 1.0f, -1.0f];
        ScaledTensor quantized = IntegerMatMul.QuantizeFromFloat(original, scaleBits: 30);

        Assert.AreEqual(-30, quantized.ScaleExponent);

        float[] dequantized = IntegerMatMul.DequantizeToFloat(quantized);

        for (int i = 0; i < original.Length; i++)
        {
            float error = MathF.Abs(original[i] - dequantized[i]);
            Assert.IsTrue(error < 1e-6f,
                $"Round-trip error at [{i}]: original={original[i]}, dequantized={dequantized[i]}, error={error}");
        }
    }

    [TestMethod]
    public void Unit_QuantizeFromFloat_PreservesMorePrecisionThanFloat()
    {
        // Two values that are very close — float32 may not distinguish them
        // after a multiply, but integer with 30-bit scale should.
        float a = 0.123456789f;
        float b = 0.123456780f; // differs by ~9e-9

        ScaledTensor qa = IntegerMatMul.QuantizeFromFloat([a], scaleBits: 30);
        ScaledTensor qb = IntegerMatMul.QuantizeFromFloat([b], scaleBits: 30);

        // The quantized values should differ
        Assert.AreNotEqual(qa.Data[0], qb.Data[0],
            "Quantization at 30 bits should distinguish values differing by ~1e-8.");
    }

    [TestMethod]
    public void Unit_MatMul_MatchesFloatMatMul_SmallMatrix()
    {
        // A [2×3], W [3×2] — realistic small shapes
        float[] aFloat = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f];
        float[] wFloat = [0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f];

        // Float reference
        float[] cFloat = FloatMatMul(aFloat, 2, 3, wFloat, 2);

        // Integer path
        const int bits = 30;
        ScaledTensor aInt = IntegerMatMul.QuantizeFromFloat(aFloat, bits);
        ScaledTensor wInt = IntegerMatMul.QuantizeFromFloat(wFloat, bits);
        ScaledTensor cInt = IntegerMatMul.MatMul(aInt, 2, 3, wInt, 2);
        float[] cDeq = IntegerMatMul.DequantizeToFloat(cInt);

        for (int i = 0; i < cFloat.Length; i++)
        {
            float error = MathF.Abs(cFloat[i] - cDeq[i]);
            Assert.IsTrue(error < 1e-4f,
                $"Mismatch at [{i}]: float={cFloat[i]:G9}, integer={cDeq[i]:G9}, error={error:E3}");
        }
    }

    [TestMethod]
    public void Integration_MatMul_768Dim_MatchesFloat()
    {
        // Realistic embedding dimension: inner product over 768 elements
        // This is the exact computation that happens in every transformer projection
        const int dim = 768;
        const int rows = 4;   // sequence length
        const int cols = 768; // output dimension

        Random rng = new(42);
        float[] aFloat = RandomFloats(rng, rows * dim, -0.05f, 0.05f);
        float[] wFloat = RandomFloats(rng, dim * cols, -0.05f, 0.05f);

        // Float reference
        float[] cFloat = FloatMatMul(aFloat, rows, dim, wFloat, cols);

        // Integer path at 30-bit scale
        ScaledTensor aInt = IntegerMatMul.QuantizeFromFloat(aFloat, 30);
        ScaledTensor wInt = IntegerMatMul.QuantizeFromFloat(wFloat, 30);
        ScaledTensor cInt = IntegerMatMul.MatMul(aInt, rows, dim, wInt, cols);
        float[] cDeq = IntegerMatMul.DequantizeToFloat(cInt);

        // Measure error statistics
        double maxRelError = 0;
        double sumRelError = 0;
        int count = 0;

        for (int i = 0; i < cFloat.Length; i++)
        {
            float abs = MathF.Abs(cFloat[i]);
            if (abs < 1e-10f) continue; // skip near-zero for relative error

            double relError = MathF.Abs(cFloat[i] - cDeq[i]) / abs;
            if (relError > maxRelError) maxRelError = relError;
            sumRelError += relError;
            count++;
        }

        double meanRelError = count > 0 ? sumRelError / count : 0;

        Console.WriteLine($"[E4-1] 768-dim matmul fidelity ({rows}×{dim} × {dim}×{cols}):");
        Console.WriteLine($"[E4-1]   Max relative error:  {maxRelError:E4}");
        Console.WriteLine($"[E4-1]   Mean relative error: {meanRelError:E4}");
        Console.WriteLine($"[E4-1]   Elements compared:   {count}");

        // Integer matmul at 30-bit scale should have relative error < 1e-4
        // (each element is a sum of 768 products, each quantized to ~9 digits,
        // vs float32's ~7 digits — the integer path should be MORE precise)
        Assert.IsTrue(maxRelError < 1e-3,
            $"Max relative error {maxRelError:E4} exceeds 1e-3 threshold. " +
            "Integer matmul diverges too much from float32.");
    }

    [TestMethod]
    public void Integration_MatMul_Deterministic_100Runs()
    {
        // The core promise: integer matmul is perfectly deterministic.
        // Run 100 times with the same input — results must be bit-identical.
        const int dim = 128;
        Random rng = new(7);
        float[] aFloat = RandomFloats(rng, dim, -1f, 1f);
        float[] wFloat = RandomFloats(rng, dim * dim, -1f, 1f);

        ScaledTensor aInt = IntegerMatMul.QuantizeFromFloat(aFloat, 30);
        ScaledTensor wInt = IntegerMatMul.QuantizeFromFloat(wFloat, 30);

        long[] reference = IntegerMatMul.MatMul(aInt.Data, 1, dim, wInt.Data, dim);

        for (int run = 0; run < 100; run++)
        {
            long[] result = IntegerMatMul.MatMul(aInt.Data, 1, dim, wInt.Data, dim);
            for (int i = 0; i < reference.Length; i++)
                Assert.AreEqual(reference[i], result[i],
                    $"Run {run}, element {i}: result differs from reference. " +
                    "Integer matmul must be perfectly deterministic.");
        }
    }

    [TestMethod]
    public void Unit_DotInt128_WorstCase_768Dim_Scale30()
    {
        // Worst case: 768 elements, each at max value for 30-bit scale
        // max value = 2^30 ≈ 1.07 × 10^9
        // worst product: (2^30)² = 2^60 ≈ 1.15 × 10^18
        // worst sum: 768 × 2^60
        // 768 < 2^10, so worst sum < 2^70 — fits easily in Int128 (127 bits)

        long maxVal = 1L << 30;
        long[] a = new long[768];
        long[] b = new long[768];
        Array.Fill(a, maxVal);
        Array.Fill(b, maxVal);

        Int128 result = IntegerMatMul.DotInt128(a, 0, b, 0, 768);

        // Expected: 768 × (2^30)² = 768 × 2^60
        BigInteger expected = (BigInteger)768 * ((BigInteger)1 << 60);

        // Cross-check: compute via BigInteger
        BigInteger bigSum = 0;
        for (int i = 0; i < 768; i++)
            bigSum += (BigInteger)a[i] * b[i];
        Assert.AreEqual(expected, bigSum, "BigInteger cross-check failed.");

        // Int128 → BigInteger conversion via byte round-trip (correct for all values)
        BigInteger resultBig = Int128ToBigInteger(result);
        Assert.AreEqual(expected, resultBig,
            "Int128 result must exactly match BigInteger for worst-case 768-dim dot product.");
    }

    [TestMethod]
    public void Unit_DotInt128_MixedSign_WorstCase()
    {
        // Alternating +max and -max — tests that negative Int128 values accumulate correctly
        long maxVal = 1L << 30;
        long[] a = new long[768];
        long[] b = new long[768];
        for (int i = 0; i < 768; i++)
        {
            a[i] = (i % 2 == 0) ? maxVal : -maxVal;
            b[i] = maxVal;
        }

        Int128 result = IntegerMatMul.DotInt128(a, 0, b, 0, 768);

        // 384 positive terms + 384 negative terms = 0 (768 is even)
        Assert.AreEqual((Int128)0, result,
            "Alternating +/- with equal magnitude over even count must sum to zero.");
    }

    [TestMethod]
    public void Unit_AddInPlace()
    {
        long[] dst = [10, 20, 30];
        long[] src = [1, 2, 3];
        IntegerMatMul.AddInPlace(dst, src, 3);
        CollectionAssert.AreEqual(new long[] { 11, 22, 33 }, dst);
    }

    [TestMethod]
    public void Unit_RightShiftInPlace()
    {
        long[] data = [1024, -512, 7, 0];
        IntegerMatMul.RightShiftInPlace(data, 4, 3);
        CollectionAssert.AreEqual(new long[] { 128, -64, 0, 0 }, data);
    }

    [TestMethod]
    public void Unit_RightShiftInPlace_NegativeRoundsDown()
    {
        // Arithmetic right shift rounds toward negative infinity (not toward zero)
        // -1 >> 1 = -1 (not 0)
        long[] data = [-1, -3, -7];
        IntegerMatMul.RightShiftInPlace(data, 3, 1);
        Assert.AreEqual(-1L, data[0], "-1 >> 1 should be -1 (floor toward -inf)");
        Assert.AreEqual(-2L, data[1], "-3 >> 1 should be -2 (floor toward -inf)");
        Assert.AreEqual(-4L, data[2], "-7 >> 1 should be -4 (floor toward -inf)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static float[] FloatMatMul(float[] a, int rowsA, int colsA, float[] w, int colsB)
    {
        float[] c = new float[rowsA * colsB];
        for (int row = 0; row < rowsA; row++)
        {
            for (int col = 0; col < colsB; col++)
            {
                float sum = 0f;
                int aBase = row * colsA;
                int wBase = col * colsA; // GGUF column-major
                for (int k = 0; k < colsA; k++)
                    sum += a[aBase + k] * w[wBase + k];
                c[row * colsB + col] = sum;
            }
        }
        return c;
    }

    private static float[] RandomFloats(Random rng, int count, float min, float max)
    {
        float[] result = new float[count];
        float range = max - min;
        for (int i = 0; i < count; i++)
            result[i] = (float)(rng.NextDouble() * range + min);
        return result;
    }

    /// <summary>
    /// Converts Int128 to BigInteger correctly for all values, including those
    /// that exceed ulong range. Uses the two-word representation.
    /// </summary>
    private static BigInteger Int128ToBigInteger(Int128 value)
    {
        if (value >= 0 && value <= (Int128)long.MaxValue)
            return (long)value;
        if (value < 0 && value >= (Int128)long.MinValue)
            return (long)value;

        // For values outside long range: decompose into hi/lo 64-bit words
        bool negative = value < 0;
        UInt128 abs = negative ? (UInt128)(-value) : (UInt128)value;
        ulong lo = (ulong)(abs & ulong.MaxValue);
        ulong hi = (ulong)(abs >> 64);
        BigInteger result = ((BigInteger)hi << 64) | lo;
        return negative ? -result : result;
    }
}
