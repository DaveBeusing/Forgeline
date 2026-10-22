using System.Globalization;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Subjects describe separate observations; their quantities must never be summed across rows.</summary>
public enum OperationsDiagnosisSubject : byte
{
    Production,
    UnitProduction,
    StockPolicy,
    TransportRequest,
    CargoTransport
}

/// <summary>Ordered by diagnostic priority. Unknown is not evidence of a particular failure.</summary>
public enum OperationsDiagnosisReason : byte
{
    None = 0,
    NoPower = 1,
    NoInput = 2,
    NoSourceStock = 3,
    NoRoute = 4,
    NoTruck = 5,
    Capacity = 6,
    WaitingForBay = 7,
    ProductionPaused = 8,
    OutputFull = 9,
    NoDriveFuel = 10,
    DestinationFull = 11,
    Unknown = byte.MaxValue
}

/// <summary>Values are copied observations of one owned selection at one completed tick.</summary>
public readonly record struct OperationsDiagnosisRow(
    OperationsDiagnosisSubject Subject,
    EntityId Entity,
    ResourceId Resource,
    LogisticsTransportRequestId RequestId,
    string Status,
    double? Demand,
    double? LocalQuantity,
    double? FreeQuantity,
    double? ReservedQuantity,
    double? OutstandingRequestQuantity,
    double? ConfirmedIncomingQuantity,
    double? LoadedCargoQuantity,
    double? SourceFreeQuantity,
    EntityId Source,
    EntityId Destination,
    EntityId AssignedTruck,
    int? DockQueuePosition,
    int? DockWaitingCount,
    OperationsDiagnosisReason PrimaryReason,
    OperationsDiagnosisReason SecondaryReason = OperationsDiagnosisReason.None,
    OperationsDiagnosisReason TertiaryReason = OperationsDiagnosisReason.None);

/// <summary>Only a reported transport request establishes a directed, owned supply-chain link.</summary>
public readonly record struct OperationsDiagnosisLink(
    LogisticsTransportRequestId RequestId, EntityId Source, EntityId Destination, ResourceId Resource);

/// <summary>
/// Copied, read-only, UI-independent diagnostic projection. Never owns economic state,
/// initiates dispatch or resolves entities outside the already authorized player snapshot.
/// </summary>
public sealed class OperationsDiagnosisSnapshot
{
    public const int MaximumRows = 128;
    public const int MaximumLinks = 64;
    public const int MaximumTraceDepth = 8;

