///////////////////////////
using SpatialDbLib.Lattice;
namespace SpatialDbLibTest;

public class LatticeTestHelpers
{
    public static SpatialLattice GetOwningLattice(ISpatialNode region)
    {
        var current = region;

        while (true)
        {
            if (current is SpatialLattice lattice)
                return lattice;

            current = current switch
            {
                VenueLeafNode leaf => leaf.Parent,
                OctetBranchNode branch => branch.Parent,
                _ => throw new InvalidOperationException("Unexpected region type")
            };
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
