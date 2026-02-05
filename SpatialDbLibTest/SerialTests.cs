///////////////////////////
using SpatialDbLib.Lattice;
using System.Collections.Concurrent;
using static SpatialDbLib.Lattice.AdmitResult;
namespace SpatialDbLibTest;

[TestClass]
public class SerialTests
{
    // T1: Alternating depth insertions
    [TestMethod]
    public void Test_AlternatingDepthInsertions()
    {
        var lattice = new SpatialLattice();
        var objects = new List<SpatialObject>();

        // Insert at depth 3 (deep)
        var obj1 = new SpatialObject([
            new(100),  // depth 0
            new(50),     // depth 1
            new(25),     // depth 2
            new(12)      // depth 3
        ]);
        objects.Add(obj1);
        var result = lattice.Insert(obj1);
        Assert.IsTrue(result is Created, $"Insertion failed for object {obj1.Guid}");

        // Insert at depth 1 (shallow)
        var obj2 = new SpatialObject([
            new(200),  // depth 0
            new(100)   // depth 1
        ]);
        objects.Add(obj2);
        result = lattice.Insert(obj2);
        Assert.IsTrue(result is Created);
        // Insert at depth 2 (medium)

        var obj3 = new SpatialObject([
            new(150),
            new(75),
            new(37)
        ]);
        objects.Add(obj3);
        result = lattice.Insert(obj3);
        Assert.IsTrue(result is Created);

        // Verify all findable
        foreach (var obj in objects)
        {
            var leaf = lattice.ResolveOccupyingLeaf(obj);
            Assert.IsNotNull(leaf, $"Object {obj.Guid} not found");
            Assert.IsTrue(leaf.Contains(obj), $"Leaf does not contain object {obj.Guid}");
        }
    }

    // T2: Force multiple sublattice levels
    [TestMethod]
    public void Test_NestedSublattices()
    {
        var lattice = new SpatialLattice();

        // Create object that forces 3 levels of sublattices
        // (fill a leaf at depth 0, then force subdivision to depth 1, then depth 2...)
        for (int i = 0; i < 20; i++)
        {
            var obj = new SpatialObject([new(1, 1, 1)]);
            lattice.Insert(obj);
        }

        // Verify sublattice structure exists
        int sublatticeCount = 0;
        void CountSublattices(ISpatialNode node)
        {
            switch (node)
            {
                case SubLatticeBranchNode s:
                    sublatticeCount++;
                    CountSublattices(s.Sublattice.m_root);
                    break;
                case OctetParentNode p:
                    foreach (var c in p.Children)
                        CountSublattices(c);
                    break;
            }
        }
        CountSublattices(lattice.m_root);

        Assert.IsTrue(sublatticeCount > 0, "No sublattices created");
    }

    // T3: Concurrent deep and shallow insertions
    [TestMethod]
    public void Test_ConcurrentMixedDepthInsertions()
    {
        var lattice = new SpatialLattice();
        var tasks = new List<Task>();
        var allObjects = new ConcurrentBag<SpatialObject>();

        // Spawn threads doing deep inserts
        for (int t = 0; t < 4; t++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    var depth = (i % 3) + 1;  // Depths 1, 2, 3
                    var positions = new List<LongVector3>();
                    var pos = new LongVector3(
                        Random.Shared.Next(1000),
                        Random.Shared.Next(1000),
                        Random.Shared.Next(1000));

                    for (int d = 0; d <= depth; d++)
                    {
                        positions.Add(pos);
                        pos = new(pos.X / 2, pos.Y / 2, pos.Z / 2);
                    }

                    var obj = new SpatialObject(positions);
                    allObjects.Add(obj);
                    lattice.Insert(obj);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Verify all objects findable
        foreach (var obj in allObjects)
        {
            var leaf = lattice.ResolveOccupyingLeaf(obj);
            Assert.IsNotNull(leaf);
        }
    }

    // T4: Remove from mixed depths
    [TestMethod]
    public void Test_RemoveFromMixedDepths()
    {
        var lattice = new SpatialLattice();
        var deepObj = new SpatialObject([
            new(100, 100, 100),
        new(50, 50, 50),
        new(25, 25, 25)
        ]);
        var shallowObj = new SpatialObject([
            new(200, 200, 200)
        ]);

        lattice.Insert(deepObj);
        lattice.Insert(shallowObj);

        // Remove shallow
        lattice.Remove(shallowObj);
        Assert.IsNull(lattice.ResolveOccupyingLeaf(shallowObj));
        Assert.IsNotNull(lattice.ResolveOccupyingLeaf(deepObj));

        // Remove deep
        lattice.Remove(deepObj);
        Assert.IsNull(lattice.ResolveOccupyingLeaf(deepObj));
    }

    // T5: Bulk insert with mixed depths
    [TestMethod]
    public void Test_BulkInsertMixedDepths()
    {
        var lattice = new SpatialLattice();
        var objects = new List<SpatialObject>();

        // Create objects with varying depths
        for (int i = 0; i < 50; i++)
        {
            var depth = i % 4;  // 0-3
            var positions = new List<LongVector3> { new(i * 10, i * 10, i * 10) };

            for (int d = 1; d <= depth; d++)
                positions.Add(new(i * 10 / (d + 1), i * 10 / (d + 1), i * 10 / (d + 1)));

            objects.Add(new SpatialObject(positions));
        }

        // Bulk insert
        var result = lattice.Insert(objects.ToArray());
        Assert.IsInstanceOfType(result, typeof(AdmitResult.BulkCreated));

        // Verify all present
        foreach (var obj in objects)
            Assert.IsNotNull(lattice.ResolveOccupyingLeaf(obj));
    }

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
                        Scan(s.Sublattice.m_root);
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
        {
            // test bulk insert
            List<SpatialObject> tmp = [];
            for (int i = 0; i < 1000; i++)
            {
                var objtmp = new SpatialObject([new (44456543789), new(-122224456789),new(5678900000987)]);
                tmp.Add(objtmp);
            }

            var r = lattice.Insert(tmp);

            Assert.IsTrue(r is BulkCreated);
            foreach (var obj in tmp)
            {
                insertedObjects.Add(obj);
                var leaf = lattice.ResolveOccupyingLeaf(obj);
                Assert.IsNotNull(leaf);
                Assert.IsTrue(leaf.Contains(obj));
            }
            
            Console.WriteLine("Bulk insert deeply into lattice.");
        }

        // Final validate all objects where lattice position stacks depth > 1 inner to outer transform works.
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
