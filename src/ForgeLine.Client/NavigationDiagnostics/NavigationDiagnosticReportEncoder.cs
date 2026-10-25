using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

/// <summary>
/// Encodes a bounded partial report from an already copied presentation snapshot.
/// The caller must authorize developer access and supply the latest session, tick,
/// actual single selection and matching map/seed immediately before each request.
/// This helper cannot establish freshness or authorization for its caller.
/// </summary>
internal static class NavigationDiagnosticReportEncoder
{
    internal const int MaximumReportBytes = 64 * 1024;
    private const int MaximumMapIdCharacters = 128;
    private const int MaximumBuildFieldCharacters = 256;
    private const string MissingNavigationObservation = "navigation_observation_not_connected";

    internal static bool TryEncode(
        PresentationSnapshot snapshot,
        SimulationSessionId expectedSession,
        SimulationTick expectedTick,
        PlayerId viewer,
        EntityId selectedEntity,
        string mapId,
        ulong seed,
        out byte[] report)
    {
        report = [];
        if (snapshot is null || !expectedSession.IsSpecified || !viewer.IsSpecified ||
            !selectedEntity.IsValid || !ValidMapId(mapId) ||
            snapshot.SessionId != expectedSession || snapshot.Tick != expectedTick ||
            snapshot.PlayerExperience is not PlayerExperienceSnapshot experience ||
            experience.Player != viewer || experience.Tick != expectedTick ||
            snapshot.PlayerActions is not PlayerActionSnapshot actions ||
            actions.SessionId != expectedSession || actions.Tick != expectedTick)
        {
            return false;
        }

        PlayerSelectionSummary selection = experience.Selection;
        bool cargoTruck = selection.CommonUnitId == UnitIds.CargoTruck;
        if (selection.Count != 1 || selection.PrimaryEntity != selectedEntity ||
            selection.Kind != PlayerSelectionKind.Unit ||
            !cargoTruck && selection.CommonUnitId != UnitIds.SupplyTruck ||
            !selection.HasHealth || !double.IsFinite(selection.HealthFraction) || selection.HealthFraction <= 0 ||
            actions.Tactical is not PlayerTacticalActionReadModel tactical ||
            tactical.RequestedSelectionCount != 1 ||
            tactical.SelectedEntities.Count != 1 || tactical.SelectedEntities[0] != selectedEntity ||
            !MatchesRenderedTruck(snapshot, viewer, selectedEntity, selection.CommonUnitId) ||
            !MatchesActionSelection(actions, selectedEntity, cargoTruck))
        {
            return false;
        }

        // Tactical.SelectedEntities is the copied owned selection. Its rejected
        // count concerns combat eligibility and can legitimately include trucks.
        bool hasQueue = TryReadQueue(snapshot, viewer, selection, actions, cargoTruck, out PlayerVehicleQueueSummary queue);

        // No report buffer or vehicle strings are allocated before selection validation.
        // The non-expandable stream is a hard byte limit, including JSON escaping.
        try
        {
            using var stream = new MemoryStream(new byte[MaximumReportBytes], writable: true);
            stream.SetLength(0);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            writer.WriteNumber("reportSchemaVersion", 1);
            writer.WriteString("reportKind", "logistics_navigation_partial_observation");
            writer.WriteString("coverage", "owned_selection_and_queue_only");
            writer.WriteStartObject("build");
            WriteBoundedString(writer, "informationalVersion", ClientBuildIdentity.InformationalVersion);
            WriteBoundedString(writer, "sourceRevision", ClientBuildIdentity.SourceRevision);
            WriteBoundedString(writer, "buildId", ClientBuildIdentity.BuildId);
            WriteBoundedString(writer, "channel", ClientBuildIdentity.Channel);
            writer.WriteEndObject();
            writer.WriteString("mapId", mapId);
            writer.WriteNumber("seed", seed);
            writer.WriteNumber("sessionId", expectedSession.Value);
            writer.WriteNumber("sourceTick", expectedTick.Value);
            writer.WriteNumber("viewer", viewer.Value);
            WriteUnavailable(writer, "diagnosticContractRevision", MissingNavigationObservation);
            WriteUnavailable(writer, "activeModules", MissingNavigationObservation);
            WriteUnavailable(writer, "configuredLimits", MissingNavigationObservation);

            writer.WriteStartObject("selectedVehicle");
            writer.WriteString("availability", "available");
            writer.WriteNumber("sourceTick", expectedTick.Value);
            writer.WriteNull("navigationRevision");
            WriteEntity(writer, "entity", selectedEntity);
            writer.WriteNumber("unitId", selection.CommonUnitId.Value);
            writer.WriteString("vehicleKind", cargoTruck ? "CargoTruck" : "SupplyTruck");
            if (hasQueue)
            {
                WriteQueue(writer, queue, expectedTick);
            }
            else
            {
                WriteUnavailable(writer, "queue", "current_authorized_queue_observation_not_available");
            }

            WriteUnavailable(writer, "originalTarget", MissingNavigationObservation);
            WriteUnavailable(writer, "route", MissingNavigationObservation);
            WriteUnavailable(writer, "approachPhase", MissingNavigationObservation);
            WriteUnavailable(writer, "movementStatus", MissingNavigationObservation);
            WriteUnavailable(writer, "lastProgressTick", MissingNavigationObservation);
            WriteUnavailable(writer, "blockReason", MissingNavigationObservation);
            WriteUnavailable(writer, "visibleBlocker", MissingNavigationObservation);
            WriteUnavailable(writer, "searchState", MissingNavigationObservation);
            WriteUnavailable(writer, "ownsDock", "dock_ownership_observation_not_available");
            WriteUnavailable(writer, "yieldState", MissingNavigationObservation);
            WriteUnavailable(writer, "reservationState", MissingNavigationObservation);
            WriteUnavailable(writer, "waitAgeTicks", "queue_change_tick_is_not_wait_start");
            WriteUnavailable(writer, "replanReason", MissingNavigationObservation);
            WriteUnavailable(writer, "fuelRestriction", "fuel_presence_observation_not_available");
            writer.WriteEndObject();

            writer.WriteStartObject("fleet");
            WriteUnavailable(writer, "waitingSearches", MissingNavigationObservation);
            WriteUnavailable(writer, "activeSearches", MissingNavigationObservation);
            WriteUnavailable(writer, "searchWork", MissingNavigationObservation);
            WriteUnavailable(writer, "maximumWaitTicks", MissingNavigationObservation);
            WriteUnavailable(writer, "cacheHits", MissingNavigationObservation);
            WriteUnavailable(writer, "cacheBytes", MissingNavigationObservation);
            WriteUnavailable(writer, "activeWorkspaces", MissingNavigationObservation);
            WriteUnavailable(writer, "regionalUpdates", MissingNavigationObservation);
            WriteUnavailable(writer, "completedDeliveries", "authoritative_delivery_observation_not_available");
            WriteUnavailable(writer, "replans", MissingNavigationObservation);
            writer.WriteEndObject();

            writer.WriteStartObject("geometry");
            WriteUnavailable(writer, "drivingCorridor", MissingNavigationObservation);
            WriteUnavailable(writer, "orientedVehicleHull", MissingNavigationObservation);
            WriteUnavailable(writer, "grantedBrakingLookahead", MissingNavigationObservation);
            WriteUnavailable(writer, "observedConflict", MissingNavigationObservation);
            WriteUnavailable(writer, "reservedPassage", MissingNavigationObservation);
            WriteUnavailable(writer, "dirtyRegions", MissingNavigationObservation);
            writer.WriteEndObject();
            WriteUnavailable(writer, "gameplayTimingWindow", "optional_instrumentation_not_connected");
            WriteUnavailable(writer, "gameplayAllocationWindow", "optional_instrumentation_not_connected");
            writer.WriteEndObject();
            writer.Flush();
            report = stream.ToArray();
            return true;
        }
        catch (NotSupportedException)
        {
            // A report exceeding the fixed stream capacity is rejected, never truncated.
            return false;
        }
    }

