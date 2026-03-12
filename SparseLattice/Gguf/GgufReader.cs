using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

// ---------------------------------------------------------------------------
// Public enumerations
// ---------------------------------------------------------------------------

public enum GgufValueType : uint
{
    UInt8   = 0,
    Int8    = 1,
    UInt16  = 2,
    Int16   = 3,
    UInt32  = 4,
    Int32   = 5,
    Float32 = 6,
    Bool    = 7,
    String  = 8,
    Array   = 9,
    UInt64  = 10,
    Int64   = 11,
    Float64 = 12,
}

public enum GgufDType : uint
{
    F32  = 0,
    F16  = 1,
    Q4_0 = 2,
    Q4_1 = 3,
    Q5_0 = 6,
    Q5_1 = 7,
    Q8_0 = 8,
    Q2_K = 10,
    Q3_K = 11,
    Q4_K = 12,
    Q5_K = 13,
    Q6_K = 14,
    Q8_K = 15,
    BF16 = 30,
    MXFP4 = 39,
}

// ---------------------------------------------------------------------------
// GgufValue — typed metadata value
// ---------------------------------------------------------------------------

public sealed class GgufValue
{
    public GgufValueType Type { get; }

    private readonly object m_value;

    internal GgufValue(GgufValueType type, object value)
    {
        Type    = type;
        m_value = value;
    }

    public uint    AsUInt32()  => Type == GgufValueType.UInt32  ? (uint)m_value    : throw Mismatch("uint");
    public int     AsInt32()   => Type == GgufValueType.Int32   ? (int)m_value     : throw Mismatch("int");
    public ulong   AsUInt64()  => Type == GgufValueType.UInt64  ? (ulong)m_value   : throw Mismatch("ulong");
    public long    AsInt64()   => Type == GgufValueType.Int64   ? (long)m_value    : throw Mismatch("long");
    public float   AsFloat32() => Type == GgufValueType.Float32 ? (float)m_value   : throw Mismatch("float");
    public double  AsFloat64() => Type == GgufValueType.Float64 ? (double)m_value  : throw Mismatch("double");
    public bool    AsBool()    => Type == GgufValueType.Bool    ? (bool)m_value    : throw Mismatch("bool");
    public string  AsString()  => Type == GgufValueType.String  ? (string)m_value  : throw Mismatch("string");
    public byte    AsUInt8()   => Type == GgufValueType.UInt8   ? (byte)m_value    : throw Mismatch("byte");
    public sbyte   AsInt8()    => Type == GgufValueType.Int8    ? (sbyte)m_value   : throw Mismatch("sbyte");
    public ushort  AsUInt16()  => Type == GgufValueType.UInt16  ? (ushort)m_value  : throw Mismatch("ushort");
    public short   AsInt16()   => Type == GgufValueType.Int16   ? (short)m_value   : throw Mismatch("short");

    public IReadOnlyList<GgufValue> AsArray()
        => Type == GgufValueType.Array
            ? (IReadOnlyList<GgufValue>)m_value
            : throw Mismatch("array");

    /// <summary>Returns the value as a string regardless of underlying type — for diagnostics.</summary>
    public override string ToString() => m_value?.ToString() ?? "(null)";

    private InvalidOperationException Mismatch(string expected)
        => new($"GgufValue type mismatch: expected {expected}, actual {Type}");
}

// ---------------------------------------------------------------------------
// GgufTensorInfo
// ---------------------------------------------------------------------------

public sealed class GgufTensorInfo
{
    public string   Name        { get; }
    public int[]    Shape       { get; }   // e.g. [768, 768]
    public GgufDType DType      { get; }
    public long     FileOffset  { get; }   // from start of tensor data section

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

