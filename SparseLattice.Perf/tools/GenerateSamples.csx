#!/usr/bin/env dotnet-script
// Run: dotnet script GenerateSamples.csx
// Harvests 1000 real code lines from the SpatialDb solution .cs files
// and writes them to TestData/Samples/samples.txt

var roots = new[]
{
    @"D:\Projects\SpatialDb\SpatialDbLib",
    @"D:\Projects\SpatialDb\SpatialDbApp",
    @"D:\Projects\SpatialDb\SpatialDbLibTest",
    @"D:\Projects\SpatialDb\SpatialGame",
    @"D:\Projects\SpatialDb\SpatialGameTest",
    @"D:\Projects\SpatialDb\SparseLattice",
    @"D:\Projects\SpatialDb\SparseLattice.Test",
    @"D:\Projects\SpatialDb\SparseLattice.Perf",
};

var lines = new List<string>(1200);
var seen  = new HashSet<string>(StringComparer.Ordinal);

foreach (var root in roots)
{
    if (!Directory.Exists(root)) continue;

    foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
    {
        if (lines.Count >= 1000) break;

        var fp = file.Replace('\\', '/').ToLowerInvariant();
        if (fp.Contains("/bin/") || fp.Contains("/obj/") || fp.Contains("/testdata/")) continue;

        foreach (var raw in File.ReadLines(file))
        {
            if (lines.Count >= 1000) break;

            var l = raw.Trim();
            if (l.Length < 10) continue;
            if (l.StartsWith("//") || l.StartsWith("/*") || l.StartsWith("*")) continue;
            if (l.StartsWith("using ") || l.StartsWith("namespace ") || l.StartsWith("#")) continue;
            if (l.StartsWith("[") && l.EndsWith("]")) continue;  // attributes
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"^[{}\[\]();,\s]+$")) continue;
            if (!seen.Add(l)) continue;  // deduplicate

            lines.Add(l);
        }
    }
    if (lines.Count >= 1000) break;
}

var outDir  = @"D:\Projects\SpatialDb\SparseLattice.Perf\TestData\Samples";
var outFile = Path.Combine(outDir, "samples.txt");
Directory.CreateDirectory(outDir);
File.WriteAllLines(outFile, lines.Take(1000), System.Text.Encoding.UTF8);

Console.WriteLine($"Written {Math.Min(lines.Count, 1000)} lines to {outFile}");
Console.WriteLine($"First 5:");
foreach (var l in lines.Take(5)) Console.WriteLine($"  {l}");
Console.WriteLine($"Last 5:");
foreach (var l in lines.Skip(Math.Max(0, Math.Min(lines.Count, 1000) - 5)).Take(5))
    Console.WriteLine($"  {l}");
