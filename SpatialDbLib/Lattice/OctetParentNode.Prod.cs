#if !DIAGNOSTIC
using SpatialDbLib.Synchronize;
using System.Runtime.CompilerServices;
///////////////////////////////
namespace SpatialDbLib.Lattice;
public abstract partial class OctetParentNode
{
    partial void Subdivide_Impl(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        using var parentLock = new SlimSyncer(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent");
        using var leafLock = new SlimSyncer(((ISync)subdividingleaf).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf");
        if (subdividingleaf.IsRetired) return;
        var migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        var occupantsSnapshot = migrationSnapshot.Objects;
        IInternalChildNode newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }
    partial void Migrate_Impl(IList<ISpatialObject> objs)
    {
        using var s = RentArray<List<ISpatialObject>>(8, out var buckets);
        for (int i = 0; i < objs.Count; i++)
        {
            var obj = objs[i];
            if (!Bounds.Contains(obj.LocalPosition)) throw new InvalidOperationException("Migrant has no home.");
            if (SelectChild(obj.LocalPosition) is not SelectChildResult result) throw new InvalidOperationException("Containment invariant violated");
            var idx = result.IndexInParent;
            buckets[idx] ??= new List<ISpatialObject>();
            buckets[idx].Add(obj);
        }
        for (byte i = 0; i < 8; i++)
        {
            if (buckets[i] == null) continue;
            Children[i].MigrateInternal(buckets[i]);
        }
    }
}
#endif