    private static long ComputeByteCount(GgufDType dtype, long elements)
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
            // K-quant super-blocks of 256 elements
            GgufDType.Q2_K => (elements / 256) * (2 + 2 + 256 / 16 + 256 / 4),    // 256: d(f16)+dmin(f16)+scales(16)+qs(64) = 84
            GgufDType.Q3_K => (elements / 256) * (2 + 256 / 8 + 256 / 4 + 12),    // 256: d(f16)+hmask(32)+qs(64)+scales(12) = 110
            GgufDType.Q4_K => (elements / 256) * (2 + 2 + 12 + 256 / 2),           // 256: d(f16)+dmin(f16)+scales(12)+qs(128) = 144
            GgufDType.Q5_K => (elements / 256) * (2 + 2 + 12 + 256 / 2 + 256 / 8),// 256: d(f16)+dmin(f16)+scales(12)+qs(128)+qh(32) = 176
            GgufDType.Q6_K => (elements / 256) * (2 + 256 / 2 + 256 / 4 + 256 / 16),// 256: d(f16)+ql(128)+qh(64)+scales(16) = 210
            GgufDType.Q8_K => (elements / 256) * (4 + 256 + 16 * 2),               // 256: d(f32)+qs(256)+bsums(16*2) = 292
            GgufDType.MXFP4 => (elements / 32) * 17,
            _              => elements * 4,
        };
    }
}

// ---------------------------------------------------------------------------
// GgufReader
// ---------------------------------------------------------------------------

/// <summary>
/// Reads a GGUF model file, exposes metadata key-value pairs and tensor information,
/// and can dequantize tensors to <c>float[]</c>.
/// </summary>
/// <remarks>
/// Supported quantization formats: F32, F16, Q4_0, Q8_0.
/// The file is kept open (memory-mapped via <see cref="FileStream"/>) until
/// <see cref="Dispose"/> is called. Tensor data is read on demand.
/// </remarks>
public sealed class GgufReader : IDisposable
{
    // Magic bytes: "GGUF" = 0x47 0x47 0x55 0x46
    private static readonly uint s_magic = 0x46554747u;  // little-endian: "GGUF"

    private readonly FileStream          m_stream;
    private readonly long                m_tensorDataOffset;  // absolute offset in file
    private readonly IReadOnlyDictionary<string, GgufValue>  m_metadata;
    private readonly IReadOnlyList<GgufTensorInfo>            m_tensors;
    private readonly Dictionary<string, GgufTensorInfo>      m_tensorIndex;

    private bool m_disposed;

    // -----------------------------------------------------------------------
    // Public properties
    // -----------------------------------------------------------------------

    public uint   Version      { get; }
    public string Architecture { get; }
    public string ModelName    { get; }
    public int    EmbeddingLength   { get; }
    public int    ContextLength     { get; }
    public int    HeadCount         { get; }
    public int    LayerCount        { get; }
    public int    FeedForwardLength { get; }
    public long   TensorDataOffset  => m_tensorDataOffset;

    public IReadOnlyList<string> Tokens     { get; }
    public IReadOnlyList<string> Merges     { get; }
    public IReadOnlyList<int>    TokenTypes { get; }
    public int BosTokenId { get; }
    public int EosTokenId { get; }
    public int UnkTokenId { get; }

    public IReadOnlyDictionary<string, GgufValue>  Metadata    => m_metadata;
    public IReadOnlyList<GgufTensorInfo>            TensorInfos => m_tensors;

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    /// <summary>Opens a GGUF file and parses its header, metadata, and tensor table.</summary>
    public static GgufReader Open(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("GGUF file not found", path);

        return new GgufReader(path);
    }

    private GgufReader(string path)
    {
        m_stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65536, useAsync: false);

