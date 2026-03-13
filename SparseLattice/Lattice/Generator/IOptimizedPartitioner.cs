///////////////////////////////////////////
namespace SparseLattice.Lattice.Generator;

public interface IOptimizedPartitioner
{
    int Dimensions { get; }

    int PartitionInPlace(
        Span<SparseOccupant<string>> span,
        ushort splitDimension,
        long pivotValue);
}

public sealed class PartitionerDescriptor
{
    public int Dimensions { get; init; }
    public int MaxNonzeros { get; init; } = 32;
    public string ClassName { get; init; } = "GeneratedPartitioner";
    public string Namespace { get; init; } = "SparseLattice.Lattice.Generated";
}
