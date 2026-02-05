//using SpatialDbLib.Lattice;
//using SpatialDbLib.Simulation;

//namespace SpatialDbLibTest;

//[TestClass]
//public class SimulationTests
//{

//    [TestMethod]
//    public void BasicMovementTest()
//    {
//        var lattice = new TickableSpatialLattice();
//        var obj = new TickableSpatialObject(new LongVector3(1000, 1000, 1000));

//        // Insert object
//        lattice.Insert(new List<SpatialObject> { obj });

//        // Give it velocity
//        obj.Accelerate(new IntVector3(100, 0, 0));  // Moving east at 100 units/tick

//        // Tick with 16ms delta (60fps equivalent)
//        var result = obj.Tick();
//        Assert.IsNotNull(result);

//        Assert.AreEqual(TickAction.Move, result.Value.Action);
//        // Verify position changed appropriately
//    }
//}
