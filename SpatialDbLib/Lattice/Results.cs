///////////////////////////////
namespace SpatialDbLib.Lattice;

public abstract class AdmitResult
{
    private AdmitResult() { }

    public sealed class Created(SpatialObjectProxy proxy, SpatialNode node) 
        : AdmitResult
    {
        public SpatialObjectProxy Proxy { get; } = proxy;
        public SpatialNode Node { get; } = node;
    }

    public sealed class Rejected : AdmitResult { }

    public sealed class Escalate : AdmitResult { }

    public sealed class Retry : AdmitResult { }

    public sealed class Subdivide(VenueLeafNode leaf) 
        : AdmitResult
    {
        public VenueLeafNode Leaf { get; } = leaf;
    }

    public sealed class Delegate(VenueLeafNode leaf)
        : AdmitResult
    {
        public VenueLeafNode Leaf { get; } = leaf;
    }

    public static AdmitResult Create(SpatialObjectProxy proxy, SpatialNode node) => new Created(proxy, node);
    public static AdmitResult Reject() => new Rejected();
    public static AdmitResult EscalateRequest() => new Escalate();
    public static AdmitResult RetryRequest() => new Retry();
    public static AdmitResult SubdivideRequest(VenueLeafNode leaf) => new Subdivide(leaf);
    public static AdmitResult DelegateRequest(VenueLeafNode leaf) => new Delegate(leaf);
}

public readonly struct SelectChildResult(byte indexInParent, IChildNode childNode)
{
    public readonly byte IndexInParent = indexInParent;
    public readonly IChildNode ChildNode = childNode;
}






