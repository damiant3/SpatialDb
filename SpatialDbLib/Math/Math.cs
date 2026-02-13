using System.Numerics;
////////////////////////////
namespace SpatialDbLib.Math;
public static class LatticeUniverse
{
    public const int RootBits = 63;
    public const long HalfExtent = 1L << RootBits - 1;
    public static readonly Region RootRegion =
        new(new LongVector3(-HalfExtent, -HalfExtent, -HalfExtent),
            new LongVector3(+HalfExtent, +HalfExtent, +HalfExtent));
}
public interface ILatticeCoordinateTransform
{
    LongVector3 OuterToInnerCanonical(LongVector3 outerPosition);
    LongVector3 OuterToInnerInsertion(LongVector3 outerPosition, Guid guid);
    LongVector3 InnerToOuter(LongVector3 innerPosition);
    IntVector3 OuterToInnerVelocity(IntVector3 outerVelocity);
    IntVector3 InnerToOuterVelocity(IntVector3 innerVelocity);
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
    public IntVector3 OuterToInnerVelocity(IntVector3 outerVelocity)
    {
        var innerSize = InnerLatticeBounds.Size;
        var outerSize = OuterLatticeBounds.Size;
        if (innerSize == outerSize)
            return outerVelocity;
        var scaleX = innerSize.X / outerSize.X;
        var scaleY = innerSize.Y / outerSize.Y;
        var scaleZ = innerSize.Z / outerSize.Z;
        return new IntVector3(
            (int)(outerVelocity.X * (long)scaleX),
            (int)(outerVelocity.Y * (long)scaleY),
            (int)(outerVelocity.Z * (long)scaleZ));
    }
    public IntVector3 InnerToOuterVelocity(IntVector3 innerVelocity)
    {
        var innerSize = InnerLatticeBounds.Size;
        var outerSize = OuterLatticeBounds.Size;
        if (innerSize == outerSize)
            return innerVelocity;
        var scaleX = innerSize.X / outerSize.X;
        var scaleY = innerSize.Y / outerSize.Y;
        var scaleZ = innerSize.Z / outerSize.Z;
        return new IntVector3(
            (int)(innerVelocity.X / (long)scaleX),
            (int)(innerVelocity.Y / (long)scaleY),
            (int)(innerVelocity.Z / (long)scaleZ));
    }
    public LongVector3 OuterToInnerInsertion(LongVector3 outer, Guid guid)
    {
        if (!ContainsOuter(outer)) throw new ArgumentOutOfRangeException(nameof(outer));
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
            z = (z ^ z >> 30) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ z >> 27) * 0x94D049BB133111EBUL;
            return z ^ z >> 31;
        }
        static LongVector3 OuterToInnerFromSizeOne(Guid g)
        {
            Span<byte> bytes = stackalloc byte[16];
            g.TryWriteBytes(bytes);
            ulong s0 = BitConverter.ToUInt64(bytes[..8]);
            ulong s1 = BitConverter.ToUInt64(bytes[8..]);
            ulong state = s0 ^ s1 * 0x9E3779B97F4A7C15UL;
            long half = LatticeUniverse.HalfExtent;
            long NextCoord()
            {
                ulong r = SplitMix64(ref state);
                ulong u63 = r >> 1;
                return (long)u63 - half;
            }
            return new LongVector3(NextCoord(), NextCoord(), NextCoord());
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
        var repX = baseX + discriminator % scaleX;
        var repY = baseY + (discriminator >> 21) % scaleY;
        var repZ = baseZ + (discriminator >> 42) % scaleZ;
        return new LongVector3(unchecked((long)repX),unchecked((long)repY),unchecked((long)repZ));
    }
    public LongVector3 OuterToInnerCanonical(LongVector3 outerPosition)
    {
        if (!ContainsOuter(outerPosition)) throw new ArgumentOutOfRangeException(nameof(outerPosition));
        if (OuterLatticeBounds.Size == InnerLatticeBounds.Size)
            return outerPosition;
        var outerOffset = outerPosition.OffsetFrom(OuterLatticeBounds.Min);
        var outerSize = OuterLatticeBounds.Size;
        var innerSize = InnerLatticeBounds.Size;
        return new LongVector3(
            InnerLatticeBounds.Min.X + (long)(outerOffset.X * (innerSize.X / outerSize.X)),
            InnerLatticeBounds.Min.Y + (long)(outerOffset.Y * (innerSize.Y / outerSize.Y)),
            InnerLatticeBounds.Min.Z + (long)(outerOffset.Z * (innerSize.Z / outerSize.Z)));
    }
    public LongVector3 InnerToOuter(LongVector3 innerPosition)
    {
        if (!ContainsInner(innerPosition)) throw new ArgumentOutOfRangeException(nameof(innerPosition));
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
            OuterLatticeBounds.Min.Z + (long)(innerOffset.Z / scaleZ));
    }
}
public readonly struct Region
{
    public Region(LongVector3 min, LongVector3 max)
    {
        if (min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z)
            throw new ArgumentException("Invalid region bounds for axis alignment");
        Min = min;
        Max = max;
        Mid = LongVector3.Midpoint(min, max);
    }
    public LongVector3 Min { get; }
    public LongVector3 Max { get; }
    public LongVector3 Mid { get; }
    public ULongVector3 Size =>
        new((ulong)(Max.X - Min.X),
            (ulong)(Max.Y - Min.Y),
            (ulong)(Max.Z - Min.Z));
    public bool Contains(LongVector3 position)
        => position.X >= Min.X && position.X < Max.X
        && position.Y >= Min.Y && position.Y < Max.Y
        && position.Z >= Min.Z && position.Z < Max.Z;
    public bool IntersectsSphere_SimpleImpl(LongVector3 center, ulong radius)
    {   // there is probably a better way to do this, but hey its 2026 and we vibing, baby.
        var closest = new LongVector3(
            System.Math.Max(Min.X, System.Math.Min(center.X, Max.X)),
            System.Math.Max(Min.Y, System.Math.Min(center.Y, Max.Y)),
            System.Math.Max(Min.Z, System.Math.Min(center.Z, Max.Z)));
        var distSq = (closest - center).MagnitudeSquaredBig;
        return distSq <= (BigInteger)radius * radius;
    }
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