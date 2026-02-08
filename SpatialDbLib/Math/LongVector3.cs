using SystemMath = System.Math;
////////////////////////////
namespace SpatialDbLib.Math;

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
        => SystemMath.Max(SystemMath.Abs(X), SystemMath.Max(SystemMath.Abs(Y), SystemMath.Abs(Z)));

    public long SumAbs()
        => SystemMath.Abs(X) + SystemMath.Abs(Y) + SystemMath.Abs(Z);

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

    internal int DistanceTo(LongVector3 other)
    {
        long dx = X - other.X;
        long dy = Y - other.Y;
        long dz = Z - other.Z;
        return (int)SystemMath.Sqrt(dx * dx + dy * dy + dz * dz);
    }

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
