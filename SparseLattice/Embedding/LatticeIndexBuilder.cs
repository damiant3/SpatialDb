using SparseLattice.Lattice;
using SparseLattice.Math;
///////////////////////////////////////
namespace SparseLattice.Embedding;

/// <summary>
/// Builds a frozen <see cref="EmbeddingLattice{TPayload}"/> from a text corpus
/// by embedding each item, quantizing the resulting float vectors, and constructing
/// the sparse decision tree in a single pipeline call.
/// </summary>
public sealed class LatticeIndexBuilder<TPayload>
{
    private readonly IEmbeddingSource m_source;
    private readonly QuantizationOptions m_quantizationOptions;

    public LatticeIndexBuilder(IEmbeddingSource source, QuantizationOptions? quantizationOptions = null)
    {
        m_source = source ?? throw new ArgumentNullException(nameof(source));
        m_quantizationOptions = quantizationOptions ?? QuantizationOptions.Default;
    }

    /// <summary>
    /// Embeds all items in <paramref name="corpus"/> in batch, quantizes each embedding,
    /// builds the sparse lattice, freezes it, and returns the frozen index.
    /// </summary>
    public async Task<EmbeddingLattice<TPayload>> BuildAsync(
        IReadOnlyList<(string text, TPayload payload)> corpus,
        LatticeOptions? latticeOptions = null,
        CancellationToken ct = default)
    {
        if (corpus.Count == 0)
        {
            EmbeddingLattice<TPayload> empty = new([], latticeOptions);
            empty.Freeze();
            return empty;
        }

        List<string> texts = new(corpus.Count);
        foreach ((string text, TPayload _) in corpus)
            texts.Add(text);

        float[][] embeddings = await m_source.EmbedBatchAsync(texts, ct).ConfigureAwait(false);

        SparseOccupant<TPayload>[] occupants = new SparseOccupant<TPayload>[corpus.Count];
        for (int i = 0; i < corpus.Count; i++)
        {
            SparseVector vector = EmbeddingAdapter.Quantize(embeddings[i], m_quantizationOptions);
            occupants[i] = new SparseOccupant<TPayload>(vector, corpus[i].payload);
        }

        EmbeddingLattice<TPayload> lattice = new(occupants, latticeOptions);
        lattice.Freeze();
        return lattice;
    }
}
