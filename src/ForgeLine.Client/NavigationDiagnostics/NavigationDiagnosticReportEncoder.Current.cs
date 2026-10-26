using System.Text.Json;
using ForgeLine.Game;

namespace ForgeLine.Client;

internal static partial class NavigationDiagnosticReportEncoder
{
    private static readonly JsonSerializerOptions CurrentOptions = new() { IncludeFields = true };
    private static void WriteCurrent<T>(Utf8JsonWriter writer, string name, T value, NavigationDiagnosticSnapshot source)
    {
        writer.WriteStartObject(name);
        writer.WriteString("availability", value is null ? "not_available" : "available");
        writer.WriteNumber("sourceTick", source.Tick.Value);
        writer.WritePropertyName("navigationRevision");
        JsonSerializer.Serialize(writer, source.Revision, CurrentOptions);
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value, CurrentOptions);
        writer.WriteEndObject();
    }

    private static void WriteCurrentVehicle(Utf8JsonWriter writer, NavigationDiagnosticVehicle vehicle,
        NavigationDiagnosticSnapshot source)
    {
        var f = vehicle.Facts;
        WriteCurrent(writer, "originalTarget", f.OriginalOrder?.WorldTarget, source);
        WriteCurrent(writer, "route", new { vehicle.CompleteRoute, Points = vehicle.Route }, source);
        WriteCurrent(writer, "movementStatus", f.Movement.ToString(), source);
        WriteCurrent(writer, "approachPhase", f.Dock?.Status.ToString(), source);
        WriteCurrent(writer, "lastProgressTick", f.LastProgressTick?.Value, source);
        WriteCurrent(writer, "blockReason", f.BlockReason?.ToString(), source);
        WriteCurrent(writer, "visibleBlocker", f.VisibleBlocker, source);
        WriteCurrent(writer, "searchState", f.SearchStatus?.ToString(), source);
        WriteCurrent(writer, "ownsDock", f.OwnsDock, source);
        WriteCurrent(writer, "yieldState", f.TrafficAction?.ToString(), source);
        if (vehicle.CompleteReservations) WriteCurrent(writer, "reservationState", vehicle.ReservedPassages, source);
        else WriteUnavailable(writer, "reservationState", "current_reservation_scope_or_tick_unavailable");
        WriteCurrent(writer, "waitAgeTicks", f.WaitAgeTicks, source);
        WriteCurrent(writer, "replanCount", f.Replans, source);
        WriteCurrent(writer, "replanReason", f.LastReplan, source);
        WriteCurrent(writer, "status", ForgeLine.Presentation.NavigationDiagnosticsViewModel.Status(f), source);
        WriteCurrent(writer, "fuelRestriction", new { f.CanMove, f.EffectiveMaximumSpeed, f.FuelQuantity, f.FuelCapacity }, source);
        WriteCurrent(writer, "boundCurrentObservation", f, source);
        WriteCurrent(writer, "orientedVehicleHull", new { f.Pose, f.Profile.HalfExtents }, source);
        WriteCurrent(writer, "actualCheckedMotion", f.ActualMotion, source);
    }
}
