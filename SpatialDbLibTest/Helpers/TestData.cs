using SpatialDbLib.Lattice;
using SpatialDbLib.Math;
///////////////////////////////////
namespace SpatialDbLibTest.Helpers;

public static class TestData
{
    public static Dictionary<int, List<ISpatialObject>> GetTinyClusteredObjects(int TASKS_PER_ITERATION, long SpaceRange)
    {
        var objsToInsert = new Dictionary<int, List<ISpatialObject>>();
        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            objsToInsert.Add(i, []);

            // Pick a random cluster center for this batch
            var clusterCenter = new LongVector3(
                FastRandom.NextLong(-SpaceRange, SpaceRange),
                FastRandom.NextLong(-SpaceRange, SpaceRange),
                FastRandom.NextLong(-SpaceRange, SpaceRange));

            // 12 objects tightly clustered around that center (fits in one leaf)
            for (int j = 0; j < 10000; j++)
            {
                // Small offset from cluster center (within same leaf bounds)
                var offset = new LongVector3(
                    FastRandom.NextLong(-100, 100),
                    FastRandom.NextLong(-100, 100),
                    FastRandom.NextLong(-100, 100));

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

    public static Dictionary<int, List<ISpatialObject>> GetTinyDispersedObjects(int TASKS_PER_ITERATION, long SpaceRange)
    {
        var objsToInsert = new Dictionary<int, List<ISpatialObject>>();
        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            objsToInsert.Add(i, []);

            // Only 5 objects per batch, maximally dispersed
            for (int j = 0; j < 256; j++)
            {
                objsToInsert[i].Add(
                    new SpatialObject([
                        new LongVector3(
                        FastRandom.NextLong(-SpaceRange, SpaceRange),
                        FastRandom.NextLong(-SpaceRange, SpaceRange),
                        FastRandom.NextLong(-SpaceRange, SpaceRange))
                    ]));
            }
        }
        return objsToInsert;
    }

    public static Dictionary<int, List<ISpatialObject>> GetSkewedObjects(int TASKS_PER_ITERATION, int BATCH_SIZE, long SpaceRange)
    {
        var objsToInsert = new Dictionary<int, List<ISpatialObject>>();

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
                            FastRandom.NextLong(-SpaceRange, SpaceRange),
                            FastRandom.NextLong(-SpaceRange, SpaceRange),
                            FastRandom.NextLong(-SpaceRange, SpaceRange))
                        ]));
                }
            }
        }

        return objsToInsert;
    }

    public static Dictionary<int, List<ISpatialObject>> GetBimodalObjects(int TASKS_PER_ITERATION, int BATCH_SIZE, long SpaceRange)
    {
        var objsToInsert = new Dictionary<int, List<ISpatialObject>>();

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

    public static Dictionary<int, List<ISpatialObject>> GetClusteredObjects(int TASKS_PER_ITERATION, int BATCH_SIZE, long SpaceRange)
    {
        var objsToInsert = new Dictionary<int, List<ISpatialObject>>();

        var center = LongVector3.Zero;
        var variance = SpaceRange / 1024L; // very tight cluster

        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            objsToInsert.Add(i, []);
            for (int j = 0; j < BATCH_SIZE; j++)
            {
                objsToInsert[i].Add(
                    new SpatialObject([
                        new LongVector3(
                        center.X + FastRandom.NextLong(-variance, variance),
                        center.Y + FastRandom.NextLong(-variance, variance),
                        center.Z + FastRandom.NextLong(-variance, variance)
                    )
                    ]));
            }
        }

        return objsToInsert;
    }

    public static Dictionary<int, List<ISpatialObject>> GetSinglePathObjects(int TASKS_PER_ITERATION, int BATCH_SIZE, long SpaceRange)
    {
        var objsToInsert = new Dictionary<int, List<ISpatialObject>>();

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

    public static Dictionary<int, List<ISpatialObject>> GetUniformObjects(int TASKS_PER_ITERATION, int BATCH_SIZE, long SpaceRange)
    {
        var objsToInsert = new Dictionary<int, List<ISpatialObject>>();
        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            var list = new List<ISpatialObject>(BATCH_SIZE);
            for (int j = 0; j < BATCH_SIZE; j++)
            {
                list.Add(new SpatialObject([new LongVector3(
                    FastRandom.NextLong(-SpaceRange, SpaceRange),
                    FastRandom.NextLong(-SpaceRange, SpaceRange),
                    FastRandom.NextLong(-SpaceRange, SpaceRange)
                )]));
            }
            objsToInsert[i] = list;
        }
        return objsToInsert;
    }
}
