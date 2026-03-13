#if !DIAGNOSTIC
using Common.Core.Rentals;
using Common.Core.Sync;
///////////////////////////////
namespace SpatialDbLib.Lattice;
public abstract partial class OctetParentNode
{
    partial void Subdivide_Impl(OctetParentNode parent, VenueLeafNode subdividingleaf, byte latticeDepth, int childIndex, bool branchOrSublattice)
    {
        using SlimSyncer parentLock = new(((ISync)parent).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Parent");
        using SlimSyncer leafLock = new(((ISync)subdividingleaf).Sync, SlimSyncer.LockMode.Write, "SubdivideAndMigrate: Leaf");
        if (subdividingleaf.IsRetired) return;
        MultiObjectScope<ISpatialObject> migrationSnapshot = subdividingleaf.LockAndSnapshotForMigration();
        List<ISpatialObject> occupantsSnapshot = migrationSnapshot.Objects;
        IInternalChildNode newBranch = CreateBranchNodeWithLeafs(parent, subdividingleaf, latticeDepth, branchOrSublattice, occupantsSnapshot);
        parent.Children[childIndex] = newBranch;
        subdividingleaf.Retire();
        migrationSnapshot.Dispose();
    }
    partial void Migrate_Impl(IList<ISpatialObject> objs)
    {
        using ArrayRentalContract<List<ISpatialObject>> s = ArrayRental.Rent<List<ISpatialObject>>(8, out List<ISpatialObject>[] buckets);
        for (int i = 0; i < objs.Count; i++)
        {
            ISpatialObject obj = objs[i];
            if (!Bounds.Contains(obj.LocalPosition)) throw new InvalidOperationException("Migrant has no home.");
            if (SelectChild(obj.LocalPosition) is not SelectChildResult result) throw new InvalidOperationException("Containment invariant violated");
            byte idx = result.IndexInParent;
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
