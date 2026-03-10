using System.Numerics;
using SparseLattice.Lattice;
using SparseLattice.Math;
////////////////////////////////////////////
namespace SparseLattice.Test.Lattice;

[TestClass]
public sealed class EmbeddingLatticeBuilderTests
{
    [TestMethod]
    public void Unit_Builder_EmptyInput_ProducesEmptyLeaf()
    {
        SparseOccupant<string>[] empty = [];
        ISparseNode root = EmbeddingLatticeBuilder.Build(empty);
        Assert.IsInstanceOfType<SparseLeafNode<string>>(root);
        SparseLeafNode<string> leaf = (SparseLeafNode<string>)root;
        Assert.AreEqual(0, leaf.Count);
    }

    [TestMethod]
    public void Unit_Builder_SingleItem_ProducesLeaf()
    {
        SparseVector vector = new([new(0, 100L)], 10);
        SparseOccupant<string>[] items = [new(vector, "item1")];
        ISparseNode root = EmbeddingLatticeBuilder.Build(items);
        Assert.IsInstanceOfType<SparseLeafNode<string>>(root);
        SparseLeafNode<string> leaf = (SparseLeafNode<string>)root;
        Assert.AreEqual(1, leaf.Count);
        Assert.AreEqual("item1", leaf.Occupants[0].Payload);
    }

    [TestMethod]
    public void Unit_Builder_UnderThreshold_ProducesLeaf()
    {
        LatticeOptions options = new() { LeafThreshold = 16 };
        SparseOccupant<int>[] items = new SparseOccupant<int>[8];
        for (int i = 0; i < 8; i++)
        {
            long value = (i + 1) * 100L;
            SparseVector vector = new([new(0, value)], 10);
            items[i] = new(vector, i);
        }

        ISparseNode root = EmbeddingLatticeBuilder.Build(items, options);
        Assert.IsInstanceOfType<SparseLeafNode<int>>(root);
        Assert.AreEqual(8, ((SparseLeafNode<int>)root).Count);
    }

    [TestMethod]
    public void Unit_Builder_OverThreshold_CreatesBranch()
    {
        LatticeOptions options = new() { LeafThreshold = 4 };
        SparseOccupant<int>[] items = new SparseOccupant<int>[20];
        for (int i = 0; i < 20; i++)
        {
            long value = (i - 10) * 1000L;
            SparseVector vector = new([new(0, value == 0 ? 1L : value)], 10);
            items[i] = new(vector, i);
        }

        ISparseNode root = EmbeddingLatticeBuilder.Build(items, options);
        Assert.IsInstanceOfType<SparseBranchNode>(root);
    }

    [TestMethod]
    public void Invariant_Builder_PreservesAllElements()
    {
        LatticeOptions options = new() { LeafThreshold = 4 };
        SparseOccupant<int>[] items = new SparseOccupant<int>[50];
        for (int i = 0; i < 50; i++)
        {
            long value = (i + 1) * 100L;
            SparseVector vector = new([new((ushort)(i % 10), value)], 20);
            items[i] = new(vector, i);
        }

        ISparseNode root = EmbeddingLatticeBuilder.Build(items, options);
        int totalCount = CountLeafOccupants<int>(root);
        Assert.AreEqual(50, totalCount);
    }

    [TestMethod]
    public void Invariant_Builder_LeavesSizeWithinThreshold()
    {
        LatticeOptions options = new() { LeafThreshold = 8 };
        SparseOccupant<int>[] items = new SparseOccupant<int>[100];
        for (int i = 0; i < 100; i++)
        {
            long xVal = (i % 10 + 1) * 100L;
            long yVal = (i / 10 + 1) * 200L;
            SparseVector vector = new([new(0, xVal), new(1, yVal)], 5);
            items[i] = new(vector, i);
        }

        ISparseNode root = EmbeddingLatticeBuilder.Build(items, options);
        AssertLeafSizesWithinBound(root, options.LeafThreshold * 2);
    }

    [TestMethod]
    public void Invariant_NoDenseChildrenAllocated()
    {
        LatticeOptions options = new() { LeafThreshold = 4 };
        SparseOccupant<int>[] items = new SparseOccupant<int>[30];
        for (int i = 0; i < 30; i++)
        {
            long value = (i + 1) * 50L;
            SparseVector vector = new([new((ushort)(i % 5), value)], 10);
            items[i] = new(vector, i);
        }

        ISparseNode root = EmbeddingLatticeBuilder.Build(items, options);
        AssertNoDenseChildArrays(root);
    }

