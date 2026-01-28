///////////////////////////////
namespace SpatialDbLib.Lattice;

public abstract class AdmitResult
{
    private AdmitResult() { }

    public sealed class Created(SpatialObjectProxy proxy, IVenueParent node)
        : AdmitResult
    {
        public SpatialObjectProxy Proxy { get; } = proxy;
        public IVenueParent Node { get; } = node;
    }

    public sealed class Rejected : AdmitResult { }

    public sealed class Escalate : AdmitResult { }

    public sealed class Retry : AdmitResult { }

    public sealed class Subdivide(IVenueParent vendue)
        : AdmitResult
    {
        public IVenueParent Venue { get; } = vendue;
    }

    public sealed class Delegate(IVenueParent vendue)
        : AdmitResult
    {
        public IVenueParent Venue { get; } = vendue;
    }

    public static AdmitResult Create(SpatialObjectProxy proxy, IVenueParent vendue) => new Created(proxy, vendue);
    public static AdmitResult Reject() => new Rejected();
    public static AdmitResult EscalateRequest() => new Escalate();
    public static AdmitResult RetryRequest() => new Retry();
    public static AdmitResult SubdivideRequest(IVenueParent vendue) => new Subdivide(vendue);
    public static AdmitResult DelegateRequest(IVenueParent vendue) => new Delegate(vendue);
}

public readonly struct SelectChildResult(byte indexInParent, IChildNode childNode)
{
    public readonly byte IndexInParent = indexInParent;
    public readonly IChildNode ChildNode = childNode;
}






