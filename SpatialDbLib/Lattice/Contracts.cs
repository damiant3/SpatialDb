///////////////////////////////
using System.Buffers;

namespace SpatialDbLib.Lattice;

public abstract class AdmitResult
{
    private AdmitResult() { }

    public sealed class BulkCreated(List<SpatialObjectProxy> proxies)
        : AdmitResult
    {
        public List<SpatialObjectProxy> Proxies { get; } = proxies;
    }

    public sealed class Created(SpatialObjectProxy proxy)
        : AdmitResult
    {
        public SpatialObjectProxy Proxy { get; } = proxy;
    }

    public sealed class Rejected : AdmitResult { }

    public sealed class Escalate : AdmitResult { }

    public sealed class Retry : AdmitResult { }

    public sealed class Subdivide(LeafNode leaf)
        : AdmitResult
    {
        public LeafNode Leaf { get; } = leaf;
    }

    public sealed class Delegate(LeafNode leaf)
        : AdmitResult
    {
        public LeafNode Leaf { get; } = leaf;
    }

    public static AdmitResult BulkCreate(List<SpatialObjectProxy> proxies) => new BulkCreated(proxies);
    public static AdmitResult Create(SpatialObjectProxy proxy) => new Created(proxy);
    public static AdmitResult Reject() => new Rejected();
    public static AdmitResult EscalateRequest() => new Escalate();
    public static AdmitResult RetryRequest() => new Retry();
    public static AdmitResult SubdivideRequest(LeafNode leaf) => new Subdivide(leaf);
    public static AdmitResult DelegateRequest(LeafNode leaf) => new Delegate(leaf);

}

public class ArrayRentalContract(byte[] array)
    : IDisposable
{
    private bool m_disposed = false;
    private readonly byte[] m_array = array;

    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        if (m_array != null)
            ArrayPool<byte>.Shared.Return(m_array, clearArray: false);
        GC.SuppressFinalize(this);
    }
}

internal sealed class BufferSlice(int start, int length)
{
    public int Start { get; } = start;
    public int Length { get; } = length;
    public Span<SpatialObject> GetSpan(Span<SpatialObject> rootBuffer) => rootBuffer.Slice(Start, Length);
}

public readonly struct SelectChildResult(byte indexInParent, IChildNode<OctetParentNode> childNode)
{
    public readonly byte IndexInParent = indexInParent;
    public readonly IChildNode<OctetParentNode> ChildNode = childNode;
}






