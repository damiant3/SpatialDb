///////////////////////////
using SpatialDbLib.Lattice;
namespace SpatialDbLibTest;

public class LatticeTestHelpers
{
    public static ISpatialLattice GetOwningLattice(ISpatialNode region)
    {
        var current = region;
        while (true)
        {
            switch (current)
            {
                case SpatialRootNode root:
                    return root.OwningLattice
                        ?? throw new InvalidOperationException("Root has no owning lattice");

                case LeafNode leaf:
                    current = leaf.Parent;
                    break;

                case OctetBranchNode branch:
                    current = branch.Parent;
                    break;

                default:
                    throw new InvalidOperationException($"Unexpected node type: {current.GetType().Name}");
            }
        }
    }

    public static List<SpatialObject> ForceSublattice(SpatialLattice lattice, LongVector3 pos, int count = 20)
    {
        List<SpatialObject> tempObjs = [];
        try
        {
            for (int i = 0; i < count; i++)
            {
                var obj = new SpatialObject([pos]);
                tempObjs.Add(obj);
                lattice.Insert(obj);
            }
        }
        catch (Exception ex) 
        { 
            Console.WriteLine("Exception during ForceSublattice: " + ex); 
        }
        return tempObjs;
    }

    public static void AssertAllLeavesEmpty(ISpatialNode node)
    {
        switch (node)
        {
            case SubLatticeBranchNode sub:
                AssertAllLeavesEmpty(sub.Sublattice);
                break;
            case VenueLeafNode leaf:
                Assert.AreEqual(0, leaf.Occupants.Count, "Leaf should be empty");
                break;
            case OctetParentNode parent:
                foreach (var child in parent.Children)
                    AssertAllLeavesEmpty(child);
                break;

        }
    }
}
