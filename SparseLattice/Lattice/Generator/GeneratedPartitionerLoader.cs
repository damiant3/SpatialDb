using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
///////////////////////////////////////////
namespace SparseLattice.Lattice.Generator;

/// <summary>
/// Compiles a generated partitioner source string via Roslyn and loads the resulting
/// assembly into a <see cref="CollectibleAssemblyLoadContext"/> so it can be unloaded
/// when the lattice is rebuilt for a new dimensionality.
/// </summary>
public sealed class GeneratedPartitionerLoader : IDisposable
{
    private readonly CollectibleLoadContext m_alc;
    private bool m_disposed;

    public IOptimizedPartitioner Partitioner { get; }

    private GeneratedPartitionerLoader(CollectibleLoadContext alc, IOptimizedPartitioner partitioner)
    {
        m_alc = alc;
        Partitioner = partitioner;
    }

    /// <summary>
    /// Generates source for <paramref name="descriptor"/>, compiles it, loads the assembly,
    /// and returns a loader holding both the ALC handle and the resolved partitioner instance.
    /// Throws <see cref="InvalidOperationException"/> if compilation fails.
    /// </summary>
    public static GeneratedPartitionerLoader Compile(PartitionerDescriptor descriptor)
    {
        string source = SparseLatticeCodeGenerator.GeneratePartitionerSource(descriptor);
        string fullyQualifiedName = SparseLatticeCodeGenerator.FullyQualifiedClassName(descriptor);

        CSharpSyntaxTree syntaxTree = (CSharpSyntaxTree)CSharpSyntaxTree.ParseText(source);

        List<MetadataReference> references = BuildMetadataReferences();

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: $"SparseLattice.Generated.{descriptor.ClassName}_{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));

        using MemoryStream ms = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            string diagnostics = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
            throw new InvalidOperationException(
                $"Roslyn compilation failed for {descriptor.ClassName}:{Environment.NewLine}{diagnostics}");
        }

        ms.Seek(0, SeekOrigin.Begin);

        CollectibleLoadContext alc = new($"SparseLattice.Generated.{descriptor.ClassName}");
        Assembly assembly = alc.LoadFromStream(ms);

        Type? generatedType = assembly.GetType(fullyQualifiedName)
            ?? throw new InvalidOperationException(
                $"Generated assembly does not contain type '{fullyQualifiedName}'.");

        IOptimizedPartitioner partitioner = (IOptimizedPartitioner)(
            Activator.CreateInstance(generatedType)
            ?? throw new InvalidOperationException(
                $"Failed to create instance of '{fullyQualifiedName}'."));

        return new GeneratedPartitionerLoader(alc, partitioner);
    }

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        m_alc.Unload();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private static List<MetadataReference> BuildMetadataReferences()
    {
        List<MetadataReference> refs = [];
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                continue;
            refs.Add(MetadataReference.CreateFromFile(asm.Location));
        }
        return refs;
    }

    private sealed class CollectibleLoadContext(string name)
        : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
            => null;
    }
}
