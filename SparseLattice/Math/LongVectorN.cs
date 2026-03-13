using System.Numerics;
///////////////////////////////
namespace SparseLattice.Math;

public readonly struct LongVectorN : IEquatable<LongVectorN>
{
    readonly long[] m_components;

    public int Dimensions => m_components.Length;

    public long this[int index]
    {
        get
        {
            if ((uint)index >= (uint)m_components.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return m_components[index];
        }
    }

    public LongVectorN(int dimensions)
    {
        if (dimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions));
        m_components = new long[dimensions];
    }

    public LongVectorN(long[] components)
    {
        if (components is not { Length: > 0 })
            throw new ArgumentException("Components must be non-empty.", nameof(components));
        m_components = components;
    }

    public ReadOnlySpan<long> Components => m_components.AsSpan();

    public BigInteger MagnitudeSquaredBig
    {
        get
        {
            BigInteger sum = 0;
            foreach (long component in m_components)
                sum += (BigInteger)component * component;
            return sum;
        }
    }

    public BigInteger SumAbsBig
    {
        get
        {
            BigInteger sum = 0;
            foreach (long component in m_components)
                sum += BigInteger.Abs(component);
            return sum;
        }
    }

    public BigInteger DotBig(LongVectorN other)
    {
        AssertSameDimensions(this, other);
        BigInteger sum = 0;
        for (int i = 0; i < m_components.Length; i++)
            sum += (BigInteger)m_components[i] * other.m_components[i];
        return sum;
    }

    public static LongVectorN operator +(LongVectorN a, LongVectorN b)
    {
        AssertSameDimensions(a, b);
        long[] result = new long[a.Dimensions];
        for (int i = 0; i < result.Length; i++)
            result[i] = a.m_components[i] + b.m_components[i];
        return new LongVectorN(result);
    }

    public static LongVectorN operator -(LongVectorN a, LongVectorN b)
    {
        AssertSameDimensions(a, b);
        long[] result = new long[a.Dimensions];
        for (int i = 0; i < result.Length; i++)
            result[i] = a.m_components[i] - b.m_components[i];
        return new LongVectorN(result);
    }

    public static LongVectorN operator *(LongVectorN a, long factor)
    {
        long[] result = new long[a.Dimensions];
        for (int i = 0; i < result.Length; i++)
            result[i] = a.m_components[i] * factor;
        return new LongVectorN(result);
    }

    public static LongVectorN operator /(LongVectorN a, long divisor)
    {
        long[] result = new long[a.Dimensions];
        for (int i = 0; i < result.Length; i++)
            result[i] = a.m_components[i] / divisor;
        return new LongVectorN(result);
    }

    public static bool operator ==(LongVectorN a, LongVectorN b) => a.Equals(b);
    public static bool operator !=(LongVectorN a, LongVectorN b) => !a.Equals(b);

    public bool Equals(LongVectorN other)
    {
        if (m_components.Length != other.m_components.Length) return false;
        for (int i = 0; i < m_components.Length; i++)
            if (m_components[i] != other.m_components[i])
                return false;
        return true;
    }

    public override bool Equals(object? obj)
        => obj is LongVectorN other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (long component in m_components)
            hash.Add(component);
        return hash.ToHashCode();
    }

    public override string ToString()
        => $"({string.Join(", ", m_components)})";

    // overflow-safe midpoint: (min & max) + ((min ^ max) >> 1)
    public static long Midpoint(long min, long max)
        => (min & max) + ((min ^ max) >> 1);

    public static LongVectorN Midpoint(LongVectorN min, LongVectorN max)
    {
        AssertSameDimensions(min, max);
        long[] result = new long[min.Dimensions];
        for (int i = 0; i < result.Length; i++)
            result[i] = Midpoint(min.m_components[i], max.m_components[i]);
        return new LongVectorN(result);
    }

    public static LongVectorN Zero(int dimensions) => new(dimensions);

    static void AssertSameDimensions(LongVectorN a, LongVectorN b)
    {
        if (a.Dimensions != b.Dimensions)
            throw new InvalidOperationException(
                $"Dimension mismatch: {a.Dimensions} vs {b.Dimensions}");
    }
}
