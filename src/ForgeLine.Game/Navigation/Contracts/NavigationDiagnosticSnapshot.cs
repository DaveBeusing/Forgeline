using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct NavigationDiagnosticStamp(
    NavigationModuleAvailability Availability, SimulationTick SourceTick, NavigationSnapshotRevision Revision);

public readonly record struct NavigationCheckedHorizon(NavigationOrderBinding Binding, NavigationDiagnosticStamp Source,
    NavigationPose Start, NavigationPose CheckedEnd, NavigationVehicleProfile Profile, float DurationSeconds, float AllowedFraction);

public readonly record struct NavigationDiagnosticReservation(NavigationDiagnosticStamp Source, EntityId Owner,
    ulong CommandGeneration, SimulationTick GrantedAt, SimulationTick LastDecisionTick, bool Entered,
    bool ActiveCommand, NavigationTrafficPassage Passage, NavigationVersion? GrantedGeometryRevision);

public readonly record struct NavigationDiagnosticConflict(NavigationDiagnosticStamp Source, EntityId Blocker,
    NavigationPose Pose, Vector3 HalfExtents, NavigationObstacleKind Kind);

public readonly record struct NavigationDiagnosticReplan(NavigationDiagnosticStamp Source, EntityId Vehicle,
    ulong PreviousRequestId, ulong RequestId, MovementOrder OriginalOrder, NavigationBlockReason Reason, ulong ActorReplans);

public readonly record struct NavigationDiagnosticVehicleFacts(
    PlayerId Owner, EntityId Entity, UnitId Unit, NavigationDiagnosticStamp Observation,
    NavigationPose Pose, NavigationVehicleProfile Profile, GroundMovementStatus Movement,
    MovementOrder? OriginalOrder, ulong? RequestId, ulong? CommandGeneration,
    NavigationDiagnosticStamp Traffic, NavigationTrafficAction? TrafficAction, NavigationBlockReason? BlockReason,
    SimulationTick? LastProgressTick, ulong? WaitAgeTicks, ulong? Replans, Vector3? RecoveryTarget,
    EntityId? VisibleBlocker, NavigationSearchStatus? SearchStatus,
    VehicleQueueEntry? Queue, VehicleDockVisit? Dock, bool? OwnsDock,
    bool? CanMove, float? EffectiveMaximumSpeed, double? FuelQuantity, double? FuelCapacity,
    NavigationMotionProposal? ActualMotion, NavigationDiagnosticReplan? LastReplan, NavigationCheckedHorizon? CheckedHorizon, NavigationDiagnosticConflict? Conflict);

/// <summary>Copied own-vehicle observations. No collision frame or mutable route crosses this boundary.</summary>
public sealed class NavigationDiagnosticVehicle
{
    internal NavigationDiagnosticVehicle(NavigationDiagnosticVehicleFacts facts,
        ReadOnlySpan<Vector3> route, bool completeRoute, ReadOnlySpan<NavigationDiagnosticReservation> passages, bool completeReservations)
    {
        if (!facts.Owner.IsSpecified || !facts.Entity.IsValid || route.Length > 128 || passages.Length > 32)
            throw new ArgumentException("Invalid bounded navigation observation.");
        Facts = facts;
        Route = Array.AsReadOnly(route.ToArray());
        CompleteRoute = completeRoute;
        CompleteReservations = completeReservations;
        ReservedPassages = Array.AsReadOnly(passages.ToArray());
    }
    public NavigationDiagnosticVehicleFacts Facts { get; }
    public IReadOnlyList<Vector3> Route { get; }
    public bool CompleteRoute { get; }
    public bool CompleteReservations { get; }
    public IReadOnlyList<NavigationDiagnosticReservation> ReservedPassages { get; }
}

public sealed class NavigationDiagnosticSnapshot
{
    internal NavigationDiagnosticSnapshot(SimulationSessionId session, PlayerId viewer, SimulationTick tick,
        NavigationSnapshotRevision revision, NavigationModuleConnections modules, NavigationIntegrationLimits limits,
        ReadOnlySpan<NavigationDiagnosticVehicle> vehicles, NavigationDeliveryWindow? deliveries)
    {
        if (!session.IsSpecified || !viewer.IsSpecified || vehicles.Length > 128)
            throw new ArgumentException("Invalid diagnostic session or bound.");
        limits.Validate();
        var identities = new HashSet<EntityId>();
        foreach (var vehicle in vehicles)
            if (vehicle.Facts.Owner != viewer || vehicle.Facts.Observation.SourceTick != tick ||
                vehicle.Facts.Observation.Revision != revision || !identities.Add(vehicle.Facts.Entity))
                throw new ArgumentException("Diagnostics require distinct current owned observations.");
        Session = session; Viewer = viewer; Tick = tick; Revision = revision; Modules = modules; Limits = limits;
        Vehicles = Array.AsReadOnly(vehicles.ToArray());
        CompletedDeliveries = deliveries;
    }
    public NavigationDeliveryWindow? CompletedDeliveries { get; }
    public SimulationSessionId Session { get; }
    public PlayerId Viewer { get; }
    public SimulationTick Tick { get; }
    public NavigationSnapshotRevision Revision { get; }
    public NavigationModuleConnections Modules { get; }
    public NavigationIntegrationLimits Limits { get; }
    public IReadOnlyList<NavigationDiagnosticVehicle> Vehicles { get; }
}
