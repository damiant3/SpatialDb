///////////////////////////
using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
using static SpatialDbLib.Lattice.AdmitResult;
namespace SpatialDbLibTest;

[TestClass]
public class SerialTests
{
    [TestMethod]
    public void SerialTests_Omnibus()
    {
        var lattice = new SpatialLattice();
        List<ISpatialObject> insertedObjects = [];

        // === I1: Coordinate round-trip (root) ===
        {
            var t = lattice.BoundsTransform;
            var positions = new[]
            {
                LongVector3.Zero,
                new(1),
                new(-1),
                new(1234567890, -987654321, 567890123),
                LatticeUniverse.RootRegion.Min,
                LatticeUniverse.RootRegion.Max - new LongVector3(1),
            };

            var newobjects = positions.Select(a => new SpatialObject([a])).ToList();
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
                    case ISubLatticeBranch s:
                        Scan(s.GetSublattice().GetRootNode());
                        break;
                    case VenueLeafNode l:
                        if (l.Contains(obj)) count++;
                        break;
                    case OctetParentNode p:
                        foreach (var c in p.Children) Scan(c);
                        break;
                }
            }

            Scan(lattice.m_root);
            Assert.AreEqual(1, count);
            Console.WriteLine("Object exists in exactly one leaf passed.");
        }

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
            Assert.ThrowsException<InvalidOperationException>(() => created.Proxy!.Commit());
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
            Assert.IsTrue(x is Created);
            var leaf = lattice.ResolveOccupyingLeaf(obj);
            Assert.IsNotNull(leaf);
            var owning = LatticeTestHelpers.GetOwningLattice(leaf);
            var projected = owning.BoundsTransform.InnerToOuter(obj.LocalPosition);
            Assert.AreEqual(pos, projected);
            Console.WriteLine("Local lattice projection matches original passed.");
        }

        // === I10: Alternating depth insertions ===
        {
            var objects = new List<SpatialObject>();

            // Insert at depth 3 (deep)
            var obj1 = new SpatialObject([
                new(100),
                new(50),
                new(25),
                new(12)
            ]);
            objects.Add(obj1);
            insertedObjects.Add(obj1);
            var result = lattice.Insert(obj1);
            Assert.IsTrue(result is Created, $"Insertion failed for object {obj1.Guid}");

            // Insert at depth 1 (shallow)
            var obj2 = new SpatialObject([
                new(200),
                new(100)
            ]);
            objects.Add(obj2);
            insertedObjects.Add(obj2);
            result = lattice.Insert(obj2);
            Assert.IsTrue(result is Created);

            // Insert at depth 2 (medium)
            var obj3 = new SpatialObject([
                new(150),
                new(75),
                new(37)
            ]);
            objects.Add(obj3);
            insertedObjects.Add(obj3);
            result = lattice.Insert(obj3);
            Assert.IsTrue(result is Created);

            // Verify all findable
            foreach (var obj in objects)
            {
                var leaf = lattice.ResolveOccupyingLeaf(obj);
                Assert.IsNotNull(leaf, $"Object {obj.Guid} not found");
                Assert.IsTrue(leaf.Contains(obj), $"Leaf does not contain object {obj.Guid}");
            }

            Console.WriteLine("Alternating depth insertions passed.");
        }

        // === I11: Nested sublattice structure validation ===
        {
            // Verify multiple levels of sublattices exist (from forcing sublattices earlier)
            int sublatticeCount = 0;
            void CountSublattices(ISpatialNode node)
            {
                switch (node)
                {
                    case ISubLatticeBranch s:
                        sublatticeCount++;
                        CountSublattices(s.GetSublattice().GetRootNode());
                        break;
                    case OctetParentNode p:
                        foreach (var c in p.Children)
                            CountSublattices(c);
                        break;
                }
            }
            CountSublattices(lattice.m_root);

            Assert.IsTrue(sublatticeCount > 0, "No sublattices created");
            Console.WriteLine($"Nested sublattice structure validated ({sublatticeCount} sublattices).");
        }

        // === I12: Remove from mixed depths ===
        {
            var deepObj = new SpatialObject([new(300), new(150), new(75)]);
            var shallowObj = new SpatialObject([new(400)]);

            lattice.Insert(deepObj);
            lattice.Insert(shallowObj);

            lattice.Remove(shallowObj);
            Assert.IsNull(lattice.ResolveOccupyingLeaf(shallowObj));
            Assert.IsNotNull(lattice.ResolveOccupyingLeaf(deepObj));

            lattice.Remove(deepObj);
            Assert.IsNull(lattice.ResolveOccupyingLeaf(deepObj));

            Console.WriteLine("Remove from mixed depths passed.");
        }

        // === I13: Bulk insert with varying depths ===
        {
            var objects = new List<ISpatialObject>();

            for (int i = 0; i < 50; i++)
            {
                var depth = i % 4;  // 0-3
                var positions = new List<LongVector3> { new(i * 10 + 500, i * 10 + 500, i * 10 + 500) };
                for (int d = 1; d <= depth; d++)
                    positions.Add(new((i * 10 + 500) / (d + 1), (i * 10 + 500) / (d + 1), (i * 10 + 500) / (d + 1)));
                objects.Add(new SpatialObject(positions));
            }

            var result = lattice.Insert(objects);
            Assert.IsInstanceOfType(result, typeof(BulkCreated));

            foreach (var obj in objects)
            {
                insertedObjects.Add(obj);
                Assert.IsNotNull(lattice.ResolveOccupyingLeaf(obj));
            }

            Console.WriteLine("Bulk insert with varying depths passed.");
        }

        // === I14: Deep lattice projections matches original ===
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
                    Assert.AreEqual(obj.GetPositionStack()[obj.PositionStackDepth - 2], projected, $"Object {obj} failed inner to outer transform validation.");
                }
            }

            Assert.IsTrue(atLeastOne, "No inserted objects have LatticePositionStackDepth > 1 to validate.");
            Console.WriteLine("All deep lattice projections matches original passed.");
        }
    }
}
