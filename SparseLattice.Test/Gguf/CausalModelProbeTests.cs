using SparseLattice.Gguf;
using SparseLattice.Lattice;
using SparseLattice.Math;
///////////////////////////////////////////////
namespace SparseLattice.Test.Gguf;

[TestClass]
public sealed class CausalModelProbeTests
{
    private const string OllamaRoot = @"D:\AI\OllamaModels";

    [TestMethod]
    [TestCategory("Integration")]
    public void Probe_GptOss_Architecture()
    {
        string? gguf = OllamaModelLocator.LocateGgufOllama("gpt-oss", OllamaRoot, "20b");
        if (gguf is null)
        {
            Assert.Inconclusive("gpt-oss:20b GGUF not found.");
            return;
        }

        using GgufReader reader = GgufReader.Open(gguf);
        Console.WriteLine($"Architecture: {reader.Architecture}");
        Console.WriteLine($"Name:         {reader.ModelName}");
        Console.WriteLine($"Embedding:    {reader.EmbeddingLength}");
        Console.WriteLine($"Heads:        {reader.HeadCount}");
        Console.WriteLine($"Layers:       {reader.LayerCount}");
        Console.WriteLine($"FF:           {reader.FeedForwardLength}");
        Console.WriteLine($"Context:      {reader.ContextLength}");
        Console.WriteLine($"Vocab:        {reader.Tokens.Count}");
        Console.WriteLine($"Merges:       {reader.Merges.Count}");
        Console.WriteLine($"BOS/EOS/UNK:  {reader.BosTokenId}/{reader.EosTokenId}/{reader.UnkTokenId}");
        Console.WriteLine($"Tensors:      {reader.TensorInfos.Count}");
        Console.WriteLine();

        Dictionary<uint, string> dtypeCounts = [];
        foreach (GgufTensorInfo t in reader.TensorInfos)
        {
            string key = t.DType.ToString();
            if (!Enum.IsDefined(t.DType))
                key = $"Unknown({(uint)t.DType})";
            if (!dtypeCounts.ContainsKey((uint)t.DType))
                dtypeCounts[(uint)t.DType] = key;
        }

        Console.WriteLine("Dtype distribution:");
        foreach (var group in reader.TensorInfos.GroupBy(t => (uint)t.DType))
        {
            string name = dtypeCounts[group.Key];
            Console.WriteLine($"  {name,-20} {group.Count()} tensors");
        }
        Console.WriteLine();

        string[] uniquePrefixes = reader.TensorInfos
            .Select(t => t.Name.Replace("blk.0.", "blk.N."))
            .Where(n => !n.StartsWith("blk.") || n.StartsWith("blk.N."))
            .Distinct()
            .OrderBy(n => n)
            .ToArray();

        Console.WriteLine("Tensor name patterns:");
        foreach (string name in uniquePrefixes)
        {
            GgufTensorInfo info = name.Contains("blk.N.")
                ? reader.TensorInfos.First(t => t.Name == name.Replace("blk.N.", "blk.0."))
                : reader.TensorInfos.First(t => t.Name == name);
            string dtypeName = Enum.IsDefined(info.DType) ? info.DType.ToString() : $"?({(uint)info.DType})";
            Console.WriteLine($"  {name,-50} [{string.Join(",", info.Shape),20}] {dtypeName}");
        }

        Console.WriteLine();
        Console.WriteLine("Key metadata:");
        foreach (string key in reader.Metadata.Keys
            .Where(k =>
                k.Contains("head_count") || k.Contains("kv") || k.Contains("norm") ||
                k.Contains("rope") || k.Contains("attention") || k.Contains("type") ||
                k.Contains("causal") || k.Contains("pool") || k.Contains("vocab"))
            .OrderBy(k => k))
        {
            Console.WriteLine($"  {key} = {reader.Metadata[key]}");
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void CausalAttention_LowerTriangular_MasksFuturePositions()
    {
        int seqLen = 4;
        int embd = 8;
        int nHeads = 2;
        int headDim = embd / nHeads;
        int nKvHeads = 1;
        int kvDim = nKvHeads * headDim;
        int scaleBits = 20;
        int fracBits = IntegerTranscendentals.DefaultFracBits;

        Random rng = new(42);
        long[] q = MakeRandomLongs(rng, seqLen * embd, 1000);
        long[] k = MakeRandomLongs(rng, seqLen * kvDim, 1000);
        long[] v = MakeRandomLongs(rng, seqLen * kvDim, 1000);

        long[] causalOut = IntegerAttention.CausalGroupedQueryAttention(
            q, k, v, seqLen, embd, kvDim, nHeads, nKvHeads, -scaleBits, fracBits);

        Assert.AreEqual(seqLen * embd, causalOut.Length);

        long[] firstRowCausal = new long[embd];
        Array.Copy(causalOut, 0, firstRowCausal, 0, embd);
        bool firstRowAllZero = firstRowCausal.All(x => x == 0);
        Assert.IsFalse(firstRowAllZero, "First position should have non-zero output (self-attention).");

        long[] nonCausalOut = IntegerAttention.GroupedQueryAttention(
            q, k, v, seqLen, embd, kvDim, nHeads, nKvHeads, -scaleBits, fracBits);

        long[] lastRowCausal = new long[embd];
        long[] lastRowNonCausal = new long[embd];
        Array.Copy(causalOut, (seqLen - 1) * embd, lastRowCausal, 0, embd);
        Array.Copy(nonCausalOut, (seqLen - 1) * embd, lastRowNonCausal, 0, embd);
        CollectionAssert.AreEqual(lastRowCausal, lastRowNonCausal,
            "Last position sees all positions in both causal and non-causal — outputs should match.");

        Console.WriteLine("[E4-7] Causal mask: first row has output, last row matches non-causal ✓");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void CausalAttention_Deterministic_BitIdentical()
    {
        int seqLen = 3;
        int embd = 8;
        int nHeads = 2;
        int nKvHeads = 1;
        int kvDim = nKvHeads * (embd / nHeads);
        int scaleBits = 20;

        Random rng = new(123);
        long[] q = MakeRandomLongs(rng, seqLen * embd, 500);
        long[] k = MakeRandomLongs(rng, seqLen * kvDim, 500);
        long[] v = MakeRandomLongs(rng, seqLen * kvDim, 500);

        long[] out1 = IntegerAttention.CausalGroupedQueryAttention(
            q, k, v, seqLen, embd, kvDim, nHeads, nKvHeads, -scaleBits);
        long[] out2 = IntegerAttention.CausalGroupedQueryAttention(
            q, k, v, seqLen, embd, kvDim, nHeads, nKvHeads, -scaleBits);

        CollectionAssert.AreEqual(out1, out2, "Causal attention must be bit-identical across runs.");
        Console.WriteLine("[E4-7] Causal attention determinism: bit-identical ✓");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void VocabLattice_Build_And_QueryTopK()
    {
        int vocabSize = 100;
        int dims = 16;
        Random rng = new(42);

        float[] embeddings = new float[vocabSize * dims];
        for (int i = 0; i < embeddings.Length; i++)
            embeddings[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        VocabLattice lattice = new(embeddings, vocabSize, dims, k: 10);
        Assert.AreEqual(vocabSize, lattice.VocabSize);
        Assert.AreEqual(dims, lattice.Dimensions);

        float[] query = new float[dims];
        Array.Copy(embeddings, 5 * dims, query, 0, dims);

        int[] topK = lattice.QueryTopK(query, 10);
        Assert.IsTrue(topK.Length > 0, "KNN should return at least one result.");
        Assert.IsTrue(topK.Contains(5), "Exact match for token 5 should appear in top-K.");

        Console.WriteLine($"[E4-7] VocabLattice top-10 for token 5 query: [{string.Join(", ", topK)}]");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void VocabLattice_ScoreCandidates_RanksCorrectly()
    {
        int vocabSize = 50;
        int dims = 8;
        Random rng = new(99);

        float[] embeddings = new float[vocabSize * dims];
        for (int i = 0; i < embeddings.Length; i++)
            embeddings[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        float[] query = new float[dims];
        Array.Copy(embeddings, 7 * dims, query, 0, dims);

        int[] candidates = [0, 5, 7, 10, 20];
        (int TokenId, float Score)[] scored = VocabLattice.ScoreCandidates(query, candidates, embeddings, dims);

        Assert.AreEqual(candidates.Length, scored.Length);
        Assert.AreEqual(7, scored[0].TokenId,
            "Exact match token 7 should have the highest dot-product score.");

        Console.WriteLine($"[E4-7] ScoreCandidates: top={scored[0].TokenId} (score={scored[0].Score:F4}) ✓");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void VocabLattice_ArgmaxBruteForce_MatchesExact()
    {
        int vocabSize = 50;
        int dims = 8;
        Random rng = new(77);

        float[] embeddings = new float[vocabSize * dims];
        for (int i = 0; i < embeddings.Length; i++)
            embeddings[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        float[] query = new float[dims];
        Array.Copy(embeddings, 12 * dims, query, 0, dims);

        int argmax = VocabLattice.ArgmaxBruteForce(query, embeddings, vocabSize, dims);
        Assert.AreEqual(12, argmax,
            "Brute-force argmax should find exact match at token 12.");

        Console.WriteLine($"[E4-7] ArgmaxBruteForce: token={argmax} ✓");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void VocabLattice_RecallAtK_VsBruteForce()
    {
        int vocabSize = 200;
        int dims = 32;
        int k = 32;
        int numQueries = 20;
        Random rng = new(42);

        float[] embeddings = new float[vocabSize * dims];
        for (int i = 0; i < embeddings.Length; i++)
            embeddings[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        // L2-normalize each embedding so dot-product ranking matches L2 ranking
        for (int t = 0; t < vocabSize; t++)
        {
            float norm = 0f;
            int rowBase = t * dims;
            for (int d = 0; d < dims; d++)
                norm += embeddings[rowBase + d] * embeddings[rowBase + d];
            norm = MathF.Sqrt(norm);
            if (norm > 1e-8f)
                for (int d = 0; d < dims; d++)
                    embeddings[rowBase + d] /= norm;
        }

        VocabLattice lattice = new(embeddings, vocabSize, dims, k: k);

        int hits = 0;
        int total = 0;
        for (int q = 0; q < numQueries; q++)
        {
            float[] query = new float[dims];
            for (int d = 0; d < dims; d++)
                query[d] = (float)(rng.NextDouble() * 2.0 - 1.0);

            int bruteForceTop1 = VocabLattice.ArgmaxBruteForce(query, embeddings, vocabSize, dims);

            int[] latticeTopK = lattice.QueryTopK(query, k);
            (int TokenId, float Score)[] scored =
                VocabLattice.ScoreCandidates(query, latticeTopK, embeddings, dims);

            total++;
            if (scored.Length > 0 && scored[0].TokenId == bruteForceTop1)
                hits++;
        }

        double recall = (double)hits / total;
        Console.WriteLine($"[E4-7] VocabLattice recall@{k}: {recall:F4} ({hits}/{total})");
        Assert.IsTrue(recall >= 0.7, $"Lattice recall@{k} should be ≥ 0.70, got {recall:F4}.");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_CausalSource_LoadAndForward_Smoke()
    {
        string? gguf = ResolveGgufPath("embeddinggemma");
        if (gguf is null)
        {
            Assert.Inconclusive("embeddinggemma GGUF not found.");
            return;
        }

        using IntegerCausalSource src = IntegerCausalSource.Load(gguf);
        Assert.AreEqual(768, src.Dimensions);

        int[] tokens = src.Tokenizer.Encode("Hello, world!", addSpecialTokens: true);
        float[] hidden = src.ForwardCausalFloat(tokens);

        Assert.AreEqual(768, hidden.Length);

        float norm = 0f;
        for (int i = 0; i < hidden.Length; i++)
            norm += hidden[i] * hidden[i];
        norm = MathF.Sqrt(norm);

        Console.WriteLine($"[E4-7] CausalSource smoke: dim={hidden.Length}, L2 norm={norm:F6}");
        Console.WriteLine($"[E4-7]   first 5: [{string.Join(", ", hidden.Take(5).Select(v => $"{v:G4}"))}]");
        Assert.IsTrue(norm > 1e-6f, $"Hidden state should be non-zero, got L2 norm={norm:F6}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_CausalSource_Deterministic()
    {
        string? gguf = ResolveGgufPath("embeddinggemma");
        if (gguf is null)
        {
            Assert.Inconclusive("embeddinggemma GGUF not found.");
            return;
        }

        using IntegerCausalSource src = IntegerCausalSource.Load(gguf);
        int[] tokens = src.Tokenizer.Encode("public int Add(int a, int b)", addSpecialTokens: true);

        long[] ref1 = src.ForwardCausal(tokens);
        long[] ref2 = src.ForwardCausal(tokens);

        for (int d = 0; d < ref1.Length; d++)
            Assert.AreEqual(ref1[d], ref2[d], $"Dim {d}: not bit-identical across causal runs.");

        Console.WriteLine("[E4-7] Causal determinism: bit-identical ✓");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_CausalSource_VocabLattice_TokenPrediction()
    {
        string? gguf = ResolveGgufPath("embeddinggemma");
        if (gguf is null)
        {
            Assert.Inconclusive("embeddinggemma GGUF not found.");
            return;
        }

        using IntegerCausalSource src = IntegerCausalSource.Load(gguf);
        int[] tokens = src.Tokenizer.Encode("The capital of France is", addSpecialTokens: true);

        float[] hidden = src.ForwardCausalFloat(tokens);

        VocabLattice lattice = src.GetVocabLattice(k: 64);
        int[] candidates = lattice.QueryTopK(hidden, 64);

        Assert.IsTrue(candidates.Length > 0, "VocabLattice should return candidates.");

        Console.WriteLine($"[E4-7] VocabLattice returned {candidates.Length} candidates");
        Console.WriteLine($"[E4-7]   Top candidate IDs: [{string.Join(", ", candidates.Take(10))}]");

        string[] topTokens = candidates.Take(10)
            .Select(id => src.Tokenizer.Decode([id]))
            .ToArray();
        Console.WriteLine($"[E4-7]   Top decoded: [{string.Join(", ", topTokens.Select(t => $"\"{t}\""))}]");
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static string? ResolveGgufPath(string modelName)
    {
        string? dir = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && dir is not null; depth++)
        {
            string candidate = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(candidate))
                return OllamaModelLocator.LocateGguf(modelName, candidate);
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static long[] MakeRandomLongs(Random rng, int count, int magnitude)
    {
        long[] result = new long[count];
        for (int i = 0; i < count; i++)
            result[i] = (long)(rng.NextDouble() * 2.0 * magnitude - magnitude);
        return result;
    }

    // -----------------------------------------------------------------
    // Gemma3 4B/12B probes (K-quant models)
    // -----------------------------------------------------------------

    [TestMethod]
    [TestCategory("Integration")]
    public void Probe_Gemma3_Architecture()
    {
        string? gguf = OllamaModelLocator.LocateGgufOllama("gemma3", OllamaRoot);
        if (gguf is null)
        {
            Assert.Inconclusive("gemma3 GGUF not found in Ollama.");
            return;
        }

        using GgufReader reader = GgufReader.Open(gguf);
        Console.WriteLine($"Architecture: {reader.Architecture}");
        Console.WriteLine($"Name:         {reader.ModelName}");
        Console.WriteLine($"Embedding:    {reader.EmbeddingLength}");
        Console.WriteLine($"Heads:        {reader.HeadCount}");
        Console.WriteLine($"Layers:       {reader.LayerCount}");
        Console.WriteLine($"FF:           {reader.FeedForwardLength}");
        Console.WriteLine($"Context:      {reader.ContextLength}");
        Console.WriteLine($"Vocab:        {reader.Tokens.Count}");
        Console.WriteLine($"Tensors:      {reader.TensorInfos.Count}");
        Console.WriteLine();

        Console.WriteLine("Dtype distribution:");
        foreach (IGrouping<uint, GgufTensorInfo> group in reader.TensorInfos.GroupBy(t => (uint)t.DType))
        {
            string name = Enum.IsDefined((GgufDType)group.Key)
                ? ((GgufDType)group.Key).ToString()
                : $"Unknown({group.Key})";
            Console.WriteLine($"  {name,-20} {group.Count()} tensors");
        }
        Console.WriteLine();

        string[] uniquePrefixes = reader.TensorInfos
            .Select(t => t.Name.Replace("blk.0.", "blk.N."))
            .Where(n => !n.StartsWith("blk.") || n.StartsWith("blk.N."))
            .Distinct()
            .OrderBy(n => n)
            .ToArray();

        Console.WriteLine("Tensor name patterns:");
        foreach (string name in uniquePrefixes)
        {
            GgufTensorInfo info = name.Contains("blk.N.")
                ? reader.TensorInfos.First(t => t.Name == name.Replace("blk.N.", "blk.0."))
                : reader.TensorInfos.First(t => t.Name == name);
            string dtypeName = Enum.IsDefined(info.DType) ? info.DType.ToString() : $"?({(uint)info.DType})";
            Console.WriteLine($"  {name,-50} [{string.Join(",", info.Shape),20}] {dtypeName}");
        }

        Console.WriteLine();
        Console.WriteLine("Key metadata:");
        foreach (string key in reader.Metadata.Keys
            .Where(k =>
                k.Contains("head_count") || k.Contains("kv") || k.Contains("norm") ||
                k.Contains("rope") || k.Contains("attention") || k.Contains("type") ||
                k.Contains("causal") || k.Contains("pool") || k.Contains("vocab"))
            .OrderBy(k => k))
        {
            Console.WriteLine($"  {key} = {reader.Metadata[key]}");
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Probe_Gemma3_Dequantize_Smoke()
    {
        string? gguf = OllamaModelLocator.LocateGgufOllama("gemma3", OllamaRoot);
        if (gguf is null)
        {
            Assert.Inconclusive("gemma3 GGUF not found in Ollama.");
            return;
        }

        using GgufReader reader = GgufReader.Open(gguf);

        // Test dequantization of the first layer's attention weights — these are typically K-quanted
        string[] tensorNames = ["token_embd.weight", "blk.0.attn_q.weight", "blk.0.ffn_gate.weight", "blk.0.ffn_down.weight"];
        foreach (string name in tensorNames)
        {
            if (!reader.HasTensor(name))
            {
                Console.WriteLine($"  {name}: not found, skipping");
                continue;
            }

            GgufTensorInfo info = reader.TensorInfos.First(t => t.Name == name);
            Console.Write($"  {name,-40} dtype={info.DType,-8} shape=[{string.Join(",", info.Shape)}] ... ");

            float[] data = reader.ReadTensorF32(name);

            float absMax = 0f;
            int nanCount = 0;
            int infCount = 0;
            int zeroCount = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i];
                if (float.IsNaN(v)) { nanCount++; continue; }
                if (float.IsInfinity(v)) { infCount++; continue; }
                if (v == 0f) zeroCount++;
                float a = MathF.Abs(v);
                if (a > absMax) absMax = a;
            }

            Console.WriteLine($"elements={data.Length:N0}  absMax={absMax:G4}  NaN={nanCount}  Inf={infCount}  zero={zeroCount}");
            Assert.AreEqual(0, nanCount, $"{name}: NaN values in dequantized tensor");
            Assert.AreEqual(0, infCount, $"{name}: Inf values in dequantized tensor");
            Assert.IsTrue(absMax > 0f, $"{name}: all-zero tensor");
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_Gemma3_CausalSource_LoadAndForward_Smoke()
    {
        string? gguf = OllamaModelLocator.LocateGgufOllama("gemma3", OllamaRoot);
        if (gguf is null)
        {
            Assert.Inconclusive("gemma3 GGUF not found in Ollama.");
            return;
        }

        Console.Write("Loading IntegerCausalSource from gemma3... ");
        IntegerCausalSource src;
        try
        {
            src = IntegerCausalSource.Load(gguf, scaleBits: 30,
                onProgress: (step, total, name) =>
                {
                    if (step % 50 == 0 || step == total)
                        Console.Write($"\r  Loading... {step}/{total}    ");
                });
        }
        catch (OutOfMemoryException)
        {
            Console.WriteLine("\r  OutOfMemoryException — model too large for int64 quantization on this machine.");
            Console.WriteLine("  Gemma3 4B (48 layers, 3840-dim) requires ~20 GB+ RAM for int64 weights.");
            Console.WriteLine("  This is expected. Use embeddinggemma (24 layers, 768-dim) for CPU testing.");
            Assert.Inconclusive("Model too large for int64 quantization.");
            return;
        }

        using (src)
        {
            Console.WriteLine($"\r  Loaded: {src.ModelName}, {src.Dimensions}d, {src.LayerCount} layers, {src.VocabSize} vocab");

            int[] tokens = src.Tokenizer.Encode("Hello, world!", addSpecialTokens: true);
            Console.WriteLine($"  Tokens: [{string.Join(", ", tokens)}]");

            float[] hidden = src.ForwardCausalFloat(tokens);
            Assert.AreEqual(src.Dimensions, hidden.Length);

            float norm = 0f;
            for (int i = 0; i < hidden.Length; i++)
                norm += hidden[i] * hidden[i];
            norm = MathF.Sqrt(norm);

            Console.WriteLine($"  Hidden: dim={hidden.Length}, L2 norm={norm:F6}");
            Console.WriteLine($"  First 5: [{string.Join(", ", hidden.Take(5).Select(v => $"{v:G4}"))}]");
            Assert.IsTrue(norm > 1e-6f, $"Hidden state should be non-zero, got L2 norm={norm:F6}");
        }
    }
}
