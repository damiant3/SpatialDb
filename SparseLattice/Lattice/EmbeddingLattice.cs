using System.Numerics;
using SparseLattice.Math;
////////////////////////////////
namespace SparseLattice.Lattice;

public sealed class EmbeddingLattice<TPayload>
{
    ISparseNode m_root;
    bool m_frozen;

    public bool IsFrozen => m_frozen;

    internal ISparseNode Root => m_root;

    public EmbeddingLattice(SparseOccupant<TPayload>[] occupants, LatticeOptions? options = null)
    {
        m_root = EmbeddingLatticeBuilder.Build(occupants, options);
        m_frozen = false;
    }

    internal static EmbeddingLattice<TPayload> FromFrozenRoot(ISparseNode root)
    {
        EmbeddingLattice<TPayload> lattice = new(root);
        lattice.m_frozen = true;
        return lattice;
    }

    EmbeddingLattice(ISparseNode root)
    {
        m_root = root;
        m_frozen = false;
    }

    public void Freeze()
    {
        if (m_frozen)
            throw new InvalidOperationException("Lattice is already frozen.");
        m_root = FreezeNode(m_root);
        m_frozen = true;
    }

    public SparseTreeStats CollectStats()
        => CollectStatsRecursive(m_root, 0);

    public SparsityReport CollectSparsityReport()
    {
        SparsityReportAccumulator acc = new();
        CollectSparsityRecursive(m_root, acc);
        return acc.Build();
    }

    public List<SparseOccupant<TPayload>> QueryWithinDistanceL2(
        SparseVector center,
        BigInteger radiusSquared)
    {
        List<SparseOccupant<TPayload>> results = [];
        QueryRecursiveL2(m_root, center, radiusSquared, results);
        return results;
    }

    public List<SparseOccupant<TPayload>> QueryWithinDistanceL1(
        SparseVector center,
        BigInteger maxL1Distance)
    {
        List<SparseOccupant<TPayload>> results = [];
        QueryRecursiveL1(m_root, center, maxL1Distance, results);
        return results;
    }

    public List<SparseOccupant<TPayload>> QueryKNearestL2(SparseVector center, int k)
    {
        if (k <= 0)
            return [];
        KnnHeap heap = new(k);
        QueryKNearestRecursiveL2(m_root, center, k, heap);
        return heap.DrainAscending();
    }

    public List<SparseOccupant<TPayload>> QueryKNearestL1(SparseVector center, int k)
    {
        if (k <= 0)
            return [];
        KnnHeap heap = new(k);
        QueryKNearestRecursiveL1(m_root, center, k, heap);
        return heap.DrainAscending();
    }

    static ISparseNode FreezeNode(ISparseNode node)
    {
        if (node is SparseLeafNode<TPayload>)
            return node;

        if (!node.TryGetBranch(out ushort splitDimension, out long splitValue,
                out ISparseNode? below, out ISparseNode? above))
            return node;

        ISparseNode? frozenBelow = below is not null ? FreezeNode(below) : null;
        ISparseNode? frozenAbove = above is not null ? FreezeNode(above) : null;
        return new FrozenBranchNode(splitDimension, splitValue, frozenBelow, frozenAbove);
    }

    static SparseTreeStats CollectStatsRecursive(ISparseNode node, int depth)
    {
        if (node is SparseLeafNode<TPayload> leaf)
            return new SparseTreeStats
            {
                TotalNodes = 1,
                BranchNodes = 0,
                LeafNodes = 1,
                TotalOccupants = leaf.Count,
                MaxDepth = depth,
                AverageLeafOccupancy = leaf.Count,
            };

        if (!node.TryGetBranch(out ushort _, out long _, out ISparseNode? below, out ISparseNode? above))
            return new SparseTreeStats { TotalNodes = 1, MaxDepth = depth };

        SparseTreeStats belowStats = below is not null
            ? CollectStatsRecursive(below, depth + 1)
            : new SparseTreeStats();

        SparseTreeStats aboveStats = above is not null
            ? CollectStatsRecursive(above, depth + 1)
            : new SparseTreeStats();

        int totalLeaves = belowStats.LeafNodes + aboveStats.LeafNodes;
        double avgOccupancy = totalLeaves > 0
            ? (double)(belowStats.TotalOccupants + aboveStats.TotalOccupants) / totalLeaves
            : 0;

        return new SparseTreeStats
        {
            TotalNodes = 1 + belowStats.TotalNodes + aboveStats.TotalNodes,
            BranchNodes = 1 + belowStats.BranchNodes + aboveStats.BranchNodes,
            LeafNodes = belowStats.LeafNodes + aboveStats.LeafNodes,
            TotalOccupants = belowStats.TotalOccupants + aboveStats.TotalOccupants,
            MaxDepth = System.Math.Max(belowStats.MaxDepth, aboveStats.MaxDepth),
            AverageLeafOccupancy = avgOccupancy,
        };
    }

