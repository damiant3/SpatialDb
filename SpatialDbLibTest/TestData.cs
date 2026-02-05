using SpatialDbLib.Lattice;
///////////////////////////
namespace SpatialDbLibTest;

public partial class ParallelTests
{
    public int SpaceRange = int.MaxValue;

    public Dictionary<int, List<SpatialObject>> GetTinyClusteredObjects()
    {
        var objsToInsert = new Dictionary<int, List<SpatialObject>>();
        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            objsToInsert.Add(i, []);

            // Pick a random cluster center for this batch
            var clusterCenter = new LongVector3(
                FastRandom.NextInt(-SpaceRange, SpaceRange),
                FastRandom.NextInt(-SpaceRange, SpaceRange),
                FastRandom.NextInt(-SpaceRange, SpaceRange));

            // 12 objects tightly clustered around that center (fits in one leaf)
            for (int j = 0; j < 10000; j++)
            {
                // Small offset from cluster center (within same leaf bounds)
                var offset = new LongVector3(
                    FastRandom.NextInt(-100, 100),
                    FastRandom.NextInt(-100, 100),
                    FastRandom.NextInt(-100, 100));

                objsToInsert[i].Add(
                    new SpatialObject([
                        new LongVector3(
                        clusterCenter.X + offset.X,
                        clusterCenter.Y + offset.Y,
                        clusterCenter.Z + offset.Z)
                    ]));
            }
        }
        return objsToInsert;
    }

    public Dictionary<int, List<SpatialObject>> GetTinyDispersedObjects()
    {
        var objsToInsert = new Dictionary<int, List<SpatialObject>>();
        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            objsToInsert.Add(i, []);

            // Only 5 objects per batch, maximally dispersed
            for (int j = 0; j < 256; j++)
            {
                objsToInsert[i].Add(
                    new SpatialObject([
                        new LongVector3(
                        FastRandom.NextInt(-SpaceRange, SpaceRange),
                        FastRandom.NextInt(-SpaceRange, SpaceRange),
                        FastRandom.NextInt(-SpaceRange, SpaceRange))
                    ]));
            }
        }
        return objsToInsert;
    }

    public Dictionary<int, List<SpatialObject>> GetSkewedObjects()
    {
        var objsToInsert = new Dictionary<int, List<SpatialObject>>();

        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            objsToInsert.Add(i, []);
            for (int j = 0; j < BATCH_SIZE; j++)
            {
                // 90% in one octant, 10% random
                if (FastRandom.NextInt(0, 10) < 9)
                {
                    objsToInsert[i].Add(
                        new SpatialObject([
                            new LongVector3(
                            SpaceRange - 1,
                            SpaceRange - 1,
                            SpaceRange - 1)
                        ]));
                }
                else
                {
                    objsToInsert[i].Add(
                        new SpatialObject([
                            new LongVector3(
                            FastRandom.NextInt(-SpaceRange, SpaceRange),
                            FastRandom.NextInt(-SpaceRange, SpaceRange),
                            FastRandom.NextInt(-SpaceRange, SpaceRange))
                        ]));
                }
            }
        }

        return objsToInsert;
    }

    public Dictionary<int, List<SpatialObject>> GetBimodalObjects()
    {
        var objsToInsert = new Dictionary<int, List<SpatialObject>>();

        var a = new LongVector3(-SpaceRange + 1);
        var b = new LongVector3(SpaceRange - 1);

        //var a = new LongVector3(1);
        //var b = new LongVector3(-1);
        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            objsToInsert.Add(i, []);
            for (int j = 0; j < BATCH_SIZE; j++)
            {
                objsToInsert[i].Add((j % 2) == 0 ? new SpatialObject([a]) : new SpatialObject([b]));
            }
        }

        return objsToInsert;
    }

    public Dictionary<int, List<SpatialObject>> GetClusteredObjects()
    {
        var objsToInsert = new Dictionary<int, List<SpatialObject>>();

        var center = LongVector3.Zero;
        int variance = SpaceRange / 1024; // very tight cluster

        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            objsToInsert.Add(i, []);
            for (int j = 0; j < BATCH_SIZE; j++)
            {
                objsToInsert[i].Add(
                    new SpatialObject([
                        new LongVector3(
                        center.X + FastRandom.NextInt(-variance, variance),
                        center.Y + FastRandom.NextInt(-variance, variance),
                        center.Z + FastRandom.NextInt(-variance, variance)
                    )
                    ]));
            }
        }

        return objsToInsert;
    }

    public Dictionary<int, List<SpatialObject>> GetSinglePathObjects()
    {
        var objsToInsert = new Dictionary<int, List<SpatialObject>>();

        // Pick a point extremely close to one octant corner
        var basePoint = new LongVector3(
            SpaceRange - 1,
            SpaceRange - 1,
            SpaceRange - 1);

        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            objsToInsert.Add(i, []);
            for (int j = 0; j < BATCH_SIZE; j++)
            {
                objsToInsert[i].Add(
                    new SpatialObject([basePoint]));
            }
        }

        return objsToInsert;
    }

    public Dictionary<int, List<SpatialObject>> GetUniformObjects()
    {
        var objsToInsert = new Dictionary<int, List<SpatialObject>>();
        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            var list = new List<SpatialObject>(BATCH_SIZE);
            for (int j = 0; j < BATCH_SIZE; j++)
            {
                list.Add(new SpatialObject([new LongVector3(
                    FastRandom.NextInt(-SpaceRange, SpaceRange),
                    FastRandom.NextInt(-SpaceRange, SpaceRange),
                    FastRandom.NextInt(-SpaceRange, SpaceRange)
                )]));
            }
            objsToInsert[i] = list;
        }
        return objsToInsert;
    }
}
