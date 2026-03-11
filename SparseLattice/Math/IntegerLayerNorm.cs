using System.Numerics;
using System.Runtime.CompilerServices;
///////////////////////////////////////////////
namespace SparseLattice.Math;

/// <summary>
/// LayerNorm in exact integer arithmetic.
///
/// <para>
/// The float LayerNorm formula is:
/// <c>output[d] = (x[d] - mean) / sqrt(variance + eps) * weight[d] + bias[d]</c>
/// </para>
///
/// <para>
/// In the integer domain we cannot compute <c>sqrt</c> exactly (it's irrational for
/// most inputs). Instead we compute the integer square root <c>isqrt(v)</c> such that
/// <c>isqrt(v)² ≤ v &lt; (isqrt(v)+1)²</c>, then express the normalized value as:
/// <c>output[d] = (x[d] - mean) * invScale / isqrt(variance) * weight[d] + bias[d]</c>
/// where all multiplications use <see cref="Int128"/> to avoid overflow and the final
/// result is right-shifted back to the target scale.
/// </para>
///
/// <para>
/// The only source of error is the integer square root (floor, ±1 ULP) and the
/// right-shift truncation. Both are deterministic and bounded. The mean and variance
/// computations are exact via <see cref="Int128"/>/<see cref="BigInteger"/> accumulation.
/// </para>
/// </summary>
public static class IntegerLayerNorm
{
    /// <summary>
    /// Applies LayerNorm in-place to each row of <paramref name="x"/>.
    ///
    /// <para>
    /// <paramref name="x"/> is row-major [seqLen × embd]. Weight and bias are [embd].
    /// All three must share the same <paramref name="scaleExponent"/> on input.
    /// The output values are written back to <paramref name="x"/> at the same scale.
    /// </para>
    ///
    /// <para>
    /// The algorithm:
    /// 1. Compute mean = sum(row) / embd  (exact Int128 sum, integer division)
    /// 2. Compute variance = sum((row[d] - mean)²) / embd  (Int128 products, long result)
    /// 3. Compute invStd ≈ outputScale / isqrt(variance)  where outputScale is chosen
    ///    to preserve precision
    /// 4. Apply: x[d] = ((x[d] - mean) * invStd >> shift) * weight[d] >> shift + bias[d]
    /// </para>
    /// </summary>
    /// <param name="x">Row-major activations [seqLen × embd], modified in-place.</param>
    /// <param name="seqLen">Number of rows (sequence length).</param>
    /// <param name="embd">Number of columns (embedding dimension).</param>
    /// <param name="weight">Per-dimension scale, length <paramref name="embd"/>.</param>
    /// <param name="bias">Per-dimension bias, length <paramref name="embd"/>.</param>
    /// <param name="scaleExponent">
    /// The scale exponent shared by x, weight, and bias. The output is at the same scale.
    /// </param>
    public static void ApplyInPlace(long[] x, int seqLen, int embd,
        long[] weight, long[] bias, int scaleExponent)
    {
        for (int t = 0; t < seqLen; t++)
        {
            int rowBase = t * embd;
            NormalizeRow(x, rowBase, embd, weight, bias, scaleExponent);
        }
    }

