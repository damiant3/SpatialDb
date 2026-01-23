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

    public sealed class Subdivide(OccupantLeafNode leaf) 
        : AdmitResult
    {
        public OccupantLeafNode Leaf { get; } = leaf;
    }

    public sealed class Delegate(OccupantLeafNode leaf)
        : AdmitResult
    {
        public OccupantLeafNode Leaf { get; } = leaf;
    }

    public static AdmitResult Create(SpatialObjectProxy proxy, SpatialNode node) => new Created(proxy, node);
    public static AdmitResult Reject() => new Rejected();
    public static AdmitResult EscalateRequest() => new Escalate();
    public static AdmitResult RetryRequest() => new Retry();
    public static AdmitResult SubdivideRequest(OccupantLeafNode leaf) => new Subdivide(leaf);
    public static AdmitResult DelegateRequest(OccupantLeafNode leaf) => new Delegate(leaf);
}

public readonly struct SelectChildResult(byte indexInParent, SpatialNode childNode)
{
    public readonly byte IndexInParent = indexInParent;
    public readonly SpatialNode ChildNode = childNode;
}






