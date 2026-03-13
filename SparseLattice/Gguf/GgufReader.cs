using System.Collections.Frozen;
using System.Text;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

/// <summary>
/// Reads a GGUF model file, exposes metadata key-value pairs and tensor information,
/// and can dequantize tensors to <c>float[]</c>.
/// </summary>
/// <remarks>
/// Supported quantization formats: F32, F16, BF16, MXFP4, Q4_0, Q8_0, Q2_K–Q8_K.
/// The file is kept open via <see cref="FileStream"/> until <see cref="Dispose"/> is called.
/// Tensor data is read on demand.
/// </remarks>
public sealed class GgufReader : IDisposable
{
    static readonly uint s_magic = 0x46554747u;

    readonly FileStream          m_stream;
    readonly long                m_tensorDataOffset;
    readonly IReadOnlyDictionary<string, GgufValue>  m_metadata;
    readonly IReadOnlyList<GgufTensorInfo>            m_tensors;
    readonly Dictionary<string, GgufTensorInfo>      m_tensorIndex;

    bool m_disposed;
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

    public static GgufReader Open(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("GGUF file not found", path);

        return new GgufReader(path);
    }

    GgufReader(string path)
    {
        m_stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65536, useAsync: false);

        try
        {
            using BinaryReader r = new(m_stream, Encoding.UTF8, leaveOpen: true);

            uint magic = r.ReadUInt32();
            if (magic != s_magic)
                throw new InvalidDataException(
                    $"Not a GGUF file: expected magic 0x{s_magic:X8}, got 0x{magic:X8}");

            Version = r.ReadUInt32();
            if (Version is not (2 or 3))
                throw new InvalidDataException($"Unsupported GGUF version {Version} (expected 2 or 3)");

            ulong tensorCount    = r.ReadUInt64();
            ulong metadataCount  = r.ReadUInt64();

            Dictionary<string, GgufValue> metadata = new((int)metadataCount, StringComparer.Ordinal);
            for (ulong i = 0; i < metadataCount; i++)
            {
                string       key   = ReadGgufString(r);
                GgufValueType vtype = (GgufValueType)r.ReadUInt32();
                GgufValue    value = ReadValue(r, vtype);
                metadata[key] = value;
            }
            m_metadata = metadata.ToFrozenDictionary(StringComparer.Ordinal);

            List<GgufTensorInfo> tensors = new((int)tensorCount);
            for (ulong i = 0; i < tensorCount; i++)
            {
                string  name   = ReadGgufString(r);
                uint    ndim   = r.ReadUInt32();
                int[]   shape  = new int[ndim];
                for (uint d = 0; d < ndim; d++)
                    shape[d] = (int)r.ReadUInt64();
                GgufDType dtype  = (GgufDType)r.ReadUInt32();
                ulong     offset = r.ReadUInt64();
                tensors.Add(new GgufTensorInfo(name, shape, dtype, (long)offset));
            }
            m_tensors = tensors;

            long pos     = m_stream.Position;
            long aligned = (pos + 31L) & ~31L;
            m_tensorDataOffset = aligned;

            m_tensorIndex = new Dictionary<string, GgufTensorInfo>(tensors.Count, StringComparer.Ordinal);
            foreach (GgufTensorInfo t in tensors)
                m_tensorIndex[t.Name] = t;

            string arch  = GetString("general.architecture") ?? "";
            Architecture = arch;
            ModelName    = GetString("general.name")
                           ?? Path.GetFileNameWithoutExtension(path);

            EmbeddingLength   = GetInt($"{arch}.embedding_length");
            ContextLength     = GetInt($"{arch}.context_length");
            HeadCount         = GetInt($"{arch}.attention.head_count");
            LayerCount        = GetInt($"{arch}.block_count");
            FeedForwardLength = GetInt($"{arch}.feed_forward_length");

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

    public bool HasTensor(string name) => m_tensorIndex.ContainsKey(name);

    public GgufTensorInfo GetTensorInfo(string name)
    {
        if (!m_tensorIndex.TryGetValue(name, out GgufTensorInfo? info))
            throw new KeyNotFoundException($"Tensor '{name}' not found in GGUF file");
        return info;
    }

    public float[] ReadTensorF32(string name)
    {
        GgufTensorInfo info = GetTensorInfo(name);
        return ReadTensorF32(info);
    }

    public float[,] ReadTensorF32Matrix(string name)
    {
        GgufTensorInfo info = GetTensorInfo(name);
        if (info.Shape.Length != 2)
            throw new InvalidOperationException(
                $"Tensor '{name}' has {info.Shape.Length} dimensions, expected 2");

        float[] flat = ReadTensorF32(info);

        int cols = info.Shape[0];
        int rows = info.Shape[1];

        float[,] matrix = new float[rows, cols];
        Buffer.BlockCopy(flat, 0, matrix, 0, flat.Length * sizeof(float));
        return matrix;
    }

    public void Dispose()
    {
        if (!m_disposed)
        {
            m_disposed = true;
            m_stream.Dispose();
        }
    }
    float[] ReadTensorF32(GgufTensorInfo info)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);

