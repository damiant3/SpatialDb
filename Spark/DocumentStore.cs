using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// Manages a collection of loose text/markdown documents in a project directory.
/// Provides keyword-based relevance scoring for prompt conditioning (lightweight RAG).
/// Documents are indexed by extracting keywords; at query time, the most relevant
/// chunks are returned for injection into the SD prompt context.
/// </summary>
sealed class DocumentStore
{
    readonly string m_projectDir;
    readonly string m_indexPath;
    List<DocumentEntry> m_entries = [];

    public IReadOnlyList<DocumentEntry> Entries => m_entries;
    public int Count => m_entries.Count;

    public DocumentStore(string projectDir)
    {
        m_projectDir = projectDir;
        m_indexPath = Path.Combine(projectDir, ".spark_docs.json");
        Load();
    }

    // ── Index management ────────────────────────────────────────

    void Load()
    {
        if (!File.Exists(m_indexPath)) return;
        try
        {
            string json = File.ReadAllText(m_indexPath);
            m_entries = JsonSerializer.Deserialize<List<DocumentEntry>>(json) ?? [];
        }
        catch { m_entries = []; }
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(m_entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(m_indexPath, json);
    }

    /// <summary>
    /// Scan the project directory for text/markdown files and index any new ones.
    /// Existing entries are kept; deleted files are pruned.
    /// </summary>
    public int Ingest(string[] patterns)
    {
        if (patterns.Length == 0) patterns = ["*.txt", "*.md"];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int added = 0;

        foreach (string pattern in patterns)
        {
            foreach (string file in Directory.GetFiles(m_projectDir, pattern, SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                seen.Add(name);

                // Skip if already indexed and file hasn't changed
                DocumentEntry? existing = m_entries.FirstOrDefault(
                    e => e.FileName.Equals(name, StringComparison.OrdinalIgnoreCase));
                DateTime lastWrite = File.GetLastWriteTimeUtc(file);
                if (existing is not null && existing.IndexedUtc >= lastWrite)
                    continue;

                // Read and index
                try
                {
                    string content = File.ReadAllText(file);
                    string[] keywords = ExtractKeywords(content);
                    string summary = content.Length > 500 ? content[..500] : content;

                    if (existing is not null)
                    {
                        existing.Content = content;
                        existing.Summary = summary;
                        existing.Keywords = keywords;
                        existing.IndexedUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        m_entries.Add(new DocumentEntry
                        {
                            FileName = name,
                            Content = content,
                            Summary = summary,
                            Keywords = keywords,
                            IndexedUtc = DateTime.UtcNow,
                            Role = ClassifyRole(name),
                        });
                        added++;
                    }
                }
                catch { /* skip unreadable files */ }
            }
        }

        // Prune deleted files
        m_entries.RemoveAll(e => !seen.Contains(e.FileName));

        if (added > 0 || m_entries.Count > 0) Save();
        return added;
    }

    /// <summary>
    /// Add or update a document by name. Writes to disk and updates the index.
    /// </summary>
    public void Put(string fileName, string content, string role = "reference")
    {
        string path = Path.Combine(m_projectDir, fileName);
        File.WriteAllText(path, content);

        string[] keywords = ExtractKeywords(content);
        string summary = content.Length > 500 ? content[..500] : content;

        DocumentEntry? existing = m_entries.FirstOrDefault(
            e => e.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Content = content;
            existing.Summary = summary;
            existing.Keywords = keywords;
            existing.IndexedUtc = DateTime.UtcNow;
            existing.Role = role;
        }
        else
        {
            m_entries.Add(new DocumentEntry
            {
                FileName = fileName,
                Content = content,
                Summary = summary,
                Keywords = keywords,
                IndexedUtc = DateTime.UtcNow,
                Role = role,
            });
        }
        Save();
    }

    /// <summary>
    /// Get the content of a document by filename.
    /// </summary>
    public string? Get(string fileName)
    {
        DocumentEntry? entry = m_entries.FirstOrDefault(
            e => e.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;

        // Re-read from disk for freshness
        string path = Path.Combine(m_projectDir, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : entry.Content;
    }

    // ── Relevance queries (lightweight RAG) ─────────────────────

    /// <summary>
    /// Given a prompt string, find the most relevant document chunks.
    /// Returns up to <paramref name="maxChunks"/> text snippets sorted by relevance.
    /// </summary>
    public List<(string source, string chunk, double score)> FindRelevant(
        string promptText, int maxChunks = 3, int chunkSize = 300)
    {
        string[] queryTokens = Tokenize(promptText.ToLowerInvariant());
        if (queryTokens.Length == 0) return [];

        List<(string source, string chunk, double score)> scored = [];

        foreach (DocumentEntry entry in m_entries)
        {
            // Score entire doc by keyword overlap
            double docScore = ScoreKeywordOverlap(queryTokens, entry.Keywords);
            if (docScore <= 0) continue;

            // Split content into chunks and score each
            string[] chunks = ChunkText(entry.Content, chunkSize);
            foreach (string chunk in chunks)
            {
                string[] chunkTokens = Tokenize(chunk.ToLowerInvariant());
                double chunkScore = ScoreKeywordOverlap(queryTokens, chunkTokens);
                if (chunkScore > 0)
                    scored.Add((entry.FileName, chunk.Trim(), chunkScore + docScore * 0.3));
            }
        }

        return scored
            .OrderByDescending(s => s.score)
            .Take(maxChunks)
            .ToList();
    }

    /// <summary>
    /// Build a context string from the most relevant documents for a given prompt.
    /// This is injected into the SD prompt to ground the generation in the story world.
    /// </summary>
    public string BuildContext(string promptText, int maxTokens = 400)
    {
        List<(string source, string chunk, double score)> chunks = FindRelevant(promptText, maxChunks: 5);
        if (chunks.Count == 0) return "";

        List<string> parts = [];
        int totalLen = 0;
        foreach ((string source, string chunk, _) in chunks)
        {
            if (totalLen + chunk.Length > maxTokens * 4) break; // rough char-to-token ratio
            parts.Add(chunk);
            totalLen += chunk.Length;
        }

        return string.Join(" ", parts);
    }

    // ── Text processing helpers ─────────────────────────────────

    static string[] ExtractKeywords(string text)
    {
        // Extract significant words (3+ chars, no common stopwords)
        string[] tokens = Tokenize(text.ToLowerInvariant());
        HashSet<string> stopwords =
        [
            "the", "and", "for", "are", "but", "not", "you", "all", "can", "her",
            "was", "one", "our", "out", "has", "have", "had", "this", "that", "with",
            "they", "from", "been", "said", "each", "which", "their", "will", "other",
            "about", "many", "then", "them", "these", "some", "would", "into",
        ];

        return tokens
            .Where(t => t.Length >= 3 && !stopwords.Contains(t))
            .Distinct()
            .Take(200)
            .ToArray();
    }

    static string[] Tokenize(string text)
        => Regex.Split(text, @"[^a-zA-Z0-9']+")
            .Where(w => w.Length > 0)
            .ToArray();

    static double ScoreKeywordOverlap(string[] queryTokens, string[] docTokens)
    {
        if (docTokens.Length == 0) return 0;
        HashSet<string> docSet = new(docTokens, StringComparer.OrdinalIgnoreCase);
        int hits = queryTokens.Count(t => docSet.Contains(t));
        return hits > 0 ? (double)hits / queryTokens.Length : 0;
    }

    static string[] ChunkText(string text, int chunkSize)
    {
        if (text.Length <= chunkSize) return [text];

        List<string> chunks = [];
        // Split on paragraph boundaries, then fill chunks
        string[] paragraphs = Regex.Split(text, @"\n\s*\n");
        string current = "";
        foreach (string para in paragraphs)
        {
            if (current.Length + para.Length > chunkSize && current.Length > 0)
            {
                chunks.Add(current);
                current = "";
            }
            current += (current.Length > 0 ? "\n\n" : "") + para;
        }
        if (current.Length > 0) chunks.Add(current);
        return [.. chunks];
    }

    static string ClassifyRole(string fileName)
    {
        string lower = fileName.ToLowerInvariant();
        if (lower.Contains("prompt")) return "prompts";
        if (lower.Contains("universe") || lower.Contains("glossary") || lower.Contains("lore")) return "universe";
        if (lower.Contains("character") || lower.Contains("npc")) return "characters";
        if (lower.Contains("location") || lower.Contains("place") || lower.Contains("world")) return "locations";
        return "reference";
    }
}

sealed class DocumentEntry
{
    [JsonPropertyName("fileName")]  public string FileName { get; set; } = "";
    [JsonPropertyName("role")]      public string Role { get; set; } = "reference";
    [JsonPropertyName("summary")]   public string Summary { get; set; } = "";
    [JsonPropertyName("keywords")]  public string[] Keywords { get; set; } = [];
    [JsonPropertyName("indexedUtc")] public DateTime IndexedUtc { get; set; }

    // Content is kept in memory after load but not serialized to the index
    // (the source file is the single source of truth).
    [JsonIgnore] public string Content { get; set; } = "";
}
