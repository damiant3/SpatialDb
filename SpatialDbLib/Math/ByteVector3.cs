using SystemMath = System.Math;
////////////////////////////
namespace SpatialDbLib.Math;

public readonly struct ByteVector3(byte x, byte y, byte z)
{
    public readonly byte X = x;
    public readonly byte Y = y;
    public readonly byte Z = z;

    public ByteVector3(byte xyz) : this(xyz, xyz, xyz) { }

    public ShortVector3 ToShort() => new(X, Y, Z);
    public IntVector3 ToInt() => new(X, Y, Z);
    public LongVector3 ToLong() => new(X, Y, Z);

    public static ByteVector3 operator +(ByteVector3 a, ByteVector3 b)
        => new((byte)(a.X + b.X), (byte)(a.Y + b.Y), (byte)(a.Z + b.Z));

    public static ByteVector3 operator -(ByteVector3 a, ByteVector3 b)
        => new((byte)(a.X - b.X), (byte)(a.Y - b.Y), (byte)(a.Z - b.Z));

    public static ByteVector3 operator *(ByteVector3 a, byte factor)
        => new((byte)(a.X * factor), (byte)(a.Y * factor), (byte)(a.Z * factor));

    public static ByteVector3 operator /(ByteVector3 a, byte divisor)
        => new((byte)(a.X / divisor), (byte)(a.Y / divisor), (byte)(a.Z / divisor));

    public bool IsZero => X == 0 && Y == 0 && Z == 0;

    public byte MaxComponentAbs()
        => SystemMath.Max(X, SystemMath.Max(Y, Z));

    public int SumAbs()
        => X + Y + Z;

    public override string ToString() => $"({X}, {Y}, {Z})";

    public override bool Equals(object? obj)
        => obj is ByteVector3 o && this == o;

    public override int GetHashCode()
        => HashCode.Combine(X, Y, Z);

    public static bool operator ==(ByteVector3 a, ByteVector3 b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    public static bool operator !=(ByteVector3 a, ByteVector3 b)
        => !(a == b);

    public static readonly ByteVector3 Zero = new(0);
}
