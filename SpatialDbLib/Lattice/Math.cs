///////////////////////////////
namespace SpatialDbLib.Lattice;

public static class LatticeUniverse
{
    public const int RootBits = 63;
    public const long HalfExtent = 1L << (RootBits - 1);

    public static readonly Region RootRegion =
        new(
            new LongVector3(-HalfExtent, -HalfExtent, -HalfExtent),
            new LongVector3(+HalfExtent, +HalfExtent, +HalfExtent)
        );
}

public interface ILatticeCoordinateTransform
{
    LongVector3 OuterToInnerCanonical(LongVector3 outerPosition);
    LongVector3 OuterToInnerInsertion(LongVector3 outerPosition, Guid guid);
    LongVector3 InnerToOuter(LongVector3 innerPosition);
    bool ContainsOuter(LongVector3 outerPosition);
    bool ContainsInner(LongVector3 innerPosition);
}

public class ParentToSubLatticeTransform(Region outerBounds)
    : ILatticeCoordinateTransform
{
    public Region OuterLatticeBounds { get; } = outerBounds;
    public static Region InnerLatticeBounds => LatticeUniverse.RootRegion;

    public bool ContainsOuter(LongVector3 outerPosition)
        => OuterLatticeBounds.Contains(outerPosition);

    public bool ContainsInner(LongVector3 innerPosition)
        => InnerLatticeBounds.Contains(innerPosition);

    public LongVector3 OuterToInnerInsertion(LongVector3 outer, Guid guid)
    {
#if DEBUG
        if (!ContainsOuter(outer))
            throw new ArgumentOutOfRangeException(nameof(outer));
#endif
        var innerSize = InnerLatticeBounds.Size;
        var outerSize = OuterLatticeBounds.Size;

        if (innerSize == outerSize)
            return outer;

        return outerSize.X == 1
            ? OuterToInnerFromSizeOne(guid)
            : OuterToInnerFromLarge(outer, guid.GetDiscriminator(), innerSize, outerSize);

        static ulong SplitMix64(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static LongVector3 OuterToInnerFromSizeOne(Guid g)
        {
            // get 128 bits from the guid
            Span<byte> bytes = stackalloc byte[16];
            g.TryWriteBytes(bytes);

            ulong s0 = BitConverter.ToUInt64(bytes[..8]);
            ulong s1 = BitConverter.ToUInt64(bytes[8..]);

            // combine into a 64-bit seed with full avalanche
            ulong state = s0 ^ (s1 * 0x9E3779B97F4A7C15UL);

            long half = LatticeUniverse.HalfExtent;

            long NextCoord()
            {
                // 64 random bits
                ulong r = SplitMix64(ref state);

                // keep 63 bits -> uniform in [0, 2^63)
                ulong u63 = r >> 1;

                // reinterpret as signed offset in [-2^62, +2^62)
                return (long)u63 - half;
            }

            return new LongVector3(
                NextCoord(),
                NextCoord(),
                NextCoord()
            );
        }
    }

    public LongVector3 OuterToInnerFromLarge(LongVector3 outer, ulong discriminator, ULongVector3 innerSize, ULongVector3 outerSize)
    {
        var outerOffset = outer.OffsetFrom(OuterLatticeBounds.Min);

        var scaleX = innerSize.X / outerSize.X;
        var scaleY = innerSize.Y / outerSize.Y;
        var scaleZ = innerSize.Z / outerSize.Z;

        var baseX = (ulong)InnerLatticeBounds.Min.X + outerOffset.X * scaleX;
        var baseY = (ulong)InnerLatticeBounds.Min.Y + outerOffset.Y * scaleY;
        var baseZ = (ulong)InnerLatticeBounds.Min.Z + outerOffset.Z * scaleZ;

        var repX = baseX + (discriminator % scaleX);
        var repY = baseY + ((discriminator >> 21) % scaleY);
        var repZ = baseZ + ((discriminator >> 42) % scaleZ);

        return new LongVector3(
            unchecked((long)repX),
            unchecked((long)repY),
            unchecked((long)repZ)
        );
    }

    public LongVector3 OuterToInnerCanonical(LongVector3 outerPosition)
    {
#if DEBUG
        if (!ContainsOuter(outerPosition))
            throw new ArgumentOutOfRangeException(nameof(outerPosition));
#endif
        if (OuterLatticeBounds.Size == InnerLatticeBounds.Size)
            return outerPosition;

        var outerOffset = outerPosition.OffsetFrom(OuterLatticeBounds.Min);
        var outerSize = OuterLatticeBounds.Size;
        var innerSize = InnerLatticeBounds.Size;

        return new LongVector3(
            InnerLatticeBounds.Min.X +
                (long)(outerOffset.X * (innerSize.X / outerSize.X)),

            InnerLatticeBounds.Min.Y +
                (long)(outerOffset.Y * (innerSize.Y / outerSize.Y)),

            InnerLatticeBounds.Min.Z +
                (long)(outerOffset.Z * (innerSize.Z / outerSize.Z))
        );
    }

    public LongVector3 InnerToOuter(LongVector3 innerPosition)
    {
#if DEBUG
        if (!ContainsInner(innerPosition))
            throw new ArgumentOutOfRangeException(nameof(innerPosition));
#endif

        if (OuterLatticeBounds.Size == InnerLatticeBounds.Size)
            return innerPosition;

        var innerOffset = innerPosition.OffsetFrom(InnerLatticeBounds.Min);
        var outerSize = OuterLatticeBounds.Size;
        var innerSize = InnerLatticeBounds.Size;

        var scaleX = innerSize.X / outerSize.X;
        var scaleY = innerSize.Y / outerSize.Y;
        var scaleZ = innerSize.Z / outerSize.Z;

        return new LongVector3(
            OuterLatticeBounds.Min.X + (long)(innerOffset.X / scaleX),
            OuterLatticeBounds.Min.Y + (long)(innerOffset.Y / scaleY),
            OuterLatticeBounds.Min.Z + (long)(innerOffset.Z / scaleZ)
        );
    }
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

    public static LongVector3 operator+(LongVector3 a, long b)
        => new(a.X + b, a.Y + b, a.Z + b);
    public static LongVector3 operator +(LongVector3 a, LongVector3 b)
        => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static LongVector3 operator -(LongVector3 a, LongVector3 b)
        => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static LongVector3 operator *(LongVector3 a, long factor)
        => new(a.X * factor, a.Y * factor, a.Z * factor);

    public static LongVector3 operator /(LongVector3 a, long divisor)
        => new(a.X / divisor, a.Y / divisor, a.Z / divisor);

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

public readonly struct Region
{
    public Region(LongVector3 min, LongVector3 max)
    {
#if DEBUG
        if (min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
            throw new ArgumentException("Invalid region bounds for axis alignment");
#endif
        Min = min;
        Max = max;
        Mid = LongVector3.Midpoint(min, max);
    }

    public LongVector3 Min { get; }
    public LongVector3 Max { get; }
    public LongVector3 Mid { get; }
    public ULongVector3 Size =>
        new(
            (ulong)(Max.X - Min.X),
            (ulong)(Max.Y - Min.Y),
            (ulong)(Max.Z - Min.Z)
        );

    public bool Contains(LongVector3 position)
        => position.X >= Min.X && position.X < Max.X
        && position.Y >= Min.Y && position.Y < Max.Y
        && position.Z >= Min.Z && position.Z < Max.Z;

    public override string ToString() => $"Min={Min}, Max={Max}";
}

public static class GuidExtensions
{
    public static ulong GetDiscriminator(this Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }
}