    static void CollectSparsityRecursive(ISparseNode node, SparsityReportAccumulator acc)
    {
        if (node is SparseLeafNode<TPayload> leaf)
        {
            acc.RecordLeaf(leaf);
            return;
        }

        if (!node.TryGetBranch(out ushort _, out long _, out ISparseNode? below, out ISparseNode? above))
            return;

        int realizedCount = (below is not null ? 1 : 0) + (above is not null ? 1 : 0);
        acc.RecordBranch(realizedCount);

        if (below is not null) CollectSparsityRecursive(below, acc);
        if (above is not null) CollectSparsityRecursive(above, acc);
    }

    static void QueryRecursiveL2(
        ISparseNode node,
        SparseVector center,
        BigInteger radiusSquared,
        List<SparseOccupant<TPayload>> results)
    {
        if (node is SparseLeafNode<TPayload> leaf)
        {
            foreach (SparseOccupant<TPayload> occupant in leaf.Occupants)
                if (center.DistanceSquaredL2(occupant.Position) <= radiusSquared)
                    results.Add(occupant);
            return;
        }

        if (!node.TryGetBranch(out ushort splitDimension, out long splitValue,
                out ISparseNode? below, out ISparseNode? above))
            return;

        DescendBranchL2(splitDimension, splitValue, below, above, center, radiusSquared, results);
    }

    static void DescendBranchL2(
        ushort splitDimension, long splitValue,
        ISparseNode? below, ISparseNode? above,
        SparseVector center, BigInteger radiusSquared,
        List<SparseOccupant<TPayload>> results)
    {
        long centerVal = center.ValueAt(splitDimension);
        ISparseNode? near = centerVal < splitValue ? below : above;
        ISparseNode? far = centerVal < splitValue ? above : below;

        if (near is not null)
            QueryRecursiveL2(near, center, radiusSquared, results);

        if (far is not null)
        {
            long diff = centerVal - splitValue;
            BigInteger splitDistSquared = (BigInteger)diff * diff;
            if (splitDistSquared <= radiusSquared)
                QueryRecursiveL2(far, center, radiusSquared, results);
        }
    }

    static void QueryRecursiveL1(
        ISparseNode node,
        SparseVector center,
        BigInteger maxDistance,
        List<SparseOccupant<TPayload>> results)
    {
        if (node is SparseLeafNode<TPayload> leaf)
        {
            foreach (SparseOccupant<TPayload> occupant in leaf.Occupants)
                if (center.DistanceL1(occupant.Position) <= maxDistance)
                    results.Add(occupant);
            return;
        }

        if (!node.TryGetBranch(out ushort splitDimension, out long splitValue,
                out ISparseNode? below, out ISparseNode? above))
            return;

        DescendBranchL1(splitDimension, splitValue, below, above, center, maxDistance, results);
    }

    static void DescendBranchL1(
        ushort splitDimension, long splitValue,
        ISparseNode? below, ISparseNode? above,
        SparseVector center, BigInteger maxDistance,
        List<SparseOccupant<TPayload>> results)
    {
        long centerVal = center.ValueAt(splitDimension);
        ISparseNode? near = centerVal < splitValue ? below : above;
        ISparseNode? far = centerVal < splitValue ? above : below;

        if (near is not null)
            QueryRecursiveL1(near, center, maxDistance, results);

        if (far is not null)
        {
            BigInteger splitDist = BigInteger.Abs(centerVal - splitValue);
            if (splitDist <= maxDistance)
                QueryRecursiveL1(far, center, maxDistance, results);
        }
    }

    // --- KNN L2 with heap pruning ---

    void QueryKNearestRecursiveL2(ISparseNode node, SparseVector center, int k, KnnHeap heap)
    {
        if (node is SparseLeafNode<TPayload> leaf)
        {
            foreach (SparseOccupant<TPayload> occupant in leaf.Occupants)
                heap.TryInsert(occupant, center.DistanceSquaredL2Fast(occupant.Position));
            return;
        }

        if (!node.TryGetBranch(out ushort splitDimension, out long splitValue,
                out ISparseNode? below, out ISparseNode? above))
            return;

        DescendKnnL2(splitDimension, splitValue, below, above, center, k, heap);
    }