        try
        {
            using BinaryReader r = new(m_stream, Encoding.UTF8, leaveOpen: true);

            // --- Header ---
            uint magic = r.ReadUInt32();
        if (magic != s_magic)
                throw new InvalidDataException(
                    $"Not a GGUF file: expected magic 0x{s_magic:X8}, got 0x{magic:X8}");

            Version = r.ReadUInt32();
            if (Version is not (2 or 3))
                throw new InvalidDataException($"Unsupported GGUF version {Version} (expected 2 or 3)");

            ulong tensorCount    = r.ReadUInt64();
            ulong metadataCount  = r.ReadUInt64();

            // --- Metadata ---
            Dictionary<string, GgufValue> metadata = new((int)metadataCount, StringComparer.Ordinal);
            for (ulong i = 0; i < metadataCount; i++)
            {
                string       key   = ReadGgufString(r);
                GgufValueType vtype = (GgufValueType)r.ReadUInt32();
                GgufValue    value = ReadValue(r, vtype);
                metadata[key] = value;
            }
            m_metadata = metadata.ToFrozenDictionary(StringComparer.Ordinal);

            // --- Tensor info table ---
            List<GgufTensorInfo> tensors = new((int)tensorCount);
            for (ulong i = 0; i < tensorCount; i++)
            {
                string  name   = ReadGgufString(r);
                uint    ndim   = r.ReadUInt32();
                int[]   shape  = new int[ndim];
                // GGUF stores dimensions in reverse order (last dim first for row-major)
                // but we store them as written: shape[0] is the first GGUF dimension.
                for (uint d = 0; d < ndim; d++)
                    shape[d] = (int)r.ReadUInt64();
                GgufDType dtype  = (GgufDType)r.ReadUInt32();
                ulong     offset = r.ReadUInt64();
                tensors.Add(new GgufTensorInfo(name, shape, dtype, (long)offset));
            }
            m_tensors = tensors;

            // Align to 32-byte boundary for tensor data section
            long pos     = m_stream.Position;
            long aligned = (pos + 31L) & ~31L;
            m_tensorDataOffset = aligned;

            // Build reverse-lookup index
            m_tensorIndex = new Dictionary<string, GgufTensorInfo>(tensors.Count, StringComparer.Ordinal);
            foreach (GgufTensorInfo t in tensors)
                m_tensorIndex[t.Name] = t;

            // --- Extract well-known metadata fields ---
            Architecture = GetString("general.architecture") ?? "";
            ModelName    = GetString("general.name")
                           ?? Path.GetFileNameWithoutExtension(path);

            string arch  = Architecture;
            EmbeddingLength   = GetInt($"{arch}.embedding_length");
            ContextLength     = GetInt($"{arch}.context_length");
            HeadCount         = GetInt($"{arch}.attention.head_count");
            LayerCount        = GetInt($"{arch}.block_count");
            FeedForwardLength = GetInt($"{arch}.feed_forward_length");

            // --- Tokenizer ---
            Tokens     = GetStringArray("tokenizer.ggml.tokens")   ?? [];
            Merges     = GetStringArray("tokenizer.ggml.merges")   ?? [];
            TokenTypes = GetIntArray("tokenizer.ggml.token_type")  ?? [];
            BosTokenId = GetInt("tokenizer.ggml.bos_token_id");
            EosTokenId = GetInt("tokenizer.ggml.eos_token_id");
            UnkTokenId = GetInt("tokenizer.ggml.unknown_token_id");
        }
        catch
        {
            m_stream.Dispose();
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // Tensor access
    // -----------------------------------------------------------------------

    public bool HasTensor(string name) => m_tensorIndex.ContainsKey(name);

    public GgufTensorInfo GetTensorInfo(string name)
    {
        if (!m_tensorIndex.TryGetValue(name, out GgufTensorInfo? info))
            throw new KeyNotFoundException($"Tensor '{name}' not found in GGUF file");
        return info;
    }

    /// <summary>
    /// Reads a tensor and dequantizes it to <c>float[]</c> in row-major order.
    /// Supported dtypes: F32, F16, Q4_0, Q8_0.
    /// </summary>
    public float[] ReadTensorF32(string name)
    {
        GgufTensorInfo info = GetTensorInfo(name);
        return ReadTensorF32(info);
    }

    /// <summary>
    /// Reads a 2-D tensor as a row-major matrix [rows, cols].
    /// The tensor's shape must have exactly 2 dimensions.
    /// </summary>
    public float[,] ReadTensorF32Matrix(string name)
    {
        GgufTensorInfo info = GetTensorInfo(name);
        if (info.Shape.Length != 2)
            throw new InvalidOperationException(
                $"Tensor '{name}' has {info.Shape.Length} dimensions, expected 2");

        float[] flat = ReadTensorF32(info);

        // GGUF shape is stored [cols, rows] (column-major convention from ggml).
        // shape[0] = number of columns (n_embd for weight rows)
        // shape[1] = number of rows
        int cols = info.Shape[0];
        int rows = info.Shape[1];

        float[,] matrix = new float[rows, cols];
        Buffer.BlockCopy(flat, 0, matrix, 0, flat.Length * sizeof(float));
        return matrix;
    }

    // -----------------------------------------------------------------------
    // Dispose
    // -----------------------------------------------------------------------

    public void Dispose()
    {
        if (!m_disposed)
        {
            m_disposed = true;
            m_stream.Dispose();
        }
    }

    // -----------------------------------------------------------------------
    // Private — tensor reading + dequantisation
    // -----------------------------------------------------------------------

    private float[] ReadTensorF32(GgufTensorInfo info)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        long absoluteOffset = m_tensorDataOffset + info.FileOffset;
        m_stream.Seek(absoluteOffset, SeekOrigin.Begin);

        float[] result = new float[info.ElementCount];

        switch (info.DType)
        {
            case GgufDType.F32:
                ReadF32(m_stream, result);
                break;
            case GgufDType.F16:
                ReadF16(m_stream, result);
                break;
            case GgufDType.Q8_0:
                DequantizeQ8_0(m_stream, result);
                break;
            case GgufDType.Q4_0:
                DequantizeQ4_0(m_stream, result);
                break;
            case GgufDType.BF16:
                DequantizeBF16(m_stream, result);
                break;
            case GgufDType.MXFP4:
                DequantizeMXFP4(m_stream, result);
                break;
            case GgufDType.Q2_K:
                DequantizeQ2K(m_stream, result);
                break;
            case GgufDType.Q3_K:
                DequantizeQ3K(m_stream, result);
                break;
            case GgufDType.Q4_K:
                DequantizeQ4K(m_stream, result);
                break;
            case GgufDType.Q5_K:
                DequantizeQ5K(m_stream, result);
                break;
            case GgufDType.Q6_K:
                DequantizeQ6K(m_stream, result);
                break;
            case GgufDType.Q8_K:
                DequantizeQ8K(m_stream, result);
                break;
            default:
                throw new NotSupportedException(
                    $"Dequantization of dtype {info.DType} is not yet implemented");
        }

        return result;
    }

