///////////////////////////////
namespace SpatialDbLib.Lattice;

// Discuss if this should be a discriminated union instead.
public readonly struct AdmitResult(AdmitResult.AdmitResponse response, OccupantLeafNode? occupantHostNode, SpatialObjectProxy? proxy)
{
    public enum AdmitResponse
    {
        Created,
        Rejected,
        Escalate,
        Subdivide,
        Delegate,
        Retry
    }

    public AdmitResponse Response { get; } = response;
    public OccupantLeafNode? TargetLeaf { get; } = occupantHostNode;
    public SpatialObjectProxy? Proxy { get; } = proxy;

    public static AdmitResult Create(OccupantLeafNode region, SpatialObjectProxy proxy)
        => new(AdmitResponse.Created, region, proxy);

    public static AdmitResult Reject()
        => new(AdmitResponse.Rejected, null, null);

    public static AdmitResult RequestEscalate()
        => new(AdmitResponse.Escalate, null, null);
    public static AdmitResult RequestSubdivide()
        => new(AdmitResponse.Subdivide, null, null);

    public static AdmitResult RequestDelegate()
        => new(AdmitResponse.Delegate, null, null);

    public static AdmitResult RequestRetry()
        => new(AdmitResponse.Retry, null, null);
}

public readonly struct SelectChildResult(byte indexInParent, SpatialNode childNode)
{
    public readonly byte IndexInParent = indexInParent;
    public readonly SpatialNode ChildNode = childNode;
}






