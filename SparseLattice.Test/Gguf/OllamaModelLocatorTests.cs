using SparseLattice.Gguf;
using System.Text;
///////////////////////////////////////////////
namespace SparseLattice.Test.Gguf;

[TestClass]
public sealed class OllamaModelLocatorTests
{
    // -----------------------------------------------------------------------
    // Unit tests — no real files required
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Unit_ReadModelDigest_ValidManifest_ReturnsDigest()
    {
        string json = """
            {
              "schemaVersion": 2,
              "layers": [
                {
                  "mediaType": "application/vnd.ollama.image.license",
                  "digest": "sha256:aaaa",
                  "size": 100
                },
                {
                  "mediaType": "application/vnd.ollama.image.model",
                  "digest": "sha256:970aa74c0a90ef7482477cf803618e776e173c007bf957f635f1015bfcfef0e6",
                  "size": 274290656
                }
              ]
            }
            """;

        string tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, json, Encoding.UTF8);
            string? digest = OllamaModelLocator.ReadModelDigest(tmpFile);
            Assert.AreEqual(
                "sha256:970aa74c0a90ef7482477cf803618e776e173c007bf957f635f1015bfcfef0e6",
                digest);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [TestMethod]
    public void Unit_ReadModelDigest_NoModelLayer_ReturnsNull()
    {
        string json = """
            {
              "schemaVersion": 2,
              "layers": [
                {
                  "mediaType": "application/vnd.ollama.image.license",
                  "digest": "sha256:aaaa",
                  "size": 100
                }
              ]
            }
            """;

        string tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, json, Encoding.UTF8);
            string? digest = OllamaModelLocator.ReadModelDigest(tmpFile);
            Assert.IsNull(digest);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [TestMethod]
    public void Unit_ReadModelDigest_InvalidJson_ReturnsNull()
    {
        string tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "not json at all }{", Encoding.UTF8);
            string? digest = OllamaModelLocator.ReadModelDigest(tmpFile);
            Assert.IsNull(digest);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [TestMethod]
    public void Unit_ReadModelDigest_EmptyLayers_ReturnsNull()
    {
        string json = """{"schemaVersion":2,"layers":[]}""";
        string tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, json, Encoding.UTF8);
            string? digest = OllamaModelLocator.ReadModelDigest(tmpFile);
            Assert.IsNull(digest);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [TestMethod]
    public void Unit_ResolveBlob_ColonSeparatorNormalisedToHyphen()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Create a blob file with hyphen separator (as on disk)
            string blobPath = Path.Combine(tmpDir, "sha256-970aa74cabc");
            File.WriteAllBytes(blobPath, [0x47, 0x47, 0x55, 0x46]); // GGUF magic