    void DescendKnnL2(
        ushort splitDimension, long splitValue,
        ISparseNode? below, ISparseNode? above,
        SparseVector center, int k, KnnHeap heap)
    {
        long centerVal = center.ValueAtFast(splitDimension);
        ISparseNode? near = centerVal < splitValue ? below : above;
        ISparseNode? far  = centerVal < splitValue ? above : below;

        if (near is not null)
            QueryKNearestRecursiveL2(near, center, k, heap);

        if (far is not null)
        {
            long diff = centerVal - splitValue;
            ulong splitDistSquared = (ulong)(diff * diff);
            if (!heap.IsFull || splitDistSquared <= heap.WorstDistance)
                QueryKNearestRecursiveL2(far, center, k, heap);
        }
    }

    // --- KNN L1 with heap pruning ---

    private void QueryKNearestRecursiveL1(ISparseNode node, SparseVector center, int k, KnnHeap heap)
    {
        if (node is SparseLeafNode<TPayload> leaf)
        {
            foreach (SparseOccupant<TPayload> occupant in leaf.Occupants)
                heap.TryInsert(occupant, center.DistanceL1Fast(occupant.Position));
            return;
        }

        if (!node.TryGetBranch(out ushort splitDimension, out long splitValue,
                out ISparseNode? below, out ISparseNode? above))
            return;

        DescendKnnL1(splitDimension, splitValue, below, above, center, k, heap);
    }
    void DescendKnnL1(
        ushort splitDimension, long splitValue,
        ISparseNode? below, ISparseNode? above,
        SparseVector center, int k, KnnHeap heap)
    {
        long centerVal = center.ValueAtFast(splitDimension);
        ISparseNode? near = centerVal < splitValue ? below : above;
        ISparseNode? far  = centerVal < splitValue ? above : below;

        if (near is not null)
            QueryKNearestRecursiveL1(near, center, k, heap);

        if (far is not null)
        {
            long splitDist = centerVal - splitValue;
            ulong absSplitDist = (ulong)(splitDist < 0 ? -splitDist : splitDist);
            if (!heap.IsFull || absSplitDist <= heap.WorstDistance)
                QueryKNearestRecursiveL1(far, center, k, heap);
        }
    }
    private sealed class KnnHeap(int capacity)
    {
        readonly (SparseOccupant<TPayload> occupant, ulong distance)[] m_heap
            = new (SparseOccupant<TPayload>, ulong)[capacity];

        int m_count;

        public bool IsFull => m_count == capacity;
        public ulong WorstDistance => m_heap[0].distance;

        public void TryInsert(SparseOccupant<TPayload> occupant, ulong distance)
        {
            if (m_count < capacity)
            {
                m_heap[m_count] = (occupant, distance);
                m_count++;
                SiftUp(m_count - 1);
            }
            else if (distance < m_heap[0].distance)
            {
                m_heap[0] = (occupant, distance);
                SiftDown(0);
            }
        }

        public List<SparseOccupant<TPayload>> DrainAscending()
        {
            int count = m_count;
            System.Array.Sort(m_heap, 0, count,
                Comparer<(SparseOccupant<TPayload>, ulong)>.Create(
                    (a, b) => a.Item2.CompareTo(b.Item2)));
            List<SparseOccupant<TPayload>> result = new(count);
            for (int i = 0; i < count; i++)
                result.Add(m_heap[i].occupant);
            return result;
        }

        void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (m_heap[index].distance <= m_heap[parent].distance)
                    break;
                (m_heap[index], m_heap[parent]) = (m_heap[parent], m_heap[index]);
                index = parent;
            }
        }

        void SiftDown(int index)
        {
            while (true)
            {
                int largest = index;
                int left = 2 * index + 1;
                int right = 2 * index + 2;
                if (left < m_count && m_heap[left].distance > m_heap[largest].distance)
                    largest = left;
                if (right < m_count && m_heap[right].distance > m_heap[largest].distance)
                    largest = right;
                if (largest == index)
                    break;
                (m_heap[index], m_heap[largest]) = (m_heap[largest], m_heap[index]);
                index = largest;
            }
        }
    }
}
