using System.Diagnostics;
using SparseLattice.Gguf;
using SparseLattice.Embedding;
using SparseLattice.Lattice;
using SparseLattice.Math;

/// <summary>
/// Usage:
///   dotnet run -c Release -- local         run local CPU embed harness only
///   dotnet run -c Release -- lattice        run lattice KNN harness only (no GGUF needed after build)
///   dotnet run -c Release -- both           run local embed + lattice KNN
///   dotnet run -c Release -- bench          run full BenchmarkDotNet suite
///   dotnet run -c Release               (default: local)
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "local";
        switch (mode)
        {
            case "bench":
                BenchmarkDotNet.Running.BenchmarkRunner.Run<EmbeddingPerfBenchmarks>();
                break;
            case "lattice":
                RunLatticeOnly().GetAwaiter().GetResult();
                break;
            case "both":
                RunLocalEmbed().GetAwaiter().GetResult();
                RunLatticeOnly().GetAwaiter().GetResult();
                break;
            default:
                RunLocalEmbed().GetAwaiter().GetResult();
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Local embed harness
    // -----------------------------------------------------------------------

    private static async Task RunLocalEmbed()
    {
        Console.WriteLine("=== Local Embed Harness ===");
        string? gguf = ResolveGguf();
        if (gguf is null) { Console.WriteLine("GGUF not found — skipping embed harness."); return; }

        using TransformerEmbeddingSource src = LoadWithProgress(gguf);
        string sample = "public int Add(int a, int b) => a + b;";

        Console.Write("Warming up (5)... ");
        for (int i = 0; i < 5; i++) _ = await src.EmbedAsync(sample).ConfigureAwait(false);
        Console.WriteLine("done.");

        int runs = 50;
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < runs; i++) _ = await src.EmbedAsync(sample).ConfigureAwait(false);
        sw.Stop();
        Console.WriteLine($"Single embed:  {sw.Elapsed.TotalMilliseconds / runs:F2} ms/op  ({runs} runs)");

        foreach (int concurrency in new[] { 1, 8, 32 })
        {
            int iters = 20;
            var tasks = new List<Task<float[]>>(concurrency);
            Stopwatch swp = Stopwatch.StartNew();
            for (int i = 0; i < iters; i++)
            {
                tasks.Clear();
                for (int c = 0; c < concurrency; c++)
                    tasks.Add(src.EmbedAsync(sample));
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            swp.Stop();
            double totalOps = (double)iters * concurrency;
            Console.WriteLine($"Parallel c={concurrency,2}: {totalOps / swp.Elapsed.TotalSeconds:F1} ops/sec   avg {swp.Elapsed.TotalMilliseconds / totalOps:F2} ms/op");
        }
    }

    // -----------------------------------------------------------------------
    // Lattice KNN harness — focused, no embed cost, varied corpus sizes
    // -----------------------------------------------------------------------

    private static Task RunLatticeOnly()
    {
        Console.WriteLine("=== Lattice KNN Harness ===");

        string[] samples = LoadSamplesFromFile();

        // Build SparseVectors directly from text — no floats, no GGUF, no model.
        // 768 dimensions matches nomic-embed-text for apples-to-apples lattice benchmarks.
        DirectHashEmbeddingSource hasher = new(dimensions: 768);

        const int maxCorpus = 5000;
        Console.Write($"Building {maxCorpus} sparse vectors via direct hashing... ");
        Stopwatch swHash = Stopwatch.StartNew();
        SparseVector[] allVectors = hasher.EmbedSparseBatch(
            Enumerable.Range(0, maxCorpus).Select(i => samples[i % samples.Length]).ToList());
        swHash.Stop();
        Console.WriteLine($"done in {swHash.Elapsed.TotalMilliseconds:F1} ms.");

        QuantizationOptions qo = new();
        SparseOccupant<string>[] allOccupants = allVectors
            .Select((v, i) => new SparseOccupant<string>(v, $"id{i}"))
            .ToArray();

        Console.WriteLine($"{"Corpus",8}  {"K",3}  {"Build ms",10}  {"QPS",10}  {"avg ms/q",10}  {"p99 ms",8}");
        Console.WriteLine(new string('-', 65));

        foreach (int n in new[] { 200
    //        , 500
    //        , 1000
    //        , 2000
    //        , 5000
        })
        {
            SparseOccupant<string>[] occupants = allOccupants[..n];

            Stopwatch swBuild = Stopwatch.StartNew();
            EmbeddingLattice<string> lattice = new(occupants, new LatticeOptions { LeafThreshold = 16 });
            lattice.Freeze();
            swBuild.Stop();

            foreach (int k in new[] { 5, 10 })
            {
                int qCount = System.Math.Min(500, n);
                double[] latencies = new double[qCount];

                // Warmup
                for (int q = 0; q < System.Math.Min(20, qCount); q++)
                    _ = lattice.QueryKNearestL2(occupants[q % n].Position, k);

                // Measure
                Stopwatch swq = Stopwatch.StartNew();
                for (int q = 0; q < qCount; q++)
                {
                    Stopwatch sqi = Stopwatch.StartNew();
                    _ = lattice.QueryKNearestL2(occupants[q % n].Position, k);
                    sqi.Stop();
                    latencies[q] = sqi.Elapsed.TotalMilliseconds;
                }
                swq.Stop();

                System.Array.Sort(latencies);
                double p99 = latencies[(int)(qCount * 0.99)];
                double qps = qCount / swq.Elapsed.TotalSeconds;
                double avgMs = swq.Elapsed.TotalMilliseconds / qCount;
                Console.WriteLine($"{n,8}  {k,3}  {swBuild.Elapsed.TotalMilliseconds,10:F1}  {qps,10:F1}  {avgMs,10:F3}  {p99,8:F3}");
            }
        }

        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    static string? ResolveGguf()
    {
        string? path = Environment.GetEnvironmentVariable("EMBEDDING_GGUF_PATH");
        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;

        string? testData = GetTestDataEmbeddingsDir();
        if (testData is null) return null;

        try
        {
            string? resolved = OllamaModelLocator.LocateGguf("nomic-embed-text", testData);
            if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved)) return resolved;
        }
        catch { }

        // Fall back: largest sha256-* blob
        string[] blobs = Directory.GetFiles(testData, "sha256-*", SearchOption.TopDirectoryOnly);
        return blobs.Length > 0
            ? blobs.OrderByDescending(f => new FileInfo(f).Length).First()
            : null;
    }

    static string? GetTestDataEmbeddingsDir()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    // -----------------------------------------------------------------------
    // Samples file helpers
    // -----------------------------------------------------------------------

    static string[] LoadSamplesFromFile()
    {
        try
        {
            string file = GetOrCreateSamplesFile();
            if (!File.Exists(file)) throw new FileNotFoundException("Samples file missing after creation attempt.", file);
            var lines = File.ReadAllLines(file)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Take(1000)
                            .ToArray();
            if (lines.Length == 0) throw new Exception("No valid sample lines found.");
            return lines;
        }
        catch
        {
            // Fallback to small built-in list if anything goes wrong
            return new[]
            {
                "public int Add(int a, int b) => a + b;",
                "public string Greet(string name) => $\"Hello, {name}!\";",
                "public bool IsEven(int n) => n % 2 == 0;",
                "public double Sqrt(double x) => Math.Sqrt(x);",
                "public void Log(string msg) => Console.WriteLine(msg);",
                "public int Max(int a, int b) => a > b ? a : b;",
                "public string Reverse(string s) => new string(s.Reverse().ToArray());",
                "public int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);",
                "public Task<T> AsTask<T>(T value) => Task.FromResult(value);",
                "public byte[] ToBytes(string s) => Encoding.UTF8.GetBytes(s);",
            };
        }
    }

    static string GetOrCreateSamplesFile()
    {
        string? root = LocateRepositoryRoot() ?? AppContext.BaseDirectory;
        string samplesDir = Path.Combine(root, "TestData", "Samples");
        Directory.CreateDirectory(samplesDir);
        string samplesFile = Path.Combine(samplesDir, "samples.txt");

        if (File.Exists(samplesFile)) return samplesFile;

        // Gather lines from .cs files under root
        var lines = new List<string>(capacity: 1200);
        try
        {
            var csFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                                   .Where(f =>
                                   {
                                       string lf = f.Replace('\\', '/').ToLowerInvariant();
                                       if (lf.Contains("/bin/") || lf.Contains("/obj/") || lf.Contains("/.git/")) return false;
                                       if (lf.Contains("/testdata/") && !lf.Contains("/testdata/samples")) return false;
                                       return true;
                                   });

            foreach (var file in csFiles)
            {
                foreach (var raw in File.ReadLines(file))
                {
                    if (lines.Count >= 1000) break;
                    var l = raw.Trim();
                    if (string.IsNullOrWhiteSpace(l)) continue;
                    if (l.StartsWith("//")) continue;
                    // Skip common region/pragma directives
                    if (l.StartsWith("#") || l.StartsWith("using ") || l.StartsWith("namespace ")) continue;
                    if (l.Length < 6) continue;
                    lines.Add(l);
                }
                if (lines.Count >= 1000) break;
            }
        }
        catch
        {
            // ignore and fall back below
        }

        // If not enough lines collected, try looser collection or pad
        if (lines.Count < 1000)
        {
            try
            {
                var fallbackFiles = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
                foreach (var file in fallbackFiles)
                {
                    foreach (var raw in File.ReadLines(file))
                    {
                        if (lines.Count >= 1000) break;
                        var l = raw.Trim();
                        if (string.IsNullOrWhiteSpace(l)) continue;
                        if (l.StartsWith("//")) continue;
                        if (l.Length < 4) continue;
                        lines.Add(l);
                    }
                    if (lines.Count >= 1000) break;
                }
            }
            catch
            {
                // ignore
            }

            // Pad by repeating existing lines or adding simple generated samples
            int idx = 0;
            while (lines.Count < 1000)
            {
                if (lines.Count > 0)
                {
                    lines.Add(lines[idx % lines.Count]);
                    idx++;
                }
                else
                {
                    lines.Add($"public string SampleLine{lines.Count}() => \"sample{lines.Count}\";");
                }
            }
        }

        // Write first 1000 lines to file
        File.WriteAllLines(samplesFile, lines.Take(1000), System.Text.Encoding.UTF8);
        return samplesFile;
    }

    static string? LocateRepositoryRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            // look for .sln or .csproj or .git
            if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
            if (Directory.GetFiles(dir, "*.sln", SearchOption.TopDirectoryOnly).Length > 0) return dir;
            if (Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    // -----------------------------------------------------------------------
    // Shared GGUF load with console progress bar
    // -----------------------------------------------------------------------

    static TransformerEmbeddingSource LoadWithProgress(string gguf)
    {
        int barWidth = 40;
        int lastPct  = -1;
        Stopwatch sw = Stopwatch.StartNew();
        string shortName = Path.GetFileName(gguf);
        if (shortName.Length > 14) shortName = shortName[..14] + "…";

        Console.Write($"\nLoading {shortName}  [");
        Console.Write(new string(' ', barWidth));
        Console.Write("]   0%");

        TransformerEmbeddingSource src = TransformerEmbeddingSource.Load(gguf,
            onProgress: (step, total, name) =>
            {
                int pct    = (int)System.Math.Round(100.0 * step / total);
                int filled = (int)System.Math.Round((double)barWidth * step / total);
                if (pct == lastPct) return;
                lastPct = pct;

                Console.CursorLeft = 0;
                string label = name.Length > 26 ? name[..26] : name;
                Console.Write($"Loading {shortName}  [");
                Console.Write(new string('#', filled));
                Console.Write(new string(' ', barWidth - filled));
                Console.Write($"] {pct,3}%  {label,-26}  {sw.Elapsed.TotalSeconds:F1}s  ");
            });

        sw.Stop();
        // Print final 100% bar then move to next line
        Console.CursorLeft = 0;
        Console.Write($"Loading {shortName}  [");
        Console.Write(new string('#', barWidth));
        Console.Write($"] 100%  done{new string(' ', 30)}");
        Console.WriteLine($"\nLoaded in {sw.Elapsed.TotalSeconds:F1}s");
        return src;
    }
}
