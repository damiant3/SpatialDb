///////////////////////////////////////////////
namespace SparseLattice.Math;

public static class IntegerFFN
{
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
