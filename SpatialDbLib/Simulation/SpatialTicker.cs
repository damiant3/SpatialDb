using SpatialDbLib.Lattice;
//////////////////////////////////
namespace SpatialDbLib.Simulation;

public class SpatialTicker
{
    public static async Task TickParallelAsync(TickableSpatialLattice lattice, int maxThreads = -1)
    {
        if (maxThreads <= 0) maxThreads = Environment.ProcessorCount;

        TickableOctetParentNode? root = lattice.GetRootNode() as TickableOctetParentNode;
        if (root == null) return;

        IInternalChildNode[] children = root.Children;
        List<Task> tasks = new();

        int threadsUsed = System.Math.Min(maxThreads, children.Length);
        List<List<IChildNode<OctetParentNode>>> partitions = PartitionChildren(children, threadsUsed);

        foreach (List<IChildNode<OctetParentNode>> partition in partitions)
        {
            tasks.Add(Task.Run(() =>
            {
                foreach (IChildNode<OctetParentNode> child in partition)
                {
                    if (child is ITickableChildNode tickableChild)
                    {
                        using IDisposable depthScope = SpatialLattice.PushLatticeDepth(lattice.LatticeDepth);
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

    private static List<List<IChildNode<OctetParentNode>>> PartitionChildren(IInternalChildNode[] children, int numPartitions)
    {
        List<List<IChildNode<OctetParentNode>>> partitions = new();
        for (int i = 0; i < numPartitions; i++)
            partitions.Add([]);

        for (int i = 0; i < children.Length; i++)
            partitions[i % numPartitions].Add(children[i]);

        return partitions;
    }
}
