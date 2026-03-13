using System.Collections.Frozen;
using System.Text;
/////////////////////////////
namespace SparseLattice.Gguf;

// Supports SentencePiece (▁ prefix) and Classic WordPiece (## prefix),
// auto-detected from vocab content.
public sealed class WordPieceTokenizer
{
    readonly FrozenDictionary<string, int> m_vocab;
    readonly string[] m_idToToken;
    readonly bool m_useSentencePiecePrefix;

    // U+2581 LOWER ONE EIGHTH BLOCK
    const char SpPrefix = '\u2581';

    public int VocabSize  { get; }
    public int BosTokenId { get; }
    public int EosTokenId { get; }
    public int UnkTokenId { get; }
    public int PadTokenId { get; }

    public static WordPieceTokenizer FromGguf(GgufReader reader)
    {
        if (reader.Tokens.Count == 0)
            throw new InvalidOperationException(
                "GGUF file contains no tokenizer vocabulary (tokenizer.ggml.tokens is empty).");

        return new WordPieceTokenizer(reader.Tokens, reader.BosTokenId, reader.EosTokenId, reader.UnkTokenId);
    }

    internal WordPieceTokenizer(
        IReadOnlyList<string> tokens,
        int bosTokenId,
        int eosTokenId,
        int unkTokenId,
        int padTokenId = 0)
    {
        BosTokenId = bosTokenId;
        EosTokenId = eosTokenId;
        UnkTokenId = unkTokenId;
        PadTokenId = padTokenId;
        VocabSize  = tokens.Count;

        m_idToToken = new string[tokens.Count];
        Dictionary<string, int> vocab = new(tokens.Count, StringComparer.Ordinal);
        for (int i = 0; i < tokens.Count; i++)
        {
            m_idToToken[i] = tokens[i];
            vocab.TryAdd(tokens[i], i);
        }
        m_vocab = vocab.ToFrozenDictionary(StringComparer.Ordinal);

        // Auto-detect convention: if the vocab has ▁-prefixed tokens, use SentencePiece mode.
        // Count ▁ tokens vs ## tokens to decide.
        int spCount = 0, wpCount = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Length > 1 && tokens[i][0] == SpPrefix) spCount++;
            if (tokens[i].StartsWith("##", StringComparison.Ordinal)) wpCount++;
        }
        m_useSentencePiecePrefix = spCount > wpCount;
    }

    public int[] Encode(string text, bool addSpecialTokens = true)
    {
        List<int> ids = [];

        if (addSpecialTokens)
            ids.Add(BosTokenId);

        foreach (string word in Pretokenize(text))
            ids.AddRange(EncodeWord(word));

        if (addSpecialTokens)
            ids.Add(EosTokenId);

        return [.. ids];
    }

    public string Decode(IReadOnlyList<int> tokenIds)
    {
        StringBuilder sb = new();
        foreach (int id in tokenIds)
        {
            if (id == BosTokenId || id == EosTokenId || id == PadTokenId)
                continue;
            if (id < 0 || id >= m_idToToken.Length)
                continue;

            string token = m_idToToken[id];

            if (m_useSentencePiecePrefix)
            {
                // ▁ marks a new word (add space before it)
                if (token.Length > 0 && token[0] == SpPrefix)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(token.AsSpan(1));
                }
                else
                    sb.Append(token);
            }
            else
            {
                // ## marks a continuation (no space before it)
                if (token.StartsWith("##", StringComparison.Ordinal))
                    sb.Append(token.AsSpan(2));
                else
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(token);
                }
            }
        }
        return sb.ToString();
    }

    static IEnumerable<string> Pretokenize(string text)
    {
        StringBuilder word = new();
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (word.Length > 0)
                {
                    yield return word.ToString().ToLowerInvariant();
                    word.Clear();
                }
            }
            else if (char.IsPunctuation(c) || (c < 0x7F && char.IsSymbol(c)) || IsCjkChar(c))
            {
                if (word.Length > 0)
                {
                    yield return word.ToString().ToLowerInvariant();
                    word.Clear();
                }
                yield return char.ToLowerInvariant(c).ToString();
            }
            else
                word.Append(c);
        }
        if (word.Length > 0)
            yield return word.ToString().ToLowerInvariant();
    }

    List<int> EncodeWord(string word)
    {
        if (word.Length == 0) return [];

        if (m_useSentencePiecePrefix)
            return EncodeWordSentencePiece(word);
        else
            return EncodeWordClassicWP(word);
    }

    // ▁ is prepended to the entire word, then greedy longest-match left-to-right.
    // First match consumes the ▁ (word-initial); subsequent pieces are bare (continuation).
    List<int> EncodeWordSentencePiece(string word)
    {
        // Build the ▁-prefixed word string that the tokenizer walks through.
        string prefixed = SpPrefix + word;
        int n = prefixed.Length;

        List<int> ids = [];
        int i = 0;

        while (i < n)
        {
            bool match = false;
            for (int j = n; j > i; j--)
            {
                string sub = prefixed[i..j];
                if (m_vocab.TryGetValue(sub, out int subId))
                {
                    ids.Add(subId);
                    i = j;
                    match = true;
                    break;
                }
            }

            if (!match)
            {
                // No match at this position — entire word is [UNK]
                return [UnkTokenId];
            }
        }

        return ids;
    }

    List<int> EncodeWordClassicWP(string word)
    {
        if (m_vocab.TryGetValue(word, out int fullId))
            return [fullId];

        List<int> ids = [];
        int start = 0;

        while (start < word.Length)
        {
            int end    = word.Length;
            int bestId = -1;
            string prefix = start == 0 ? "" : "##";

            while (end > start)
            {
                string sub = prefix + word[start..end];
                if (m_vocab.TryGetValue(sub, out int subId))
                {
                    bestId = subId;
                    break;
                }
                end--;
            }

            if (bestId < 0)
                return [UnkTokenId];

            ids.Add(bestId);
            start = end;
        }

        return ids;
    }

    static bool IsCjkChar(char c)
        => (c >= '\u4E00' && c <= '\u9FFF')
        || (c >= '\u3400' && c <= '\u4DBF')
        || (c >= '\uF900' && c <= '\uFAFF')
        || (c >= '\u2E80' && c <= '\u2EFF');
}
