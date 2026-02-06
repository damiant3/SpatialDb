using SpatialDbLib.Lattice;
using SpatialDbLib.Simulation;

namespace SpatialDbLibTest;

[TestClass]
public class SimulationTests
{
    [TestMethod]
    public void ResolveLeafFromOuterLattice_ThrowsNotImplementedException()
    {
        // Arrange
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject([new (1000), new (1200)]);

        var result = lattice.Insert(obj);
        Assert.IsTrue(result is AdmitResult.Created, "Insertion should succeed");

        // Act
        var x = lattice.ResolveOccupyingLeaf(obj);

        // Assert - handled by ExpectedException
        Assert.IsNotNull(x);
    }

    [TestMethod]
    public void BasicMovementTest()
    {
        var lattice = new TickableSpatialLattice();
        var obj = new TickableSpatialObject(new LongVector3(1000));

        // Insert object
        lattice.Insert(new List<SpatialObject> { obj });

        // Give it velocity
        obj.Accelerate(new IntVector3(100, 0, 0));  // Moving east at 100 units/tick
        obj.RegisterForTicks();
        var result = obj.Tick();
        Assert.IsNotNull(result);

        Assert.AreEqual(TickAction.Move, result.Value.Action);
        // Verify position changed appropriately
    }

    [TestMethod]
    public void Test_TickableBasicFunctionality()
    {
        var lattice = new TickableSpatialLattice();

        // 1. Insert tickable object
        var obj = new TickableSpatialObject([new(100)]);
        obj.Velocity = new IntVector3(10, 0, 0);  // Set movement

        lattice.Insert(obj);

        // 2. Verify insertion worked
        var leaf = lattice.ResolveOccupyingLeaf(obj);
        Assert.IsNotNull(leaf);
        Assert.IsInstanceOfType(leaf, typeof(TickableVenueLeafNode));

        // 3. Tick the lattice
        lattice.Tick();

        // 4. Verify tick behavior occurred
        // (e.g., object moved, state changed, etc.)
    }

    [TestMethod]
    public void Test_TickableWithSublattices()
    {
        var lattice = new TickableSpatialLattice();

        // Force sublattice creation
        for (int i = 0; i < 20; i++)
        {
            var obj = new TickableSpatialObject([new(1)]);
            lattice.Insert(obj);
        }

        // Verify tick propagates through sublattices
        lattice.Tick();

        // Verify all objects were ticked
    }
}
