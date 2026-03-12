///////////////////////////////////////////////
namespace SparseLattice.Math;

/// <summary>
/// Integer SiLU-gated feed-forward network matching the nomic-bert FFN structure:
/// gate projection, up projection, element-wise SiLU gating, down projection.
/// </summary>
public static class IntegerFFN
{
    /// <summary>Applies the SiLU-gated FFN in integer arithmetic with Half weights.</summary>
    public static long[] Apply(
        long[] x, int seqLen, int embd,
        Half[] wGate, Half[] wUp, Half[] wDown,
        int nFf,
        int scaleBits,
        int fracBits = IntegerTranscendentals.DefaultFracBits)
    {
        long[] gate = null!;
        long[] up = null!;

        if (nFf >= 1024)
        {
            Parallel.Invoke(
                () => gate = IntegerMatMul.MatMul(x, seqLen, embd, wGate, nFf, scaleBits, scaleBits),
                () => up = IntegerMatMul.MatMul(x, seqLen, embd, wUp, nFf, scaleBits, scaleBits));
        }
        else
        {
            gate = IntegerMatMul.MatMul(x, seqLen, embd, wGate, nFf, scaleBits, scaleBits);
            up = IntegerMatMul.MatMul(x, seqLen, embd, wUp, nFf, scaleBits, scaleBits);
        }

        int total = seqLen * nFf;
        for (int i = 0; i < total; i++)
        {
            long siluGate = IntegerTranscendentals.FixedSiLU(gate[i], fracBits);
            up[i] = (long)(((Int128)siluGate * up[i]) >> fracBits);
        }

        return IntegerMatMul.MatMul(up, seqLen, nFf, wDown, embd, scaleBits, scaleBits);
    }

    /// <summary>Applies the SiLU-gated FFN in integer arithmetic with float32 weights.</summary>
    public static long[] Apply(
        long[] x, int seqLen, int embd,
        float[] wGate, float[] wUp, float[] wDown,
        int nFf,
        int scaleBits,
        int fracBits = IntegerTranscendentals.DefaultFracBits)
    {
        long[] gate = null!;
        long[] up = null!;

        if (nFf >= 1024)
        {
            Parallel.Invoke(
                () => gate = IntegerMatMul.MatMul(x, seqLen, embd, wGate, nFf, scaleBits, scaleBits),
                () => up = IntegerMatMul.MatMul(x, seqLen, embd, wUp, nFf, scaleBits, scaleBits));
        }
        else
        {
            gate = IntegerMatMul.MatMul(x, seqLen, embd, wGate, nFf, scaleBits, scaleBits);
            up = IntegerMatMul.MatMul(x, seqLen, embd, wUp, nFf, scaleBits, scaleBits);
        }

        int total = seqLen * nFf;
        for (int i = 0; i < total; i++)
        {
            long siluGate = IntegerTranscendentals.FixedSiLU(gate[i], fracBits);
            up[i] = (long)(((Int128)siluGate * up[i]) >> fracBits);
        }

        return IntegerMatMul.MatMul(up, seqLen, nFf, wDown, embd, scaleBits, scaleBits);
    }

    /// <summary>Applies the SiLU-gated FFN with pre-quantized int64 weights (legacy).</summary>
    public static long[] Apply(
        long[] x, int seqLen, int embd,
        long[] wGate, long[] wUp, long[] wDown,
        int nFf,
        int matmulShift,
        int fracBits = IntegerTranscendentals.DefaultFracBits)
    {
        long[] gate = null!;
        long[] up = null!;

        if (nFf >= 1024)
        {
            Parallel.Invoke(
                () => gate = IntegerMatMul.MatMul(x, seqLen, embd, wGate, nFf, matmulShift),
                () => up = IntegerMatMul.MatMul(x, seqLen, embd, wUp, nFf, matmulShift));
        }
        else
        {
            gate = IntegerMatMul.MatMul(x, seqLen, embd, wGate, nFf, matmulShift);
            up = IntegerMatMul.MatMul(x, seqLen, embd, wUp, nFf, matmulShift);
        }

        int total = seqLen * nFf;
        for (int i = 0; i < total; i++)
        {
            long siluGate = IntegerTranscendentals.FixedSiLU(gate[i], fracBits);
            up[i] = (long)(((Int128)siluGate * up[i]) >> fracBits);
        }

        return IntegerMatMul.MatMul(up, seqLen, nFf, wDown, embd, matmulShift);
    }
}
