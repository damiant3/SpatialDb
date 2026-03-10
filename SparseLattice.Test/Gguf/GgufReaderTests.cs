using SparseLattice.Gguf;
using System.Buffers.Binary;
using System.Text;
///////////////////////////////////////////////
namespace SparseLattice.Test.Gguf;

[TestClass]
public sealed class GgufReaderTests
{
    // -----------------------------------------------------------------------
    // Helpers — minimal GGUF binary builder
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal valid GGUF byte sequence with the given metadata and
    /// one optional F32 tensor, writes it to a temp file, and returns the path.
    /// Caller is responsible for deleting the file.
    /// </summary>
    private static string WriteTempGguf(
        uint version,
        IEnumerable<(string key, GgufValueType type, object value)> metadata,
        (string name, float[] data)? tensor = null)
    {
        using MemoryStream ms = new();
        using BinaryWriter w  = new(ms, Encoding.UTF8, leaveOpen: true);

        // Magic: "GGUF" = 0x47 0x47 0x55 0x46 (little-endian uint32 = 0x46554747)
        w.Write(0x46554747u);
        w.Write(version);

        // TensorCount
        ulong tensorCount = tensor.HasValue ? 1UL : 0UL;
        w.Write(tensorCount);

        // MetadataKVCount
        List<(string key, GgufValueType type, object value)> kvList = [..metadata];
        w.Write((ulong)kvList.Count);

        // Metadata entries
        foreach ((string key, GgufValueType type, object value) in kvList)
        {
            WriteGgufString(w, key);
            w.Write((uint)type);
            WriteGgufValue(w, type, value);
        }

        // Tensor info table
        if (tensor.HasValue)
        {
            WriteGgufString(w, tensor.Value.name);
            w.Write(1u);                          // ndim = 1
            w.Write((ulong)tensor.Value.data.Length); // shape[0]
            w.Write((uint)GgufDType.F32);
            w.Write(0UL);                         // offset = 0 from tensor data start
        }

        // Pad to 32-byte alignment
        long pos     = ms.Position;
        long aligned = (pos + 31L) & ~31L;
        long padding = aligned - pos;
        for (long i = 0; i < padding; i++) w.Write((byte)0);

        // Tensor data
        if (tensor.HasValue)
        {
            foreach (float f in tensor.Value.data)
                w.Write(f);
        }

        w.Flush();
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static void WriteGgufString(BinaryWriter w, string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        w.Write((ulong)bytes.Length);
        w.Write(bytes);
    }

    private static void WriteGgufValue(BinaryWriter w, GgufValueType type, object value)
    {
        switch (type)
        {
            case GgufValueType.UInt32:  w.Write((uint)value);   break;
            case GgufValueType.Int32:   w.Write((int)value);    break;
            case GgufValueType.Float32: w.Write((float)value);  break;
            case GgufValueType.Bool:    w.Write((bool)value ? (byte)1 : (byte)0); break;
            case GgufValueType.String:  WriteGgufString(w, (string)value); break;
            case GgufValueType.UInt64:  w.Write((ulong)value);  break;
            case GgufValueType.Int64:   w.Write((long)value);   break;
            default:
                throw new NotSupportedException($"WriteGgufValue: type {type} not supported in test builder");
        }
    }

    // -----------------------------------------------------------------------
    // Unit tests — magic / version validation
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_Open_BadMagic_Throws()
    {
        byte[] bytes = [0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00];
        string path  = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);
            Assert.ThrowsException<InvalidDataException>(() => GgufReader.Open(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Unit_Open_UnsupportedVersion_Throws()
    {
        using MemoryStream ms = new();
        using BinaryWriter w  = new(ms);
        w.Write(0x46554747u);  // magic
        w.Write(1u);           // version 1 — not supported
        w.Write(0UL);          // tensorCount
        w.Write(0UL);          // metadataCount
        w.Flush();
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, ms.ToArray());
            Assert.ThrowsException<InvalidDataException>(() => GgufReader.Open(path));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Unit_Open_NonExistentFile_Throws()
    {
        Assert.ThrowsException<FileNotFoundException>(
            () => GgufReader.Open(@"C:\does\not\exist.gguf"));
    }

    // -----------------------------------------------------------------------
    // Unit tests — metadata round-trip
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_Metadata_StringValue_RoundTrips()
    {
        string path = WriteTempGguf(3,
        [
            ("general.architecture", GgufValueType.String, "nomic-bert"),
            ("general.name",         GgufValueType.String, "nomic-embed-text"),
        ]);
        try
        {
            using GgufReader r = GgufReader.Open(path);
            Assert.AreEqual(3u, r.Version);
            Assert.AreEqual("nomic-bert",       r.Architecture);
            Assert.AreEqual("nomic-embed-text", r.ModelName);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Unit_Metadata_UInt32Value_RoundTrips()
    {
        string path = WriteTempGguf(3,
        [
            ("general.architecture",              GgufValueType.String, "bert"),
            ("bert.embedding_length",             GgufValueType.UInt32, 768u),
            ("bert.context_length",               GgufValueType.UInt32, 512u),
            ("bert.attention.head_count",         GgufValueType.UInt32, 12u),
            ("bert.block_count",                  GgufValueType.UInt32, 6u),
            ("bert.feed_forward_length",          GgufValueType.UInt32, 3072u),
        ]);
        try
        {
            using GgufReader r = GgufReader.Open(path);
            Assert.AreEqual(768,  r.EmbeddingLength);
            Assert.AreEqual(512,  r.ContextLength);
            Assert.AreEqual(12,   r.HeadCount);
            Assert.AreEqual(6,    r.LayerCount);
            Assert.AreEqual(3072, r.FeedForwardLength);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Unit_Metadata_MissingKeys_ReturnDefaults()
    {
        string path = WriteTempGguf(3, [("general.architecture", GgufValueType.String, "bert")]);
        try
        {
            using GgufReader r = GgufReader.Open(path);
            Assert.AreEqual(0, r.EmbeddingLength);
            Assert.AreEqual(0, r.HeadCount);
            Assert.AreEqual(0, r.LayerCount);
        }
        finally { File.Delete(path); }
    }

    // -----------------------------------------------------------------------
    // Unit tests — F32 tensor round-trip
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_F32Tensor_RoundTrips()
    {
        float[] original = [1.0f, -2.0f, 3.14f, 0.0f, -0.001f];
        string path = WriteTempGguf(3,
            [("general.architecture", GgufValueType.String, "bert")],
            ("test_tensor", original));
        try
        {
            using GgufReader r = GgufReader.Open(path);
            Assert.IsTrue(r.HasTensor("test_tensor"));
            float[] loaded = r.ReadTensorF32("test_tensor");
            Assert.AreEqual(original.Length, loaded.Length);
            for (int i = 0; i < original.Length; i++)
                Assert.AreEqual(original[i], loaded[i], 1e-6f,
                    $"Element {i} mismatch");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Unit_ReadTensorF32_UnknownName_Throws()
    {
        string path = WriteTempGguf(3, [("general.architecture", GgufValueType.String, "bert")]);
        try
        {
            using GgufReader r = GgufReader.Open(path);
            Assert.ThrowsException<KeyNotFoundException>(
                () => r.ReadTensorF32("does_not_exist"));
        }
        finally { File.Delete(path); }
    }

    // -----------------------------------------------------------------------
    // Unit tests — F16 dequantization
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_HalfToFloat_KnownValues()
    {
        // 1.0f in F16 = 0x3C00
        Assert.AreEqual(1.0f,  GgufReader.HalfToFloat(0x3C00), 1e-4f);
        // -1.0f in F16 = 0xBC00
        Assert.AreEqual(-1.0f, GgufReader.HalfToFloat(0xBC00), 1e-4f);
        // 0.0f in F16 = 0x0000
        Assert.AreEqual(0.0f,  GgufReader.HalfToFloat(0x0000), 1e-6f);
        // 0.5f in F16 = 0x3800
        Assert.AreEqual(0.5f,  GgufReader.HalfToFloat(0x3800), 1e-4f);
        // -0.5f in F16 = 0xB800
        Assert.AreEqual(-0.5f, GgufReader.HalfToFloat(0xB800), 1e-4f);
        // 2.0f in F16 = 0x4000
        Assert.AreEqual(2.0f,  GgufReader.HalfToFloat(0x4000), 1e-4f);
    }

    // -----------------------------------------------------------------------
    // Unit tests — Q8_0 dequantization
    // -----------------------------------------------------------------------

    // Build a temp GGUF with a Q8_0 tensor written raw into the tensor data section.
    // One block of 32 elements: scale = 0.5 (F16), values = [1..32].
    [TestMethod]
    public void Unit_Q8_0_SingleBlock_DequantizesCorrectly()
    {
        const int Elements   = 32;
        const ushort ScaleF16 = 0x3800;   // 0.5f in F16
        float expectedScale   = 0.5f;

        byte[] blockBytes = new byte[2 + Elements];
        blockBytes[0] = (byte)(ScaleF16 & 0xFF);
        blockBytes[1] = (byte)(ScaleF16 >> 8);
        for (int i = 0; i < Elements; i++)
            blockBytes[2 + i] = (byte)(sbyte)(i - 16); // values -16..15

        string path = WriteRawTensorGguf("q8_tensor", GgufDType.Q8_0,
            new int[] { Elements }, blockBytes);
        try
        {
            using GgufReader r = GgufReader.Open(path);
            float[] result = r.ReadTensorF32("q8_tensor");
            Assert.AreEqual(Elements, result.Length);
            for (int i = 0; i < Elements; i++)
            {
                float expected = expectedScale * (float)(sbyte)(i - 16);
                Assert.AreEqual(expected, result[i], 1e-5f, $"element {i}");
            }
        }
        finally { File.Delete(path); }
    }

    // -----------------------------------------------------------------------
    // Unit tests — Q4_0 dequantization
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_Q4_0_SingleBlock_DequantizesCorrectly()
    {
        const int Elements    = 32;
        const ushort ScaleF16 = 0x3C00;  // 1.0f in F16
        float expectedScale   = 1.0f;

        // 16 bytes encoding 32 nibbles: nibble[i] = i % 16 (0–15)
        // decoded value = nibble - 8 (-8..7)
        byte[] blockBytes = new byte[2 + 16];
        blockBytes[0] = (byte)(ScaleF16 & 0xFF);
        blockBytes[1] = (byte)(ScaleF16 >> 8);
        for (int j = 0; j < 16; j++)
        {
            int lo = (j * 2)     % 16;  // nibble 0,2,4,...
            int hi = (j * 2 + 1) % 16;  // nibble 1,3,5,...
            blockBytes[2 + j] = (byte)((hi << 4) | lo);
        }

        string path = WriteRawTensorGguf("q4_tensor", GgufDType.Q4_0,
            new int[] { Elements }, blockBytes);
        try
        {
            using GgufReader r = GgufReader.Open(path);
            float[] result = r.ReadTensorF32("q4_tensor");
            Assert.AreEqual(Elements, result.Length);
            for (int i = 0; i < Elements; i++)
            {
                byte raw      = blockBytes[2 + i / 2];
                int nibble    = (i % 2 == 0) ? (raw & 0x0F) : (raw >> 4);
                float expected = expectedScale * (float)(nibble - 8);
                Assert.AreEqual(expected, result[i], 1e-5f, $"element {i}");
            }
        }
        finally { File.Delete(path); }
    }

    // -----------------------------------------------------------------------
    // Unit tests — tensor index
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_HasTensor_ReturnsTrueForPresent_FalseForAbsent()
    {
        string path = WriteTempGguf(3,
            [("general.architecture", GgufValueType.String, "bert")],
            ("my_weight", new float[] { 1f, 2f, 3f }));
        try
        {
            using GgufReader r = GgufReader.Open(path);
            Assert.IsTrue(r.HasTensor("my_weight"));
            Assert.IsFalse(r.HasTensor("no_such_tensor"));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void Unit_TensorInfos_Count_Matches()
    {
        string path = WriteTempGguf(3,
            [("general.architecture", GgufValueType.String, "bert")],
            ("token_embd.weight", new float[] { 1f, 2f }));
        try
        {
            using GgufReader r = GgufReader.Open(path);
            Assert.AreEqual(1, r.TensorInfos.Count);
            Assert.AreEqual("token_embd.weight", r.TensorInfos[0].Name);
            Assert.AreEqual(GgufDType.F32, r.TensorInfos[0].DType);
        }
        finally { File.Delete(path); }
    }

    // -----------------------------------------------------------------------
    // Unit tests — GgufValue typed accessors
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_GgufValue_TypeMismatch_Throws()
    {
        string path = WriteTempGguf(3,
        [
            ("general.architecture", GgufValueType.String,  "bert"),
            ("some.uint",            GgufValueType.UInt32, 42u),
        ]);
        try
        {
            using GgufReader r = GgufReader.Open(path);
            // Accessing a UInt32 as string should throw
            Assert.ThrowsException<InvalidOperationException>(
                () => r.Metadata["some.uint"].AsString());
            // Accessing a string as uint should throw
            Assert.ThrowsException<InvalidOperationException>(
                () => r.Metadata["general.architecture"].AsUInt32());
        }
        finally { File.Delete(path); }
    }

    // -----------------------------------------------------------------------
    // Integration tests — real GGUF file (nomic-embed-text)
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_NomicEmbedText_ArchitectureAndDimensions()
    {
        string? ggufPath = FindNomicGguf();
        if (ggufPath is null) { WriteSkip("nomic-embed-text GGUF not found"); return; }

        using GgufReader r = GgufReader.Open(ggufPath);

        Console.WriteLine($"[GGUF] Architecture    : {r.Architecture}");
        Console.WriteLine($"[GGUF] ModelName       : {r.ModelName}");
        Console.WriteLine($"[GGUF] EmbeddingLength : {r.EmbeddingLength}");
        Console.WriteLine($"[GGUF] ContextLength   : {r.ContextLength}");
        Console.WriteLine($"[GGUF] HeadCount       : {r.HeadCount}");
        Console.WriteLine($"[GGUF] LayerCount      : {r.LayerCount}");
        Console.WriteLine($"[GGUF] FeedForward     : {r.FeedForwardLength}");
        Console.WriteLine($"[GGUF] Version         : {r.Version}");
        Console.WriteLine($"[GGUF] Tensors         : {r.TensorInfos.Count}");
        Console.WriteLine($"[GGUF] Vocab size      : {r.Tokens.Count}");

        // nomic-embed-text is a BERT-family model with 768 dimensions
        Assert.IsTrue(r.EmbeddingLength == 768,
            $"Expected EmbeddingLength=768, got {r.EmbeddingLength}");
        Assert.IsTrue(r.Tokens.Count > 30_000,
            $"Expected vocab > 30000, got {r.Tokens.Count}");
        Assert.IsTrue(r.TensorInfos.Count > 100,
            $"Expected >100 tensors, got {r.TensorInfos.Count}");
        Assert.IsFalse(string.IsNullOrEmpty(r.Architecture),
            "Architecture should not be empty");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_NomicEmbedText_TokenEmbeddingTensor_HasCorrectShape()
    {
        string? ggufPath = FindNomicGguf();
        if (ggufPath is null) { WriteSkip("nomic-embed-text GGUF not found"); return; }

        using GgufReader r = GgufReader.Open(ggufPath);

        // Print all tensor names for diagnostics
        Console.WriteLine($"[GGUF] First 20 tensors:");
        foreach (GgufTensorInfo t in r.TensorInfos.Take(20))
            Console.WriteLine($"  {t.Name,-50} shape=[{string.Join(",", t.Shape)}] dtype={t.DType}");

        // The token embedding matrix must exist
        GgufTensorInfo? embd = r.TensorInfos.FirstOrDefault(t =>
            t.Name.Contains("token_embd") || t.Name.Contains("embedding"));
        Assert.IsNotNull(embd, "Expected a token embedding tensor");
        Console.WriteLine($"[GGUF] Token embd tensor: {embd.Name} shape=[{string.Join(",", embd.Shape)}]");

        // Shape should include the embedding dimension (768)
        Assert.IsTrue(Array.Exists(embd.Shape, d => d == 768),
            $"Expected shape to contain 768, got [{string.Join(",", embd.Shape)}]");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_NomicEmbedText_ReadSmallTensor_DoesNotThrow()
    {
        string? ggufPath = FindNomicGguf();
        if (ggufPath is null) { WriteSkip("nomic-embed-text GGUF not found"); return; }

        using GgufReader r = GgufReader.Open(ggufPath);

        // Find the smallest tensor (by element count) to keep the test fast
        GgufTensorInfo? smallest = r.TensorInfos
            .Where(t => t.DType is GgufDType.F32 or GgufDType.F16 or GgufDType.Q8_0 or GgufDType.Q4_0)
            .OrderBy(t => t.ElementCount)
            .FirstOrDefault();

        Assert.IsNotNull(smallest, "Expected at least one readable tensor");
        Console.WriteLine($"[GGUF] Reading smallest tensor: {smallest.Name} " +
            $"({smallest.ElementCount} elements, dtype={smallest.DType})");

        float[] data = r.ReadTensorF32(smallest.Name);
        Assert.AreEqual((int)smallest.ElementCount, data.Length);

        // Sanity: not all zeros, not all NaN
        bool hasNonZero = data.Any(v => v != 0f);
        bool hasNaN     = data.Any(float.IsNaN);
        Assert.IsTrue(hasNonZero, "Tensor is unexpectedly all-zeros");
        Assert.IsFalse(hasNaN,    "Tensor contains NaN values after dequantization");
        Console.WriteLine($"[GGUF] Sample values: [{string.Join(", ", data.Take(5).Select(v => v.ToString("F4")))}]");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_NomicEmbedText_Metadata_PrintAll()
    {
        string? ggufPath = FindNomicGguf();
        if (ggufPath is null) { WriteSkip("nomic-embed-text GGUF not found"); return; }

        using GgufReader r = GgufReader.Open(ggufPath);
        Console.WriteLine($"[GGUF] Metadata ({r.Metadata.Count} keys):");
        foreach ((string key, GgufValue v) in r.Metadata.OrderBy(kv => kv.Key))
        {
            string display = v.Type == GgufValueType.Array
                ? $"[array len={v.AsArray().Count}]"
                : v.ToString()!.Length > 80
                    ? v.ToString()![..80] + "..."
                    : v.ToString()!;
            Console.WriteLine($"  {key,-55} = {display}");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes a GGUF file with a single tensor of a specific dtype, whose raw bytes
    /// are written directly into the tensor data section. Used for quantization tests.
    /// </summary>
    private static string WriteRawTensorGguf(
        string tensorName, GgufDType dtype, int[] shape, byte[] rawBytes)
    {
        using MemoryStream ms = new();
        using BinaryWriter w  = new(ms, Encoding.UTF8, leaveOpen: true);

        // Header
        w.Write(0x46554747u);  // magic
        w.Write(3u);           // version
        w.Write(1UL);          // tensorCount
        w.Write(1UL);          // metadataCount

        // Metadata: general.architecture = "bert"
        WriteGgufString(w, "general.architecture");
        w.Write((uint)GgufValueType.String);
        WriteGgufString(w, "bert");

        // Tensor info
        WriteGgufString(w, tensorName);
        w.Write((uint)shape.Length);
        foreach (int d in shape) w.Write((ulong)d);
        w.Write((uint)dtype);
        w.Write(0UL);  // offset = 0

        // Pad to 32-byte alignment
        long pos     = ms.Position;
        long aligned = (pos + 31L) & ~31L;
        for (long i = 0; i < aligned - pos; i++) w.Write((byte)0);

        // Raw tensor bytes
        w.Write(rawBytes);
        w.Flush();

        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static string? FindNomicGguf()
    {
        string embeddingsDir = GetTestDataEmbeddingsDir();
        if (!Directory.Exists(embeddingsDir)) return null;
        return OllamaModelLocator.LocateGguf("nomic-embed-text", embeddingsDir);
    }

    private static string GetTestDataEmbeddingsDir()
    {
        string? dir = Path.GetDirectoryName(typeof(GgufReaderTests).Assembly.Location);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "SparseLattice.Test", "TestData", "Embeddings");
            if (Directory.Exists(candidate)) return candidate;
            string relative = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(relative)) return relative;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine(
            Path.GetDirectoryName(typeof(GgufReaderTests).Assembly.Location) ?? ".",
            "TestData", "Embeddings");
    }

    private static void WriteSkip(string msg) => Console.WriteLine($"[SKIP] {msg}");
}
