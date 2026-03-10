using System.Numerics;
using SparseLattice.Math;
/////////////////////////////////
namespace SparseLattice.Lattice;

public sealed class EmbeddingLattice<TPayload>
{
    private ISparseNode m_root;
    private bool m_frozen;

    public bool IsFrozen => m_frozen;

    /// <summary>
    /// Exposes the root node for serialization. Accessible to <see cref="SparseLatticeSerializer"/>.
    /// </summary>
    internal ISparseNode Root => m_root;

    public EmbeddingLattice(SparseOccupant<TPayload>[] occupants, LatticeOptions? options = null)
    {
        m_root = EmbeddingLatticeBuilder.Build(occupants, options);
        m_frozen = false;
    }

    /// <summary>
    /// Constructs an already-frozen lattice from a pre-built frozen root node.
    /// Used exclusively by <see cref="SparseLatticeSerializer.Load{TPayload}"/>.
    /// </summary>
    internal static EmbeddingLattice<TPayload> FromFrozenRoot(ISparseNode root)
    {
        EmbeddingLattice<TPayload> lattice = new(root);
        lattice.m_frozen = true;
        return lattice;
    }

    private EmbeddingLattice(ISparseNode root)
    {
        m_root = root;
        m_frozen = false;
    }

    /// <summary>
    /// Freezes the lattice. Converts all mutable build-phase <see cref="SparseBranchNode"/>
    /// instances into compact <see cref="FrozenBranchNode"/> instances backed by fixed-size arrays.
    /// After freeze the tree is immutable and safe for concurrent lock-free reads.
    /// Throws <see cref="InvalidOperationException"/> if called more than once.
    /// </summary>
    public void Freeze()
    {
        if (m_frozen)
            throw new InvalidOperationException("Lattice is already frozen.");
        m_root = FreezeNode(m_root);
        m_frozen = true;
    }

    /// <summary>
    /// Collects structural statistics by walking the frozen (or pre-freeze) tree.
    /// </summary>
    public SparseTreeStats CollectStats()
        => CollectStatsRecursive(m_root, 0);

    /// <summary>
    /// Collects a full <see cref="SparsityReport"/> by walking the tree.
    /// Reports nnz distribution, dimension coverage, leaf occupancy, and branch balance.
    /// </summary>
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

    /// <summary>
    /// Returns the K nearest occupants by L2 distance using a max-heap bounded to K entries.
    /// The heap's worst distance serves as a live pruning radius, avoiding full tree scans
    /// once K candidates have been found and the tree can be cut early.
    /// </summary>
    public List<SparseOccupant<TPayload>> QueryKNearestL2(SparseVector center, int k)
    {
        if (k <= 0)
            return [];
        KnnHeap heap = new(k);
        QueryKNearestRecursiveL2(m_root, center, k, heap);
        return heap.DrainAscending();
    }

    /// <summary>
    /// Returns the K nearest occupants by L1 (Manhattan) distance using a max-heap bounded to K entries.
    /// </summary>
    public List<SparseOccupant<TPayload>> QueryKNearestL1(SparseVector center, int k)
    {
        if (k <= 0)
            return [];
        KnnHeap heap = new(k);
        QueryKNearestRecursiveL1(m_root, center, k, heap);
        return heap.DrainAscending();
    }

    // --- freeze ---

    private static ISparseNode FreezeNode(ISparseNode node)
    {
        if (node is SparseLeafNode<TPayload>)
            return node;

        if (node is SparseBranchNode mutable)
        {
            ISparseNode? below = mutable.Below is not null ? FreezeNode(mutable.Below) : null;
            ISparseNode? above = mutable.Above is not null ? FreezeNode(mutable.Above) : null;
            return new FrozenBranchNode(mutable.SplitDimension, mutable.SplitValue, below, above);
        }

        if (node is FrozenBranchNode frozen)
        {
            ISparseNode? below = frozen.Below is not null ? FreezeNode(frozen.Below) : null;
            ISparseNode? above = frozen.Above is not null ? FreezeNode(frozen.Above) : null;
            return new FrozenBranchNode(frozen.SplitDimension, frozen.SplitValue, below, above);
        }

        return node;
    }

