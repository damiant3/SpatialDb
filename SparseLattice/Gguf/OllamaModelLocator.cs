using System.Text.Json;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

public static class OllamaModelLocator
{
    public static string? LocateGguf(string modelName, string searchDir)
    {
        string manifestPath = Path.Combine(searchDir, modelName);
        if (!File.Exists(manifestPath))
            return null;

        string? digest = ReadModelDigest(manifestPath);
        if (digest is null)
            return null;

        return ResolveBlob(digest, searchDir);
    }

    public static string? ReadModelDigest(string manifestPath)
    {
        string json;
        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (IOException)
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("layers", out JsonElement layers))
                return null;

            foreach (JsonElement layer in layers.EnumerateArray())
            {
                if (!layer.TryGetProperty("mediaType", out JsonElement mediaType))
                    continue;
                if (mediaType.GetString() != "application/vnd.ollama.image.model")
                    continue;
                if (!layer.TryGetProperty("digest", out JsonElement digest))
                    continue;
                return digest.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? ResolveBlob(string digest, string blobDir)
    {
        // Normalize colon separator to hyphen: "sha256:abc..." → "sha256-abc..."
        string fileName = digest.Replace(':', '-');
        string fullPath = Path.Combine(blobDir, fileName);

        return File.Exists(fullPath) ? fullPath : null;
    }

    public static string? LocateGgufOllama(string modelName, string ollamaRoot, string? tag = null)
    {
        string manifestDir = Path.Combine(ollamaRoot, "manifests", "registry.ollama.ai", "library", modelName);
        if (!Directory.Exists(manifestDir))
            return null;

        string? manifestPath = null;
        if (tag is not null)
        {
            string candidate = Path.Combine(manifestDir, tag);
            if (File.Exists(candidate))
                manifestPath = candidate;
        }
        else
        {
            string latest = Path.Combine(manifestDir, "latest");
            if (File.Exists(latest))
                manifestPath = latest;
            else
            {
                string[] files = Directory.GetFiles(manifestDir);
                if (files.Length > 0)
                    manifestPath = files[0];
            }
        }

        if (manifestPath is null)
            return null;

        string? digest = ReadModelDigest(manifestPath);
        if (digest is null)
            return null;

        string blobDir = Path.Combine(ollamaRoot, "blobs");
        return ResolveBlob(digest, blobDir);
    }
}