            // Resolve using colon separator (as in manifest)
            string? result = OllamaModelLocator.ResolveBlob("sha256:970aa74cabc", tmpDir);
            Assert.AreEqual(blobPath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [TestMethod]
    public void Unit_ResolveBlob_MissingFile_ReturnsNull()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tmpDir);
        try
        {
            string? result = OllamaModelLocator.ResolveBlob("sha256:doesnotexist", tmpDir);
            Assert.IsNull(result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [TestMethod]
    public void Unit_LocateGguf_ManifestAndBlobPresent_ReturnsPath()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Write a manifest file named "my-model"
            string manifest = """
                {
                  "schemaVersion": 2,
                  "layers": [
                    {
                      "mediaType": "application/vnd.ollama.image.model",
                      "digest": "sha256:deadbeef1234",
                      "size": 1024
                    }
                  ]
                }
                """;
            File.WriteAllText(Path.Combine(tmpDir, "my-model"), manifest, Encoding.UTF8);

            // Write the blob file
            string blobPath = Path.Combine(tmpDir, "sha256-deadbeef1234");
            File.WriteAllBytes(blobPath, [0x47, 0x47, 0x55, 0x46]);

            string? result = OllamaModelLocator.LocateGguf("my-model", tmpDir);
            Assert.AreEqual(blobPath, result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [TestMethod]
    public void Unit_LocateGguf_ManifestMissing_ReturnsNull()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tmpDir);
        try
        {
            string? result = OllamaModelLocator.LocateGguf("nomic-embed-text", tmpDir);
            Assert.IsNull(result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [TestMethod]
    public void Unit_LocateGguf_BlobMissing_ReturnsNull()
    {
        string tmpDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tmpDir);
        try
        {
            // Write manifest but no blob
            string manifest = """
                {
                  "schemaVersion": 2,
                  "layers": [
                    {
                      "mediaType": "application/vnd.ollama.image.model",
                      "digest": "sha256:deadbeef1234",
                      "size": 1024
                    }
                  ]
                }
                """;
            File.WriteAllText(Path.Combine(tmpDir, "my-model"), manifest, Encoding.UTF8);

            string? result = OllamaModelLocator.LocateGguf("my-model", tmpDir);
            Assert.IsNull(result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    // -----------------------------------------------------------------------
    // Integration tests — require real TestData/Embeddings directory
    // -----------------------------------------------------------------------

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_NomicEmbedText_ResolvesToExistingBlob()
    {
        string embeddingsDir = GetTestDataEmbeddingsDir();
        if (!Directory.Exists(embeddingsDir))
        {
            WriteSkipMessage("TestData/Embeddings directory not found");
            return;
        }

        string? blobPath = OllamaModelLocator.LocateGguf("nomic-embed-text", embeddingsDir);
        if (blobPath is null)
        {
            WriteSkipMessage("nomic-embed-text manifest or blob not found in TestData/Embeddings");
            return;
        }

        Assert.IsTrue(File.Exists(blobPath),
            $"Resolved blob path does not exist: {blobPath}");
        Assert.IsTrue(Path.GetFileName(blobPath).StartsWith("sha256-"),
            $"Expected blob filename to start with 'sha256-', got: {Path.GetFileName(blobPath)}");

        FileInfo fi = new(blobPath);
        Assert.IsTrue(fi.Length > 100 * 1024 * 1024,
            $"Expected blob larger than 100 MB, got {fi.Length / 1024 / 1024} MB");

        Console.WriteLine($"[LOCATOR] nomic-embed-text → {blobPath} ({fi.Length / 1024 / 1024} MB)");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_EmbeddingGemma_ResolvesToExistingBlob()
    {
        string embeddingsDir = GetTestDataEmbeddingsDir();
        if (!Directory.Exists(embeddingsDir))
        {
            WriteSkipMessage("TestData/Embeddings directory not found");
            return;
        }

        string? blobPath = OllamaModelLocator.LocateGguf("embeddinggemma", embeddingsDir);
        if (blobPath is null)
        {
            WriteSkipMessage("embeddinggemma manifest or blob not found in TestData/Embeddings");
            return;
        }

        Assert.IsTrue(File.Exists(blobPath),
            $"Resolved blob path does not exist: {blobPath}");

        FileInfo fi = new(blobPath);
        Assert.IsTrue(fi.Length > 100 * 1024 * 1024,
            $"Expected blob larger than 100 MB, got {fi.Length / 1024 / 1024} MB");

        Console.WriteLine($"[LOCATOR] embeddinggemma → {blobPath} ({fi.Length / 1024 / 1024} MB)");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public void Integration_NomicManifest_DigestMatchesKnownBlob()
    {
        string embeddingsDir = GetTestDataEmbeddingsDir();
        if (!Directory.Exists(embeddingsDir))
        {
            WriteSkipMessage("TestData/Embeddings directory not found");
            return;
        }

        string manifestPath = Path.Combine(embeddingsDir, "nomic-embed-text");
        if (!File.Exists(manifestPath))
        {
            WriteSkipMessage("nomic-embed-text manifest not found");
            return;
        }

        string? digest = OllamaModelLocator.ReadModelDigest(manifestPath);
        Assert.IsNotNull(digest, "Expected a non-null digest from nomic-embed-text manifest");
        Assert.IsTrue(digest.StartsWith("sha256:"),
            $"Expected digest to start with 'sha256:', got: {digest}");

        Console.WriteLine($"[LOCATOR] nomic-embed-text digest = {digest}");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string GetTestDataEmbeddingsDir()
    {
        string? dir = Path.GetDirectoryName(typeof(OllamaModelLocatorTests).Assembly.Location);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "SparseLattice.Test", "TestData", "Embeddings");
            if (Directory.Exists(candidate)) return candidate;
            string relative = Path.Combine(dir, "TestData", "Embeddings");
            if (Directory.Exists(relative)) return relative;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.Combine(
            Path.GetDirectoryName(typeof(OllamaModelLocatorTests).Assembly.Location) ?? ".",
            "TestData", "Embeddings");
    }

    private static void WriteSkipMessage(string message)
        => Console.WriteLine($"[SKIP] {message}");
}
