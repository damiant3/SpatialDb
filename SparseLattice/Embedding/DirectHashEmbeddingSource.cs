using SparseLattice.Math;
////////////////////////////////////////////////////
namespace SparseLattice.Embedding;

/// <summary>
/// Produces <see cref="SparseVector"/> instances directly from text using a
/// bag-of-tokens hashing scheme — no floats, no model, no GGUF file.
///
/// Each token in the input text is hashed to a dimension index in [0, Dimensions).
/// A count is accumulated per dimension, then converted to a <c>long</c> by scaling
/// so that the values occupy a useful range for L2 distance comparisons in the lattice.
/// The result is sorted ascending by dimension (as required by <see cref="SparseVector"/>)
/// and zero entries are omitted.
///
/// This is deterministic, allocation-light, and suitable as a stand-in corpus source
/// for benchmarking the lattice itself without any embedding inference cost.
/// </summary>
public sealed class DirectHashEmbeddingSource : IEmbeddingSource
{
    private static readonly char[] s_separators =
        [' ', '\n', '\r', '\t', '.', ',', '(', ')', '{', '}', '[', ']', ';', ':', '<', '>', '=', '/', '*', '+', '-', '!', '?', '"', '\'', '\\'];

    private readonly int  m_dimensions;
    private readonly long m_scale;

    /// <param name="dimensions">
    /// Number of sparse dimensions. Should be >= 64. Larger values reduce collision
    /// rate but increase sparsity. 768 matches nomic-embed-text for apples-to-apples
    /// lattice benchmarks.
    /// </param>
    /// <param name="scale">
    /// Integer scale applied to token counts before storing as <c>long</c>.
    /// Defaults to 1_000_000 so that a single-occurrence token has value 1_000_000,
    /// keeping distances in a numerically comfortable range.
    /// </param>
    public DirectHashEmbeddingSource(int dimensions = 768, long scale = 1_000_000L)
    {
        if (dimensions < 8)
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Must be >= 8.");
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), "Must be positive.");
        m_dimensions = dimensions;
        m_scale      = scale;
    }

    // IEmbeddingSource — still satisfies the interface for pipeline compatibility,
    // but the float[] path is intentionally thin (used only if called via the interface).
    public string ModelName  => $"direct-hash-{m_dimensions}d";
    public int    Dimensions => m_dimensions;

    /// <summary>
    /// Returns a dummy <c>float[]</c> to satisfy <see cref="IEmbeddingSource"/>.
    /// Prefer <see cref="EmbedSparse"/> for lattice use.
    /// </summary>
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

    // -----------------------------------------------------------------------
    // Core: text → SparseVector, zero floats, zero allocations beyond the entry array
    // -----------------------------------------------------------------------

    /// <summary>
    /// Converts <paramref name="text"/> directly into a <see cref="SparseVector"/>
    /// using token-hashing. No floating-point arithmetic is performed.
    /// </summary>
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

    /// <summary>Batch version — avoids redundant renting per item.</summary>
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

    // -----------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------

    private void Tokenize(string text, long[] counts)
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

    private static bool IsSeparator(char c)
    {
        // Inline the check — faster than IndexOf on the separators array.
        return c is ' ' or '\n' or '\r' or '\t'
            or '.' or ',' or '(' or ')' or '{' or '}' or '[' or ']'
            or ';' or ':' or '<' or '>' or '=' or '/' or '*' or '+' or '-'
            or '!' or '?' or '"' or '\'' or '\\';
    }

    private static int HashToken(ReadOnlySpan<char> token)
    {
        // djb2 variant over chars — stable, fast, well-distributed.
        uint h = 5381u;
        foreach (char c in token)
            h = ((h << 5) + h) ^ c;
        return (int)h;
    }

    private SparseVector BuildSparseVector(long[] counts)
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
