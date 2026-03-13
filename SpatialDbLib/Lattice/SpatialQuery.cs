using System.Numerics;
using Common.Core.Sync;
using SpatialDbLib.Math;
///////////////////////////////
namespace SpatialDbLib.Lattice;

internal static class SpatialQuery
{
    internal static void CollectWithinSphere(
        ISpatialNode node,
        LongVector3 center,
        ulong radius,
        List<ISpatialObject> results,
        bool acquireLeafLock)
    {
        if (!node.Bounds.IntersectsSphere_SimpleImpl(center, radius)) return;

        switch (node)
        {
            case VenueLeafNode leaf:
            {
                SlimSyncer? syncer = acquireLeafLock
                    ? new SlimSyncer(((ISync)node).Sync, SlimSyncer.LockMode.Read, "SpatialQuery.CollectWithinSphere: VenueLeafNode")
                    : null;
                try
                {
                    BigInteger radiusSquared = (BigInteger)radius * radius;
                    foreach (ISpatialObject obj in leaf.Occupants)
                    {
                        BigInteger distSq = (obj.LocalPosition - center).MagnitudeSquaredBig;
                        if (distSq <= radiusSquared) results.Add(obj);
                    }
                }
                finally
                {
                    syncer?.Dispose();
                }
                break;
            }
            case OctetParentNode parent:
                foreach (IInternalChildNode child in parent.Children)
                    if (child.Bounds.IntersectsSphere_SimpleImpl(center, radius))
                        CollectWithinSphere(child, center, radius, results, acquireLeafLock);
                break;
            case SubLatticeBranchNode sub:
                LongVector3 innerCenter = sub.Sublattice.BoundsTransform.OuterToInnerCanonical(center);
                ULongVector3 innerSize = LatticeUniverse.RootRegion.Size;
                ULongVector3 outerSize = sub.Sublattice.BoundsTransform.OuterLatticeBounds.Size;
                double scale = innerSize.X / (double)outerSize.X;
                ulong innerRadius = (ulong)(radius * scale);
                IEnumerable<ISpatialObject> subResults = sub.Sublattice.QueryWithinDistance(innerCenter, innerRadius);
                results.AddRange(subResults);
                break;
        }
    }
}
