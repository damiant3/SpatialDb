#if DIAGNOSTIC
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SpatialDbLibTest.Diagnostic
{
    public static class VariantTestHarness
    {
        // Compile C# source into an in-memory assembly and invoke VariantEntry.RunVariant()
        // Expects the source to provide:
        //   public static class VariantEntry { public static string RunVariant(); }
        // Returns the string result from RunVariant (e.g., JSON or single-line status).
        public static string CompileAndRunVariant(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var refs = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            var compilation = CSharpCompilation.Create(
                assemblyName: "SpatialDbLibTest.VariantRunner",
                syntaxTrees: new[] { syntaxTree },
                references: refs,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release)
            );

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);
            if (!emitResult.Success)
            {
                var diag = string.Join(Environment.NewLine, emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
                return $"COMPILE_ERROR:{Environment.NewLine}{diag}";
            }

            ms.Seek(0, SeekOrigin.Begin);

            // Load into collectible ALC so unloading is possible
            var alc = new CollectibleAssemblyLoadContext();
            try
            {
                var assembly = alc.LoadFromStream(ms);
                var entryType = assembly.GetType("VariantEntry");
                if (entryType == null) return "RUN_ERROR: VariantEntry type not found";
                var method = entryType.GetMethod("RunVariant", BindingFlags.Public | BindingFlags.Static);
                if (method == null) return "RUN_ERROR: RunVariant method not found";

                // invoke and return string result
                var sw = Stopwatch.StartNew();
                var resultObj = method.Invoke(null, Array.Empty<object>());
                sw.Stop();

                // prefer the returned string; if null, return elapsed time
                return resultObj as string ?? $"OK: elapsedMs={sw.ElapsedMilliseconds}";
            }
            finally
            {
                alc.Unload();
                // Give the runtime a moment to finalize unload (optional)
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // Compile a single-source variant containing a static method
        // with signature matching OctetParentNode.SubdivideImplDelegate and register it
        // for the requested variant. Returns a disposable handle that will unregister
        // and unload the compiled assembly when disposed.
        public static IDisposable CompileAndRegisterSubdivideImpl(string source, SpatialDbLib.Lattice.OctetParentNode.SubdivideVariant variant)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var refs = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            var assemblyName = $"VariantImpl_{Guid.NewGuid():N}";
            var compilation = CSharpCompilation.Create(
                assemblyName: assemblyName,
                syntaxTrees: new[] { syntaxTree },
                references: refs,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release)
            );

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);
            if (!emitResult.Success)
            {
                var diag = string.Join(Environment.NewLine, emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
                throw new InvalidOperationException($"Compile failed for variant impl:{Environment.NewLine}{diag}");
            }

            ms.Seek(0, SeekOrigin.Begin);

            var alc = new CollectibleAssemblyLoadContext();
            var asm = alc.LoadFromStream(ms);

            // Expect the compiled source to contain a public static type 'VariantImpl' with static method 'Impl'
            var variantType = asm.GetType("VariantImpl") ?? asm.GetType("VariantNs.VariantImpl") ?? asm.GetTypes().FirstOrDefault(t => t.GetMethod("Impl", BindingFlags.Public | BindingFlags.Static) != null);
            if (variantType == null) throw new InvalidOperationException("Compiled assembly does not contain a VariantImpl type with static Impl method.");

            var method = variantType.GetMethod("Impl", BindingFlags.Public | BindingFlags.Static);
            if (method == null) throw new InvalidOperationException("Compiled VariantImpl.Impl method not found.");

            // Get the delegate Type defined on the loaded OctetParentNode type in the main assembly
            var octetType = typeof(SpatialDbLib.Lattice.OctetParentNode);
            var delegateType = octetType.GetNestedType("SubdivideImplDelegate", BindingFlags.Public | BindingFlags.NonPublic);
            if (delegateType == null) throw new InvalidOperationException("Could not find SubdivideImplDelegate on OctetParentNode.");

            // Create delegate instance of that delegateType, targeting the compiled method
            var del = System.Delegate.CreateDelegate(delegateType, method);

            // Register it via OctetParentNode.RegisterSubdivideImpl
            var registerMethod = octetType.GetMethod("RegisterSubdivideImpl", BindingFlags.Public | BindingFlags.Static);
            if (registerMethod == null) throw new InvalidOperationException("RegisterSubdivideImpl not found on OctetParentNode.");
            registerMethod.Invoke(null, new object[] { variant, del });

            // Return handle that will unregister and unload the ALC when disposed
            return new RegisteredVariantHandle(variant, alc);
        }

        // Simple disposable that unregisters and unloads the assembly
        private sealed class RegisteredVariantHandle : IDisposable
        {
            readonly SpatialDbLib.Lattice.OctetParentNode.SubdivideVariant _variant;
            readonly CollectibleAssemblyLoadContext _alc;
            bool _disposed;

            public RegisteredVariantHandle(SpatialDbLib.Lattice.OctetParentNode.SubdivideVariant variant, CollectibleAssemblyLoadContext alc)
            {
                _variant = variant;
                _alc = alc;
            }

            public void Dispose()
            {
                if (_disposed) return;
                try
                {
                    var octetType = typeof(SpatialDbLib.Lattice.OctetParentNode);
                    var unregister = octetType.GetMethod("UnregisterSubdivideImpl", BindingFlags.Public | BindingFlags.Static);
                    unregister?.Invoke(null, new object[] { _variant });
                }
                finally
                {
                    _alc.Unload();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    _disposed = true;
                }
            }
        }

        // Simple collectible ALC
        private class CollectibleAssemblyLoadContext : AssemblyLoadContext
        {
            public CollectibleAssemblyLoadContext() : base(isCollectible: true) { }
            protected override Assembly? Load(AssemblyName assemblyName)
            {
                // Fallback to default load - tests typically rely on already loaded assemblies.
                return null;
            }
        }
    }
}
#endif