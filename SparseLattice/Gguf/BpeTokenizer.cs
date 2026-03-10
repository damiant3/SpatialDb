using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;
///////////////////////////////////////
namespace SparseLattice.Gguf;

/// <summary>
/// Byte-Pair Encoding tokenizer loaded from a GGUF model file.
/// Implements the GPT-2-style BPE algorithm with byte-level encoding,
/// as used by nomic-embed-text and similar models.
/// </summary>
public sealed class BpeTokenizer
{
    // GPT-2 byte-to-unicode mapping: maps each byte 0-255 to a unique printable
    // unicode character so that all bytes are representable as vocab tokens.
    private static readonly FrozenDictionary<byte, char> s_byteToUnicode;
    private static readonly FrozenDictionary<char, byte> s_unicodeToByte;

    // Regex for GPT-2-style pre-tokenisation (splits on contractions,
    // punctuation, whitespace, and letter/digit runs).
    // Matches: optional leading space + letters, contractions, numbers,
    // non-whitespace runs, or whitespace runs.
    private static readonly Regex s_pretokenizePattern = new(
        @"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+",
        RegexOptions.Compiled);

    static BpeTokenizer()
    {
        // Build the standard GPT-2 byte ? unicode table.
        // The 188 "safe" printable bytes map to themselves; the remaining 68
        // bytes that are control characters or whitespace map to code-points
        // starting at U+0100.
        Dictionary<byte, char> b2u = new(256);
        int nextCodePoint = 0x100;
        for (int b = 0; b < 256; b++)
        {
            bool isSafe = (b >= '!' && b <= '~')   // printable ASCII (excl. space)
                       || (b >= 0xA1 && b <= 0xAC) // latin-1 supplement part 1
                       || (b >= 0xAE && b <= 0xFF); // latin-1 supplement part 2
            b2u[(byte)b] = isSafe ? (char)b : (char)(nextCodePoint++);
        }
        s_byteToUnicode = b2u.ToFrozenDictionary();

        Dictionary<char, byte> u2b = new(256);
        foreach (KeyValuePair<byte, char> kv in b2u)
            u2b[kv.Value] = kv.Key;
        s_unicodeToByte = u2b.ToFrozenDictionary();
    }

    private readonly FrozenDictionary<string, int> m_vocab;       // token string ? id
    private readonly FrozenDictionary<(string, string), int> m_mergeRanks; // pair ? rank
    private readonly string[] m_idToToken;

    public int VocabSize   { get; }
    public int BosTokenId  { get; }
    public int EosTokenId  { get; }
    public int UnkTokenId  { get; }

    internal static char ByteToUnicodeChar(byte b) => s_byteToUnicode[b];

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    /// <summary>Builds a <see cref="BpeTokenizer"/> from an open <see cref="GgufReader"/>.</summary>
    public static BpeTokenizer FromGguf(GgufReader reader)
    {
        IReadOnlyList<string> tokens  = reader.Tokens;
        IReadOnlyList<string> merges  = reader.Merges;

        if (tokens.Count == 0)
            throw new InvalidOperationException(
                "GGUF file contains no tokenizer vocabulary (tokenizer.ggml.tokens is empty).");

        return new BpeTokenizer(tokens, merges, reader.BosTokenId, reader.EosTokenId, reader.UnkTokenId);
    }

