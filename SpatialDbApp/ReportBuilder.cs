using SpatialDbLib.Math;
using SpatialDbLib.Simulation;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
/////////////////////////////////
namespace SpatialDbApp.Reporting;

internal static class ReportBuilder
{
    public static string FinalReport(
        List<TickableSpatialObject> objects,
        TickableSpatialLattice lattice,
        ConcurrentDictionary<TickableSpatialObject, LongVector3>? initialPositions,
        Stopwatch stopwatch,
        int tickCount,
        int totalMovementDetected,
        int monitorChecks,
        int objectCount)
    {
        var finalStationaryCount = 0;
        var finalMissingCount = 0;
        var finalNoMovementCount = 0;
        var unmoved = new List<(TickableSpatialObject obj, string diagnosis)>();
        foreach (var obj in objects!)
        {
            if (obj.IsStationary) finalStationaryCount++;
            var leaf = lattice!.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;
            if (leaf == null) finalMissingCount++;
            var finalPos = obj.LocalPosition;
            var initialPos = initialPositions![obj];
            if (finalPos == initialPos)
            {
                finalNoMovementCount++;
                var diagnosis = new System.Text.StringBuilder();
                diagnosis.Append($"Pos: {initialPos}, Vel: {obj.Velocity}");
                var hasOccupyingLeaf = leaf != null;
                diagnosis.Append($", HasLeaf: {hasOccupyingLeaf}");
                var originalLeaf = lattice.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;
                var initialLeafStillExists = false;
                var movedToNewLeaf = false;
                if (leaf != null)
                {
                    initialLeafStillExists = leaf.Bounds.Contains(initialPos);
                    movedToNewLeaf = !leaf.Bounds.Contains(initialPos);
                }
                diagnosis.Append($", LeafContainsInitialPos: {initialLeafStillExists}");
                diagnosis.Append($", MovedLeaf: {movedToNewLeaf}");
                var isRegisteredInLeaf = false;
                var isLeafRetired = false;
                if (leaf != null)
                {
                    var tickableList2 = leaf.m_tickableObjects;
                    isRegisteredInLeaf = tickableList2?.Contains(obj) ?? false;
                    isLeafRetired = leaf.IsRetired;
                }
                diagnosis.Append($", RegInLeaf: {isRegisteredInLeaf}");
                diagnosis.Append($", LeafRetired: {isLeafRetired}");
                diagnosis.Append($", Stationary: {obj.IsStationary}");
                unmoved.Add((obj, diagnosis.ToString()));
            }
        }
        var sb = new StringBuilder();
        sb.AppendLine($"\n=== Simulation Results ===");
        sb.AppendLine($"Duration: {stopwatch.ElapsedMilliseconds}ms");
        sb.AppendLine($"Total ticks: {tickCount}");
        sb.AppendLine($"Ticks/sec: {tickCount * 1000.0 / stopwatch.ElapsedMilliseconds:N0}");
        sb.AppendLine($"Avg tick duration: {(tickCount > 0 ? stopwatch.ElapsedMilliseconds / (double)tickCount : 0):N2}ms");
        sb.AppendLine($"Monitor checks: {monitorChecks}");
        sb.AppendLine($"Total movement events detected: {totalMovementDetected}");
        sb.AppendLine($"Avg movements per check: {(monitorChecks > 0 ? totalMovementDetected / (double)monitorChecks : 0):N1}");
        sb.AppendLine($"\n=== Validation ===");
        sb.AppendLine($"Objects that became stationary: {finalStationaryCount}");
        sb.AppendLine($"Objects missing from lattice: {finalMissingCount}");
        sb.AppendLine($"Objects that never moved from initial position: {finalNoMovementCount}");

        if (finalNoMovementCount > 0)
        {
            sb.AppendLine($"\n=== Unmoved Object Diagnostics ({finalNoMovementCount} total) ===");
            foreach (var (obj, diagnosis) in unmoved.Take(10))
                sb.AppendLine($"  {diagnosis}");
            var unmovedWithLeaf = unmoved.Count(x => x.diagnosis.Contains("HasLeaf: True"));
            var unmovedRegistered = unmoved.Count(x => x.diagnosis.Contains("RegInLeaf: True"));
            sb.AppendLine($"\nUnmoved objects WITH occupying leaf: {unmovedWithLeaf}/{finalNoMovementCount}");
            sb.AppendLine($"Unmoved objects REGISTERED in leaf: {unmovedRegistered}/{finalNoMovementCount}");
            var unmovedPercentage = finalNoMovementCount / (double)objectCount * 100;
            sb.AppendLine($"Unmoved percentage: {unmovedPercentage:N3}%");
            var expectedTicksPerObject = tickCount / (double)objectCount;
            sb.AppendLine($"Expected ticks per object: {expectedTicksPerObject:N2}");
            if (expectedTicksPerObject < 0.1)
                sb.AppendLine("WARNING: Tick rate too low to guarantee all objects move");
        }

        if (finalStationaryCount != 0)
            throw new InvalidOperationException("No objects should become stationary during simulation");
        if (finalMissingCount != 0)
            throw new InvalidOperationException("All objects should remain in the lattice throughout simulation");
        var minExpectedTicksPerObject = 0.5;
        if (tickCount / (double)objectCount >= minExpectedTicksPerObject)
        {
            if (finalNoMovementCount != 0)
                throw new InvalidOperationException($"All objects should have moved at least once during simulation (had {tickCount} ticks for {objectCount} objects)");
        }
        else
            sb.AppendLine($"⚠ Skipping movement assertion: Only {tickCount / (double)objectCount:N2} ticks/object (< {minExpectedTicksPerObject})");

        if (tickCount <= 0)
            throw new InvalidOperationException("Ticker should have completed at least one tick");
        if (totalMovementDetected <= 0)
            throw new InvalidOperationException("Monitor should have detected movement");
        sb.AppendLine("\n✓ Grand simulation test PASSED");
        return sb.ToString();
    }
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