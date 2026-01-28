///////////////////////////////
namespace SpatialDbLib.Lattice;

public abstract class AdmitResult
{
    private AdmitResult() { }

    public sealed class Created(SpatialObjectProxy proxy, LeafNode node)
        : AdmitResult
    {
        public SpatialObjectProxy Proxy { get; } = proxy;
        public LeafNode Node { get; } = node;
    }

    public sealed class Rejected : AdmitResult { }

    public sealed class Escalate : AdmitResult { }

    public sealed class Retry : AdmitResult { }

    public sealed class Subdivide(LeafNode vendue)
        : AdmitResult
    {
        public LeafNode Venue { get; } = vendue;
    }

    public sealed class Delegate(LeafNode vendue)
        : AdmitResult
    {
        public LeafNode Venue { get; } = vendue;
    }

    public static AdmitResult Create(SpatialObjectProxy proxy, LeafNode vendue) => new Created(proxy, vendue);
    public static AdmitResult Reject() => new Rejected();
    public static AdmitResult EscalateRequest() => new Escalate();
    public static AdmitResult RetryRequest() => new Retry();
    public static AdmitResult SubdivideRequest(LeafNode vendue) => new Subdivide(vendue);
    public static AdmitResult DelegateRequest(LeafNode vendue) => new Delegate(vendue);
}

public readonly struct SelectChildResult(byte indexInParent, IChildNode childNode)
{
    public readonly byte IndexInParent = indexInParent;
    public readonly IChildNode ChildNode = childNode;
}






