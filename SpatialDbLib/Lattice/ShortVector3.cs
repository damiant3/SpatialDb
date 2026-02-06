namespace SpatialDbLib.Lattice;

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
    public static LongVector3 operator +(ShortVector3 velocity, LongVector3 position)
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
