using SpatialDbLib.Lattice;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public class SpatialTicker
{
    public static async Task TickParallelAsync(TickableSpatialLattice lattice, int maxThreads = -1)
    {
        if (maxThreads <= 0) maxThreads = Environment.ProcessorCount;

        var root = lattice.GetRootNode() as TickableOctetParentNode;
        if (root == null) return;

        var children = root.Children;
        var tasks = new List<Task>();

        // Partition children across threads
        int threadsUsed = System.Math.Min(maxThreads, children.Length);
        var partitions = PartitionChildren(children, threadsUsed);

        foreach (var partition in partitions)
        {
            tasks.Add(Task.Run(() =>
            {
                foreach (var child in partition)
                {
                    if (child is ITickableChildNode tickableChild)
                    {
                        using var depthScope = SpatialLattice.PushLatticeDepth(lattice.LatticeDepth);
                        tickableChild.Tick();
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    public static void TickParallel(TickableSpatialLattice lattice, int maxThreads = -1)
    {
        TickParallelAsync(lattice, maxThreads).Wait();
    }

    private static List<List<IChildNode<OctetParentNode>>> PartitionChildren(IChildNode<OctetParentNode>[] children, int numPartitions)
    {
        var partitions = new List<List<IChildNode<OctetParentNode>>>();
        for (int i = 0; i < numPartitions; i++)
            partitions.Add([]);

        for (int i = 0; i < children.Length; i++)
        {
            partitions[i % numPartitions].Add(children[i]);
        }

        return partitions;
    }
}