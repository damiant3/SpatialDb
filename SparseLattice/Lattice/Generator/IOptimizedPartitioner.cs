using SparseLattice.Math;
///////////////////////////////////////////
namespace SparseLattice.Lattice.Generator;

/// <summary>
/// Contract that all generated (or fallback runtime) partition implementations must satisfy.
/// The Roslyn generator emits a type implementing this interface specialized for a fixed
/// dimensionality and nnz budget, allowing the JIT to unroll and eliminate bounds checks.
/// </summary>
public interface IOptimizedPartitioner
{
    /// <summary>Total embedding dimensions this partitioner was specialized for.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Partitions <paramref name="span"/> in-place around <paramref name="pivotValue"/>
    /// on the given <paramref name="splitDimension"/>.
    /// Returns the boundary index: elements [0..boundary) are strictly below pivot,
    /// elements [boundary..Length) are at or above pivot.
    /// </summary>
    int PartitionInPlace(
        Span<SparseOccupant<string>> span,
        ushort splitDimension,
        long pivotValue);
}

/// <summary>
/// Descriptor passed to the code generator to produce a dimension-specialized partitioner.
/// </summary>
public sealed class PartitionerDescriptor
{
    /// <summary>Embedding dimensionality. Must be > 0.</summary>
    public int Dimensions { get; init; }

    /// <summary>Expected maximum nonzero entries per vector. Used to size internal stack buffers.</summary>
    public int MaxNonzeros { get; init; } = 32;

    /// <summary>Name to use for the generated class (must be a valid C# identifier).</summary>
    public string ClassName { get; init; } = "GeneratedPartitioner";

    /// <summary>Namespace for the generated class.</summary>
    public string Namespace { get; init; } = "SparseLattice.Lattice.Generated";
}
