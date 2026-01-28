///////////////////////////
using SpatialDbLib.Lattice;
using static SpatialDbLib.Lattice.AdmitResult;
namespace SpatialDbLibTest;

[TestClass]
public class SerialTests
{
    [TestMethod]
    public void SerialTests_Omnibus()
    {
        var lattice = new SpatialLattice();
        List<SpatialObject> insertedObjects = [];

        // === I1: Coordinate round-trip (root) ===
        {
            var t = lattice.BoundsTransform;
            var positions = new[]
            {
                LongVector3.Zero,
                new(1), new(-1),
                new(1234567890, -987654321, 567890123),
                LatticeUniverse.RootRegion.Min,
                LatticeUniverse.RootRegion.Max - new LongVector3(1),
            };

            var newobjects = positions.Select(a => new SpatialObject([a])).ToList<SpatialObject>();
            foreach (var p in positions)
            {
                var inner = t.OuterToInnerCanonical(p);
                var outer = t.InnerToOuter(inner);
                Assert.AreEqual(p, outer);
            }

            Console.WriteLine("Coordinate round-trips passed.");
        }

        // === I2 + I3: Insert / committed visibility ===
        {
            var obj = new SpatialObject([LongVector3.Zero]);
            insertedObjects.Add(obj);
            var r = lattice.Insert(obj);
            var created = r as Created;
            Assert.IsNotNull(created);
            Assert.IsTrue(created.Proxy.IsCommitted);
            var leaf = lattice.ResolveOccupyingLeaf(obj);
            Assert.IsNotNull(leaf);
            Assert.IsTrue(leaf.Contains(obj));
            Console.WriteLine("Insert, Proxy, Commit, and leaf.Contains() for uncommited and commited objects passed.");
        }

        // === I4 + I5: Leaf overflow creates sublattice and stack ===
        {
            insertedObjects.AddRange(LatticeTestHelpers.ForceSublattice(lattice, LongVector3.Zero));
            Console.WriteLine("Force sublattice worked.");
        }

        // === I6: Object exists in exactly one leaf ===
        {
            var obj = new SpatialObject([LongVector3.Zero]);
            insertedObjects.Add(obj);
            lattice.Insert(obj);

            int count = 0;
            void Scan(ISpatialNode n)
            {
                switch (n)
                {
                    case SubLatticeBranchNode s:
                        Scan(s.Sublattice);
                        break;
                    case VenueLeafNode l:
                        if (l.Contains(obj)) count++;
                        break;
                    case OctetParentNode p:
                        foreach (var c in p.Children) Scan(c);
                        break;

                }
            }

            Scan(lattice);
            Assert.AreEqual(1, count);
        }
        Console.WriteLine("Object exists in exactly one leaf passed.");


        // === I7 + I8: Double commit throws, remove removes ===
        {
            var obj = new SpatialObject([LongVector3.Zero]);
            insertedObjects.Add(obj);
            var r = lattice.Insert(obj);
            var created = r as Created;
            Assert.IsNotNull(created);
            Assert.IsTrue(created.Proxy.IsCommitted);
            var leaf = lattice.ResolveOccupyingLeaf(obj);
            Assert.IsTrue(leaf != null);
            lattice.Remove(obj);
            insertedObjects.Remove(obj);
            Assert.ThrowsException<InvalidOperationException>(()=>created.Proxy!.Commit());
            leaf = lattice.ResolveOccupyingLeaf(obj);
            Assert.IsTrue(leaf == null);
            Console.WriteLine("Double commit throws, Removed items can't be resolved passed.");
        }

        // === I9: Local lattice projection matches original ===
        {
            var pos = new LongVector3(123, 456, -789);
            insertedObjects.AddRange(LatticeTestHelpers.ForceSublattice(lattice, pos));
            var obj = new SpatialObject([pos]);
            insertedObjects.Add(obj);
            var x = lattice.Insert(obj);
            Assert.IsTrue(x  is Created);
            var leaf = lattice.ResolveOccupyingLeaf(obj);
            Assert.IsNotNull(leaf);
            var owning = LatticeTestHelpers.GetOwningLattice(leaf);
            var projected = owning.BoundsTransform.InnerToOuter(obj.LocalPosition);
            Assert.AreEqual(pos, projected);
            Console.WriteLine("Local lattice projection matches original passed.");
        }
        // === I9: Deep Insertion ===
        {
            var obj = new SpatialObject([LongVector3.Zero, LongVector3.Zero]);
            insertedObjects.Add(obj);
            var r = lattice.Insert(obj);
            var created = r as Created;
            Assert.IsNotNull(created);
            Assert.IsTrue(created.Proxy.IsCommitted);
            var leaf = lattice.ResolveOccupyingLeaf(obj);
            Assert.IsNotNull(leaf);
            Assert.IsTrue(leaf.Contains(obj));
            Console.WriteLine("Object insert deeply into lattice.");
        }

        // validate all objects where lattice position stacks depth > 1 inner to outer transform works.
        {
            bool atLeastOne = false;
            foreach (var obj in insertedObjects)
            {
                if (obj.PositionStackDepth > 1)
                {
                    atLeastOne = true;
                    var objLeaf = lattice.ResolveLeaf(obj);
                    Assert.IsNotNull(objLeaf);
                    var objLattice = LatticeTestHelpers.GetOwningLattice(objLeaf);
                    Assert.IsNotNull(objLattice);
                    var projected = objLattice.BoundsTransform.InnerToOuter(obj.LocalPosition);
                    Assert.AreEqual(obj.GetPositionStack()[obj.PositionStackDepth-2], projected, $"Object {obj} failed inner to outer transform validation.");
                }
            }

            Assert.IsTrue(atLeastOne, "No inserted objects have LatticePositionStackDepth > 1 to validate.");
            Console.WriteLine("All deep lattice projections matches original passed.");
        }
    }
}
