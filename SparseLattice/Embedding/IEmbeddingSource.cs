using System.Threading;
using System.Threading.Tasks;
//////////////////////////////////////////
namespace SparseLattice.Embedding;

/// <summary>
/// Abstraction over any source that produces float-vector embeddings for text.
/// Both the Ollama HTTP path and any future ONNX direct path implement this contract.
/// The lattice layer never depends on how embeddings are produced — only that they
/// arrive as <c>float[]</c> and that all vectors from a given source share the same
/// <see cref="Dimensions"/> count.
/// </summary>
public interface IEmbeddingSource
{
    /// <summary>Human-readable model identifier (e.g. "nomic-embed-text").</summary>
    string ModelName { get; }

    /// <summary>
    /// Number of dimensions in every embedding vector produced by this source.
    /// May be 0 if the source has not yet connected to the server; callers should
    /// treat 0 as "not yet known" and call <see cref="EmbedAsync"/> to discover it.
    /// </summary>
    int Dimensions { get; }

    /// <summary>Embeds a single text string and returns its float vector.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Embeds a batch of text strings. Default implementation calls
    /// <see cref="EmbedAsync"/> sequentially; override for true batching.
    /// </summary>
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