    public OperationsDiagnosisSnapshot(SimulationSessionId session, SimulationTick tick, PlayerId player,
        IReadOnlyList<OperationsDiagnosisRow> rows, IReadOnlyList<OperationsDiagnosisLink>? links = null,
        int totalRows = -1)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Session = session;
        Tick = tick;
        Player = player;
        Rows = Array.AsReadOnly(rows.Take(MaximumRows).ToArray());
        Links = Array.AsReadOnly((links ?? []).Take(MaximumLinks).ToArray());
        TotalRows = totalRows < 0 ? rows.Count : totalRows;
    }

    public SimulationSessionId Session { get; }
    public SimulationTick Tick { get; }
    public PlayerId Player { get; }
    public IReadOnlyList<OperationsDiagnosisRow> Rows { get; }
    public IReadOnlyList<OperationsDiagnosisLink> Links { get; }
    public int TotalRows { get; }

    public static OperationsDiagnosisSnapshot? Resolve(PresentationSnapshot? snapshot) =>
        snapshot is { PlayerExperience: { } experience, PlayerActions: { } actions, Diagnosis: { } diagnosis } &&
        snapshot.SessionId.IsSpecified && snapshot.SessionId == diagnosis.Session &&
        snapshot.Tick == diagnosis.Tick && actions.SessionId == diagnosis.Session &&
        actions.Tick == diagnosis.Tick && experience.Tick == diagnosis.Tick &&
        experience.Player == diagnosis.Player && !experience.IsMatchComplete ? diagnosis : null;

    /// <summary>Bounded backward walk. Unknown or missing edges never turn into guessed links.</summary>
    public IReadOnlyList<OperationsDiagnosisLink> TraceUpstream(EntityId destination)
    {
        if (!destination.IsValid) return Array.Empty<OperationsDiagnosisLink>();
        var visited = new HashSet<EntityId> { destination };
        var chain = new List<OperationsDiagnosisLink>(MaximumTraceDepth);
        for (int depth = 0; depth < MaximumTraceDepth; depth++)
        {
            OperationsDiagnosisLink? next = null;
            foreach (var link in Links)
                if (link.Destination == destination && link.Source.IsValid && link.RequestId.IsSpecified)
                {
                    next = link;
                    break;
                }
            if (!next.HasValue || !visited.Add(next.Value.Source)) break;
            chain.Add(next.Value);
            destination = next.Value.Source;
        }
        return Array.AsReadOnly(chain.ToArray());
    }

    /// <summary>Text-only developer diagnostics; no renderer or world-state access is required.</summary>
    public IReadOnlyList<string> ToDiagnosticLines()
    {
        var lines = new List<string>(Rows.Count + 1)
        {
            "OPERATIONS SESSION " + Session.Value.ToString(CultureInfo.InvariantCulture) +
            " TICK " + Tick.Value.ToString(CultureInfo.InvariantCulture) +
            " ROWS " + Rows.Count.ToString(CultureInfo.InvariantCulture) +
            "/" + TotalRows.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var row in Rows)
            lines.Add(row.Subject + " " + row.Entity + " " + row.Resource +
                " STATUS " + row.Status + " BLOCK " + Label(row.PrimaryReason) +
                " DEMAND " + Number(row.Demand) + " LOCAL " + Number(row.LocalQuantity) +
                " FREE " + Number(row.FreeQuantity) + " RESERVED " + Number(row.ReservedQuantity) +
                " REQUESTED " + Number(row.OutstandingRequestQuantity) +
                " CONFIRMED " + Number(row.ConfirmedIncomingQuantity) +
                " LOADED " + Number(row.LoadedCargoQuantity));
        return Array.AsReadOnly(lines.ToArray());
    }

    private static string Number(double? value) => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "NOT OBSERVED";

    public static string Label(OperationsDiagnosisReason reason) => reason switch
    {
        OperationsDiagnosisReason.None => "NO REPORTED BLOCK",
        OperationsDiagnosisReason.NoPower => "NO POWER",
        OperationsDiagnosisReason.NoInput => "NO INPUT",
        OperationsDiagnosisReason.NoSourceStock => "NO SOURCE STOCK",
        OperationsDiagnosisReason.NoRoute => "NO ROUTE",
        OperationsDiagnosisReason.NoTruck => "NO TRUCK",
        OperationsDiagnosisReason.Capacity => "CAPACITY",
        OperationsDiagnosisReason.WaitingForBay => "WAITING FOR BAY",
        OperationsDiagnosisReason.ProductionPaused => "PRODUCTION PAUSED",
        OperationsDiagnosisReason.OutputFull => "OUTPUT FULL",
        OperationsDiagnosisReason.NoDriveFuel => "NO DRIVE FUEL",
        OperationsDiagnosisReason.DestinationFull => "DESTINATION FULL",
        _ => "UNKNOWN/NOT OBSERVED"
    };

    internal static OperationsDiagnosisSnapshot? Capture(PresentationSnapshot snapshot)
    {
        if (!snapshot.SessionId.IsSpecified || snapshot.PlayerActions is not { } actions ||
            snapshot.PlayerExperience is not { } experience ||
            experience.IsMatchComplete || experience.Player == PlayerId.None ||
            experience.Tick != snapshot.Tick || actions.Tick != snapshot.Tick ||
            actions.SessionId != snapshot.SessionId) return null;

        var logistics = actions.Logistics;
        var production = actions.Production;
        var units = actions.UnitProduction;
        if (logistics is null && production is null && units is null) return null;

        var rows = new List<OperationsDiagnosisRow>();
        var links = new List<OperationsDiagnosisLink>();

        if (production is not null)
        {
            var reasons = new ReasonAccumulator();
            reasons.Add(production.BlockReason switch
            {
                ProductionBlockReason.NoPower => OperationsDiagnosisReason.NoPower,
                ProductionBlockReason.NoInput => OperationsDiagnosisReason.NoInput,
                ProductionBlockReason.OutputFull => OperationsDiagnosisReason.OutputFull,
                ProductionBlockReason.Paused => OperationsDiagnosisReason.ProductionPaused,
                ProductionBlockReason.None or ProductionBlockReason.DesiredStockReached => OperationsDiagnosisReason.None,
                _ => OperationsDiagnosisReason.Unknown
            });
            rows.Add(new(OperationsDiagnosisSubject.Production, production.Entity, default, default,
                production.Status.ToString(), null, null, null, null, null, null, null, null,
                default, default, default, null, null, reasons.First, reasons.Second, reasons.Third));
        }
        if (units is not null)
        {
            var reasons = new ReasonAccumulator();
            reasons.Add(units.BlockReason switch
            {
                UnitProductionBlockReason.NoPower => OperationsDiagnosisReason.NoPower,
                UnitProductionBlockReason.NoInput => OperationsDiagnosisReason.NoInput,
                UnitProductionBlockReason.Paused => OperationsDiagnosisReason.ProductionPaused,
                UnitProductionBlockReason.None => OperationsDiagnosisReason.None,
                _ => OperationsDiagnosisReason.Unknown
            });
            rows.Add(new(OperationsDiagnosisSubject.UnitProduction, units.Entity, default, default,
                units.Status.ToString(), null, null, null, null, null, null, null, null,
                default, default, default, null, null, reasons.First, reasons.Second, reasons.Third));
        }
        if (logistics is not null)
        {
            foreach (var policy in logistics.Policies)
            {
                if (!policy.HasPolicy) continue; // An unconfigured policy is not real demand.
                var reasons = new ReasonAccumulator();
                reasons.Add(FromFailure(policy.FailureReason));
                reasons.Add(FromBottleneck(policy.BottleneckReason));
                if (policy.AssignedTruck.IsValid)
                    reasons.Add(FromService(policy.Service));
                rows.Add(new(OperationsDiagnosisSubject.StockPolicy, logistics.Entity, policy.ResourceId, default,
                    policy.Enabled ? policy.DistributionState.ToString() : "DISABLED",
                    policy.Enabled ? Math.Max(0, policy.DesiredTarget - policy.CurrentQuantity) : null,
                    policy.CurrentQuantity, policy.AvailableQuantity, policy.ReservedQuantity,
                    policy.RequestedQuantity, policy.AnnouncedQuantity, null, null,
                    policy.Source, logistics.Entity, policy.AssignedTruck,
                    policy.Service.VehicleQueue?.Position, policy.Service.VehicleQueue?.WaitingCount,
                    reasons.First, reasons.Second, reasons.Third));
            }
            foreach (var order in logistics.Orders)
            {
                var reasons = new ReasonAccumulator();
                reasons.Add(FromFailure(order.FailureReason));
                reasons.Add(FromBottleneck(order.BottleneckReason));
                if (reasons.First == OperationsDiagnosisReason.None &&
                    order.State is LogisticsTransportRequestState.Failed or LogisticsTransportRequestState.RetryPending)
                    reasons.Add(OperationsDiagnosisReason.Unknown);
                rows.Add(new(OperationsDiagnosisSubject.TransportRequest, logistics.Entity, order.ResourceId, order.RequestId,
                    order.State.ToString(), null, null, null, null,
                    order.RequestedQuantity, null, order.LoadedQuantity, null,
                    order.Origin, order.Destination, order.AssignedTruck, null, null,
                    reasons.First, reasons.Second, reasons.Third));
                // Both ends were checked for ownership and generation by the existing player extractor.
                if (links.Count < MaximumLinks && order.RequestId.IsSpecified && order.Origin.IsValid &&
                    order.Destination.IsValid && order.Origin != order.Destination)
                    links.Add(new(order.RequestId, order.Origin, order.Destination, order.ResourceId));
            }
            if (logistics.Cargo is { } cargo)
            {
                var reasons = new ReasonAccumulator();
                reasons.Add(cargo.WaitReason switch
                {
                    CargoTransportWaitReason.OriginResourceUnavailable => OperationsDiagnosisReason.NoSourceStock,
                    CargoTransportWaitReason.RouteUnavailable or CargoTransportWaitReason.RouteInvalidated =>
                        OperationsDiagnosisReason.NoRoute,
                    CargoTransportWaitReason.DestinationCapacity => OperationsDiagnosisReason.DestinationFull,
                    CargoTransportWaitReason.None => OperationsDiagnosisReason.None,
                    _ => OperationsDiagnosisReason.Unknown
                });
                reasons.Add(FromService(logistics.Service));
                rows.Add(new(OperationsDiagnosisSubject.CargoTransport, cargo.Entity, cargo.Resource, default,
                    cargo.Lifecycle.ToString(), null, null, null, null, cargo.RequestedQuantity,
                    null, cargo.CargoQuantity, null, cargo.Source, cargo.Destination, cargo.Entity,
                    logistics.Service.VehicleQueue?.Position, logistics.Service.VehicleQueue?.WaitingCount,
                    reasons.First, reasons.Second, reasons.Third));
            }
        }

        return rows.Count == 0 ? null :
            new OperationsDiagnosisSnapshot(snapshot.SessionId, snapshot.Tick, experience.Player, rows, links, rows.Count);
    }

    private static OperationsDiagnosisReason FromFailure(LogisticsTransportRequestFailureReason reason) => reason switch
    {
        LogisticsTransportRequestFailureReason.NoSourceSurplus => OperationsDiagnosisReason.NoSourceStock,
        LogisticsTransportRequestFailureReason.NoRoute => OperationsDiagnosisReason.NoRoute,
        LogisticsTransportRequestFailureReason.NoTruckAvailable => OperationsDiagnosisReason.NoTruck,
        LogisticsTransportRequestFailureReason.NoDriveFuel => OperationsDiagnosisReason.NoDriveFuel,
        LogisticsTransportRequestFailureReason.CapacitySaturated => OperationsDiagnosisReason.Capacity,
        LogisticsTransportRequestFailureReason.DestinationFull => OperationsDiagnosisReason.DestinationFull,
        LogisticsTransportRequestFailureReason.None => OperationsDiagnosisReason.None,
        _ => OperationsDiagnosisReason.Unknown
    };

    private static OperationsDiagnosisReason FromBottleneck(LogisticsBottleneckReason reason) => reason switch
    {
        LogisticsBottleneckReason.InsufficientSourceStock => OperationsDiagnosisReason.NoSourceStock,
        LogisticsBottleneckReason.InsufficientTruckCapacity or LogisticsBottleneckReason.SaturatedLinkOrHub =>
            OperationsDiagnosisReason.Capacity,
        LogisticsBottleneckReason.DisconnectedRoute => OperationsDiagnosisReason.NoRoute,
        LogisticsBottleneckReason.DestinationFull => OperationsDiagnosisReason.DestinationFull,
        LogisticsBottleneckReason.DriveFuelUnavailable => OperationsDiagnosisReason.NoDriveFuel,
        LogisticsBottleneckReason.None => OperationsDiagnosisReason.None,
        _ => OperationsDiagnosisReason.Unknown
    };

    private static OperationsDiagnosisReason FromService(PlayerWorkSummary work)
    {
        if (work.VehicleQueue is { } queue)
        {
            if (queue.DockReason == VehicleDockBlockReason.SourceEmpty) return OperationsDiagnosisReason.NoSourceStock;
            if (queue.DockReason == VehicleDockBlockReason.TargetFull) return OperationsDiagnosisReason.DestinationFull;
            if (queue.WaitReason == VehicleQueueWaitReason.Unavailable) return OperationsDiagnosisReason.NoDriveFuel;
            if (queue.WaitReason is VehicleQueueWaitReason.Full or VehicleQueueWaitReason.OccupiedExit ||
                queue.Position > 1) return OperationsDiagnosisReason.WaitingForBay;
        }
        if (work.LoadingQueue is { WaitingCount: > 0 }) return OperationsDiagnosisReason.WaitingForBay;
        return OperationsDiagnosisReason.None;
    }

    private struct ReasonAccumulator
    {
        public OperationsDiagnosisReason First;
        public OperationsDiagnosisReason Second;
        public OperationsDiagnosisReason Third;
        public void Add(OperationsDiagnosisReason reason)
        {
            if (reason == OperationsDiagnosisReason.None || reason == First || reason == Second || reason == Third) return;
            if (First == OperationsDiagnosisReason.None || reason < First)
            { Third = Second; Second = First; First = reason; }
            else if (Second == OperationsDiagnosisReason.None || reason < Second)
            { Third = Second; Second = reason; }
            else if (Third == OperationsDiagnosisReason.None || reason < Third) Third = reason;
        }
    }
}