        long absoluteOffset = m_tensorDataOffset + info.FileOffset;
        m_stream.Seek(absoluteOffset, SeekOrigin.Begin);

        float[] result = new float[info.ElementCount];

        switch (info.DType)
        {
            case GgufDType.F32:   GgufDequantizer.ReadF32(m_stream, result);           break;
            case GgufDType.F16:   GgufDequantizer.ReadF16(m_stream, result);           break;
            case GgufDType.BF16:  GgufDequantizer.DequantizeBF16(m_stream, result);    break;
            case GgufDType.MXFP4: GgufDequantizer.DequantizeMXFP4(m_stream, result);   break;
            case GgufDType.Q8_0:  GgufDequantizer.DequantizeQ8_0(m_stream, result);    break;
            case GgufDType.Q4_0:  GgufDequantizer.DequantizeQ4_0(m_stream, result);    break;
            case GgufDType.Q2_K:  GgufDequantizer.DequantizeQ2K(m_stream, result);     break;
            case GgufDType.Q3_K:  GgufDequantizer.DequantizeQ3K(m_stream, result);     break;
            case GgufDType.Q4_K:  GgufDequantizer.DequantizeQ4K(m_stream, result);     break;
            case GgufDType.Q5_K:  GgufDequantizer.DequantizeQ5K(m_stream, result);     break;
            case GgufDType.Q6_K:  GgufDequantizer.DequantizeQ6K(m_stream, result);     break;
            case GgufDType.Q8_K:  GgufDequantizer.DequantizeQ8K(m_stream, result);     break;
            default:
                throw new NotSupportedException(
                    $"Dequantization of dtype {info.DType} is not yet implemented");
        }

        return result;
    }

    public static float HalfToFloat(ushort h)
        => GgufDequantizer.HalfToFloat(h);

    static string ReadGgufString(BinaryReader r)
    {
        ulong len = r.ReadUInt64();
        if (len == 0) return "";
        byte[] bytes = r.ReadBytes((int)len);
        return Encoding.UTF8.GetString(bytes);
    }

    static GgufValue ReadValue(BinaryReader r, GgufValueType type)
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

    static GgufValue ReadArrayValue(BinaryReader r)
    {
        GgufValueType elementType = (GgufValueType)r.ReadUInt32();
        ulong         count       = r.ReadUInt64();
        List<GgufValue> items     = new((int)count);
        for (ulong i = 0; i < count; i++)
            items.Add(ReadValue(r, elementType));
        return new GgufValue(GgufValueType.Array, (IReadOnlyList<GgufValue>)items);
    }

    string? GetString(string key)
    {
        if (m_metadata.TryGetValue(key, out GgufValue? v) && v.Type == GgufValueType.String)
            return v.AsString();
        return null;
    }

    int GetInt(string key)
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

    IReadOnlyList<string>? GetStringArray(string key)
    {
        if (!m_metadata.TryGetValue(key, out GgufValue? v)) return null;
        if (v.Type != GgufValueType.Array) return null;
        IReadOnlyList<GgufValue> arr = v.AsArray();
        string[] result = new string[arr.Count];
        for (int i = 0; i < arr.Count; i++)
            result[i] = arr[i].Type == GgufValueType.String ? arr[i].AsString() : "";
        return result;
    }

    IReadOnlyList<int>? GetIntArray(string key)
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
