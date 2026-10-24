using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public enum OperationsCategory : byte { All, Production, Logistics, Supply, Power, Blocked }

public readonly record struct OperationsFacility(EntityId Entity, string Name, OperationsCategory Category,
    string Status, string Cause, string Explanation, int QueueCount, double? InventoryQuantity,
    double? InventoryCapacity, double? PowerDemand, double? AllocatedPower,
    double? TransportCapacity, double? Utilization, PlayerActionPanelMode Controls, double? GenerationCapacity = null,
    PlayerWorkSummary Service = default);

public readonly record struct OperationsResource(ResourceId Resource, string Name, double Quantity, double? NetRate);

/// <summary>Owned completed-tick facts. Bounded drill-down lists never contain foreign topology.</summary>
public sealed class OperationsSnapshot
{
    public const int MaximumFacilities = 256;
    public const int MaximumRoutes = 256;
    public OperationsSnapshot(SimulationSessionId session, SimulationTick tick, PlayerId player,
        IReadOnlyList<OperationsFacility> facilities, IReadOnlyList<OperationsResource> resources,
        IReadOnlyList<OperationsRoute> routes, int facilityCount, int routeCount)
    {
        Session = session; Tick = tick; Player = player;
        Facilities = Array.AsReadOnly(facilities.Take(MaximumFacilities).ToArray());
        Resources = Array.AsReadOnly(resources.Take(32).ToArray());
        Routes = Array.AsReadOnly(routes.Take(MaximumRoutes).ToArray());
        FacilityCount = facilityCount; RouteCount = routeCount;
    }
    public SimulationSessionId Session { get; }
    public SimulationTick Tick { get; }
    public PlayerId Player { get; }
    public IReadOnlyList<OperationsFacility> Facilities { get; }
    public IReadOnlyList<OperationsResource> Resources { get; }
    public IReadOnlyList<OperationsRoute> Routes { get; }
    public int FacilityCount { get; }
    public int RouteCount { get; }
    public static OperationsSnapshot? Resolve(PresentationSnapshot? snapshot) => snapshot is { } s &&
        s.SessionId.IsSpecified && s.Operations is { } o && o.Session == s.SessionId && o.Tick == s.Tick &&
        s.PlayerExperience is { } experience && experience.Tick == s.Tick && experience.Player == o.Player &&
        !experience.IsMatchComplete ? o : null;
}

public readonly record struct OperationsRoute(EntityId Source, EntityId Destination,
    double CapacityPerSecond, bool Enabled, double? Utilization);

