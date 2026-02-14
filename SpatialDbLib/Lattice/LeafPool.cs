using System.Collections.Concurrent;
using SpatialDbLib.Math;
///////////////////////////////
namespace SpatialDbLib.Lattice;

internal static class LeafPool<T> where T : VenueLeafNode
{
    private static readonly ConcurrentBag<T> s_pool = [];
    public static T Rent(Region bounds, OctetParentNode parent, Func<Region, OctetParentNode, T> factory)
    {
        if (s_pool.TryTake(out var leaf))
        {
            leaf.Reinitialize(bounds, parent);
            return leaf;
        }
        return factory(bounds, parent);
    }
    public static void Return(T leaf) => s_pool.Add(leaf);
}