using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SparseLattice.Gguf;
using SparseLattice.Embedding;
using SparseLattice.Lattice;
using SparseLattice.Math;

/// <summary>
/// Usage:
///   dotnet run -c Release -- local         run local CPU embed harness only (full transformer)
///   dotnet run -c Release -- fast          run lattice-based embed harness (token lookup, no transformer)
///   dotnet run -c Release -- lattice        run lattice KNN harness only (no GGUF needed after build)
///   dotnet run -c Release -- both           run fast embed + lattice KNN
///   dotnet run -c Release -- bench          run full BenchmarkDotNet suite
///   dotnet run -c Release -- vs            head-to-head: lattice inference vs Ollama HTTP
///   dotnet run -c Release -- serve          Ollama-compatible HTTP server backed by IntegerCausalSource
///   dotnet run -c Release -- quality        E4-8 determinism + quality benchmark
///   dotnet run -c Release               (default: fast)
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "fast";
        switch (mode)
        {
            case "bench":
                BenchmarkDotNet.Running.BenchmarkRunner.Run<EmbeddingPerfBenchmarks>();
                break;
            case "lattice":
                RunLatticeOnly().GetAwaiter().GetResult();
                break;
            case "local":
                RunLocalEmbed().GetAwaiter().GetResult();
                break;
            case "both":
                RunFastEmbed().GetAwaiter().GetResult();
                RunLatticeOnly().GetAwaiter().GetResult();
                break;
            case "compare":
                RunLocalEmbed().GetAwaiter().GetResult();
                RunFastEmbed().GetAwaiter().GetResult();
                break;
            case "vs":
                RunVsOllama().GetAwaiter().GetResult();
                break;
            case "diag":
                RunDiag();
                break;
            case "diagload":
                RunDiagLoad();
                break;
            case "intmatmul":
                RunIntegerMatMulBench();
                break;
            case "probe":
                RunCausalProbe();
                break;
            case "serve":
                RunServe(args.Skip(1).ToArray()).GetAwaiter().GetResult();
                break;
            case "quality":
                RunQualityBenchmark().GetAwaiter().GetResult();
                break;
            default:
                RunFastEmbed().GetAwaiter().GetResult();
                break;
        }
    }

    // -----------------------------------------------------------------------
    // vs: head-to-head lattice inference vs Ollama HTTP
    // -----------------------------------------------------------------------

    private static async Task RunVsOllama()
    {
        string ollamaBase = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434/";
        string model      = "nomic-embed-text";

        // Load real code samples for realistic document ingestion simulation
        string[] samples = LoadSamplesFromFile();

        // --- Load lattice source ---
        string? gguf = ResolveGguf();
        if (gguf is null) { Console.WriteLine("GGUF not found â€” cannot run lattice side. Aborting."); return; }

        Console.Write("Loading LatticeEmbeddingSource... ");
        Stopwatch swLoad = Stopwatch.StartNew();
        using LatticeEmbeddingSource lattice = LatticeEmbeddingSource.Load(gguf);
        swLoad.Stop();
        Console.WriteLine($"done in {swLoad.Elapsed.TotalSeconds:F2}s  (model ready, {lattice.Dimensions}d)");

        // --- Ping Ollama ---
        using HttpClient http = new() { BaseAddress = new Uri(ollamaBase), Timeout = TimeSpan.FromSeconds(60) };
        bool ollamaUp = false;
        try { using var ping = await http.GetAsync("api/tags").ConfigureAwait(false); ollamaUp = ping.IsSuccessStatusCode; }
        catch { }
        if (!ollamaUp) { Console.WriteLine($"Ollama not reachable at {ollamaBase} â€” aborting."); return; }
        Console.WriteLine($"Ollama at {ollamaBase}  model={model}\n");

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        async Task<float[]> OllamaEmbed(string text)
        {
            var resp = await http.PostAsJsonAsync("api/embed", new { model, input = text }).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            var arr = doc.RootElement.GetProperty("embeddings")[0];
            float[] vec = new float[arr.GetArrayLength()];
            int idx = 0;
            foreach (var el in arr.EnumerateArray()) vec[idx++] = el.GetSingle();
            return vec;
        }

        // Warm up both sides
        Console.Write("Warming up... ");
        for (int i = 0; i < 20; i++) _ = lattice.EmbedSparse(samples[i % samples.Length]);
        for (int i = 0; i < 5;  i++) _ = await OllamaEmbed(samples[i % samples.Length]).ConfigureAwait(false);
        Console.WriteLine("done.\n");

        // ================================================================
        // Q1: Single embedding latency
        //     Use case: attaching one embedding to a prompt for RAG context
        // ================================================================
        string singleSample = samples[0];
        const int latticeRuns = 500;
        const int ollamaRuns  = 30;

        var swL = Stopwatch.StartNew();
        for (int i = 0; i < latticeRuns; i++) _ = lattice.EmbedSparse(singleSample);
        swL.Stop();
        double latticeSingleMs  = swL.Elapsed.TotalMilliseconds / latticeRuns;
        double latticeSingleOps = latticeRuns / swL.Elapsed.TotalSeconds;

        var swO = Stopwatch.StartNew();
        for (int i = 0; i < ollamaRuns; i++) _ = await OllamaEmbed(singleSample).ConfigureAwait(false);
        swO.Stop();
        double ollamaSingleMs  = swO.Elapsed.TotalMilliseconds / ollamaRuns;
        double ollamaSingleOps = ollamaRuns / swO.Elapsed.TotalSeconds;

        // ================================================================
        // Q2: Document ingestion â€” batch throughput
        //     Use case: vectorising a whole codebase / document corpus
        // ================================================================
        int[] batchSizes = { 10, 100, 500 };

        // Lattice batch (synchronous, single-threaded)
        double[] latticeBatchOps = new double[batchSizes.Length];
        for (int bi = 0; bi < batchSizes.Length; bi++)
        {
            int n = batchSizes[bi];
            string[] batch = Enumerable.Range(0, n).Select(i => samples[i % samples.Length]).ToArray();
            var sw = Stopwatch.StartNew();
            _ = lattice.EmbedSparseBatch(batch);
            sw.Stop();
            latticeBatchOps[bi] = n / sw.Elapsed.TotalSeconds;
        }

        // Ollama batch â€” sequential (it has no native batch endpoint for multi-string)
        double[] ollamaBatchOps  = new double[batchSizes.Length];
        double[] ollamaBatchSecs = new double[batchSizes.Length];
        for (int bi = 0; bi < batchSizes.Length; bi++)
        {
            int n = System.Math.Min(batchSizes[bi], 20); // cap at 20 to keep runtime sane
            string[] batch = Enumerable.Range(0, n).Select(i => samples[i % samples.Length]).ToArray();
            var sw = Stopwatch.StartNew();
            foreach (var s in batch) _ = await OllamaEmbed(s).ConfigureAwait(false);
            sw.Stop();
            double measured = n / sw.Elapsed.TotalSeconds;
            // Extrapolate to full batch size for display
            ollamaBatchOps[bi]  = measured;
            ollamaBatchSecs[bi] = batchSizes[bi] / measured;
        }

        // ================================================================
        // Q3: Parallel embed â€” prompt pipeline throughput
        //     Use case: embedding many chunks concurrently for a thinking model
        // ================================================================
        int[] concurrencies = { 1, 8, 32 };
        double[] latticePar = new double[3];
        double[] ollamaPar  = new double[3];
        const int parIters = 15;

        for (int ci = 0; ci < concurrencies.Length; ci++)
        {
            int c = concurrencies[ci];
            // Lattice: offload to thread pool so we actually parallelise
            var sw = Stopwatch.StartNew();
            for (int it = 0; it < parIters; it++)
                await Task.WhenAll(Enumerable.Range(0, c)
                    .Select(i => Task.Run(() => lattice.EmbedSparse(samples[i % samples.Length])))).ConfigureAwait(false);
            sw.Stop();
            latticePar[ci] = (double)parIters * c / sw.Elapsed.TotalSeconds;

            sw = Stopwatch.StartNew();
            for (int it = 0; it < parIters; it++)
                await Task.WhenAll(Enumerable.Range(0, c)
                    .Select(i => OllamaEmbed(samples[i % samples.Length]))).ConfigureAwait(false);
            sw.Stop();
            ollamaPar[ci] = (double)parIters * c / sw.Elapsed.TotalSeconds;
        }

        // ================================================================
        // Print results
        // ================================================================

        double su = ollamaSingleMs / latticeSingleMs;

        Console.WriteLine("+----------------------------------------------------------------------------------------+");
        Console.WriteLine("|     Q1: Single embedding latency  (prompt augmentation / RAG lookup)                   |");
        Console.WriteLine("+----------------------------+------------------+------------------------+");
        Console.WriteLine($"| Lattice (token lookup)     | {latticeSingleMs,14:F4}   | {latticeSingleOps,22:F0}   |");
        Console.WriteLine($"| Ollama HTTP                | {ollamaSingleMs,14:F2}   | {ollamaSingleOps,22:F1}   |");
        Console.WriteLine("+----------------------------+------------------+------------------------+");
        Console.WriteLine($"| Lattice speedup            | {su,13:F0}x    |                          |");
        Console.WriteLine("+----------------------------+------------------+------------------------+");
        Console.WriteLine();
        Console.WriteLine("+----------------------------------------------------------------------------------------+");
        Console.WriteLine("|     Q2: Document ingestion throughput  (batch vectorisation)                           |");
        Console.WriteLine("+----------------------------+----------+----------+----------+----------+");
        Console.WriteLine("| Method                     |  10 docs | 100 docs |  500 docs| 1000 docs|");
        Console.WriteLine("+----------------------------+----------+----------+----------+----------+");
        // Lattice 1000-doc extrapolated
        double l1000 = latticeBatchOps[2]; // 500-doc rate is representative
        Console.WriteLine($"| Lattice  (embeds/sec)      |{latticeBatchOps[0],8:F0}  |{latticeBatchOps[1],8:F0}  |{latticeBatchOps[2],10:F0}  |{l1000,8:F0}  |");
        double o1000 = ollamaBatchOps[2];
        Console.WriteLine($"| Ollama   (embeds/sec)      |{ollamaBatchOps[0],8:F1}  |{ollamaBatchOps[1],8:F1}  |{ollamaBatchOps[2],10:F1}  |{o1000,8:F1}  |");
        Console.WriteLine("+----------------------------+----------+----------+----------+----------+");
        Console.WriteLine();
        Console.WriteLine("+----------------------------------------------------------------------------------------+");
        Console.WriteLine("|     Q3: Parallel prompt pipeline  (concurrent embed for thinking model)                |");
        Console.WriteLine("+----------------------------+--------------+--------------+------------+");
        Console.WriteLine($"| Lattice  (embeds/sec)      |{latticePar[0],10:F0}    |{latticePar[1],10:F0}    |{latticePar[2],11:F0}    |");
        Console.WriteLine($"| Ollama   (embeds/sec)      |{ollamaPar[0],10:F1}    |{ollamaPar[1],10:F1}    |{ollamaPar[2],11:F1}    |");
        Console.WriteLine("+----------------------------+--------------+--------------+------------+");
        Console.WriteLine();
    }

    // -----------------------------------------------------------------------
    // Local embed harness
    // -----------------------------------------------------------------------

    private static async Task RunLocalEmbed()
    {
        Console.WriteLine("=== Local Embed Harness ===");
        string? gguf = ResolveGguf();
        if (gguf is null) { Console.WriteLine("GGUF not found â€” skipping embed harness."); return; }

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
    // Fast embed harness â€” lattice-based token lookup (no transformer)
    // -----------------------------------------------------------------------

    private static async Task RunFastEmbed()
    {
        Console.WriteLine("=== Lattice Embed Harness (token lookup, no transformer) ===");
        string? gguf = ResolveGguf();
        if (gguf is null) { Console.WriteLine("GGUF not found â€” skipping fast embed harness."); return; }

        Console.Write("Loading token embeddings into lattice... ");
        Stopwatch swLoad = Stopwatch.StartNew();
        using LatticeEmbeddingSource src = LatticeEmbeddingSource.Load(gguf,
            onProgress: (step, total, name) =>
            {
                Console.Write($"\r  [{step}/{total}] {name}                    ");
            });
        swLoad.Stop();
        Console.WriteLine($"\nLoaded in {swLoad.Elapsed.TotalSeconds:F2}s");

        string sample = "public int Add(int a, int b) => a + b;";

        Console.Write("Warming up (5)... ");
        for (int i = 0; i < 5; i++) _ = src.EmbedSparse(sample);
        Console.WriteLine("done.");

        int runs = 5000;
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < runs; i++) _ = src.EmbedSparse(sample);
        sw.Stop();
        double msPerOp = sw.Elapsed.TotalMilliseconds / runs;
        double opsPerSec = runs / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"Lattice embed:  {msPerOp:F4} ms/op  ({opsPerSec:F0} ops/sec, {runs} runs)");

        string[] samples = LoadSamplesFromFile();
        int batchSize = System.Math.Min(1000, samples.Length);
        List<string> batch = samples.Take(batchSize).ToList();

        Stopwatch swBatch = Stopwatch.StartNew();
        SparseVector[] vectors = src.EmbedSparseBatch(batch);
        swBatch.Stop();
        Console.WriteLine($"Batch {batchSize}:  {swBatch.Elapsed.TotalMilliseconds:F1} ms total  ({swBatch.Elapsed.TotalMilliseconds / batchSize:F4} ms/op)");

        double avgNnz = vectors.Average(v => v.NonzeroCount);
        Console.WriteLine($"Avg nnz per embedding: {avgNnz:F1} / {src.Dimensions}");

        Console.WriteLine("\n--- float[] interface (for IEmbeddingSource compatibility) ---");
        Stopwatch swFloat = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) _ = await src.EmbedAsync(sample).ConfigureAwait(false);
        swFloat.Stop();
        Console.WriteLine($"Float embed:  {swFloat.Elapsed.TotalMilliseconds / 1000:F4} ms/op  (1000 runs)");
    }

    // -----------------------------------------------------------------------
    // Lattice KNN harness â€” focused, no embed cost, varied corpus sizes
    // -----------------------------------------------------------------------

    private static Task RunLatticeOnly()
    {
        Console.WriteLine("=== Lattice KNN Harness ===");

        string[] samples = LoadSamplesFromFile();

        // Build SparseVectors directly from text â€” no floats, no GGUF, no model.
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
        if (shortName.Length > 14) shortName = shortName[..14] + "â€¦";

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

    // -----------------------------------------------------------------------
    // vs: head-to-head lattice inference vs Ollama HTTP
    // -----------------------------------------------------------------------

    private static void RunDiag()
    {
        string? gguf = ResolveGguf();
        Console.WriteLine($"Resolved GGUF: {gguf ?? "(null)"}");
        if (gguf is null) return;
        Console.WriteLine($"File size: {new FileInfo(gguf).Length / 1024.0 / 1024.0:F1} MB");

        using var reader = SparseLattice.Gguf.GgufReader.Open(gguf);
        Console.WriteLine($"Architecture : {reader.Architecture}");
        Console.WriteLine($"ModelName    : {reader.ModelName}");
        Console.WriteLine($"EmbeddingLen : {reader.EmbeddingLength}");
        Console.WriteLine($"LayerCount   : {reader.LayerCount}");
        Console.WriteLine($"Vocab size   : {reader.Tokens.Count}");
        Console.WriteLine($"Tensors      : {reader.TensorInfos.Count}");
        foreach (var t in reader.TensorInfos)
            Console.WriteLine($"  {t.Name,-50} shape=[{string.Join(",", t.Shape)}]  dtype={t.DType}");
    }

    private static void RunDiagLoad()
    {
        string? gguf = ResolveGguf();
        if (gguf is null) { Console.WriteLine("GGUF not found."); return; }

        long fileSize = new FileInfo(gguf).Length;
        Console.WriteLine($"File : {Path.GetFileName(gguf)}");
        Console.WriteLine($"Size : {fileSize / 1024.0 / 1024.0:F1} MB");
        Console.WriteLine();

        // 1. Raw sequential read â€” pure I/O, no parsing
        {
            var sw = Stopwatch.StartNew();
            using var fs = new FileStream(gguf, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize: 1 << 20);
            byte[] buf = new byte[1 << 20];
            long total = 0;
            int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0) total += n;
            sw.Stop();
            Console.WriteLine($"Raw sequential read  : {sw.Elapsed.TotalMilliseconds,8:F1} ms   " +
                              $"{total / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds,6:F0} MB/s");
        }

        // 2. GgufReader.Open â€” header + metadata + tensor table only (no tensor data)
        {
            var sw = Stopwatch.StartNew();
            using var reader = SparseLattice.Gguf.GgufReader.Open(gguf);
            sw.Stop();
            Console.WriteLine($"GgufReader.Open      : {sw.Elapsed.TotalMilliseconds,8:F1} ms   " +
                              $"(header+metadata+tensor table, vocab={reader.Tokens.Count})");
        }

        // 3. ReadTensorF32("token_embd.weight") only
        {
            using var reader = SparseLattice.Gguf.GgufReader.Open(gguf);
            var sw = Stopwatch.StartNew();
            float[] embd = reader.ReadTensorF32("token_embd.weight");
            sw.Stop();
            double mb = embd.Length * 4.0 / 1024 / 1024;
            Console.WriteLine($"ReadTensorF32 embd   : {sw.Elapsed.TotalMilliseconds,8:F1} ms   " +
                              $"({mb:F0} MB float32, {reader.Tokens.Count}Ã—{reader.EmbeddingLength} dequantized from F16)");
        }

        // 4. Full LatticeEmbeddingSource.Load â€” everything including quantize loop
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        long heapBefore    = GC.GetTotalMemory(false);
        long workingBefore = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;

        LatticeEmbeddingSource src;
        Stopwatch swLoad;
        {
            swLoad = Stopwatch.StartNew();
            src = LatticeEmbeddingSource.Load(gguf);
            swLoad.Stop();
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        long heapAfter    = GC.GetTotalMemory(false);
        long workingAfter = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;

        long heapDelta    = heapAfter    - heapBefore;
        long workingDelta = workingAfter - workingBefore;

        Console.WriteLine($"LatticeEmbeddingSource.Load  : {swLoad.Elapsed.TotalMilliseconds,8:F1} ms   total");
        Console.WriteLine();

        // ----------------------------------------------------------------
        // Memory breakdown â€” show what's actually in the loaded structure
        // ----------------------------------------------------------------
        using (src)
        {
            int   vocabSize = 0;
            long  totalEntries = 0;
            int   minNnz = int.MaxValue, maxNnz = 0;
            long  minVal = long.MaxValue, maxVal = long.MinValue;
            int   fullyDense = 0;
            int   actuallyEmpty = 0;
            int   dims = src.Dimensions;

            // Embed every vocab entry by probing via single-tone encode
            // Instead, embed the whole batch of samples to measure operational vectors
            string[] samples = LoadSamplesFromFile();
            var vectors = src.EmbedSparseBatch(samples.ToList());

            foreach (var v in vectors)
            {
                vocabSize++;
                totalEntries += v.NonzeroCount;
                if (v.NonzeroCount < minNnz) minNnz = v.NonzeroCount;
                if (v.NonzeroCount > maxNnz) maxNnz = v.NonzeroCount;
                if (v.NonzeroCount == dims) fullyDense++;
                if (v.NonzeroCount == 0)   actuallyEmpty++;
                foreach (var e in v.Entries)
                {
                    if (e.Value < minVal) minVal = e.Value;
                    if (e.Value > maxVal) maxVal = e.Value;
                }
            }

            double avgNnz     = (double)totalEntries / vocabSize;
            double sparsityPct = 100.0 * (1.0 - avgNnz / dims);

            // SparseEntry = ushort(2) + long(8) = 10 bytes per entry
            // SparseVector struct = array ref(8) + int TotalDimensions(4) = 12 bytes overhead
            long entryBytes   = totalEntries * 10L;
            long overheadBytes = vocabSize   * 12L;
            long totalEstBytes = entryBytes + overheadBytes;

            Console.WriteLine("+-------------------------------+----------+----------+----------+----------+");
            Console.WriteLine("Spot-check individual embeddings (nnz / dims):");
            foreach (var sample in samples.Skip(samples.Length - 5))
            {
                var v = src.EmbedSparse(sample);
                Console.WriteLine($"  {v.NonzeroCount,4}/{dims}  ({100.0*v.NonzeroCount/dims:F1}%)  \"{(sample.Length > 55 ? sample[..52]+"..." : sample)}\"");
            }
        }
    }

    // -----------------------------------------------------------------------
    // Integer MatMul benchmark (E4-1)
    // -----------------------------------------------------------------------

    private static void RunIntegerMatMulBench()
    {
        Console.WriteLine("+----------------------------------------------------------------------+");
        Console.WriteLine("|       E4-1: Integer MatMul vs Float MatMul -- Performance             |");
        Console.WriteLine("+----------------------------------------------------------------------+");
        Console.WriteLine();

        int[] dims   = [128, 384, 768];
        int[] seqLens = [1, 4, 16];

        // Table header
        Console.WriteLine($"{"Shape",-28} {"Float ms",10} {"Int128 ms",10} {"Ratio",8} {"MaxRelErr",12}");
        Console.WriteLine(new string('-', 70));

        foreach (int dim in dims)
        {
            foreach (int seq in seqLens)
            {
                string shape = $"{seq}×{dim} × {dim}×{dim}";

                Random rng = new(42);
                float[] aFloat = new float[seq * dim];
                float[] wFloat = new float[dim * dim];
                for (int i = 0; i < aFloat.Length; i++) aFloat[i] = (float)(rng.NextDouble() * 0.1 - 0.05);
                for (int i = 0; i < wFloat.Length; i++) wFloat[i] = (float)(rng.NextDouble() * 0.1 - 0.05);

                // Quantize once (not timed)
                ScaledTensor aInt = IntegerMatMul.QuantizeFromFloat(aFloat, 30);
                ScaledTensor wInt = IntegerMatMul.QuantizeFromFloat(wFloat, 30);

                // Warmup
                FloatMatMulBench(aFloat, seq, dim, wFloat, dim);
                IntegerMatMul.MatMul(aInt.Data, seq, dim, wInt.Data, dim);

                // Time float path
                int iterations = dim >= 768 ? 5 : 20;
                var swFloat = Stopwatch.StartNew();
                for (int iter = 0; iter < iterations; iter++)
                    FloatMatMulBench(aFloat, seq, dim, wFloat, dim);
                swFloat.Stop();
                double floatMs = swFloat.Elapsed.TotalMilliseconds / iterations;

                // Time integer path
                var swInt = Stopwatch.StartNew();
                for (int iter = 0; iter < iterations; iter++)
                    IntegerMatMul.MatMul(aInt.Data, seq, dim, wInt.Data, dim);
                swInt.Stop();
                double intMs = swInt.Elapsed.TotalMilliseconds / iterations;

                // Measure fidelity
                float[] cFloat = FloatMatMulBench(aFloat, seq, dim, wFloat, dim);
                long[]  cInt   = IntegerMatMul.MatMul(aInt.Data, seq, dim, wInt.Data, dim);
                ScaledTensor cScaled = new(cInt, aInt.ScaleExponent + wInt.ScaleExponent);
                float[] cDeq = IntegerMatMul.DequantizeToFloat(cScaled);

                double maxRel = 0;
                for (int i = 0; i < cFloat.Length; i++)
                {
                    float abs = MathF.Abs(cFloat[i]);
                    if (abs < 1e-10f) continue;
                    double rel = MathF.Abs(cFloat[i] - cDeq[i]) / abs;
                    if (rel > maxRel) maxRel = rel;
                }

                double ratio = intMs / floatMs;
                Console.WriteLine($"{shape,-28} {floatMs,10:F2} {intMs,10:F2} {ratio,7:F2}× {maxRel,12:E2}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Float path: scalar loop with SIMD dot (Vector<float>)");
        Console.WriteLine("  Int128 path: 4× unrolled scalar with Int128 accumulator");
        Console.WriteLine("  Ratio < 1 = integer is faster; > 1 = integer is slower");
        Console.WriteLine("  MaxRelErr: max relative error between float and integer results");
        Console.WriteLine("  Target: ratio ≤ 3× (acceptable for the precision gain)");
    }

    private static float[] FloatMatMulBench(float[] a, int rowsA, int colsA, float[] w, int colsB)
    {
        float[] c = new float[rowsA * colsB];
        for (int row = 0; row < rowsA; row++)
        {
            int aBase = row * colsA;
            for (int col = 0; col < colsB; col++)
            {
                int wBase = col * colsA;
                float sum = 0f;
                for (int k = 0; k < colsA; k++)
                    sum += a[aBase + k] * w[wBase + k];
                c[row * colsB + col] = sum;
            }
        }
        return c;
    }

    private static void RunCausalProbe()
    {
        const string ollamaRoot = @"D:\AI\OllamaModels";
        string? gguf = OllamaModelLocator.LocateGgufOllama("gpt-oss", ollamaRoot, "20b");
        if (gguf is null)
        {
            Console.WriteLine("gpt-oss:20b not found.");
            return;
        }

        Console.WriteLine("GGUF: " + Path.GetFileName(gguf));
        Console.WriteLine("Size: " + (new FileInfo(gguf).Length / 1024.0 / 1024.0).ToString("F1") + " MB");
        Console.WriteLine();

        using GgufReader reader = GgufReader.Open(gguf);
        Console.WriteLine("Architecture: " + reader.Architecture);
        Console.WriteLine("Embedding:    " + reader.EmbeddingLength);
        Console.WriteLine("Heads:        " + reader.HeadCount);
        Console.WriteLine("Layers:       " + reader.LayerCount);
        Console.WriteLine("Context:      " + reader.ContextLength);
        Console.WriteLine("Vocab:        " + reader.Tokens.Count);
        Console.WriteLine("Tensors:      " + reader.TensorInfos.Count);
        Console.WriteLine();

        Console.WriteLine("=== MXFP4 dequant test ===");
        var sw = Stopwatch.StartNew();
        float[] expertWeights = reader.ReadTensorF32("blk.0.ffn_down_exps.weight");
        sw.Stop();
        Console.WriteLine("blk.0.ffn_down_exps.weight: " + expertWeights.Length.ToString("N0") + " elements");
        Console.WriteLine("Dequant time: " + sw.ElapsedMilliseconds + " ms");

        float absMax = 0f;
        float absMin = float.MaxValue;
        int nonZero = 0;
        int nanCount = 0;
        int infCount = 0;
        for (int i = 0; i < expertWeights.Length; i++)
        {
            float v = expertWeights[i];
            if (float.IsNaN(v)) { nanCount++; continue; }
            if (float.IsInfinity(v)) { infCount++; continue; }
            float a = MathF.Abs(v);
            if (a > absMax) absMax = a;
            if (a > 0 && a < absMin) absMin = a;
            if (v != 0) nonZero++;
        }

        Console.WriteLine("  abs max:   " + absMax);
        Console.WriteLine("  abs min>0: " + absMin);
        Console.WriteLine("  non-zero:  " + nonZero.ToString("N0") + " / " + expertWeights.Length.ToString("N0"));
        Console.WriteLine("  NaN:       " + nanCount);
        Console.WriteLine("  Inf:       " + infCount);
        Console.WriteLine("  First 20:  [" + string.Join(", ", expertWeights.Take(20).Select(v => v.ToString("G4"))) + "]");

        Console.WriteLine();
        Console.WriteLine("=== BF16 attention weight test ===");
        sw.Restart();
        float[] attnQ = reader.ReadTensorF32("blk.0.attn_q.weight");
        sw.Stop();
        Console.WriteLine("blk.0.attn_q.weight: " + attnQ.Length.ToString("N0") + " elements, " + sw.ElapsedMilliseconds + " ms");
        Console.WriteLine("  abs max: " + attnQ.Max(MathF.Abs));
        Console.WriteLine("  First 10: [" + string.Join(", ", attnQ.Take(10).Select(v => v.ToString("G4"))) + "]");

        Console.WriteLine();
        Console.WriteLine("=== BPE tokenizer test ===");
        BpeTokenizer tokenizer = BpeTokenizer.FromGguf(reader);
        Console.WriteLine("Vocab: " + tokenizer.VocabSize);
        Console.WriteLine("BOS/EOS: " + tokenizer.BosTokenId + "/" + tokenizer.EosTokenId);

        string testText = "Hello, world! This is a test of the gpt-oss tokenizer.";
        int[] tokens = tokenizer.Encode(testText);
        string decoded = tokenizer.Decode(tokens);
        Console.WriteLine("Input:    " + testText);
        Console.WriteLine("Tokens:   [" + string.Join(", ", tokens) + "]");
        Console.WriteLine("Decoded:  " + decoded);
        Console.WriteLine("Round-trip match: " + (testText == decoded));
    }

    // -----------------------------------------------------------------------
    // serve: Ollama-compatible HTTP server backed by IntegerCausalSource
    // -----------------------------------------------------------------------

    private static async Task RunServe(string[] extraArgs)
    {
        string ollamaRoot = Environment.GetEnvironmentVariable("OLLAMA_MODELS") ?? @"D:\AI\OllamaModels";
        string modelName = extraArgs.Length > 0 ? extraArgs[0] : "gemma3";
        string? tag = extraArgs.Length > 1 ? extraArgs[1] : null;
        int port = 11435;
        bool useLattice = extraArgs.Any(a => a.Equals("--lattice", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine("+----------------------------------------------------------------------+");
        Console.WriteLine("|       SparseLattice -- Integer Causal Server (Ollama-compatible)       |");
        Console.WriteLine("+----------------------------------------------------------------------+");
        Console.WriteLine();
        Console.WriteLine($"Model:   {modelName}" + (tag is not null ? $":{tag}" : ""));
        Console.WriteLine($"Ollama:  {ollamaRoot}");
        Console.WriteLine($"Port:    {port}");
        Console.WriteLine($"Lattice: {(useLattice ? "enabled" : "disabled")}");
        Console.WriteLine();

        Console.Write("Loading model... ");
        Stopwatch swLoad = Stopwatch.StartNew();
        IntegerCausalSource model;
        try
        {
            model = IntegerCausalSource.LoadFromOllama(
                modelName, ollamaRoot, tag, scaleBits: 30,
                onProgress: (step, total, name) =>
                {
                    if (step % 20 == 0 || step == total)
                        Console.Write($"\rLoading model... {step}/{total} ({name})    ");
                });
        }
        catch (OutOfMemoryException)
        {
            Console.WriteLine();
            Console.WriteLine("ERROR: OutOfMemoryException — model too large for int64 quantization.");
            Console.WriteLine("The integer lattice runtime quantizes all weights to int64 (8 bytes each).");
            Console.WriteLine("Large models (4B+ params) need 20+ GB RAM. Options:");
            Console.WriteLine("  1. Use a smaller model: embeddinggemma (768-dim, 24 layers)");
            Console.WriteLine("  2. Increase available RAM");
            Console.WriteLine("  3. (Future) int32 weight storage for 2× compression");
            return;
        }
        catch (FileNotFoundException ex)
        {
            Console.WriteLine();
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine("Make sure the model is pulled in Ollama: ollama pull " + modelName);
            return;
        }
        swLoad.Stop();
        Console.WriteLine($"\rModel loaded in {swLoad.Elapsed.TotalSeconds:F1}s — {model.ModelName}, {model.Dimensions}d, {model.LayerCount} layers, {model.VocabSize} vocab");

        if (useLattice)
        {
            Console.Write("Building VocabLattice... ");
            Stopwatch swLattice = Stopwatch.StartNew();
            model.GetVocabLattice(64);
            swLattice.Stop();
            Console.WriteLine($"done in {swLattice.Elapsed.TotalSeconds:F1}s");
        }

        using HttpListener listener = new();
        string prefix = $"http://localhost:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        Console.WriteLine();
        Console.WriteLine($"Listening on {prefix}");
        Console.WriteLine("Point LVSCP or curl at this endpoint. Press Ctrl+C to stop.");
        Console.WriteLine();

        CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        while (!cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().WaitAsync(cts.Token); }
            catch (OperationCanceledException) { break; }

            _ = Task.Run(() => HandleRequest(ctx, model, useLattice, cts.Token));
        }

        Console.WriteLine("Server stopped.");
        model.Dispose();
    }

    private static async Task HandleRequest(HttpListenerContext ctx, IntegerCausalSource model,
        bool useLattice, CancellationToken ct)
    {
        HttpListenerRequest req = ctx.Request;
        HttpListenerResponse resp = ctx.Response;
        string path = req.Url?.AbsolutePath ?? "/";

        try
        {
            if (path == "/api/tags" && req.HttpMethod == "GET")
            {
                await WriteJson(resp, new
                {
                    models = new[]
                    {
                        new
                        {
                            name = model.ModelName,
                            model = model.ModelName,
                            modified_at = DateTime.UtcNow.ToString("o"),
                            size = 0L,
                            digest = "integer-lattice",
                            details = new
                            {
                                format = "gguf",
                                family = "gemma3",
                                parameter_size = "unknown",
                                quantization_level = "int64"
                            }
                        }
                    }
                });
                return;
            }

            if (path == "/api/ps" && req.HttpMethod == "GET")
            {
                await WriteJson(resp, new
                {
                    models = new[]
                    {
                        new
                        {
                            name = model.ModelName,
                            model = model.ModelName,
                            size = 0L,
                            digest = "integer-lattice",
                            expires_at = DateTime.UtcNow.AddHours(24).ToString("o")
                        }
                    }
                });
                return;
            }

            if (path == "/api/show" && req.HttpMethod == "POST")
            {
                await WriteJson(resp, new
                {
                    modelfile = "",
                    parameters = "",
                    template = "",
                    license = "",
                    details = new
                    {
                        format = "gguf",
                        family = "gemma3",
                        families = "gemma3",
                        parameter_size = "unknown",
                        quantization_level = "int64"
                    },
                    model_info = new Dictionary<string, object>
                    {
                        ["general.architecture"] = "gemma3",
                        ["general.context_length"] = 2048,
                        ["general.embedding_length"] = model.Dimensions,
                        ["general.type"] = "model"
                    }
                });
                return;
            }

            if (path == "/api/generate" && req.HttpMethod == "POST")
            {
                string body;
                using (StreamReader sr = new(req.InputStream, req.ContentEncoding))
                    body = await sr.ReadToEndAsync();

                JsonDocument doc = JsonDocument.Parse(body);
                string prompt = doc.RootElement.GetProperty("prompt").GetString() ?? "";
                bool stream = true;
                if (doc.RootElement.TryGetProperty("stream", out JsonElement streamEl))
                    stream = streamEl.GetBoolean();

                int maxTokens = 256;
                if (doc.RootElement.TryGetProperty("options", out JsonElement opts)
                    && opts.TryGetProperty("num_predict", out JsonElement numPred))
                    maxTokens = numPred.GetInt32();

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Generate: \"{Truncate(prompt, 80)}\" (max={maxTokens}, stream={stream})");
                Stopwatch swGen = Stopwatch.StartNew();
                int tokenCount = 0;

                if (stream)
                {
                    resp.ContentType = "application/x-ndjson";
                    resp.SendChunked = true;
                    using StreamWriter writer = new(resp.OutputStream, leaveOpen: false);

                    string fullResult = model.GenerateStreaming(prompt, maxTokens, token =>
                    {
                        tokenCount++;
                        string line = JsonSerializer.Serialize(new { model = model.ModelName, response = token, done = false });
                        writer.WriteLine(line);
                        writer.Flush();
                    }, useLattice, 64, ct);

                    string doneLine = JsonSerializer.Serialize(new { model = model.ModelName, response = "", done = true });
                    writer.WriteLine(doneLine);
                    writer.Flush();
                }
                else
                {
                    string result = model.Generate(prompt, maxTokens, useLattice, 64);
                    tokenCount = model.Tokenizer.Encode(result, addSpecialTokens: false).Length;
                    await WriteJson(resp, new { model = model.ModelName, response = result, done = true });
                }

                swGen.Stop();
                double tokPerSec = tokenCount > 0 ? tokenCount / swGen.Elapsed.TotalSeconds : 0;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]   → {tokenCount} tokens in {swGen.Elapsed.TotalSeconds:F1}s ({tokPerSec:F2} tok/s)");
                return;
            }

            resp.StatusCode = 404;
            await WriteJson(resp, new { error = "Not found: " + path });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}");
            try
            {
                resp.StatusCode = 500;
                await WriteJson(resp, new { error = ex.Message });
            }
            catch { }
        }
    }

    private static async Task WriteJson(HttpListenerResponse resp, object payload)
    {
        resp.ContentType = "application/json";
        string json = JsonSerializer.Serialize(payload);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 3)] + "...";

    // -----------------------------------------------------------------------
    // quality: E4-8 determinism + quality benchmark
    // -----------------------------------------------------------------------

    private static async Task RunQualityBenchmark()
    {
        string ollamaRoot = Environment.GetEnvironmentVariable("OLLAMA_MODELS") ?? @"D:\AI\OllamaModels";

        Console.WriteLine("+----------------------------------------------------------------------+");
        Console.WriteLine("|       E4-8: Quality & Determinism Benchmark                           |");
        Console.WriteLine("+----------------------------------------------------------------------+");
        Console.WriteLine();

        // Try to find a Gemma model for the benchmark
        string? ggufPath = null;
        string[] candidates = ["gemma3", "gemma3:4b", "gemma3:1b"];
        foreach (string candidate in candidates)
        {
            string name = candidate.Contains(':') ? candidate[..candidate.IndexOf(':')] : candidate;
            string? tag = candidate.Contains(':') ? candidate[(candidate.IndexOf(':') + 1)..] : null;
            ggufPath = OllamaModelLocator.LocateGgufOllama(name, ollamaRoot, tag);
            if (ggufPath is not null)
            {
                Console.WriteLine($"Found model: {candidate} → {Path.GetFileName(ggufPath)}");
                break;
            }
        }

        if (ggufPath is null)
        {
            // Fall back to local test data embeddinggemma
            string? testData = GetTestDataEmbeddingsDir();
            if (testData is not null)
                ggufPath = OllamaModelLocator.LocateGguf("embeddinggemma", testData);
        }

        if (ggufPath is null)
        {
            Console.WriteLine("No Gemma model found. Pull one with: ollama pull gemma3");
            return;
        }

        Console.Write("Loading model... ");
        Stopwatch swLoad = Stopwatch.StartNew();
        IntegerCausalSource model;
        try
        {
            model = IntegerCausalSource.Load(ggufPath, scaleBits: 30,
                onProgress: (step, total, name) =>
                {
                    if (step % 20 == 0 || step == total)
                        Console.Write($"\rLoading model... {step}/{total}    ");
                });
        }
        catch (OutOfMemoryException)
        {
            Console.WriteLine();
            Console.WriteLine("ERROR: OutOfMemoryException — model too large for int64 quantization.");
            Console.WriteLine("Falling back to embeddinggemma from TestData...");
            string? testData = GetTestDataEmbeddingsDir();
            if (testData is null)
            {
                Console.WriteLine("No fallback model available.");
                return;
            }
            string? fallback = OllamaModelLocator.LocateGguf("embeddinggemma", testData);
            if (fallback is null)
            {
                Console.WriteLine("embeddinggemma not found in TestData.");
                return;
            }
            model = IntegerCausalSource.Load(fallback, scaleBits: 30,
                onProgress: (step, total, name) =>
                {
                    if (step % 20 == 0 || step == total)
                        Console.Write($"\rLoading fallback... {step}/{total}    ");
                });
        }
        swLoad.Stop();
        Console.WriteLine($"\rLoaded: {model.ModelName}, {model.Dimensions}d, {model.LayerCount} layers in {swLoad.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine();
        using (model)
        {
            // ================================================================
            // Test 1: Determinism — 10 runs of the same prompt, verify bit-identity
            // ================================================================
            Console.WriteLine("═══ Test 1: Determinism (10 runs, bit-identity check) ═══");
            string deterministicPrompt = "The capital of France is";
            int[] deterministicTokens = model.Tokenizer.Encode(deterministicPrompt, addSpecialTokens: true);
            long[]? referenceHidden = null;
            bool allIdentical = true;

            for (int run = 0; run < 10; run++)
            {
                long[] hidden = model.ForwardCausal(deterministicTokens);
                if (referenceHidden is null)
                {
                    referenceHidden = hidden;
                    Console.WriteLine($"  Run {run + 1}/10: reference (dim={hidden.Length})");
                }
                else
                {
                    bool identical = true;
                    for (int d = 0; d < hidden.Length; d++)
                    {
                        if (hidden[d] != referenceHidden[d])
                        {
                            identical = false;
                            allIdentical = false;
                            Console.WriteLine($"  Run {run + 1}/10: MISMATCH at dim {d}");
                            break;
                        }
                    }
                    if (identical)
                        Console.WriteLine($"  Run {run + 1}/10: bit-identical ✓");
                }
            }

            Console.WriteLine($"  Result: {(allIdentical ? "PASS — 10/10 bit-identical" : "FAIL — non-determinism detected")}");
            Console.WriteLine();

            // ================================================================
            // Test 2: Token prediction consistency — same prompt, same output
            // ================================================================
            Console.WriteLine("═══ Test 2: Generation Consistency (5 runs, same prompt) ═══");
            string genPrompt = "Hello, my name is";
            string? referenceOutput = null;
            bool genConsistent = true;

            for (int run = 0; run < 5; run++)
            {
                string output = model.Generate(genPrompt, maxNewTokens: 16, useLattice: false);
                if (referenceOutput is null)
                {
                    referenceOutput = output;
                    Console.WriteLine($"  Run {run + 1}/5: \"{output}\" (reference)");
                }
                else
                {
                    bool match = output == referenceOutput;
                    if (!match) genConsistent = false;
                    Console.WriteLine($"  Run {run + 1}/5: \"{output}\" {(match ? "✓" : "MISMATCH")}");
                }
            }

            Console.WriteLine($"  Result: {(genConsistent ? "PASS" : "FAIL")}");
            Console.WriteLine();

            // ================================================================
            // Test 3: Token prediction — probe what the model predicts
            // ================================================================
            Console.WriteLine("═══ Test 3: Next-Token Probes ═══");
            string[] probePrompts =
            [
                "The capital of France is",
                "2 + 2 =",
                "public static void Main(",
                "The color of the sky is",
                "Once upon a time there was a",
            ];

            foreach (string probePrompt in probePrompts)
            {
                int[] tokens = model.Tokenizer.Encode(probePrompt, addSpecialTokens: true);
                float[] hidden = model.ForwardCausalFloat(tokens);
                int predicted = VocabLattice.ArgmaxBruteForce(hidden, model.TokenEmbeddingsFloat, model.VocabSize, model.Dimensions);
                string predictedStr = model.Tokenizer.Decode([predicted]);
                Console.WriteLine($"  \"{probePrompt}\" → [{predicted}] \"{predictedStr}\"");
            }

            Console.WriteLine();

            // ================================================================
            // Test 4: Brute-force vs Lattice recall for actual model hidden states
            // ================================================================
            Console.WriteLine("═══ Test 4: Lattice Recall on Real Hidden States ═══");
            Console.Write("  Building VocabLattice... ");
            Stopwatch swLattice = Stopwatch.StartNew();
            VocabLattice lattice = model.GetVocabLattice(64);
            swLattice.Stop();
            Console.WriteLine($"done in {swLattice.Elapsed.TotalSeconds:F1}s ({lattice.VocabSize} tokens)");

            int latticeHits = 0;
            int latticeTotal = 0;
            foreach (string probePrompt in probePrompts)
            {
                int[] tokens = model.Tokenizer.Encode(probePrompt, addSpecialTokens: true);
                float[] hidden = model.ForwardCausalFloat(tokens);

                int bruteForce = VocabLattice.ArgmaxBruteForce(hidden, model.TokenEmbeddingsFloat, model.VocabSize, model.Dimensions);
                int[] knnCandidates = lattice.QueryTopK(hidden, 64);
                (int TokenId, float Score)[] scored = VocabLattice.ScoreCandidates(hidden, knnCandidates, model.TokenEmbeddingsFloat, model.Dimensions);
                int latticeTop1 = scored.Length > 0 ? scored[0].TokenId : -1;

                latticeTotal++;
                bool hit = latticeTop1 == bruteForce;
                if (hit) latticeHits++;
                string bfStr = model.Tokenizer.Decode([bruteForce]);
                string ltStr = latticeTop1 >= 0 ? model.Tokenizer.Decode([latticeTop1]) : "?";
                Console.WriteLine($"  \"{Truncate(probePrompt, 40)}\" brute=\"{bfStr}\" lattice=\"{ltStr}\" {(hit ? "✓" : "MISS")}");
            }

            double recallRate = latticeTotal > 0 ? (double)latticeHits / latticeTotal : 0;
            Console.WriteLine($"  Recall: {latticeHits}/{latticeTotal} ({recallRate:P0})");
            Console.WriteLine();

            // ================================================================
            // Test 5: Generation speed
            // ================================================================
            Console.WriteLine("═══ Test 5: Generation Speed ═══");
            string speedPrompt = "Write a function that";
            int speedTokens = 8;

            Stopwatch swBrute = Stopwatch.StartNew();
            string bruteResult = model.Generate(speedPrompt, maxNewTokens: speedTokens, useLattice: false);
            swBrute.Stop();
            double bruteTokPerSec = speedTokens / swBrute.Elapsed.TotalSeconds;
            Console.WriteLine($"  Brute-force: {speedTokens} tokens in {swBrute.Elapsed.TotalSeconds:F1}s ({bruteTokPerSec:F2} tok/s) → \"{bruteResult}\"");

            Stopwatch swLat = Stopwatch.StartNew();
            string latticeResult = model.Generate(speedPrompt, maxNewTokens: speedTokens, useLattice: true, latticeK: 64);
            swLat.Stop();
            double latTokPerSec = speedTokens / swLat.Elapsed.TotalSeconds;
            Console.WriteLine($"  Lattice:     {speedTokens} tokens in {swLat.Elapsed.TotalSeconds:F1}s ({latTokPerSec:F2} tok/s) → \"{latticeResult}\"");
            Console.WriteLine($"  Speedup: {swBrute.Elapsed.TotalSeconds / swLat.Elapsed.TotalSeconds:F2}×");
            Console.WriteLine();

            // ================================================================
            // Summary
            // ================================================================
            Console.WriteLine("═══ Summary ═══");
            Console.WriteLine($"  Determinism:    {(allIdentical ? "PASS" : "FAIL")}");
            Console.WriteLine($"  Gen consistency: {(genConsistent ? "PASS" : "FAIL")}");
            Console.WriteLine($"  Lattice recall:  {recallRate:P0}");
            Console.WriteLine($"  Brute tok/s:     {bruteTokPerSec:F2}");
            Console.WriteLine($"  Lattice tok/s:   {latTokPerSec:F2}");
        }
    }
}
