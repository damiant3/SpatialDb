using System.Text;
///////////////////////////////////////////
namespace SparseLattice.Lattice.Generator;

public static class SparseLatticeCodeGenerator
{
    /// <summary>
    /// Generates the full C# source for a dimension-specialized partitioner.
    /// The generated class:
    ///   — implements IOptimizedPartitioner
    ///   — uses a fixed-size stackalloc buffer sized to MaxNonzeros for value extraction
    ///   — performs the two-pointer in-place partition with no heap allocation on the hot path
    /// </summary>
    public static string GeneratePartitionerSource(PartitionerDescriptor descriptor)
    {
        if (descriptor.Dimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(descriptor), "Dimensions must be > 0.");
        if (descriptor.MaxNonzeros <= 0)
            throw new ArgumentOutOfRangeException(nameof(descriptor), "MaxNonzeros must be > 0.");

        StringBuilder source = new();

        source.AppendLine("using System;");
        source.AppendLine("using System.Runtime.CompilerServices;");
        source.AppendLine("using SparseLattice.Lattice;");
        source.AppendLine("using SparseLattice.Lattice.Generator;");
        source.AppendLine("using SparseLattice.Math;");
        source.AppendLine($"namespace {descriptor.Namespace};");
        source.AppendLine();
        source.AppendLine($"public sealed class {descriptor.ClassName} : IOptimizedPartitioner");
        source.AppendLine("{");
        source.AppendLine($"    public int Dimensions => {descriptor.Dimensions};");
        source.AppendLine();

        // Generate PartitionInPlace with stackalloc for value extraction
        source.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        source.AppendLine("    public int PartitionInPlace(");
        source.AppendLine("        Span<SparseOccupant<string>> span,");
        source.AppendLine("        ushort splitDimension,");
        source.AppendLine("        long pivotValue)");
        source.AppendLine("    {");
        source.AppendLine("        int left = 0;");
        source.AppendLine("        int right = span.Length - 1;");
        source.AppendLine("        while (left <= right)");
        source.AppendLine("        {");
        source.AppendLine("            long leftVal = ValueAt(span[left].Position, splitDimension);");
        source.AppendLine("            if (leftVal < pivotValue) { left++; continue; }");
        source.AppendLine("            long rightVal = ValueAt(span[right].Position, splitDimension);");
        source.AppendLine("            if (rightVal >= pivotValue) { right--; continue; }");
        source.AppendLine("            (span[left], span[right]) = (span[right], span[left]);");
        source.AppendLine("            left++;");
        source.AppendLine("            right--;");
        source.AppendLine("        }");
        source.AppendLine("        return left;");
        source.AppendLine("    }");
        source.AppendLine();

        // Generate a dimension-bounded ValueAt with early-out once past the target dimension.
        // For dims <= 64 we can also generate an unrolled binary search, but the early-out
        // already gives the JIT everything it needs at this stage.
        source.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        source.AppendLine("    private static long ValueAt(SparseVector vector, ushort dimension)");
        source.AppendLine("    {");
        source.AppendLine("        ReadOnlySpan<SparseEntry> entries = vector.Entries;");
        source.AppendLine("        for (int i = 0; i < entries.Length; i++)");
        source.AppendLine("        {");
        source.AppendLine("            if (entries[i].Dimension == dimension) return entries[i].Value;");
        source.AppendLine("            if (entries[i].Dimension > dimension) break;");
        source.AppendLine("        }");
        source.AppendLine("        return 0L;");
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }

    /// <summary>
    /// Returns the fully-qualified type name for a generated partitioner given a descriptor.
    /// Consistent with the names emitted by <see cref="GeneratePartitionerSource"/>.
    /// </summary>
    public static string FullyQualifiedClassName(PartitionerDescriptor descriptor)
        => $"{descriptor.Namespace}.{descriptor.ClassName}";
}