internal static class OperationsSnapshotFactory
{
    public static OperationsSnapshot Capture(SimulationContext context, PresentationExtractionContext extraction, RenderInstance[]? visibleInstances = null, EntityId focused = default)
    {
        var entities = context.Entities;
        var visible = new HashSet<EntityId>();
        if (visibleInstances is not null)
            foreach (ref readonly var instance in visibleInstances.AsSpan())
                if (OperationsWorldOverlay.IsOwnedVisible(instance, extraction.Player)) visible.Add(instance.Entity);
        var scenario = extraction.Scenario;
        var inventories = new HashSet<InventoryId>();
        var queues = new Dictionary<EntityId, int>();
        foreach (var request in entities.Query<ProductionRequest>())
            Count(entities.GetComponent<ProductionRequest>(request).Facility);
        foreach (var request in entities.Query<UnitProductionRequest>())
            Count(entities.GetComponent<UnitProductionRequest>(request).Facility);
        var capacity = scenario.AutomatedDistribution.LastCapacityDebugSnapshot;
        var nodeCapacity = new Dictionary<LogisticsNodeId, LogisticsNodeCapacityReadModel>();
        var edgeCapacity = new Dictionary<LogisticsEdgeId, LogisticsEdgeCapacityReadModel>();
        if (capacity.CapturedAtTick == context.Tick)
        {
            foreach (var node in capacity.Nodes) nodeCapacity[node.NodeId] = node;
            foreach (var edge in capacity.Edges) edgeCapacity[edge.EdgeId] = edge;
        }
        var localNodes = new Dictionary<LogisticsNodeId, EntityId>();
        var distribution = new Dictionary<EntityId, string>();
        if (capacity.CapturedAtTick == context.Tick)
            foreach (var request in scenario.AutomatedDistribution.LastDebugSnapshot.Requests)
                if (request.FailureReason != LogisticsTransportRequestFailureReason.None &&
                    entities.TryGetComponent(request.PolicyEntity, out LogisticsStockPolicy policy) && Owned(policy.TargetEntity))
                    distribution[policy.TargetEntity] = request.FailureReason.ToString();
        var rows = new List<OperationsFacility>(OperationsSnapshot.MaximumFacilities);
        int count = 0;
        var conveyorStatus=ConveyorConstructionService.Capture(entities,extraction.Player,scenario.Inventories).ToDictionary(p=>p.Entity);
        foreach (var entity in entities.Query<ControllableEntity>(QueryIterationOrder.StableByEntityIndex))
        {
            if (!Owned(entity)) continue;
            if(entities.TryGetComponent(entity,out ConveyorPiece conveyor))
            {
                count++;
                var belt=conveyorStatus[entity];
                var flow=belt.Flow;
                string observedCause = "NOT OBSERVED";
                if (scenario.Services.ConveyorFlow.ObservationTick == context.Tick &&
                    scenario.Services.ConveyorFlow.TryGetObservation(entity, out var observation))
                    observedCause = OperationsDiagnosisSnapshot.Label(observation.First.Cause.IsValid &&
                        observation.First.Cause != entity && !visible.Contains(observation.First.Cause)
                        ? OperationsDiagnosisReason.Unknown : OperationsChainCapture.ConveyorReason(observation.First.Reason));
                if(flow is {} stock && scenario.Inventories.Contains(stock.Buffer))inventories.Add(stock.Buffer);
                bool hasPower=entities.TryGetComponent(entity,out PowerConsumer drive);
                if(rows.Count<OperationsSnapshot.MaximumFacilities || entity == focused)
                {
                    if (rows.Count == OperationsSnapshot.MaximumFacilities) rows.RemoveAt(rows.Count - 1);
                    rows.Add(new(entity,"Conveyor / "+ConveyorGeometry.Slug(conveyor.Placement.Module),
                        OperationsCategory.Logistics,belt.Status.ToString(),
                        belt.Status is ConveyorLinkStatus.Structural or ConveyorLinkStatus.Flowing?"":"Conveyor"+belt.Status,
                        flow is {} f?FormattableString.Invariant($"{observedCause}; IN {f.InputPerSecond:0.##}/s OUT {f.OutputPerSecond:0.##}/s"):"STRUCTURAL PART / NO CARGO",
                        0,flow is not null?conveyor.BufferedQuantity:null,flow?.Capacity,
                        hasPower?drive.Demand:null,hasPower?drive.AllocatedPower:null,
                        flow is not null?ConveyorSimulationSystem.NominalThroughput:null,
                        flow is {} rate?Math.Max(rate.InputPerSecond,rate.OutputPerSecond)/ConveyorSimulationSystem.NominalThroughput:null,
                        PlayerActionPanelMode.Construction));
                }
                continue;
            }
            bool processing = entities.TryGetComponent(entity, out ProductionFacility production);
            bool units = entities.TryGetComponent(entity, out UnitProductionFacility factory) && factory.Owner == extraction.Player;
            bool depot = entities.TryGetComponent(entity, out SupplyDepot supplyDepot);
            bool provider = entities.TryGetComponent(entity, out SupplyProvider supplyProvider);
            bool consumer = entities.TryGetComponent(entity, out PowerConsumer power);
            bool generator = entities.TryGetComponent(entity, out PowerGenerator generation);
            bool storage = entities.TryGetComponent(entity, out InventoryStorage inventoryStorage);
            bool hub = entities.TryGetComponent(entity, out LogisticsHub logisticsHub);
            bool cargo = entities.TryGetComponent(entity, out CargoTransport cargoTransport);
            bool node = scenario.Logistics.TryGetNodeForEntity(entity, out var nodeId) && scenario.Logistics.TryGetNode(nodeId, out _);
            if (node) localNodes[nodeId] = entity;
            if (!(processing || units || depot || provider || consumer || generator || storage || hub || cargo || node)) continue;
            var held = new HashSet<InventoryId>();
            if (processing) { held.Add(production.InputInventory); held.Add(production.OutputInventory); }
            if (units) held.Add(factory.InputInventory);
            if (depot) held.Add(supplyDepot.InventoryId);
            if (provider) held.Add(supplyProvider.InventoryId);
            if (storage) held.Add(inventoryStorage.InventoryId);
            if (hub) held.Add(logisticsHub.InventoryId);
            if (cargo) held.Add(cargoTransport.CargoInventory);
            double quantity = 0, totalCapacity = 0; bool hasInventory = false;
            foreach (var inventory in held)
                if (scenario.Inventories.Contains(inventory))
                { inventories.Add(inventory); hasInventory = true; quantity += scenario.Inventories.GetTotalQuantity(inventory); totalCapacity += scenario.Inventories.GetTotalCapacity(inventory); }
            count++;
            var category = processing || units ? OperationsCategory.Production : depot || provider ? OperationsCategory.Supply :
                storage || hub || cargo || node ? OperationsCategory.Logistics : OperationsCategory.Power;
            string status = processing ? production.Status.ToString() : units ? factory.Status.ToString() :
                depot ? supplyDepot.State.ToString() : provider ? supplyProvider.Enabled ? "ENABLED" : "DISABLED" :
                hub ? logisticsHub.State.ToString() : consumer ? power.State.ToString() : generator ? generation.State.ToString() : "AVAILABLE";
            string cause = processing && production.BlockReason != ProductionBlockReason.None ? production.BlockReason.ToString() :
                units && factory.BlockReason != UnitProductionBlockReason.None ? factory.BlockReason.ToString() :
                consumer && power.State != PowerOperationalState.Powered ? power.State.ToString() : string.Empty;
            if (distribution.TryGetValue(entity, out string? transportCause)) cause = cause.Length > 0 ? cause + "/" + transportCause : transportCause;
            string name = category.ToString();
            if (entities.TryGetComponent(entity, out CompletedBuilding identity) && scenario.Services.BuildingDefinitions.TryGet(identity.BuildingId, out var definition))
                name = definition.DisplayName;
            nodeCapacity.TryGetValue(nodeId, out var measured);
            double? transportCapacity = node && scenario.Logistics.TryGetNode(nodeId, out var topology) ? topology.ThroughputCapacityPerSecond : null;
            var service = PlayerExperienceSnapshotFactory.CaptureWork(entities, entity, scenario.Production, scenario.UnitProduction,
                scenario.Services.UnitDefinitions, scenario.Services.BuildingDefinitions);
            if (cargo && service.VehicleQueue is { }) status = LogisticsStatusCatalog.Service(service);
            bool serviceBlocked = LogisticsStatusCatalog.IsBlocked(service);
            if (serviceBlocked)
            {
                string serviceCause = LogisticsStatusCatalog.BlockedCause(service);
                cause = cause.Length > 0 ? cause + "/" + serviceCause : serviceCause;
            }
            var row = new OperationsFacility(entity, name, category, status, cause, Explain(cause), queues.GetValueOrDefault(entity),
                hasInventory ? quantity : null, hasInventory ? totalCapacity : null, consumer ? power.Demand : null,
                consumer ? power.AllocatedPower : null, transportCapacity, nodeCapacity.ContainsKey(nodeId) ? measured.Utilization : null,
                service.VehicleQueue is { WaitReason: VehicleQueueWaitReason.Unavailable } ? PlayerActionPanelMode.Supply :
                serviceBlocked || cause.Contains("NoInput", StringComparison.Ordinal) || distribution.ContainsKey(entity) ? PlayerActionPanelMode.Logistics :
                processing ? PlayerActionPanelMode.Production : units ? PlayerActionPanelMode.UnitProduction :
                depot || provider ? PlayerActionPanelMode.Logistics : hasInventory || cargo ? PlayerActionPanelMode.Logistics : PlayerActionPanelMode.Closed,
                generator ? generation.MaximumGeneration : null, service);
            if (rows.Count < OperationsSnapshot.MaximumFacilities) rows.Add(row);
            else if (entity == focused) rows[^1] = row;
            else if (cause.Length > 0)
            {
                int replace = -1;
                for (int index = 0; index < rows.Count; index++)
                    if (rows[index].Entity != focused && rows[index].Cause.Length == 0) { replace = index; break; }
                if (replace >= 0) rows[replace] = row;
            }
        }
        var routes = new List<OperationsRoute>(OperationsSnapshot.MaximumRoutes);
        int routeCount = 0;
        foreach (var edge in scenario.Logistics.GetEdges())
        {
            if (!localNodes.TryGetValue(edge.Source, out var source) || !localNodes.TryGetValue(edge.Destination, out var destination)) continue;
            routeCount++;
            if (routes.Count < OperationsSnapshot.MaximumRoutes)
                routes.Add(new(source, destination, edge.CapacityPerSecond, edge.Enabled,
                    edgeCapacity.TryGetValue(edge.Id, out var load) ? load.Utilization : null));
        }
        foreach(var belt in conveyorStatus.Values)
            if(belt.Flow is {} flow)
                foreach(var (destination,rate) in new[]{(flow.OutputA,flow.OutputAPerSecond),(flow.OutputB,flow.OutputBPerSecond)})
                {
                    if(!destination.IsValid || !Owned(destination))continue;
                    routeCount++;
                    if(routes.Count<OperationsSnapshot.MaximumRoutes)routes.Add(new(belt.Entity,destination,
                        ConveyorSimulationSystem.NominalThroughput,belt.Status!=ConveyorLinkStatus.Offline && belt.Status!=ConveyorLinkStatus.Destroyed,
                        rate/ConveyorSimulationSystem.NominalThroughput));
                }
        var resources = new List<OperationsResource>();
        foreach (var resource in scenario.Services.Resources.Definitions)
        {
            double quantity = 0;
            foreach (var inventory in inventories) quantity += scenario.Inventories.GetQuantity(inventory, resource.Id);
            resources.Add(new(resource.Id, resource.DisplayName, quantity, null));
        }
        rows.Sort(static (a, b) =>
        { int priority = (a.Cause.Length == 0).CompareTo(b.Cause.Length == 0); return priority != 0 ? priority : a.Entity.CompareTo(b.Entity); });
        return new(scenario.Simulation.SessionId, context.Tick, extraction.Player, rows, resources, routes, count, routeCount);
        void Count(EntityId entity) { if (Owned(entity)) queues[entity] = queues.GetValueOrDefault(entity) + 1; }
        bool Owned(EntityId entity) => entities.TryGetComponent(entity, out ControllableEntity owner) &&
            owner.Owner == extraction.Player && owner.IsControllable &&
            (!entities.TryGetComponent(entity, out HealthState health) || !health.IsDepleted || entities.HasComponent<ConveyorPiece>(entity));
    }
    internal static string Explain(string cause) => cause switch
    {
        "ConveyorNoInput" => "NO UNRESERVED ELIGIBLE INPUT / CHECK SOURCE STOCK AND ACCEPTING DOWNSTREAM PORT",
        "ConveyorOffline" => "POWER OR SUPPORTING FOOT UNAVAILABLE",
        "ConveyorDestroyed" => "CARGO RETAINED / REPAIR WRECK TO RESUME AND DRAIN",
        "ConveyorDisconnected" => "UNMATED PORT / CONNECT A MATCHING BELT OR BUILDING ADAPTER",
        "ConveyorBlocked" => "NO ACCEPTING DOWNSTREAM BUILDING PORT / CHECK ROUTE AND RESOURCE FILTER",
        "ConveyorCongested" => "BUFFERED CARGO WITH NO OPEN OUTPUT / DRAIN BEFORE DEMOLITION",
        "ConveyorDamaged" or "ConveyorCritical" => "CONVEYOR BUILD MENU / V TO REPAIR OR REPLACE",
        "NoInput" => "INPUT STOCK INSUFFICIENT - CHECK LOGISTICS",
        "LoadingAccessBlocked" => "CONFIRMED LOADING ACCESS BLOCKED - FOCUS THE BUILDING",
        "LoadingExitBlocked" => "CONFIRMED LOADING EXIT BLOCKED - FOCUS THE BUILDING",
        "NoDriveFuel" => "DRIVE TANK EMPTY - OPEN VEHICLE SUPPLY",
        "SourceEmpty" => "SOURCE STOCK EMPTY - CHECK SOURCE LOGISTICS",
        "NoPower" => "ALLOCATED POWER BELOW REQUIREMENT",
        "Brownout" => "REPORTED POWER BROWNOUT",
        "Offline" => "REPORTED POWER CONSUMER OFFLINE",
        "OutputFull" => "OUTPUT STORAGE FULL - CHECK COLLECTION",
        "Paused" => "REQUEST PAUSED BY POLICY",
        "DesiredStockReached" => "TARGET STOCK REACHED",
        "InvalidInventory" => "INVENTORY UNAVAILABLE",
        "NoRoute" => "REPORTED ROUTE UNAVAILABLE - CHECK CONNECTIONS",
        "CapacitySaturated" => "REPORTED LINK OR HUB CAPACITY SATURATED",
        "DestinationFull" => "DESTINATION STORAGE FULL",
        "NoSourceSurplus" => "NO SOURCE SURPLUS FOR TRANSPORT",
        "NoTruckAvailable" => "NO AVAILABLE TRANSPORT VEHICLE",
        _ when cause.StartsWith("NoInput/", StringComparison.Ordinal) => "INPUT BLOCK WITH REPORTED TRANSPORT FAILURE",
        _ => cause.Length == 0 ? "NO REPORTED BLOCK" : "REPORTED FACILITY BLOCK"
    };
}