    /// <summary>
    /// Normalizes a single row in-place. Extracted so single-row callers (e.g. per-token
    /// embedding normalization at load time) don't pay the seqLen loop overhead.
    /// </summary>
    public static void NormalizeRow(long[] x, int rowBase, int embd,
        long[] weight, long[] bias, int scaleExponent)
    {
        // -----------------------------------------------------------------
        // 1. Mean — exact via Int128 accumulation
        // -----------------------------------------------------------------
        Int128 sum = 0;
        for (int d = 0; d < embd; d++)
            sum += x[rowBase + d];

        // Integer division truncates toward zero — deterministic.
        long mean = (long)(sum / embd);

        // -----------------------------------------------------------------
        // 2. Variance — exact via Int128 accumulation of squared deviations
        //
        // Each (x[d] - mean) fits in long. The square fits in Int128.
        // The sum of embd squares: for embd=768 and values ~2^30,
        // each square ~2^60, sum ~2^70 → fits in Int128 with 57 bits spare.
        // -----------------------------------------------------------------
        Int128 sumSq = 0;
        for (int d = 0; d < embd; d++)
        {
            long delta = x[rowBase + d] - mean;
            sumSq += (Int128)delta * delta;
        }

        // variance = sumSq / embd (integer division, truncates)
        // This is in "squared scale" — values at scale 2^(-S) produce variance at 2^(-2S).
        Int128 variance128 = sumSq / embd;

        // -----------------------------------------------------------------
        // 3. Integer square root of variance
        //
        // We need 1/sqrt(variance) but in integer domain. Strategy:
        // Compute isqrt(variance) = floor(sqrt(variance)), then express the
        // normalized value as:
        //   normalized[d] = (x[d] - mean) * outputScale / isqrt(variance)
        //
        // To avoid division (which loses precision), we multiply by a large
        // constant and then divide by isqrt. This is equivalent to:
        //   normalized[d] = (x[d] - mean) * (outputScale / isqrt)
        //
        // We want the output at the same scale as the input. Since variance
        // is at scale 2^(2*scaleExponent), isqrt(variance) is at scale
        // 2^(scaleExponent). So (x - mean) / isqrt is dimensionless — we
        // need to re-scale it to scaleExponent.
        //
        // The trick: compute (x - mean) * rescale / isqrt where rescale is
        // chosen so the output has the right magnitude. Since isqrt has the
        // same scale as x, the ratio (x - mean) / isqrt is ~O(1) in real
        // terms, and we need to bring it back to the quantized range.
        // -----------------------------------------------------------------

        // Guard: zero variance → output is constant (all deltas are zero, or eps)
        // In float LayerNorm this is guarded by eps = 1e-12. In integer domain,
        // variance=0 means all values in the row are identical.
        long isqrtVariance;
        if (variance128 <= 0)
        {
            // All values equal — after subtracting mean, all deltas are 0.
            // Apply weight and bias directly: output[d] = 0 * weight[d] + bias[d] = bias[d]
            for (int d = 0; d < embd; d++)
                x[rowBase + d] = bias[d];
            return;
        }

        isqrtVariance = ISqrt128(variance128);
        if (isqrtVariance == 0) isqrtVariance = 1; // guard against tiny variance

        // -----------------------------------------------------------------
        // 4. Apply: output[d] = (x[d] - mean) * weight[d] / isqrtVariance + bias[d]
        //
        // Scale analysis:
        //   (x[d] - mean) is at scale S (same as input)
        //   weight[d] is at scale S
        //   isqrtVariance is at scale S (sqrt of scale 2S = scale S)
        //   So (x-mean) * weight / isqrt is at scale 2S / S = S ← correct!
        //   bias[d] is at scale S ← correct for addition.
        //
        // We use Int128 for the multiply to avoid overflow:
        //   (x-mean) is ~2^30, weight is ~2^30, product ~2^60 — fits in Int128.
        //   Dividing by isqrt (~2^30) gives ~2^30 — fits back in long.
        // -----------------------------------------------------------------

        for (int d = 0; d < embd; d++)
        {
            long delta = x[rowBase + d] - mean;
            // (delta * weight[d]) / isqrtVariance + bias[d]
            Int128 product = (Int128)delta * weight[d];
            x[rowBase + d] = (long)(product / isqrtVariance) + bias[d];
        }
    }

    // -----------------------------------------------------------------------
    // Integer square root via Newton's method
    // -----------------------------------------------------------------------

    /// <summary>
    /// Computes <c>floor(sqrt(value))</c> for a non-negative <see cref="Int128"/>.
    ///
    /// <para>
    /// Uses Newton's method: <c>x_{n+1} = (x_n + value / x_n) / 2</c>.
    /// Converges in ≤ 64 iterations (halves the error each step for a 128-bit input).
    /// The result satisfies <c>result² ≤ value &lt; (result+1)²</c>.
    /// </para>
    ///
    /// <para>
    /// This is the only source of approximation in the entire LayerNorm:
    /// the floor operation loses at most 1 ULP. For comparison, float32's
    /// <c>MathF.Sqrt</c> has 0.5 ULP error on the sqrt itself, but the
    /// input to sqrt (the variance) already carries accumulated rounding
    /// from the mean subtraction and squaring — so the total error is larger.
    /// </para>
    /// </summary>
    public static long ISqrt128(Int128 value)
    {
        if (value <= 0) return 0;
        if (value <= long.MaxValue) return ISqrt64((long)value);

        // Initial guess: find highest bit position via repeated shifting
        int bits = 0;
        {
            Int128 tmp = value;
            while (tmp > 0) { tmp >>= 1; bits++; }
        }
        Int128 x = (Int128)1 << ((bits + 1) / 2);

        // Newton iterations
        for (int i = 0; i < 128; i++)
        {
            Int128 xNext = (x + value / x) >> 1;
            if (xNext >= x) break; // converged
            x = xNext;
        }

        // Adjust: Newton can overshoot by 1 — ensure x² ≤ value < (x+1)²
        while (x * x > value) x--;
        while ((x + 1) * (x + 1) <= value) x++;

        return (long)x;
    }

    /// <summary>
    /// Computes <c>floor(sqrt(value))</c> for a non-negative <c>long</c>.
    /// Faster than the Int128 path for values that fit in 64 bits.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ISqrt64(long value)
    {
        if (value <= 0) return 0;
        if (value == 1) return 1;

        // Use double sqrt as initial guess — it's accurate to ~52 bits,
        // which is enough for values up to ~2^52. For larger values we
        // refine with one Newton step.
        long x = (long)System.Math.Sqrt((double)value);

        // Clamp to avoid negative from overflow in edge cases
        if (x <= 0) x = 1;

        // One Newton refinement for values > 2^52 where double loses precision
        if (value > (1L << 52))
        {
            x = (x + value / x) >> 1;
            if (x > 0) x = (x + value / x) >> 1;
        }

        // Final adjustment using Int128 to avoid long overflow on x*x
        // when x is large (e.g. isqrt(long.MaxValue) ≈ 3.03e9, x² ≈ 9.2e18 which overflows long)
        while (x > 0 && (Int128)x * x > value) x--;
        while ((Int128)(x + 1) * (x + 1) <= value) x++;

        return x;
    }
}
