using System.Globalization;
using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

[Flags]
public enum NavigationDiagnosticLayers : byte
{
    None = 0, Hull = 1, Route = 2, ActualMotion = 4, Conflict = 8, Passages = 16, Lookahead = 32, All = 63
}

public readonly record struct NavigationDiagnosticsView(bool Visible, bool Layers, SimulationSessionId Session,
    EntityId Selected, string Feedback = "", NavigationDiagnosticLayers EnabledLayers = NavigationDiagnosticLayers.All,
    int FocusedLayer = 0, int Scroll = 0);

public static class NavigationDiagnosticsViewModel
{
    public static bool TryGetVehicle(PresentationSnapshot? snapshot, in NavigationDiagnosticsView view,
        out NavigationDiagnosticVehicle vehicle)
    {
        vehicle = null!;
        if (!view.Visible || !view.Session.IsSpecified || !view.Selected.IsValid || snapshot is null ||
            snapshot.SessionId != view.Session || snapshot.NavigationDiagnosis is not { } source ||
            source.Session != view.Session || source.Tick != snapshot.Tick ||
            snapshot.PlayerExperience is not { } experience || experience.Tick != snapshot.Tick ||
            experience.Player != source.Viewer || experience.Selection.Count != 1 ||
            experience.Selection.PrimaryEntity != view.Selected || !experience.Selection.HasHealth ||
            !double.IsFinite(experience.Selection.HealthFraction) || experience.Selection.HealthFraction <= 0) return false;
        foreach (var candidate in source.Vehicles)
        {
            var facts = candidate.Facts;
            if (facts.Entity != view.Selected || facts.Owner != source.Viewer ||
                facts.Unit != experience.Selection.CommonUnitId || facts.Observation.SourceTick != snapshot.Tick ||
                facts.Observation.Revision != source.Revision || facts.Observation.Availability != NavigationModuleAvailability.Available ||
                !facts.Pose.IsValid || !facts.Profile.IsValid) continue;
            foreach (ref readonly var instance in snapshot.Instances)
                if (instance.Entity == facts.Entity && instance.Selectable.IsSelectable &&
                    instance.Selectable.Owner == source.Viewer && !instance.UnitFeature.IsWreck &&
                    instance.UnitFeature.Unit == facts.Unit && (instance.Visibility & RenderVisibilityMask.World) != 0)
                { vehicle = candidate; return true; }
        }
        return false;
    }

