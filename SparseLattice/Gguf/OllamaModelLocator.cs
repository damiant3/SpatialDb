using System.Text.Json;
///////////////////////////////////////////////
namespace SparseLattice.Gguf;

/// <summary>
/// Resolves Ollama model names to GGUF blob file paths by reading
/// the Ollama manifest JSON files stored alongside the blob files.
/// </summary>
public static class OllamaModelLocator
{
    /// <summary>
    /// Scans <paramref name="searchDir"/> for Ollama manifest files and returns
    /// the full path to the GGUF blob for <paramref name="modelName"/>.
    /// Returns <c>null</c> if no matching manifest or blob is found.
    /// </summary>
    /// <param name="modelName">
    /// The model name as it appears as a filename in the directory,
    /// e.g. <c>"nomic-embed-text"</c> or <c>"embeddinggemma"</c>.
    /// </param>
    /// <param name="searchDir">
    /// Directory containing both Ollama manifest files and blob files
    /// (named <c>sha256-{hex}</c>).
    /// </param>
    public static string? LocateGguf(string modelName, string searchDir)
    {
        if (string.IsNullOrEmpty(modelName))
            throw new ArgumentNullException(nameof(modelName));
        if (string.IsNullOrEmpty(searchDir))
            throw new ArgumentNullException(nameof(searchDir));

        string manifestPath = Path.Combine(searchDir, modelName);
        if (!File.Exists(manifestPath))
            return null;

        string? digest = ReadModelDigest(manifestPath);
        if (digest is null)
            return null;

        return ResolveBlob(digest, searchDir);
    }

    /// <summary>
    /// Reads an Ollama manifest JSON file and returns the digest of the layer
    /// with <c>mediaType</c> equal to <c>"application/vnd.ollama.image.model"</c>.
    /// Returns <c>null</c> if the file is not a valid manifest or has no model layer.
    /// </summary>
    /// <remarks>
    /// Digest is returned exactly as stored in the manifest, e.g.
    /// <c>"sha256:970aa74c..."</c> (colon separator, not hyphen).
    /// </remarks>
    public static string? ReadModelDigest(string manifestPath)
    {
        if (string.IsNullOrEmpty(manifestPath))
            throw new ArgumentNullException(nameof(manifestPath));

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

    /// <summary>
    /// Returns the full path to a blob file for the given digest in
    /// <paramref name="blobDir"/>. Blob files are named <c>sha256-{hex}</c>
    /// (hyphen separator). The digest may use either colon or hyphen separator.
    /// Returns <c>null</c> if no matching file exists.
    /// </summary>
    public static string? ResolveBlob(string digest, string blobDir)
    {
        if (string.IsNullOrEmpty(digest))
            throw new ArgumentNullException(nameof(digest));
        if (string.IsNullOrEmpty(blobDir))
            throw new ArgumentNullException(nameof(blobDir));

        // Normalize colon separator to hyphen: "sha256:abc..." → "sha256-abc..."
        string fileName = digest.Replace(':', '-');
        string fullPath = Path.Combine(blobDir, fileName);

        return File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>
    /// Scans an Ollama model storage root (containing <c>manifests/</c> and <c>blobs/</c>
    /// subdirectories) for the model matching <paramref name="modelName"/> and optional
    /// <paramref name="tag"/>. Returns the full path to the GGUF blob, or <c>null</c>.
    /// </summary>
    /// <param name="modelName">Model name, e.g. <c>"gpt-oss"</c>.</param>
    /// <param name="ollamaRoot">
    /// Root directory containing <c>manifests/registry.ollama.ai/library/</c> and <c>blobs/</c>.
    /// </param>
    /// <param name="tag">Tag/variant, e.g. <c>"20b"</c> or <c>"latest"</c>. If null, tries <c>"latest"</c> first, then the first tag found.</param>
    public static string? LocateGgufOllama(string modelName, string ollamaRoot, string? tag = null)
    {
        if (string.IsNullOrEmpty(modelName))
            throw new ArgumentNullException(nameof(modelName));
        if (string.IsNullOrEmpty(ollamaRoot))
            throw new ArgumentNullException(nameof(ollamaRoot));

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
