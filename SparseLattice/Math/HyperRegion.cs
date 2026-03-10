using System.Numerics;
///////////////////////////////
namespace SparseLattice.Math;

public readonly struct HyperRegion : IEquatable<HyperRegion>
{
    public LongVectorN Min { get; }
    public LongVectorN Max { get; }
    public int Dimensions => Min.Dimensions;

    public HyperRegion(LongVectorN min, LongVectorN max)
    {
        if (min.Dimensions != max.Dimensions)
            throw new InvalidOperationException(
                $"Dimension mismatch: min={min.Dimensions}, max={max.Dimensions}");
        Min = min;
        Max = max;
    }

    public long MidpointAt(int dimension)
        => LongVectorN.Midpoint(Min[dimension], Max[dimension]);

    public LongVectorN Midpoint()
        => LongVectorN.Midpoint(Min, Max);

    public bool Contains(LongVectorN point)
    {
        for (int d = 0; d < Dimensions; d++)
            if (point[d] < Min[d] || point[d] > Max[d])
                return false;
        return true;
    }

    public bool ContainsSparse(SparseVector point)
    {
        int sparseIdx = 0;
        ReadOnlySpan<SparseEntry> entries = point.Entries;

        for (int d = 0; d < Dimensions; d++)
        {
            long value = 0L;
            if (sparseIdx < entries.Length && entries[sparseIdx].Dimension == d)
            {
                value = entries[sparseIdx].Value;
                sparseIdx++;
            }
            if (value < Min[d] || value > Max[d])
                return false;
        }
        return true;
    }

    public bool IntersectsHypersphere(LongVectorN center, BigInteger radiusSquared)
    {
        BigInteger distanceSquared = 0;
        for (int d = 0; d < Dimensions; d++)
        {
            long closest = System.Math.Clamp(center[d], Min[d], Max[d]);
            long diff = center[d] - closest;
            distanceSquared += (BigInteger)diff * diff;
            if (distanceSquared > radiusSquared) return false;
        }
        return true;
    }

    public bool IntersectsHypersphereSparse(SparseVector center, BigInteger radiusSquared)
    {
        BigInteger distanceSquared = 0;
        int sparseIdx = 0;
        ReadOnlySpan<SparseEntry> entries = center.Entries;

        for (int d = 0; d < Dimensions; d++)
        {
            long centerVal = 0L;
            if (sparseIdx < entries.Length && entries[sparseIdx].Dimension == d)
            {
                centerVal = entries[sparseIdx].Value;
                sparseIdx++;
            }
            long closest = System.Math.Clamp(centerVal, Min[d], Max[d]);
            long diff = centerVal - closest;
            distanceSquared += (BigInteger)diff * diff;
            if (distanceSquared > radiusSquared) return false;
        }
        return true;
    }

    public HyperRegion LowerHalf(int splitDimension)
    {
        long[] maxComponents = Max.Components.ToArray();
        maxComponents[splitDimension] = MidpointAt(splitDimension);
        return new HyperRegion(Min, new LongVectorN(maxComponents));
    }

    public HyperRegion UpperHalf(int splitDimension)
    {
        long[] minComponents = Min.Components.ToArray();
        minComponents[splitDimension] = MidpointAt(splitDimension);
        return new HyperRegion(new LongVectorN(minComponents), Max);
    }

    public bool Equals(HyperRegion other)
        => Min == other.Min && Max == other.Max;

    public override bool Equals(object? obj)
        => obj is HyperRegion other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Min, Max);

    public static bool operator ==(HyperRegion a, HyperRegion b) => a.Equals(b);
    public static bool operator !=(HyperRegion a, HyperRegion b) => !a.Equals(b);

    public override string ToString()
        => $"HyperRegion[{Dimensions}D]({Min} ? {Max})";
}
