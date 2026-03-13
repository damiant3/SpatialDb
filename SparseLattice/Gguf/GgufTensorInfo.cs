/////////////////////////////
namespace SparseLattice.Gguf;

public sealed class GgufTensorInfo
{
    public string   Name        { get; }
    public int[]    Shape       { get; }
    public GgufDType DType      { get; }
    public long     FileOffset  { get; }

    public long ElementCount { get; }
    public long ByteCount    { get; }

    internal GgufTensorInfo(string name, int[] shape, GgufDType dtype, long fileOffset)
    {
        Name       = name;
        Shape      = shape;
        DType      = dtype;
        FileOffset = fileOffset;

        long elements = 1;
        foreach (int d in shape) elements *= d;
        ElementCount = elements;
        ByteCount    = ComputeByteCount(dtype, elements);
    }

    static long ComputeByteCount(GgufDType dtype, long elements)
    {
        return dtype switch
        {
            GgufDType.F32  => elements * 4,
            GgufDType.F16  => elements * 2,
            GgufDType.BF16 => elements * 2,
            GgufDType.Q8_0 => (elements / 32) * (2 + 32),
            GgufDType.Q4_0 => (elements / 32) * (2 + 16),
            GgufDType.Q4_1 => (elements / 32) * (2 + 2 + 16),
            GgufDType.Q5_0 => (elements / 32) * (2 + 4 + 16),
            GgufDType.Q5_1 => (elements / 32) * (2 + 2 + 4 + 16),
            GgufDType.Q2_K => (elements / 256) * (2 + 2 + 256 / 16 + 256 / 4),
            GgufDType.Q3_K => (elements / 256) * (2 + 256 / 8 + 256 / 4 + 12),
            GgufDType.Q4_K => (elements / 256) * (2 + 2 + 12 + 256 / 2),
            GgufDType.Q5_K => (elements / 256) * (2 + 2 + 12 + 256 / 2 + 256 / 8),
            GgufDType.Q6_K => (elements / 256) * (2 + 256 / 2 + 256 / 4 + 256 / 16),
            GgufDType.Q8_K => (elements / 256) * (4 + 256 + 16 * 2),
            GgufDType.MXFP4 => (elements / 32) * 17,
            _              => elements * 4,
        };
    }
}