    [TestMethod]
    public void Invariant_Builder_IsDeterministic()
    {
        LatticeOptions options = new() { LeafThreshold = 4 };
        SparseOccupant<int>[] MakeItems()
        {
            SparseOccupant<int>[] items = new SparseOccupant<int>[40];
            for (int i = 0; i < 40; i++)
            {
                long value = (i + 1) * 77L;
                SparseVector vector = new([new((ushort)(i % 8), value)], 16);
                items[i] = new(vector, i);
            }
            return items;
        }

        ISparseNode root1 = EmbeddingLatticeBuilder.Build(MakeItems(), options);
        ISparseNode root2 = EmbeddingLatticeBuilder.Build(MakeItems(), options);

        string shape1 = SerializeTreeShape(root1);
        string shape2 = SerializeTreeShape(root2);
        Assert.AreEqual(shape1, shape2);
    }

    [TestMethod]
    public void Unit_BranchNode_SplitDimensionWithinBounds()
    {
        LatticeOptions options = new() { LeafThreshold = 2 };
        SparseOccupant<int>[] items =
        [
            new(new([new(3, 100L), new(7, 200L)], 10), 0),
            new(new([new(3, -100L), new(7, -200L)], 10), 1),
            new(new([new(3, 500L), new(7, 300L)], 10), 2),
            new(new([new(3, -500L), new(7, -300L)], 10), 3),
        ];

        ISparseNode root = EmbeddingLatticeBuilder.Build(items, options);
        AssertSplitDimensionsWithinBounds(root, 10);
    }

    private static int CountLeafOccupants<TPayload>(ISparseNode node)
    {
        if (node is SparseLeafNode<TPayload> leaf)
            return leaf.Count;
        if (node is SparseBranchNode branch)
        {
            int count = 0;
            if (branch.Below is not null) count += CountLeafOccupants<TPayload>(branch.Below);
            if (branch.Above is not null) count += CountLeafOccupants<TPayload>(branch.Above);
            return count;
        }
        return 0;
    }

    private static void AssertLeafSizesWithinBound(ISparseNode node, int upperBound)
    {
        if (node is SparseLeafNode<int> leaf)
            Assert.IsTrue(leaf.Count <= upperBound,
                $"Leaf has {leaf.Count} occupants, exceeds upper bound of {upperBound}");
        if (node is SparseBranchNode branch)
        {
            if (branch.Below is not null) AssertLeafSizesWithinBound(branch.Below, upperBound);
            if (branch.Above is not null) AssertLeafSizesWithinBound(branch.Above, upperBound);
        }
    }

    private static void AssertNoDenseChildArrays(ISparseNode node)
    {
        if (node is SparseBranchNode branch)
        {
            System.Reflection.FieldInfo[] fields = typeof(SparseBranchNode)
                .GetFields(System.Reflection.BindingFlags.Instance |
                           System.Reflection.BindingFlags.NonPublic |
                           System.Reflection.BindingFlags.Public);

            foreach (System.Reflection.FieldInfo field in fields)
                if (field.FieldType.IsArray && field.FieldType != typeof(byte[]))
                {
                    object? value = field.GetValue(branch);
                    if (value is Array arr)
                        Assert.IsTrue(arr.Length < 256,
                            $"Field {field.Name} has array of length {arr.Length}, looks like dense child allocation");
                }

            Assert.IsTrue(branch.RealizedChildCount <= 2);
            if (branch.Below is not null) AssertNoDenseChildArrays(branch.Below);
            if (branch.Above is not null) AssertNoDenseChildArrays(branch.Above);
        }
    }

    private static void AssertSplitDimensionsWithinBounds(ISparseNode node, int totalDimensions)
    {
        if (node is SparseBranchNode branch)
        {
            Assert.IsTrue(branch.SplitDimension < totalDimensions,
                $"SplitDimension {branch.SplitDimension} >= TotalDimensions {totalDimensions}");
            if (branch.Below is not null) AssertSplitDimensionsWithinBounds(branch.Below, totalDimensions);
            if (branch.Above is not null) AssertSplitDimensionsWithinBounds(branch.Above, totalDimensions);
        }
    }

    private static string SerializeTreeShape(ISparseNode node)
    {
        if (node is SparseLeafNode<int> leaf)
            return $"L({leaf.Count})";
        if (node is SparseBranchNode branch)
        {
            string below = branch.Below is not null ? SerializeTreeShape(branch.Below) : "null";
            string above = branch.Above is not null ? SerializeTreeShape(branch.Above) : "null";
            return $"B({branch.SplitDimension},{branch.SplitValue},{below},{above})";
        }
        return "?";
    }
}
