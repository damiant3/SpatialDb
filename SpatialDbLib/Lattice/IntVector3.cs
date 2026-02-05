
namespace SpatialDbLib.Lattice;

public readonly struct IntVector3(int x, int y, int z)
{
    public readonly int X = x;
    public readonly int Y = y;
    public readonly int Z = z;

    public IntVector3(int xyz) : this(xyz, xyz, xyz) { }

    // Convert up to LongVector3 for position math
    public LongVector3 ToLong() => new(X, Y, Z);

    // Movement: apply velocity to a position
    public static LongVector3 operator +(IntVector3 velocity, LongVector3 position)
        => new(position.X + velocity.X, position.Y + velocity.Y, position.Z + velocity.Z);

    // Acceleration math stays in IntVector3 space
    public static IntVector3 operator +(IntVector3 a, IntVector3 b)
        => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static IntVector3 operator -(IntVector3 a, IntVector3 b)
        => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static IntVector3 operator *(IntVector3 a, int factor)
        => new(a.X * factor, a.Y * factor, a.Z * factor);

    public static IntVector3 operator /(IntVector3 a, int divisor)
        => new(a.X / divisor, a.Y / divisor, a.Z / divisor);

    // Unary minus for reversing direction
    public static IntVector3 operator -(IntVector3 a)
        => new(-a.X, -a.Y, -a.Z);

    // Acceleration: scale by delta-time if you ever need fractional ticks
    // Uses long to avoid overflow during intermediate calc
    public IntVector3 ScaledBy(int numerator, int denominator)
        => new(
            (int)((long)X * numerator / denominator),
            (int)((long)Y * numerator / denominator),
            (int)((long)Z * numerator / denominator));

    // Movement queries
    public bool IsZero => X == 0 && Y == 0 && Z == 0;

    public long MagnitudeSquared => (long)X * X + (long)Y * Y + (long)Z * Z;

    public int MaxComponentAbs()
        => Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));

    public int SumAbs()
        => Math.Abs(X) + Math.Abs(Y) + Math.Abs(Z);

    public override string ToString() => $"({X}, {Y}, {Z})";

    public override bool Equals(object? obj)
        => obj is IntVector3 o && this == o;

    public override int GetHashCode()
        => HashCode.Combine(X, Y, Z);

    public static bool operator ==(IntVector3 a, IntVector3 b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    public static bool operator !=(IntVector3 a, IntVector3 b)
        => !(a == b);

    public static readonly IntVector3 Zero = new(0);
}

public readonly struct UIntVector3(uint x, uint y, uint z)
{
    public readonly uint X = x;
    public readonly uint Y = y;
    public readonly uint Z = z;

    public UIntVector3(uint xyz) : this(xyz, xyz, xyz) { }

    public ULongVector3 ToULong() => new(X, Y, Z);

    public static UIntVector3 operator +(UIntVector3 a, UIntVector3 b)
        => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static UIntVector3 operator -(UIntVector3 a, UIntVector3 b)
        => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static UIntVector3 operator *(UIntVector3 a, uint factor)
        => new(a.X * factor, a.Y * factor, a.Z * factor);

    public static UIntVector3 operator /(UIntVector3 a, uint divisor)
        => new(a.X / divisor, a.Y / divisor, a.Z / divisor);

    public static UIntVector3 operator +(LongVector3 position, UIntVector3 offset)
        => new((uint)(position.X + offset.X), (uint)(position.Y + offset.Y), (uint)(position.Z + offset.Z));

    public bool IsZero => X == 0 && Y == 0 && Z == 0;

    public ulong MagnitudeSquared => (ulong)X * X + (ulong)Y * Y + (ulong)Z * Z;

    public uint MaxComponentAbs()
        => Math.Max(X, Math.Max(Y, Z));

    public uint SumAbs()
        => X + Y + Z;

    public override string ToString() => $"({X}, {Y}, {Z})";

    public override bool Equals(object? obj)
        => obj is UIntVector3 o && this == o;

    public override int GetHashCode()
        => HashCode.Combine(X, Y, Z);

    public static bool operator ==(UIntVector3 a, UIntVector3 b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    public static bool operator !=(UIntVector3 a, UIntVector3 b)
        => !(a == b);

    public static readonly UIntVector3 Zero = new(0);
}
