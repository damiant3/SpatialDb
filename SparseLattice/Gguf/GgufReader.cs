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
    BF16 = 30,
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
