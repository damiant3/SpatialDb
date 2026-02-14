using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
                assemblyName: $"Variant_{Guid.NewGuid():N}",
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