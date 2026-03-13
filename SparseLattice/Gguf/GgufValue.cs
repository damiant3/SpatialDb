/////////////////////////////
namespace SparseLattice.Gguf;

public sealed class GgufValue
{
    public GgufValueType Type { get; }

    readonly object m_value;

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

    public override string ToString() => m_value?.ToString() ?? "(null)";

    InvalidOperationException Mismatch(string expected)
        => new($"GgufValue type mismatch: expected {expected}, actual {Type}");
}