    private static bool MatchesActionSelection(PlayerActionSnapshot actions, EntityId selectedEntity, bool cargoTruck)
    {
        if (actions.Logistics is PlayerLogisticsActionReadModel logistics && logistics.Entity != selectedEntity ||
            actions.Supply is PlayerSupplyActionReadModel supply && supply.Entity != selectedEntity)
        {
            return false;
        }

        return cargoTruck
            ? actions.Logistics?.Cargo is PlayerCargoStatusReadModel cargo && cargo.Entity == selectedEntity &&
              actions.Supply is not { IsSupplyTruck: true }
            : actions.Supply is PlayerSupplyActionReadModel truck && truck.Entity == selectedEntity && truck.IsSupplyTruck;
    }

    private static bool MatchesRenderedTruck(
        PresentationSnapshot snapshot, PlayerId viewer, EntityId selectedEntity, UnitId unit)
    {
        bool found = false;
        foreach (ref readonly RenderInstance instance in snapshot.Instances)
        {
            if (instance.Entity != selectedEntity)
            {
                continue;
            }

            if (found || !instance.Selectable.IsSelectable || instance.Selectable.Owner != viewer ||
                (instance.Selectable.Category & ControllableEntityCategory.Unit) == 0 ||
                (instance.Visibility & RenderVisibilityMask.World) == 0 ||
                instance.UnitFeature.Unit != unit || instance.UnitFeature.IsWreck)
            {
                return false;
            }

            found = true;
        }

        return found;
    }