    /// <summary>
    /// Internal constructor — also used directly by unit tests that supply
    /// a hand-built vocabulary and merge table without a GGUF file.
    /// </summary>
    internal BpeTokenizer(
        IReadOnlyList<string> tokens,
        IReadOnlyList<string> merges,
        int bosTokenId,
        int eosTokenId,
        int unkTokenId)
    {
        BosTokenId = bosTokenId;
        EosTokenId = eosTokenId;
        UnkTokenId = unkTokenId;
        VocabSize  = tokens.Count;

        m_idToToken = new string[tokens.Count];
        Dictionary<string, int> vocab = new(tokens.Count, StringComparer.Ordinal);
        for (int i = 0; i < tokens.Count; i++)
        {
            m_idToToken[i] = tokens[i];
            // Prefer the first occurrence when tokens are duplicated (shouldn't happen,
            // but be defensive without over-throwing).
            vocab.TryAdd(tokens[i], i);
        }
        m_vocab = vocab.ToFrozenDictionary(StringComparer.Ordinal);

        Dictionary<(string, string), int> ranks = new(merges.Count);
        for (int rank = 0; rank < merges.Count; rank++)
        {
            string rule = merges[rank];
            int space   = rule.IndexOf(' ');
            if (space <= 0 || space == rule.Length - 1)
                continue;   // malformed entry — skip
            string left  = rule[..space];
            string right = rule[(space + 1)..];
            ranks.TryAdd((left, right), rank);
        }
        m_mergeRanks = ranks.ToFrozenDictionary();
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Encodes <paramref name="text"/> to a sequence of token IDs.
    /// Prepends <see cref="BosTokenId"/> and appends <see cref="EosTokenId"/>
    /// when <paramref name="addSpecialTokens"/> is <c>true</c>.
    /// </summary>
    public int[] Encode(string text, bool addSpecialTokens = true)
    {
        List<int> ids = [];

        if (addSpecialTokens)
            ids.Add(BosTokenId);

        foreach (Match match in s_pretokenizePattern.Matches(text))
            ids.AddRange(EncodePretokenized(match.Value));

        if (addSpecialTokens)
            ids.Add(EosTokenId);

        return [.. ids];
    }

    /// <summary>
    /// Decodes a sequence of token IDs back to a string.
    /// Special tokens (BOS, EOS, UNK) are omitted from output.
    /// </summary>
    public string Decode(IReadOnlyList<int> tokenIds)
    {
        StringBuilder sb = new();
        foreach (int id in tokenIds)
        {
            if (id == BosTokenId || id == EosTokenId)
                continue;
            if (id < 0 || id >= m_idToToken.Length)
                continue;
            sb.Append(m_idToToken[id]);
        }

        // Convert byte-level unicode representation back to raw bytes ? string.
        string encoded = sb.ToString();
        byte[] bytes   = new byte[encoded.Length];
        int byteCount  = 0;
        foreach (char c in encoded)
        {
            if (s_unicodeToByte.TryGetValue(c, out byte b))
                bytes[byteCount++] = b;
        }
        return Encoding.UTF8.GetString(bytes, 0, byteCount);
    }

    // -----------------------------------------------------------------------
    // Private — BPE encoding of a single pre-tokenized chunk
    // -----------------------------------------------------------------------

    private List<int> EncodePretokenized(string word)
    {
        byte[] utf8            = Encoding.UTF8.GetBytes(word);
        List<string?> slots    = new(utf8.Length);
        for (int i = 0; i < utf8.Length; i++)
            slots.Add(s_byteToUnicode[utf8[i]].ToString());

        MergeUntilDone(slots);

        List<int> ids = new(slots.Count);
        foreach (string? token in slots)
        {
            if (token is null) continue;
            if (m_vocab.TryGetValue(token, out int id))
                ids.Add(id);
            else
                ids.Add(UnkTokenId);
        }
        return ids;
    }

    private void MergeUntilDone(List<string?> slots)
    {
        bool merged = true;
        while (merged)
        {
            merged = false;
            int bestRank     = int.MaxValue;
            int bestPos      = -1;
            string? bestMerged = null;

            string? prev  = null;
            int prevIdx   = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                string? cur = slots[i];
                if (cur is null) continue;
                if (prev is not null)
                {
                    if (m_mergeRanks.TryGetValue((prev, cur), out int rank) && rank < bestRank)
                    {
                        bestRank   = rank;
                        bestPos    = prevIdx;
                        bestMerged = prev + cur;
                    }
                }
                prev    = cur;
                prevIdx = i;
            }

            if (bestPos < 0)
                break;

            slots[bestPos] = bestMerged;
            int rightIdx   = bestPos + 1;
            while (rightIdx < slots.Count && slots[rightIdx] is null)
                rightIdx++;
            if (rightIdx < slots.Count)
                slots[rightIdx] = null;

            merged = true;
        }

        // Compact in-place: remove null tombstones.
        slots.RemoveAll(s => s is null);
    }
}