    public static IReadOnlyList<string> Lines(PresentationSnapshot? snapshot, in NavigationDiagnosticsView view)
    {
        if (!view.Visible) return Array.Empty<string>();
        var lines = new List<string> { "NAVIGATION DIAGNOSTICS", "Shift+F10 close | F10/Ctrl+F10 scroll | Shift+F12 export" };
        if (!TryGetVehicle(snapshot, view, out var vehicle))
        {
            lines.Add("Current observation unavailable. Select one visible owned Cargo / Supply Truck.");
            return lines;
        }
        var f = vehicle.Facts;
        var source = snapshot!.NavigationDiagnosis!;
        string Value<T>(T? value) where T : struct => value switch
        {
            float number => number.ToString("0.##", CultureInfo.InvariantCulture),
            double number => number.ToString("0.##", CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? "unavailable"
        };
        lines.Add($"Session {source.Session.Value} | tick {source.Tick.Value} | geometry {source.Revision.Geometry.Value}");
        lines.Add($"Vehicle {f.Entity} | {(f.Unit == UnitIds.CargoTruck ? "Cargo Truck" : "Supply Truck")} | {Status(f)}");
        lines.Add("F11 select layer | Shift+F11 toggle | Ctrl+F11 all off");
        lines.Add($"Layers {view.EnabledLayers} | selected {(NavigationDiagnosticLayers)(1 << view.FocusedLayer)} | Ctrl+F11 all off");
        lines.Add($"Traffic {f.Traffic.Availability}: {Value(f.TrafficAction)} / {Value(f.BlockReason)}");
        lines.Add($"Root request {Value(f.RequestId)} | generation {Value(f.CommandGeneration)}");
        lines.Add(f.OriginalOrder is { } root ? $"Original target {Format(root.WorldTarget)}" : "Original target unavailable (no active order)");
        lines.Add($"Route {vehicle.Route.Count} points{(vehicle.CompleteRoute ? "" : " (partial / unavailable)")} | search {Value(f.SearchStatus)}");
        lines.Add($"Progress tick {Value(f.LastProgressTick)} | wait {Value(f.WaitAgeTicks)} ticks | replans {Value(f.Replans)}");
        lines.Add(f.LastReplan is { } replan ? $"Last replan {replan.Reason} at {replan.Source.SourceTick.Value} | request {replan.PreviousRequestId} -> {replan.RequestId}" : "Last replan observation unavailable");
        lines.Add(f.Queue is { } q ? $"Queue {q.Building} gen {q.Generation} | position {q.Position}/{q.WaitingCount} | {q.WaitReason}" : "Queue observation unavailable");
        lines.Add(f.Dock is { } d ? $"Dock {d.Status} / {d.Reason} | gen {d.Generation} | owns {Value(f.OwnsDock)}" : "Dock observation unavailable");
        lines.Add($"Fuel {Value(f.FuelQuantity)} / {Value(f.FuelCapacity)} | can move {Value(f.CanMove)} | speed limit {Value(f.EffectiveMaximumSpeed)}");
        lines.Add($"Blocker {Value(f.VisibleBlocker)} | owned passages {(vehicle.CompleteReservations ? vehicle.ReservedPassages.Count.ToString(CultureInfo.InvariantCulture) : "unavailable")}");
        lines.Add(f.ActualMotion is { } motion ? $"Actual step {motion.DesiredSpeed:F2} m/s | braking {motion.BrakingDistance:F2} m | cleared {motion.SafeRemainingDistance:F2} m" : "Actual motion observation unavailable");
        lines.Add($"Modules: traffic {source.Modules.Traffic}, motion {source.Modules.Motion}");
        lines.Add($"Footprint routing {source.Modules.Routing} | scheduler {source.Modules.Scheduling} | regions {source.Modules.RegionalUpdates}");
        lines.Add($"Limits: {source.Limits.WorkPerTick} work/tick | {source.Limits.WorkPerSearch} work/search | {source.Limits.MaximumNeighbors} neighbors");
        if (source.Fleet is { } fleet && fleet.Viewer == source.Viewer && fleet.Tick == source.Tick)
        {
            lines.Add($"Own searches {fleet.ActiveSearches} active / {fleet.WaitingSearches} waiting | oldest {fleet.MaximumWaitTicks} ticks");
            lines.Add($"Own search work {fleet.WorkConsumedThisTick} this tick | reserved {fleet.ReservedWorkspaceBytes} bytes");
            lines.Add($"Own cache hits {fleet.CachePositiveHits + fleet.CacheNegativeHits} / misses {fleet.CacheMisses} | writes {fleet.CacheWrites} | deferrals {fleet.CacheBudgetDeferrals}");
            lines.Add($"Own arenas rents {fleet.ArenaRents} / reuses {fleet.ArenaReuses} / returns {fleet.ArenaReturns}");
            lines.Add($"Fleet limits {fleet.WorkPerTick} work/tick | {fleet.MaximumActiveSearches} active / {fleet.MaximumEntries} retained");
        }
        lines.Add($"Visible owned fleet {source.Vehicles.Count} | timing / allocations not connected");
        lines.Add(source.CompletedDeliveries is { } deliveries ? $"Completed deliveries {deliveries.CompletedDeliveries} in ({deliveries.StartExclusive.Value}, {deliveries.EndInclusive.Value}] ticks" : "Completed-delivery observation window unavailable");
        if (!string.IsNullOrWhiteSpace(view.Feedback)) lines.Add(view.Feedback);
        return lines.AsReadOnly();
    }

    public static string Status(in NavigationDiagnosticVehicleFacts facts)
    {
        if (facts.Movement == GroundMovementStatus.OutOfFuel) return "OutOfFuel";
        if (facts.BlockReason == NavigationBlockReason.SearchWork) return "WaitingForBudget";
        if (facts.SearchStatus == NavigationSearchStatus.Pending)
            return facts.FleetState switch
            {
                NavigationFleetRequestState.WaitingForAdmission => "WaitingForAdmission",
                NavigationFleetRequestState.QueuedForBudget => "WaitingForBudget",
                NavigationFleetRequestState.Searching => "Searching",
                _ => facts.LastReplan.HasValue ? "Replanning" : "WaitingForSearch"
            };
        if (facts.Movement != GroundMovementStatus.Moving &&
            facts.BlockReason == NavigationBlockReason.MovingVehicle)
            return "WaitingForTraffic";
        if (facts.Movement != GroundMovementStatus.Moving && facts.BlockReason == NavigationBlockReason.StaticObstacle)
            return "BlockedByGeometry";
        if (facts.Movement != GroundMovementStatus.Moving && facts.BlockReason == NavigationBlockReason.ReservedCorridor)
            return "WaitingForReservation";
        if (facts.Movement != GroundMovementStatus.Moving &&
            (facts.Queue?.WaitReason is VehicleQueueWaitReason.Predecessor or VehicleQueueWaitReason.OccupiedExit ||
                facts.Dock?.Status is VehicleDockStatus.Busy or VehicleDockStatus.Blocked))
            return "WaitingForDock";
        return facts.Movement.ToString();
    }

    public static void AppendLayers(DebugDraw draw, PresentationSnapshot? snapshot, in NavigationDiagnosticsView view)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (!view.Layers || !TryGetVehicle(snapshot, view, out var vehicle)) return;
        draw.Enabled = true;
        var f = vehicle.Facts;
        Vector4 hull = new(.2f, 1, .8f, 1), route = new(.4f, .7f, 1, 1), lease = new(1, .7f, .2f, 1);
        if ((view.EnabledLayers & NavigationDiagnosticLayers.Hull) != 0) Box(draw, f.Pose, f.Profile.HalfExtents, hull);
        if ((view.EnabledLayers & NavigationDiagnosticLayers.Route) != 0 && vehicle.Route.Count > 0)
        {
            draw.Line(f.Pose.Position, vehicle.Route[0], route);
            for (int i = 1; i < vehicle.Route.Count; i++) draw.Line(vehicle.Route[i - 1], vehicle.Route[i], route);
        }
        if ((view.EnabledLayers & NavigationDiagnosticLayers.Passages) != 0 && vehicle.CompleteReservations)
            foreach (var reservation in vehicle.ReservedPassages) Box(draw, reservation.Passage.Pose, reservation.Passage.HalfExtents, lease);
        if ((view.EnabledLayers & NavigationDiagnosticLayers.Lookahead) != 0 && f.CheckedHorizon is { } horizon &&
            horizon.Source.SourceTick == snapshot!.Tick && horizon.Source.Revision == snapshot.NavigationDiagnosis!.Revision)
        {
            for (int i = 0; i < 8; i++)
            {
                float fraction = horizon.AllowedFraction * i / 7;
                var pose = new NavigationPose(Vector3.Lerp(horizon.Start.Position, horizon.CheckedEnd.Position, fraction),
                    Quaternion.Normalize(Quaternion.Slerp(horizon.Start.Rotation, horizon.CheckedEnd.Rotation, fraction)));
                Box(draw, pose, horizon.Profile.HalfExtents, new(.7f, .5f, 1, .8f));
            }
        }
        if ((view.EnabledLayers & NavigationDiagnosticLayers.ActualMotion) != 0 && f.ActualMotion is { } motion) draw.Line(motion.Start.Position, motion.End.Position, new(1, 1, 1, 1));
        if ((view.EnabledLayers & NavigationDiagnosticLayers.ActualMotion) != 0 && f.RecoveryTarget is { } escape) draw.Line(f.Pose.Position, escape, new(1, .2f, .8f, 1));
        if ((view.EnabledLayers & NavigationDiagnosticLayers.Conflict) != 0 && f.Conflict is { } conflict &&
            conflict.Source.SourceTick == snapshot!.Tick && conflict.Source.Revision == snapshot.NavigationDiagnosis!.Revision)
            Box(draw, conflict.Pose, conflict.HalfExtents, new(1, .15f, .15f, 1));
    }

    private static string Format(Vector3 point) => string.Create(CultureInfo.InvariantCulture, $"({point.X:F1}, {point.Y:F1}, {point.Z:F1})");
    private static void Box(DebugDraw draw, NavigationPose pose, Vector3 half, Vector4 color)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
            corners[i] = pose.Position + Vector3.Transform(new Vector3((i & 1) == 0 ? -half.X : half.X,
                (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z), pose.Rotation);
        for (int i = 0; i < 8; i++)
        for (int bit = 1; bit <= 4; bit *= 2)
            if ((i & bit) == 0) draw.Line(corners[i], corners[i | bit], color);
    }
}
