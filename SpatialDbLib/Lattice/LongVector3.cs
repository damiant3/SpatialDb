namespace SpatialDbLib.Lattice;

public readonly struct LongVector3(long x, long y, long z)
{
    public readonly long X = x;
    public readonly long Y = y;
    public readonly long Z = z;

    public LongVector3(long xyz) : this(xyz, xyz, xyz) { }

    public ULongVector3 OffsetFrom(LongVector3 min)
    => new(
        (ulong)(X - min.X),
        (ulong)(Y - min.Y),
        (ulong)(Z - min.Z)
    );

    public long MaxComponentAbs()
        => Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));

    public long SumAbs()
        => Math.Abs(X) + Math.Abs(Y) + Math.Abs(Z);

    public override string ToString() => $"({X}, {Y}, {Z})";

    public override bool Equals(object? obj)
        => obj is LongVector3 o && this == o;

    public override int GetHashCode()
        => HashCode.Combine(X, Y, Z);

    public static bool operator ==(LongVector3 a, LongVector3 b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    public static bool operator !=(LongVector3 a, LongVector3 b)
        => !(a == b);

    public static LongVector3 operator +(LongVector3 a, long b)
        => new(a.X + b, a.Y + b, a.Z + b);
    public static LongVector3 operator +(LongVector3 a, LongVector3 b)
        => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static LongVector3 operator -(LongVector3 a, LongVector3 b)
        => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static LongVector3 operator *(LongVector3 a, long factor)
        => new(a.X * factor, a.Y * factor, a.Z * factor);

    public static LongVector3 operator /(LongVector3 a, long divisor)
        => new(a.X / divisor, a.Y / divisor, a.Z / divisor);

    public static LongVector3 operator +(LongVector3 position, IntVector3 velocity)
        => new(position.X + velocity.X, position.Y + velocity.Y, position.Z + velocity.Z);

    public static LongVector3 operator +(LongVector3 position, ShortVector3 velocity)
        => new(position.X + velocity.X, position.Y + velocity.Y, position.Z + velocity.Z);

    public static long Midpoint(long min, long max)
        => (min & max) + ((min ^ max) >> 1);

    public static LongVector3 Midpoint(LongVector3 min, LongVector3 max)
        => new(
            Midpoint(min.X, max.X),
            Midpoint(min.Y, max.Y),
            Midpoint(min.Z, max.Z)
        );

    public static readonly LongVector3 Zero = new(0);
}

public readonly struct ULongVector3(ulong x, ulong y, ulong z)
{
    public readonly ulong X = x;
    public readonly ulong Y = y;
    public readonly ulong Z = z;

    public ULongVector3(ulong xyz) : this(xyz, xyz, xyz) { }

    public override string ToString() => $"({X}, {Y}, {Z})";

    public override bool Equals(object? obj)
        => obj is ULongVector3 o && this == o;

    public override int GetHashCode()
        => HashCode.Combine(X, Y, Z);

    public static bool operator ==(ULongVector3 a, ULongVector3 b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    public static bool operator !=(ULongVector3 a, ULongVector3 b)
        => !(a == b);

    public static ULongVector3 operator +(ULongVector3 a, ULongVector3 b)
        => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static ULongVector3 operator -(ULongVector3 a, ULongVector3 b)
        => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static ULongVector3 operator *(ULongVector3 a, ulong factor)
        => new(a.X * factor, a.Y * factor, a.Z * factor);

    public static ULongVector3 operator /(ULongVector3 a, ulong divisor)
        => new(a.X / divisor, a.Y / divisor, a.Z / divisor);
}

public class LongVector3Comparer : IComparer<LongVector3>
{
    public int Compare(LongVector3 a, LongVector3 b)
    {
        int cmp = a.X.CompareTo(b.X);
        if (cmp != 0) return cmp;

        cmp = a.Y.CompareTo(b.Y);
        if (cmp != 0) return cmp;

        return a.Z.CompareTo(b.Z);
    }
}

sealed class OctantComparer(LongVector3 midPoint)
    : IComparer<LongVector3>
{
    readonly LongVector3 m_mid = midPoint;

    public int Compare(LongVector3 a, LongVector3 b)
    {
        byte xIdx = (byte)(
            ((a.X >= m_mid.X) ? 4 : 0) |
            ((a.Y >= m_mid.Y) ? 2 : 0) |
            ((a.Z >= m_mid.Z) ? 1 : 0));

        byte yIdx = (byte)(
            ((b.X >= m_mid.X) ? 4 : 0) |
            ((b.Y >= m_mid.Y) ? 2 : 0) |
            ((b.Z >= m_mid.Z) ? 1 : 0));

        return xIdx.CompareTo(yIdx);
    }
}
public readonly struct ShortVector3(short x, short y, short z)
{
    public readonly short X = x;
    public readonly short Y = y;
    public readonly short Z = z;

    public ShortVector3(short xyz) : this(xyz, xyz, xyz) { }

    public LongVector3 ToLong() => new(X, Y, Z);
    public IntVector3 ToInt() => new(X, Y, Z);

    public static ShortVector3 operator +(ShortVector3 a, ShortVector3 b)
        => new((short)(a.X + b.X), (short)(a.Y + b.Y), (short)(a.Z + b.Z));

    public static ShortVector3 operator -(ShortVector3 a, ShortVector3 b)
        => new((short)(a.X - b.X), (short)(a.Y - b.Y), (short)(a.Z - b.Z));

    public static ShortVector3 operator *(ShortVector3 a, short factor)
        => new((short)(a.X * factor), (short)(a.Y * factor), (short)(a.Z * factor));

    public static ShortVector3 operator /(ShortVector3 a, short divisor)
        => new((short)(a.X / divisor), (short)(a.Y / divisor), (short)(a.Z / divisor));

    public static ShortVector3 operator -(ShortVector3 a)
        => new((short)-a.X, (short)-a.Y, (short)-a.Z);

    // For accumulator overflow: promote to int during calc
    public static LongVector3 operator +(LongVector3 position, ShortVector3 velocity)
        => new(position.X + velocity.X, position.Y + velocity.Y, position.Z + velocity.Z);

    public bool IsZero => X == 0 && Y == 0 && Z == 0;

    public int MaxComponentAbs()
        => Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));

    public int SumAbs()
        => Math.Abs(X) + Math.Abs(Y) + Math.Abs(Z);

    public override string ToString() => $"({X}, {Y}, {Z})";

    public override bool Equals(object? obj)
        => obj is ShortVector3 o && this == o;

    public override int GetHashCode()
        => HashCode.Combine(X, Y, Z);

    public static bool operator ==(ShortVector3 a, ShortVector3 b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    public static bool operator !=(ShortVector3 a, ShortVector3 b)
        => !(a == b);

    public static readonly ShortVector3 Zero = new(0);
}

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
        => Math.Max(X, Math.Max(Y, Z));

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