    private static void ReadF32(Stream s, float[] dest)
    {
        byte[] buf = new byte[dest.Length * 4];
        ReadExact(s, buf);
        Buffer.BlockCopy(buf, 0, dest, 0, buf.Length);
        if (!BitConverter.IsLittleEndian)
            for (int i = 0; i < dest.Length; i++)
                dest[i] = BinaryPrimitives.ReverseEndianness(
                    BitConverter.SingleToInt32Bits(dest[i])) is int bits
                    ? BitConverter.Int32BitsToSingle(bits) : dest[i];
    }

    private static void ReadF16(Stream s, float[] dest)
    {
        int n   = dest.Length;
        byte[] buf = new byte[n * 2];
        ReadExact(s, buf);
        for (int i = 0; i < n; i++)
        {
            ushort h = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(i * 2, 2));
            dest[i]  = HalfToFloat(h);
        }
    }

    // Q8_0: blocks of 32 elements. Layout per block: [f16 scale | 32 × i8]
    private static void DequantizeQ8_0(Stream s, float[] dest)
    {
        const int BlockElements = 32;
        const int BlockBytes    = 2 + BlockElements;    // 2-byte f16 scale + 32 int8

        int blocks  = dest.Length / BlockElements;
        byte[] buf  = new byte[blocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx  = 0;
        for (int b = 0; b < blocks; b++)
        {
            float scale = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx, 2)));
            inIdx += 2;
            for (int j = 0; j < BlockElements; j++)
            {
                dest[outIdx++] = scale * (float)(sbyte)buf[inIdx++];
            }
        }
    }

    // Q4_0: blocks of 32 elements. Layout per block: [f16 scale | 16 bytes (32 nibbles)]
    // Each nibble is unsigned 0–15; subtract 8 to center at zero.
    private static void DequantizeQ4_0(Stream s, float[] dest)
    {
        const int BlockElements = 32;
        const int NibbleBytes   = BlockElements / 2;  // 16
        const int BlockBytes    = 2 + NibbleBytes;    // 18

        int blocks = dest.Length / BlockElements;
        byte[] buf = new byte[blocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx  = 0;
        for (int b = 0; b < blocks; b++)
        {
            float scale = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx, 2)));
            inIdx += 2;
            for (int j = 0; j < NibbleBytes; j++)
            {
                byte raw    = buf[inIdx++];
                int  lo     = (raw & 0x0F) - 8;
                int  hi     = (raw >> 4)   - 8;
                dest[outIdx++] = scale * lo;
                dest[outIdx++] = scale * hi;
            }
        }
    }

    // BF16: bfloat16, 2 bytes per element. Upper 16 bits of IEEE float32
    // (sign + 8-bit exponent + 7-bit mantissa). Shift left 16 to reconstruct float32.
    private static void DequantizeBF16(Stream s, float[] dest)
    {
        byte[] buf = new byte[dest.Length * 2];
        ReadExact(s, buf);

        for (int i = 0; i < dest.Length; i++)
        {
            ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(i * 2, 2));
            int bits = raw << 16;
            dest[i] = BitConverter.Int32BitsToSingle(bits);
        }
    }

    private static readonly float[] s_mxfp4E2M1 =
    [
        0.0f,  0.5f,  1.0f,  1.5f,  2.0f,  3.0f,  4.0f,  6.0f,
       -0.0f, -0.5f, -1.0f, -1.5f, -2.0f, -3.0f, -4.0f, -6.0f,
    ];

    private static void DequantizeMXFP4(Stream s, float[] dest)
    {
        const int BlockElements = 32;
        const int NibbleBytes = BlockElements / 2;
        const int BlockBytes = 1 + NibbleBytes;

        int blocks = dest.Length / BlockElements;
        byte[] buf = new byte[blocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        for (int b = 0; b < blocks; b++)
        {
            byte e8m0 = buf[inIdx++];
            float sharedScale = (e8m0 == 0) ? 0f : MathF.Pow(2f, e8m0 - 127);

            for (int j = 0; j < NibbleBytes; j++)
            {
                byte raw = buf[inIdx++];
                int lo = raw & 0x0F;
                int hi = (raw >> 4) & 0x0F;
                dest[outIdx++] = s_mxfp4E2M1[lo] * sharedScale;
                dest[outIdx++] = s_mxfp4E2M1[hi] * sharedScale;
            }
        }
    }

    // -----------------------------------------------------------------------
    // K-quant dequantisation (super-blocks of 256 elements)
    // Reference: ggml-quants.c from ggml/llama.cpp
    // -----------------------------------------------------------------------

    // Q2_K: super-block of 256 elements.
    // Layout: d (f16, 2B) + dmin (f16, 2B) + scales (16B) + qs (64B) = 84 bytes
    // Each of 16 sub-groups of 16 elements has a 4-bit scale and 4-bit min packed into scales[].
    // Each element is 2 bits in qs[].
    private static void DequantizeQ2K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 2 + 2 + 16 + 64;  // 84

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        for (int sb = 0; sb < superBlocks; sb++)
        {
            float d    = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx, 2)));
            float dmin = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx + 2, 2)));
            int scalesOff = inIdx + 4;
            int qsOff     = inIdx + 4 + 16;
            inIdx += BlockBytes;

            for (int j = 0; j < 256; j++)
            {
                int group = j / 16;
                byte scByte = buf[scalesOff + group];
                int sc  = scByte & 0x0F;
                int m   = scByte >> 4;
                int qByte = buf[qsOff + j / 4];
                int shift = (j % 4) * 2;
                int q = (qByte >> shift) & 3;
                dest[outIdx++] = d * sc * q - dmin * m;
            }
        }
    }

    // Q3_K: super-block of 256 elements.
    // Layout: hmask (32B) + qs (64B) + scales (12B) + d (f16, 2B) = 110 bytes
    // Each element is 3 bits: low 2 bits in qs[], high bit in hmask[].
    // 16 groups of 16 elements, each with a 6-bit scale (packed in 12 bytes).
    // Matches ggml dequantize_row_q3_K.
    private static void DequantizeQ3K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 32 + 64 + 12 + 2;  // 110

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        Span<int> scales = stackalloc int[16];
        for (int sb = 0; sb < superBlocks; sb++)
        {
            int hmaskOff  = inIdx;
            int qsOff     = inIdx + 32;
            int scalesOff = inIdx + 32 + 64;
            float d = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(
                buf.AsSpan(inIdx + 32 + 64 + 12, 2)));
            inIdx += BlockBytes;

            // Unpack 16 × 6-bit scales from 12 bytes (ggml packing):
            //   groups 0..7:  low 4 bits from scales[j] & 0x0F
            //   groups 8..15: low 4 bits from scales[j-8] >> 4
            //   high 2 bits for group j: from scales[8 + j/4], bits (2*(j%4))..(2*(j%4)+1)
            for (int j = 0; j < 16; j++)
            {
                int low4 = (j < 8)
                    ? (buf[scalesOff + j] & 0x0F)
                    : (buf[scalesOff + j - 8] >> 4);
                int high2 = (buf[scalesOff + 8 + j / 4] >> (2 * (j % 4))) & 3;
                scales[j] = (low4 | (high2 << 4)) - 32;
            }

            for (int j = 0; j < 256; j++)
            {
                int group = j / 16;
                int qByte = buf[qsOff + j / 4];
                int shift = (j % 4) * 2;
                int qLow = (qByte >> shift) & 3;
                int hBit = (buf[hmaskOff + j / 8] >> (j % 8)) & 1;
                int q = qLow | (hBit << 2);  // 3-bit value 0..7, centered at 4
                dest[outIdx++] = d * scales[group] * (q - 4);
            }
        }
    }

    // Q4_K: super-block of 256 elements.
    // Layout: d (f16, 2B) + dmin (f16, 2B) + scales (12B) + qs (128B) = 144 bytes
    // Each element is 4 bits in qs[].
    // 8 sub-blocks of 32 elements, each with 6-bit scale and 6-bit min packed in 12 bytes.
    private static void DequantizeQ4K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 2 + 2 + 12 + 128;  // 144

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        Span<byte> sc = stackalloc byte[8];
        Span<byte> m  = stackalloc byte[8];
        for (int sb = 0; sb < superBlocks; sb++)
        {
            float d    = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx, 2)));
            float dmin = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx + 2, 2)));
            int scalesOff = inIdx + 4;
            int qsOff     = inIdx + 4 + 12;
            inIdx += BlockBytes;

            UnpackQ4KScales(buf.AsSpan(scalesOff, 12), sc, m);

            for (int j = 0; j < 256; j++)
            {
                int subBlock = j / 32;
                int qByte = buf[qsOff + j / 2];
                int q = (j % 2 == 0) ? (qByte & 0x0F) : (qByte >> 4);
                dest[outIdx++] = d * sc[subBlock] * q - dmin * m[subBlock];
            }
        }
    }

    // Unpack 8 × 6-bit scales and 8 × 6-bit mins from 12 bytes (Q4_K / Q5_K format).
    // Matches ggml get_scale_min_k4().
    //   For sub-blocks 0..3: sc[j] = raw[j] & 63;  m[j] = raw[j+4] & 63
    //   For sub-blocks 4..7: sc[j] = (raw[j+4] & 0x0F) | ((raw[j-4] >> 6) << 4)
    //                        m[j]  = (raw[j+4] >> 4)    | ((raw[j]   >> 6) << 4)
    private static void UnpackQ4KScales(ReadOnlySpan<byte> raw, Span<byte> sc, Span<byte> m)
    {
        for (int j = 0; j < 4; j++)
        {
            sc[j] = (byte)(raw[j] & 63);
            m[j]  = (byte)(raw[j + 4] & 63);
        }
        for (int j = 4; j < 8; j++)
        {
            sc[j] = (byte)((raw[j + 4] & 0x0F) | ((raw[j - 4] >> 6) << 4));
            m[j]  = (byte)((raw[j + 4] >> 4) | ((raw[j] >> 6) << 4));
        }
    }

    // Q5_K: super-block of 256 elements.
    // Layout: d (f16, 2B) + dmin (f16, 2B) + scales (12B) + qh (32B) + qs (128B) = 176 bytes
    // Each element is 5 bits: low 4 bits in qs[], high bit in qh[].
    private static void DequantizeQ5K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 2 + 2 + 12 + 32 + 128;  // 176

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        Span<byte> sc = stackalloc byte[8];
        Span<byte> m  = stackalloc byte[8];
        for (int sb = 0; sb < superBlocks; sb++)
        {
            float d    = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx, 2)));
            float dmin = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(inIdx + 2, 2)));
            int scalesOff = inIdx + 4;
            int qhOff     = inIdx + 4 + 12;
            int qsOff     = inIdx + 4 + 12 + 32;
            inIdx += BlockBytes;

            UnpackQ4KScales(buf.AsSpan(scalesOff, 12), sc, m);

            for (int j = 0; j < 256; j++)
            {
                int subBlock = j / 32;
                int qByte = buf[qsOff + j / 2];
                int qLow = (j % 2 == 0) ? (qByte & 0x0F) : (qByte >> 4);
                int hBit = (buf[qhOff + j / 8] >> (j % 8)) & 1;
                int q = qLow | (hBit << 4);  // 5-bit value
                dest[outIdx++] = d * sc[subBlock] * q - dmin * m[subBlock];
            }
        }
    }

    // Q6_K: super-block of 256 elements.
    // Layout: ql (128B) + qh (64B) + scales (16B) + d (f16, 2B) = 210 bytes
    // Each element is 6 bits: low 4 bits in ql[], high 2 bits in qh[].
    // 16 sub-blocks of 16 elements, each with an 8-bit signed scale.
    private static void DequantizeQ6K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 128 + 64 + 16 + 2;  // 210

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        for (int sb = 0; sb < superBlocks; sb++)
        {
            int qlOff     = inIdx;
            int qhOff     = inIdx + 128;
            int scalesOff = inIdx + 128 + 64;
            float d = HalfToFloat(BinaryPrimitives.ReadUInt16LittleEndian(
                buf.AsSpan(inIdx + 128 + 64 + 16, 2)));
            inIdx += BlockBytes;

            for (int j = 0; j < 256; j++)
            {
                int group = j / 16;
                sbyte sc = (sbyte)buf[scalesOff + group];
                int qlByte = buf[qlOff + j / 2];
                int qLow = (j % 2 == 0) ? (qlByte & 0x0F) : (qlByte >> 4);
                int qhByte = buf[qhOff + j / 4];
                int shift = (j % 4) * 2;
                int qHigh = (qhByte >> shift) & 3;
                int q = qLow | (qHigh << 4);  // 6-bit value 0..63, centered at 32
                dest[outIdx++] = d * sc * (q - 32);
            }
        }
    }

    // Q8_K: super-block of 256 elements.
    // Layout: d (f32, 4B) + qs (256B, int8) + bsums (32B, 16×int16) = 292 bytes
    // Simple: each element is an int8 scaled by d.
    private static void DequantizeQ8K(Stream s, float[] dest)
    {
        const int SuperBlockElements = 256;
        const int BlockBytes = 4 + 256 + 32;  // 292

        int superBlocks = dest.Length / SuperBlockElements;
        byte[] buf = new byte[superBlocks * BlockBytes];
        ReadExact(s, buf);

        int outIdx = 0;
        int inIdx = 0;
        for (int sb = 0; sb < superBlocks; sb++)
        {
            float d = BitConverter.ToSingle(buf, inIdx);
            int qsOff = inIdx + 4;
            inIdx += BlockBytes;

            for (int j = 0; j < 256; j++)
                dest[outIdx++] = d * (sbyte)buf[qsOff + j];
        }
    }

    // IEEE 754 half-precision → single-precision conversion.
    // Uses System.Half when available (.NET 5+).
    public static float HalfToFloat(ushort h)
        => (float)(BitConverter.Int16BitsToHalf((short)h));

    private static void ReadExact(Stream s, byte[] buf)
    {
        int total   = 0;
        int remaining = buf.Length;
        while (remaining > 0)
        {
            int read = s.Read(buf, total, remaining);
            if (read == 0)
                throw new EndOfStreamException(
                    $"Unexpected end of GGUF stream: expected {buf.Length} bytes, got {total}");
            total     += read;
            remaining -= read;
        }
    }

    // -----------------------------------------------------------------------
    // Private — GGUF wire-format reading
    // -----------------------------------------------------------------------

    private static string ReadGgufString(BinaryReader r)
    {
        ulong len = r.ReadUInt64();
        if (len == 0) return string.Empty;
        byte[] bytes = r.ReadBytes((int)len);
        return Encoding.UTF8.GetString(bytes);
    }

    private static GgufValue ReadValue(BinaryReader r, GgufValueType type)
    {
        return type switch
        {
            GgufValueType.UInt8   => new GgufValue(type, r.ReadByte()),
            GgufValueType.Int8    => new GgufValue(type, r.ReadSByte()),
            GgufValueType.UInt16  => new GgufValue(type, r.ReadUInt16()),
            GgufValueType.Int16   => new GgufValue(type, r.ReadInt16()),
            GgufValueType.UInt32  => new GgufValue(type, r.ReadUInt32()),
            GgufValueType.Int32   => new GgufValue(type, r.ReadInt32()),
            GgufValueType.Float32 => new GgufValue(type, r.ReadSingle()),
            GgufValueType.Bool    => new GgufValue(type, r.ReadByte() != 0),
            GgufValueType.String  => new GgufValue(type, ReadGgufString(r)),
            GgufValueType.UInt64  => new GgufValue(type, r.ReadUInt64()),
            GgufValueType.Int64   => new GgufValue(type, r.ReadInt64()),
            GgufValueType.Float64 => new GgufValue(type, r.ReadDouble()),
            GgufValueType.Array   => ReadArrayValue(r),
            _ => throw new InvalidDataException($"Unknown GGUF value type {type}")
        };
    }

    private static GgufValue ReadArrayValue(BinaryReader r)
    {
        GgufValueType elementType = (GgufValueType)r.ReadUInt32();
        ulong         count       = r.ReadUInt64();
        List<GgufValue> items     = new((int)count);
        for (ulong i = 0; i < count; i++)
            items.Add(ReadValue(r, elementType));
        return new GgufValue(GgufValueType.Array, (IReadOnlyList<GgufValue>)items);
    }

    // -----------------------------------------------------------------------
    // Private — metadata helpers
    // -----------------------------------------------------------------------

    private string? GetString(string key)
    {
        if (m_metadata.TryGetValue(key, out GgufValue? v) && v.Type == GgufValueType.String)
            return v.AsString();
        return null;
    }

    private int GetInt(string key)
    {
        if (!m_metadata.TryGetValue(key, out GgufValue? v)) return 0;
        return v.Type switch
        {
            GgufValueType.Int32  => v.AsInt32(),
            GgufValueType.UInt32 => (int)v.AsUInt32(),
            GgufValueType.Int64  => (int)v.AsInt64(),
            GgufValueType.UInt64 => (int)v.AsUInt64(),
            _ => 0
        };
    }

    private IReadOnlyList<string>? GetStringArray(string key)
    {
        if (!m_metadata.TryGetValue(key, out GgufValue? v)) return null;
        if (v.Type != GgufValueType.Array) return null;
        IReadOnlyList<GgufValue> arr = v.AsArray();
        string[] result = new string[arr.Count];
        for (int i = 0; i < arr.Count; i++)
            result[i] = arr[i].Type == GgufValueType.String ? arr[i].AsString() : "";
        return result;
    }

    private IReadOnlyList<int>? GetIntArray(string key)
    {
        if (!m_metadata.TryGetValue(key, out GgufValue? v)) return null;
        if (v.Type != GgufValueType.Array) return null;
        IReadOnlyList<GgufValue> arr = v.AsArray();
        int[] result = new int[arr.Count];
        for (int i = 0; i < arr.Count; i++)
        {
            GgufValue item = arr[i];
            result[i] = item.Type switch
            {
                GgufValueType.Int32  => item.AsInt32(),
                GgufValueType.UInt32 => (int)item.AsUInt32(),
                GgufValueType.Int64  => (int)item.AsInt64(),
                GgufValueType.UInt64 => (int)item.AsUInt64(),
                _ => 0
            };
        }
        return result;
    }
}
