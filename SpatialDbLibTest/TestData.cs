using SpatialDbLib.Lattice;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpatialDbLibTest;

public partial class ParallelTests
{
    public int SpaceRange = int.MaxValue;

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

        var a = new LongVector3(-SpaceRange + 1, -SpaceRange + 1, -SpaceRange + 1);
        var b = new LongVector3(SpaceRange - 1, SpaceRange - 1, SpaceRange - 1);

        for (int i = 0; i < TASKS_PER_ITERATION; i++)
        {
            objsToInsert.Add(i, []);
            for (int j = 0; j < BATCH_SIZE; j++)
            {
                objsToInsert[i].Add(
                    new SpatialObject([
                        (j & 1) == 0 ? a : b
                    ]));
            }
        }

        return objsToInsert;
    }

    public Dictionary<int, List<SpatialObject>> GetClusteredObjects()
    {
        var objsToInsert = new Dictionary<int, List<SpatialObject>>();

        var center = new LongVector3(0, 0, 0);
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
