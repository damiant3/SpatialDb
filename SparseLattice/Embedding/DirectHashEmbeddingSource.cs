using SparseLattice.Math;
////////////////////////////////////////////////////
namespace SparseLattice.Embedding;

public sealed class DirectHashEmbeddingSource : IEmbeddingSource
{
    static readonly char[] s_separators =
        [' ', '\n', '\r', '\t', '.', ',', '(', ')', '{', '}', '[', ']', ';', ':', '<', '>', '=', '/', '*', '+', '-', '!', '?', '"', '\'', '\\'];

    readonly int  m_dimensions;
    readonly long m_scale;

    public DirectHashEmbeddingSource(int dimensions = 768, long scale = 1_000_000L)
    {
        if (dimensions < 8)
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Must be >= 8.");
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), "Must be positive.");
        m_dimensions = dimensions;
        m_scale      = scale;
    }

    public string ModelName  => $"direct-hash-{m_dimensions}d";
    public int    Dimensions => m_dimensions;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        // Project the sparse longs back to floats for interface compatibility.
        SparseVector sv   = EmbedSparse(text);
        float[]      dense = new float[m_dimensions];
        foreach (SparseEntry e in sv.Entries)
            dense[e.Dimension] = (float)(e.Value / (double)m_scale);
        return Task.FromResult(dense);
    }

    public async Task<float[][]> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        float[][] results = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
            results[i] = await EmbedAsync(texts[i], ct).ConfigureAwait(false);
        return results;
    }

    public SparseVector EmbedSparse(string text)
    {
        // Use a stack-allocated accumulator for small dimension counts,
        // heap for larger ones. We rent from a small pool to avoid per-call allocation.
        long[] counts = System.Buffers.ArrayPool<long>.Shared.Rent(m_dimensions);
        counts.AsSpan(0, m_dimensions).Clear();

        try
        {
            Tokenize(text, counts);
            return BuildSparseVector(counts);
        }
        finally
        {
            System.Buffers.ArrayPool<long>.Shared.Return(counts);
        }
    }

    public SparseVector[] EmbedSparseBatch(IReadOnlyList<string> texts)
    {
        SparseVector[] results = new SparseVector[texts.Count];
        long[] counts = System.Buffers.ArrayPool<long>.Shared.Rent(m_dimensions);
        try
        {
            for (int i = 0; i < texts.Count; i++)
            {
                counts.AsSpan(0, m_dimensions).Clear();
                Tokenize(texts[i], counts);
                results[i] = BuildSparseVector(counts);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<long>.Shared.Return(counts);
        }
        return results;
    }

    void Tokenize(string text, long[] counts)
    {
        if (string.IsNullOrEmpty(text)) return;

        ReadOnlySpan<char> span = text.AsSpan();
        int start = 0;

        for (int i = 0; i <= span.Length; i++)
        {
            bool isSep = i == span.Length || IsSeparator(span[i]);
            if (!isSep) continue;

            int len = i - start;
            if (len > 0)
            {
                // Hash the token substring without allocating a string.
                // Use a simple djb2-style hash over the chars.
                int hash = HashToken(span.Slice(start, len));
                int idx  = System.Math.Abs(hash) % m_dimensions;
                counts[idx] += m_scale;
            }
            start = i + 1;
        }
    }

    static bool IsSeparator(char c)
    {
        // Inline the check — faster than IndexOf on the separators array.
        return c is ' ' or '\n' or '\r' or '\t'
            or '.' or ',' or '(' or ')' or '{' or '}' or '[' or ']'
            or ';' or ':' or '<' or '>' or '=' or '/' or '*' or '+' or '-'
            or '!' or '?' or '"' or '\'' or '\\';
    }

    // djb2 variant over chars
    static int HashToken(ReadOnlySpan<char> token)
    {
        uint h = 5381u;
        foreach (char c in token)
            h = ((h << 5) + h) ^ c;
        return (int)h;
    }

    SparseVector BuildSparseVector(long[] counts)
    {
        // Count nonzeros first to size the entry array exactly.
        int nnz = 0;
        for (int i = 0; i < m_dimensions; i++)
            if (counts[i] != 0L) nnz++;

        if (nnz == 0)
        {
            // Degenerate: empty or all-separator input — return a single sentinel entry.
            return new SparseVector([new SparseEntry(0, 1L)], m_dimensions);
        }

        SparseEntry[] entries = new SparseEntry[nnz];
        int w = 0;
        for (int i = 0; i < m_dimensions; i++)
            if (counts[i] != 0L)
                entries[w++] = new SparseEntry((ushort)i, counts[i]);

        // Entries are already sorted ascending by dimension (we iterated 0..dims-1).
        return new SparseVector(entries, m_dimensions);
    }
}
