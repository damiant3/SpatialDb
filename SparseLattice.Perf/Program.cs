using System.Diagnostics;
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

        Console.WriteLine("â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
        Console.WriteLine("â•‘     Q1: Single embedding latency  (prompt augmentation / RAG lookup)    â•‘");
        Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¦â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¦â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
        Console.WriteLine("â•‘ Method                    â•‘    ms / embed    â•‘       embeds / sec       â•‘");
        Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
        Console.WriteLine($"â•‘ Lattice (token lookup)    â•‘ {latticeSingleMs,14:F4}   â•‘ {latticeSingleOps,22:F0}   â•‘");
        Console.WriteLine($"â•‘ Ollama HTTP               â•‘ {ollamaSingleMs,14:F2}   â•‘ {ollamaSingleOps,22:F1}   â•‘");
        Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
        Console.WriteLine($"â•‘ Lattice speedup           â•‘ {su,13:F0}Ã—    â•‘                          â•‘");
        Console.WriteLine("â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•©â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•©â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");

        Console.WriteLine();
        Console.WriteLine("â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
        Console.WriteLine("â•‘     Q2: Document ingestion throughput  (batch vectorisation)            â•‘");
        Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¦â•â•â•â•â•â•â•â•â•â•â•¦â•â•â•â•â•â•â•â•â•â•â•¦â•â•â•â•â•â•â•â•â•â•â•â•â•¦â•â•â•â•â•â•â•â•â•â•â•£");
        Console.WriteLine("â•‘ Method                    â•‘  10 docs â•‘ 100 docs â•‘  500 docs  â•‘ 1000 docsâ•‘");
        Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•£");

        // Lattice 1000-doc extrapolated
        double l1000 = latticeBatchOps[2]; // 500-doc rate is representative
        Console.WriteLine($"â•‘ Lattice  (embeds/sec)     â•‘{latticeBatchOps[0],8:F0}  â•‘{latticeBatchOps[1],8:F0}  â•‘{latticeBatchOps[2],10:F0}  â•‘{l1000,8:F0}  â•‘");
        double o1000 = ollamaBatchOps[2];
        Console.WriteLine($"â•‘ Ollama   (embeds/sec)     â•‘{ollamaBatchOps[0],8:F1}  â•‘{ollamaBatchOps[1],8:F1}  â•‘{ollamaBatchOps[2],10:F1}  â•‘{o1000,8:F1}  â•‘");
        Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•£");
        Console.WriteLine($"â•‘ Lattice speedup           â•‘{latticeBatchOps[0]/ollamaBatchOps[0],7:F0}Ã—  â•‘{latticeBatchOps[1]/ollamaBatchOps[1],7:F0}Ã—  â•‘{latticeBatchOps[2]/ollamaBatchOps[2],9:F0}Ã—  â•‘{l1000/o1000,7:F0}Ã—  â•‘");
        Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•©â•â•â•â•â•â•â•â•â•â•â•©â•â•â•â•â•â•â•â•â•â•â•â•â•©â•â•â•â•â•â•â•â•â•â•â•£");

        // Time to ingest 1000 docs
        double lattice1000secs = 1000.0 / l1000;
        double ollama1000secs  = 1000.0 / o1000;
        Console.WriteLine($"â•‘ Time to ingest 1000 docs  â•‘ Lattice: {lattice1000secs * 1000,7:F1} ms        Ollama: {ollama1000secs,6:F1} s        â•‘");
        Console.WriteLine("â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•©â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");

        Console.WriteLine();
        Console.WriteLine("â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
        Console.WriteLine("â•‘     Q3: Parallel prompt pipeline  (concurrent embed for thinking model) â•‘");
        Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¦â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¦â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¦â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
        Console.WriteLine("â•‘ Method                    â•‘   c=1        â•‘   c=8        â•‘   c=32        â•‘");
        Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
        Console.WriteLine($"â•‘ Lattice  (embeds/sec)     â•‘{latticePar[0],10:F0}    â•‘{latticePar[1],10:F0}    â•‘{latticePar[2],11:F0}    â•‘");
        Console.WriteLine($"â•‘ Ollama   (embeds/sec)     â•‘{ollamaPar[0],10:F1}    â•‘{ollamaPar[1],10:F1}    â•‘{ollamaPar[2],11:F1}    â•‘");
        Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•¬â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
        Console.WriteLine($"â•‘ Lattice speedup           â•‘{latticePar[0]/ollamaPar[0],9:F0}Ã—    â•‘{latticePar[1]/ollamaPar[1],9:F0}Ã—    â•‘{latticePar[2]/ollamaPar[2],10:F0}Ã—    â•‘");
        Console.WriteLine("â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•©â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•©â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•©â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");

        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine($"  Lattice load time : {swLoad.Elapsed.TotalSeconds:F2}s  (one-time, amortised across all calls)");
        Console.WriteLine($"  Sample corpus     : {samples.Length} real code lines from solution");
        Console.WriteLine($"  Ollama batch      : measured sequentially (no native multi-string batch endpoint)");
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

            // Embed every vocab entry by probing via single-token encode
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

            Console.WriteLine("â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
            Console.WriteLine("â•‘               Memory Diagnostics â€” LatticeEmbeddingSource           â•‘");
            Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
            Console.WriteLine($"â•‘  Dimensions (d)          : {dims,10}                                â•‘");
            Console.WriteLine($"â•‘  Sample vectors measured : {vocabSize,10}                                â•‘");
            Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
            Console.WriteLine($"â•‘  Avg nnz per vector      : {avgNnz,10:F1}  / {dims}                    â•‘");
            Console.WriteLine($"â•‘  Min nnz                 : {minNnz,10}                                â•‘");
            Console.WriteLine($"â•‘  Max nnz                 : {maxNnz,10}                                â•‘");
            Console.WriteLine($"â•‘  Fully dense vectors     : {fullyDense,10}  ({100.0*fullyDense/vocabSize:F1}% of sample)           â•‘");
            Console.WriteLine($"â•‘  Sparsity                : {sparsityPct,10:F1}%                              â•‘");
            Console.WriteLine($"â•‘  Value range             : [{minVal}, {maxVal}]");
            Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
            Console.WriteLine($"â•‘  Total SparseEntries     : {totalEntries,10:N0}  (ushort+long = 10 B each)    â•‘");
            Console.WriteLine($"â•‘  Entry bytes             : {entryBytes/1024.0/1024.0,10:F2} MB                            â•‘");
            Console.WriteLine($"â•‘  Vector overhead bytes   : {overheadBytes/1024.0/1024.0,10:F2} MB                            â•‘");
            Console.WriteLine($"â•‘  Estimated total payload : {totalEstBytes/1024.0/1024.0,10:F2} MB  (entries + vector structs)  â•‘");
            Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
            Console.WriteLine($"â•‘  GC heap delta (managed) : {heapDelta/1024.0/1024.0,10:F2} MB                            â•‘");
            Console.WriteLine($"â•‘  Working set delta       : {workingDelta/1024.0/1024.0,10:F2} MB  (includes unmanaged+JIT)  â•‘");
            Console.WriteLine("â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
            // Comparison: equivalent float32 dense storage
            long denseFloat32Bytes = (long)vocabSize * dims * 4;
            Console.WriteLine($"â•‘  Dense float32 equiv     : {denseFloat32Bytes/1024.0/1024.0,10:F2} MB  ({vocabSize} Ã— {dims} Ã— 4 B)          â•‘");
            Console.WriteLine($"â•‘  Compression vs dense    : {(totalEstBytes > 0 ? (double)denseFloat32Bytes/totalEstBytes : 0),10:F1}Ã—                              â•‘");
            Console.WriteLine("â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            Console.WriteLine();

            // Spot-check a few individual queries
            Console.WriteLine("Spot-check individual embeddings (nnz / dims):");
            string[] probes = { "public int Add(int a, int b) => a + b;", "return result;",
                                "private readonly float[] m_weights;", "void Tick()", "class Program" };
            foreach (string text in probes)
            {
                var v = src.EmbedSparse(text);
                Console.WriteLine($"  {v.NonzeroCount,4}/{dims}  ({100.0*v.NonzeroCount/dims:F1}%)  \"{(text.Length > 55 ? text[..52]+"..." : text)}\"");
            }
        }
    }
}
