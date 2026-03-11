///////////////////////////////////////////////
namespace SparseLattice.Math;

/// <summary>
/// Integer SiLU-gated feed-forward network matching the nomic-bert FFN structure:
/// gate projection, up projection, element-wise SiLU gating, down projection.
/// </summary>
public static class IntegerFFN
{
    /// <summary>Applies the SiLU-gated FFN in integer arithmetic.</summary>
    public static long[] Apply(
        long[] x, int seqLen, int embd,
        long[] wGate, long[] wUp, long[] wDown,
        int nFf,
        int matmulShift,
        int fracBits = IntegerTranscendentals.DefaultFracBits)
    {
        long[] gate = IntegerMatMul.MatMul(x, seqLen, embd, wGate, nFf, matmulShift);
        long[] up = IntegerMatMul.MatMul(x, seqLen, embd, wUp, nFf, matmulShift);

        // silu(gate) × up; product doubles the scale, shift back by fracBits
        int total = seqLen * nFf;
        for (int i = 0; i < total; i++)
        {
            long siluGate = IntegerTranscendentals.FixedSiLU(gate[i], fracBits);
            up[i] = (long)(((Int128)siluGate * up[i]) >> fracBits);
        }

        return IntegerMatMul.MatMul(up, seqLen, nFf, wDown, embd, matmulShift);
    }
}