    // --- stats ---

    private static SparseTreeStats CollectStatsRecursive(ISparseNode node, int depth)
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

        ushort splitDimension;
        long splitValue;
        ISparseNode? below;
        ISparseNode? above;

        if (node is FrozenBranchNode frozenBranch)
        {
            splitDimension = frozenBranch.SplitDimension;
            splitValue = frozenBranch.SplitValue;
            below = frozenBranch.Below;
            above = frozenBranch.Above;
        }
        else if (node is SparseBranchNode mutableBranch)
        {
            splitDimension = mutableBranch.SplitDimension;
            splitValue = mutableBranch.SplitValue;
            below = mutableBranch.Below;
            above = mutableBranch.Above;
        }
        else
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

    // --- sparsity report ---

    private static void CollectSparsityRecursive(ISparseNode node, SparsityReportAccumulator acc)
    {
        if (node is SparseLeafNode<TPayload> leaf)
        {
            acc.RecordLeaf(leaf);
            return;
        }

        ISparseNode? below;
        ISparseNode? above;

        if (node is FrozenBranchNode frozen)
        {
            below = frozen.Below;
            above = frozen.Above;
        }
        else if (node is SparseBranchNode mutable)
        {
            below = mutable.Below;
            above = mutable.Above;
        }
        else
            return;

        int realizedCount = (below is not null ? 1 : 0) + (above is not null ? 1 : 0);
        acc.RecordBranch(realizedCount);

        if (below is not null) CollectSparsityRecursive(below, acc);
        if (above is not null) CollectSparsityRecursive(above, acc);
    }

    // --- L2 radius query ---

    private static void QueryRecursiveL2(
        ISparseNode node,
        SparseVector center,
        BigInteger radiusSquared,
        List<SparseOccupant<TPayload>> results)
    {
        switch (node)
        {
            case SparseLeafNode<TPayload> leaf:
                foreach (SparseOccupant<TPayload> occupant in leaf.Occupants)
                    if (center.DistanceSquaredL2(occupant.Position) <= radiusSquared)
                        results.Add(occupant);
                break;

            case FrozenBranchNode frozen:
                DescendBranchL2(frozen.SplitDimension, frozen.SplitValue, frozen.Below, frozen.Above,
                    center, radiusSquared, results);
                break;

            case SparseBranchNode mutable:
                DescendBranchL2(mutable.SplitDimension, mutable.SplitValue, mutable.Below, mutable.Above,
                    center, radiusSquared, results);
                break;
        }
    }