    private static bool TryReadQueue(
        PresentationSnapshot snapshot, PlayerId viewer, in PlayerSelectionSummary selection,
        PlayerActionSnapshot actions, bool cargoTruck, out PlayerVehicleQueueSummary queue)
    {
        queue = default;
        PlayerVehicleQueueSummary? actionQueue = cargoTruck
            ? actions.Logistics?.Service.VehicleQueue
            : actions.Supply?.Service.VehicleQueue;
        if (selection.Work.VehicleQueue is not PlayerVehicleQueueSummary candidate ||
            actionQueue != candidate || !candidate.Building.IsValid ||
            candidate.Position < 0 || candidate.WaitingCount < 0 || candidate.ChangedAtTick > snapshot.Tick ||
            !Enum.IsDefined(candidate.Service) || !Enum.IsDefined(candidate.Status) ||
            !Enum.IsDefined(candidate.WaitReason) || !Enum.IsDefined(candidate.DockReason))
        {
            return false;
        }

        bool found = false;
        foreach (ref readonly RenderInstance instance in snapshot.Instances)
        {
            if (instance.Entity != candidate.Building)
            {
                continue;
            }

            if (found || !instance.Selectable.IsSelectable || instance.Selectable.Owner != viewer ||
                (instance.Selectable.Category & ControllableEntityCategory.Building) == 0 ||
                (instance.Visibility & RenderVisibilityMask.World) == 0)
            {
                return false;
            }

            found = true;
        }

        if (found)
        {
            queue = candidate;
        }

        return found;
    }

    private static void WriteQueue(Utf8JsonWriter writer, in PlayerVehicleQueueSummary queue, SimulationTick tick)
    {
        writer.WriteStartObject("queue");
        writer.WriteString("availability", "available");
        writer.WriteNumber("sourceTick", tick.Value);
        writer.WriteNull("navigationRevision");
        WriteEntity(writer, "building", queue.Building);
        // Holding uses generation zero; a newly registered entry can also have
        // position zero until queue placement. Neither denotes an assigned value.
        if (queue.Generation == 0)
        {
            writer.WriteNull("queueGeneration");
        }
        else
        {
            writer.WriteNumber("queueGeneration", queue.Generation);
        }

        if (queue.Position == 0)
        {
            writer.WriteNull("position");
        }
        else
        {
            writer.WriteNumber("position", queue.Position);
        }

        writer.WriteNumber("waitingCount", queue.WaitingCount);
        writer.WriteString("service", queue.Service.ToString());
        writer.WriteString("dockStatus", queue.Status.ToString());
        writer.WriteString("waitReason", queue.WaitReason.ToString());
        writer.WriteString("dockBlockReason", queue.DockReason.ToString());
        writer.WriteNumber("changedAtTick", queue.ChangedAtTick.Value);
        writer.WriteString("changedTickMeaning", "last_observed_queue_state_change_not_wait_start");
        writer.WriteEndObject();
    }

    private static void WriteEntity(Utf8JsonWriter writer, string name, EntityId entity)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("index", entity.Index);
        writer.WriteNumber("generation", entity.Generation);
        writer.WriteEndObject();
    }

    private static void WriteUnavailable(Utf8JsonWriter writer, string name, string reason)
    {
        writer.WriteStartObject(name);
        writer.WriteString("availability", "not_available");
        writer.WriteNull("value");
        writer.WriteNull("sourceTick");
        writer.WriteNull("revision");
        writer.WriteString("reason", reason);
        writer.WriteEndObject();
    }

    private static void WriteBoundedString(Utf8JsonWriter writer, string name, string value) =>
        writer.WriteString(name, value.AsSpan(0, Math.Min(value.Length, MaximumBuildFieldCharacters)));

    private static bool ValidMapId(string mapId)
    {
        if (string.IsNullOrEmpty(mapId) || mapId.Length > MaximumMapIdCharacters)
        {
            return false;
        }

        foreach (char character in mapId)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
