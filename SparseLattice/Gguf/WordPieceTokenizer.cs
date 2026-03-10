using System.Collections.Frozen;
using System.Text;
/////////////////////////////
namespace SparseLattice.Gguf;

/// <summary>
/// WordPiece / SentencePiece tokenizer for BERT-family models (nomic-embed-text, bert-base, etc.).
/// Vocabulary and token types are loaded directly from a <see cref="GgufReader"/>.
/// </summary>
/// <remarks>
/// Supports two vocabulary conventions, auto-detected from the vocab content:
/// <list type="bullet">
///   <item><b>SentencePiece (▁ prefix)</b>: word-initial subwords are prefixed with U+2581 (<c>▁</c>),
///         continuation subwords use the bare form.  Used by nomic-embed-text.</item>
///   <item><b>Classic WordPiece (## prefix)</b>: word-initial subwords are bare,
///         continuation subwords are prefixed with <c>##</c>.  Used by bert-base-uncased.</item>
/// </list>
/// </remarks>
public sealed class WordPieceTokenizer
{
    private readonly FrozenDictionary<string, int> m_vocab;
    private readonly string[] m_idToToken;
    private readonly bool m_useSentencePiecePrefix; // true = ▁ convention, false = ## convention

    /// <summary>SentencePiece word-initial marker (U+2581 LOWER ONE EIGHTH BLOCK).</summary>
    private const char SpPrefix = '\u2581';

    public int VocabSize  { get; }
    public int BosTokenId { get; }   // [CLS] = 101
    public int EosTokenId { get; }   // [SEP] = 102
    public int UnkTokenId { get; }   // [UNK] = 100
    public int PadTokenId { get; }   // [PAD] = 0

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    /// <summary>Builds a <see cref="WordPieceTokenizer"/> from an open <see cref="GgufReader"/>.</summary>
    public static WordPieceTokenizer FromGguf(GgufReader reader)
    {
        if (reader.Tokens.Count == 0)
            throw new InvalidOperationException(
                "GGUF file contains no tokenizer vocabulary (tokenizer.ggml.tokens is empty).");

        return new WordPieceTokenizer(reader.Tokens, reader.BosTokenId, reader.EosTokenId, reader.UnkTokenId);
    }

    /// <summary>
    /// Internal constructor — accepts a pre-built vocabulary list.
    /// Used by unit tests that don't require a GGUF file.
    /// </summary>
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

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Encodes <paramref name="text"/> to a sequence of token IDs using WordPiece.
    /// Prepends [CLS] and appends [SEP] when <paramref name="addSpecialTokens"/> is <c>true</c>.
    /// </summary>
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

    /// <summary>Decodes token IDs back to a string, removing subword prefixes.</summary>
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

    // -----------------------------------------------------------------------
    // Private — pre-tokenisation and WordPiece segmentation
    // -----------------------------------------------------------------------

    // Splits text into lowercase words on whitespace/punctuation/symbol boundaries,
    // matching llama.cpp's WPM preprocess() function.
    // Punctuation always splits; ASCII symbols (< 0x7F) also split.
    private static IEnumerable<string> Pretokenize(string text)
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

    // Greedy longest-match subword segmentation for a single pre-tokenized word.
    // Adapts to the detected vocab convention (▁ vs ##).
    private List<int> EncodeWord(string word)
    {
        if (word.Length == 0) return [];

        if (m_useSentencePiecePrefix)
            return EncodeWordSentencePiece(word);
        else
            return EncodeWordClassicWP(word);
    }

    // SentencePiece convention: ▁ is prepended to the entire word, then greedy
    // longest-match left-to-right through the ▁-prefixed string.  The first
    // match consumes the ▁ (word-initial token); subsequent pieces are bare
    // (continuation tokens).  This matches llama.cpp's llm_tokenizer_wpm_session.
    private List<int> EncodeWordSentencePiece(string word)
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

    // Classic WordPiece convention: word-initial subwords are bare, continuations use ## prefix.
    private List<int> EncodeWordClassicWP(string word)
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

    // CJK Unified Ideographs are treated as individual tokens in BERT.
    private static bool IsCjkChar(char c)
        => (c >= '\u4E00' && c <= '\u9FFF')
        || (c >= '\u3400' && c <= '\u4DBF')
        || (c >= '\uF900' && c <= '\uFAFF')
        || (c >= '\u2E80' && c <= '\u2EFF');
}
