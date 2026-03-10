using System.Numerics;
using SparseLattice.Math;
//////////////////////////////////
namespace SparseLattice.Lattice;

public sealed class LatticeOptions
{
    public int LeafThreshold { get; init; } = 16;
}

public static class EmbeddingLatticeBuilder
{
    public static ISparseNode Build<TPayload>(
        SparseOccupant<TPayload>[] occupants,
        LatticeOptions? options = null)
    {
        LatticeOptions effectiveOptions = options ?? new LatticeOptions();
        if (occupants.Length == 0)
            return new SparseLeafNode<TPayload>([]);
        return BuildRecursive(occupants.AsSpan(), effectiveOptions);
    }

    private static ISparseNode BuildRecursive<TPayload>(
        Span<SparseOccupant<TPayload>> span,
        LatticeOptions options)
    {
        if (span.Length <= options.LeafThreshold)
            return new SparseLeafNode<TPayload>(span.ToArray());

        ReadOnlySpan<SparseOccupant<TPayload>> readOnlySpan = span;
        int pivotDimension = FindHighestVarianceDimension(readOnlySpan);
        if (pivotDimension < 0)
            return new SparseLeafNode<TPayload>(span.ToArray());

        long pivotValue = ComputeMedianForDimension(readOnlySpan, (ushort)pivotDimension);
        int boundary = PartitionInPlace(span, (ushort)pivotDimension, pivotValue);

        if (boundary == 0 || boundary == span.Length)
            return new SparseLeafNode<TPayload>(span.ToArray());

        SparseBranchNode branch = new((ushort)pivotDimension, pivotValue);
        branch.SetBelow(BuildRecursive(span[..boundary], options));
        branch.SetAbove(BuildRecursive(span[boundary..], options));
        return branch;
    }

    private static int FindHighestVarianceDimension<TPayload>(
        ReadOnlySpan<SparseOccupant<TPayload>> span)
    {
        // collect all populated dimensions and track sum + sumSquared
        System.Collections.Concurrent.ConcurrentDictionary<ushort, (BigInteger sum, BigInteger sumSquared, int count)> dimensionStats = [];

        foreach (SparseOccupant<TPayload> occupant in span)
            foreach (SparseEntry entry in occupant.Position.Entries)
            {
                (BigInteger sum, BigInteger sumSquared, int count) current = dimensionStats.GetOrAdd(
                    entry.Dimension, _ => (0, 0, 0));
                dimensionStats[entry.Dimension] = (
                    current.sum + entry.Value,
                    current.sumSquared + (BigInteger)entry.Value * entry.Value,
                    current.count + 1);
            }

        if (dimensionStats.IsEmpty)
            return -1;

        // variance = E[x^2] - (E[x])^2 using full span.Length as population
        BigInteger bestVariance = -1;
        int bestDimension = -1;
        int populationSize = span.Length;

        foreach (System.Collections.Generic.KeyValuePair<ushort, (BigInteger sum, BigInteger sumSquared, int count)> kvp in dimensionStats)
        {
            // nonzero entries contribute their values; zero entries contribute nothing to sum/sumSquared
            // but they still count toward the population for variance
            BigInteger variance = kvp.Value.sumSquared * populationSize - kvp.Value.sum * kvp.Value.sum;
            if (variance > bestVariance)
            {
                bestVariance = variance;
                bestDimension = kvp.Key;
            }
        }

        return bestDimension;
    }

    private static long ComputeMedianForDimension<TPayload>(
        ReadOnlySpan<SparseOccupant<TPayload>> span,
        ushort dimension)
    {
        // extract values for the pivot dimension; missing = 0
        long[] values = new long[span.Length];
        for (int i = 0; i < span.Length; i++)
            values[i] = span[i].Position.ValueAt(dimension);

        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static int PartitionInPlace<TPayload>(
        Span<SparseOccupant<TPayload>> span,
        ushort dimension,
        long pivotValue)
    {
        int left = 0;
        int right = span.Length - 1;

        while (left <= right)
        {
            long leftVal = span[left].Position.ValueAt(dimension);
            if (leftVal < pivotValue)
            {
                left++;
                continue;
            }
            long rightVal = span[right].Position.ValueAt(dimension);
            if (rightVal >= pivotValue)
            {
                right--;
                continue;
            }
            (span[left], span[right]) = (span[right], span[left]);
            left++;
            right--;
        }
        return left;
    }
}