    private static void DescendBranchL2(
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

    // --- L1 radius query ---

    private static void QueryRecursiveL1(
        ISparseNode node,
        SparseVector center,
        BigInteger maxDistance,
        List<SparseOccupant<TPayload>> results)
    {
        switch (node)
        {
            case SparseLeafNode<TPayload> leaf:
                foreach (SparseOccupant<TPayload> occupant in leaf.Occupants)
                    if (center.DistanceL1(occupant.Position) <= maxDistance)
                        results.Add(occupant);
                break;

            case FrozenBranchNode frozen:
                DescendBranchL1(frozen.SplitDimension, frozen.SplitValue, frozen.Below, frozen.Above,
                    center, maxDistance, results);
                break;

            case SparseBranchNode mutable:
                DescendBranchL1(mutable.SplitDimension, mutable.SplitValue, mutable.Below, mutable.Above,
                    center, maxDistance, results);
                break;
        }
    }

    private static void DescendBranchL1(
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

    private void QueryKNearestRecursiveL2(ISparseNode node, SparseVector center, int k, KnnHeap heap)
    {
        switch (node)
        {
            case SparseLeafNode<TPayload> leaf:
                foreach (SparseOccupant<TPayload> occupant in leaf.Occupants)
                    heap.TryInsert(occupant, center.DistanceSquaredL2(occupant.Position));
                break;

            case FrozenBranchNode frozen:
                DescendKnnL2(frozen.SplitDimension, frozen.SplitValue, frozen.Below, frozen.Above,
                    center, k, heap);
                break;

            case SparseBranchNode mutable:
                DescendKnnL2(mutable.SplitDimension, mutable.SplitValue, mutable.Below, mutable.Above,
                    center, k, heap);
                break;
        }
    }

    private void DescendKnnL2(
        ushort splitDimension, long splitValue,
        ISparseNode? below, ISparseNode? above,
        SparseVector center, int k, KnnHeap heap)
    {
        long centerVal = center.ValueAt(splitDimension);
        ISparseNode? near = centerVal < splitValue ? below : above;
        ISparseNode? far = centerVal < splitValue ? above : below;

        if (near is not null)
            QueryKNearestRecursiveL2(near, center, k, heap);

        if (far is not null)
        {
            long diff = centerVal - splitValue;
            BigInteger splitDistSquared = (BigInteger)diff * diff;
            if (!heap.IsFull || splitDistSquared <= heap.WorstDistance)
                QueryKNearestRecursiveL2(far, center, k, heap);
        }
    }

    // --- KNN L1 with heap pruning ---

    private void QueryKNearestRecursiveL1(ISparseNode node, SparseVector center, int k, KnnHeap heap)
    {
        switch (node)
        {
            case SparseLeafNode<TPayload> leaf:
                foreach (SparseOccupant<TPayload> occupant in leaf.Occupants)
                    heap.TryInsert(occupant, center.DistanceL1(occupant.Position));
                break;

            case FrozenBranchNode frozen:
                DescendKnnL1(frozen.SplitDimension, frozen.SplitValue, frozen.Below, frozen.Above,
                    center, k, heap);
                break;

            case SparseBranchNode mutable:
                DescendKnnL1(mutable.SplitDimension, mutable.SplitValue, mutable.Below, mutable.Above,
                    center, k, heap);
                break;
        }
    }

    private void DescendKnnL1(
        ushort splitDimension, long splitValue,
        ISparseNode? below, ISparseNode? above,
        SparseVector center, int k, KnnHeap heap)
    {
        long centerVal = center.ValueAt(splitDimension);
        ISparseNode? near = centerVal < splitValue ? below : above;
        ISparseNode? far = centerVal < splitValue ? above : below;

        if (near is not null)
            QueryKNearestRecursiveL1(near, center, k, heap);

        if (far is not null)
        {
            BigInteger splitDist = BigInteger.Abs(centerVal - splitValue);
            if (!heap.IsFull || splitDist <= heap.WorstDistance)
                QueryKNearestRecursiveL1(far, center, k, heap);
        }
    }

    // --- max-heap bounded to K entries ---

    private sealed class KnnHeap(int capacity)
    {
        private readonly (SparseOccupant<TPayload> occupant, BigInteger distance)[] m_heap
            = new (SparseOccupant<TPayload>, BigInteger)[capacity];

        private int m_count;

        public bool IsFull => m_count == capacity;
        public BigInteger WorstDistance => m_heap[0].distance;

        public void TryInsert(SparseOccupant<TPayload> occupant, BigInteger distance)
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
            // sort the backing array in ascending distance order then return occupants
            int count = m_count;
            System.Array.Sort(m_heap, 0, count,
                Comparer<(SparseOccupant<TPayload>, BigInteger)>.Create(
                    (a, b) => a.Item2.CompareTo(b.Item2)));
            List<SparseOccupant<TPayload>> result = new(count);
            for (int i = 0; i < count; i++)
                result.Add(m_heap[i].occupant);
            return result;
        }

        private void SiftUp(int index)
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

        private void SiftDown(int index)
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
