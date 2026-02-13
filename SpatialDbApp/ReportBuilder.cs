using SpatialDbLib.Math;
using SpatialDbLib.Simulation;
using System.Collections.Concurrent;
using System.Text;
/////////////////////////////////
namespace SpatialDbApp.Reporting;

internal static class ReportBuilder
{
    public static string ReportMovedButUnregistered(TickableSpatialObject failedMovedObj, TickableVenueLeafNode? failedLeaf, ConcurrentDictionary<TickableSpatialObject, LongVector3>? initialPositions)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\n=== Detailed diagnosis of first moved-but-unregistered object ===");
        sb.AppendLine($"  GUID: {failedMovedObj.Guid}");
        sb.AppendLine($"  Initial pos: {initialPositions![failedMovedObj]}");
        sb.AppendLine($"  Current pos: {failedMovedObj.LocalPosition}");
        sb.AppendLine($"  Velocity: {failedMovedObj.Velocity}");
        sb.AppendLine($"  IsStationary: {failedMovedObj.IsStationary}");
        sb.AppendLine($"  Leaf bounds: {failedLeaf?.Bounds}");
        sb.AppendLine($"  Leaf IsRetired: {failedLeaf?.IsRetired}");
        sb.AppendLine($"  In leaf.Occupants: {failedLeaf?.Occupants?.Contains(failedMovedObj)}");
        sb.AppendLine($"  Leaf tickables count: {failedLeaf?.m_tickableObjects?.Count}");
        return sb.ToString();
    }

    public static string PostTickReport(
        int movedCount,
        int unchangedCount,
        int missingFromLeafCount,
        int notInTickablesCount,
        int leafChangedCount,
        int movedButNotInTickables,
        int unchangedButNotInTickables,
        int objectCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Post-tick check complete:");
        sb.AppendLine($"  Objects that moved: {movedCount}/{objectCount}");
        sb.AppendLine($"  Objects unchanged: {unchangedCount}/{objectCount}");
        sb.AppendLine($"  Objects that crossed leaf boundaries: {leafChangedCount}");
        sb.AppendLine($"  Objects missing from leaf: {missingFromLeafCount}");
        sb.AppendLine($"  Objects NOT in m_tickable_objects: {notInTickablesCount}");
        sb.AppendLine($"    - Moved but not in tickables: {movedButNotInTickables}");
        sb.AppendLine($"    - Unmoved and not in tickables: {unchangedButNotInTickables}");
        return sb.ToString();
    }

    public static string BuildPostInsertSummary(
        int objectInOccupantsCount,
        int proxyInOccupantsCount,
        int tickableNotRegisteredCount,
        IReadOnlyList<string> postInsertIssues,
        int objectCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\n=== Exhaustive Post-Insert Validation ===");
        sb.AppendLine($"  Original objects in Occupants: {objectInOccupantsCount}/{objectCount}");
        sb.AppendLine($"  Proxies still in Occupants: {proxyInOccupantsCount}");
        sb.AppendLine($"  Objects NOT in m_tickable_objects: {tickableNotRegisteredCount}");
        if (postInsertIssues != null && postInsertIssues.Count > 0)
        {
            sb.AppendLine($"\n⚠ POST-INSERT ISSUES ({postInsertIssues.Count} total, showing first 20):");
            foreach (var issue in postInsertIssues.Take(20))
                sb.AppendLine($"  {issue}");
        }
        else
        {
            sb.AppendLine("✓ Post-insert validation PASSED - all proxies committed, all objects in m_tickable_objects");
        }
        return sb.ToString();
    }

    public static string BuildPreFlightSummary(int missingLeafCount, int unregisteredCount, int wrongPositionCount, IReadOnlyList<string> preFlightIssues)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\nRunning pre-flight validation...");
        sb.AppendLine($"Pre-flight check complete:");
        sb.AppendLine($"  Objects missing leaf: {missingLeafCount}");
        sb.AppendLine($"  Objects not registered in leaf: {unregisteredCount}");
        sb.AppendLine($"  Objects with wrong position: {wrongPositionCount}");
        if (preFlightIssues != null && preFlightIssues.Count > 0)
        {
            sb.AppendLine($"\n⚠ PRE-FLIGHT FAILURES ({preFlightIssues.Count} issues, showing first 20):");
            foreach (var issue in preFlightIssues.Take(20))
                sb.AppendLine($"  {issue}");
        }
        else
        {
            sb.AppendLine("✓ Pre-flight validation PASSED - all objects properly registered");
        }
        return sb.ToString();
    }

    public static string BuildPostTickSummary(
        int movedCount, int unchangedCount, int leafChangedCount,
        int missingFromLeafCount, int notInTickableCount,
        int movedButNotInTickables, int unchangedButNotInTickables,
        IReadOnlyList<string> postTickIssues,
        int objectCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\n=== Post-Tick Validation ===");
        sb.AppendLine($"  Objects that moved: {movedCount}/{objectCount}");
        sb.AppendLine($"  Objects unchanged: {unchangedCount}/{objectCount}");
        sb.AppendLine($"  Objects that crossed leaf boundaries: {leafChangedCount}");
        sb.AppendLine($"  Objects missing from leaf: {missingFromLeafCount}");
        sb.AppendLine($"  Objects NOT in m_tickable_objects: {notInTickableCount}");
        sb.AppendLine($"    - Moved but not in tickables: {movedButNotInTickables}");
        sb.AppendLine($"    - Unmoved and not in tickables: {unchangedButNotInTickables}");
        if (postTickIssues != null && postTickIssues.Count > 0)
        {
            sb.AppendLine($"\n⚠ POST-TICK ISSUES ({postTickIssues.Count} total, showing first 20):");
            foreach (var issue in postTickIssues.Take(20))
                sb.AppendLine($"  {issue}");
        }
        else
        {
            sb.AppendLine("✓ Post-tick validation PASSED");
        }
        return sb.ToString();
    }
}