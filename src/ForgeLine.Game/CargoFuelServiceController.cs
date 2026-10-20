using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum CargoFuelServicePhase : byte
{
    ToProvider,
    Servicing,
    Exiting,
    AwaitingFuelRescue
}

public enum CargoFuelServiceReason : byte
{
    None,
    NoEligibleProvider,
    UnreachableProvider,
    InsufficientTravelFuel,
    ProviderUnavailable,
    SourceEmpty,
    DockBlocked,
    ExitBlocked
}

/// <summary>Simulation-owned, restartable propulsion-fuel service; cargo and transport reservations remain untouched.</summary>
public readonly record struct CargoFuelServiceState(
    EntityId Provider,
    CargoFuelServicePhase Phase,
    CargoFuelServiceReason Reason,
    SimulationTick StartedAtTick,
    ulong RetryAtTick = 0);

/// <summary>Independent service owner using the existing physical loading bay and InventoryStore.</summary>
internal sealed class CargoFuelServiceController(
    InventoryStore inventories,
    VehicleDockingService docks,
    HierarchicalNavigationSystem? navigation)
{
    private const double Epsilon = 0.000000001;
    private const double StartFraction = 0.45;
    private const ulong RetryTicks = 20;
    private const double BatchSize = 20;
    private readonly HierarchicalPathfinder? _pathfinder =
        navigation is null ? null : new HierarchicalPathfinder(navigation.World);

    public bool Process(SimulationContext context, EntityId truck, in CargoTransport cargo)
    {
        EntityRegistry entities = context.Entities;
        if (entities.HasComponent<SupplyTruck>(truck) ||
            !entities.TryGetComponent(truck, out UnitFuelState fuel))
            return true;

        if (!inventories.Contains(fuel.InventoryId))
        {
            Mark(context, truck, new(EntityId.Invalid, CargoFuelServicePhase.AwaitingFuelRescue,
                CargoFuelServiceReason.ProviderUnavailable, context.Tick, context.Tick.Value + RetryTicks));
            return false;
        }

        double available = inventories.GetAvailableQuantity(fuel.InventoryId, ResourceIds.Fuel);
        if (entities.TryGetComponent(truck, out CargoFuelServiceState service))
        {
            if (service.Phase == CargoFuelServicePhase.AwaitingFuelRescue)
            {
                if (entities.HasComponent<ResupplyOrder>(truck)) return false;
                if (context.Tick.Value < service.RetryAtTick && available < fuel.Capacity * StartFraction)
                    return false;
                entities.RemoveComponent<CargoFuelServiceState>(truck);
            }
            else
            {
                ExecuteService(context, truck, cargo, fuel, service);
                return false;
            }
        }

        if (available >= fuel.Capacity * StartFraction &&
            !entities.HasComponent<CargoDeliveryFuelDeferral>(truck))
            return true;
        if (entities.HasComponent<ResupplyOrder>(truck) ||
            entities.HasComponent<SupplyRescueAssignment>(truck))
            return false;
        if (entities.TryGetComponent(truck, out VehicleDockVisit visit) &&
            visit.Generation > 0)
            return true; // Never interrupt occupied cargo bays.

        if (!TryChooseProvider(context, truck, cargo.Owner, fuel, available,
                out EntityId provider, out CargoFuelServiceReason reason))
        {
            Mark(context, truck, new(EntityId.Invalid, CargoFuelServicePhase.AwaitingFuelRescue,
                reason, context.Tick, context.Tick.Value + RetryTicks));
            return false;
        }

        // A service detour must never clear the cargo job, its physical load or reserved stock.
        ClearServiceMovement(entities, truck);
        Mark(context, truck, new(provider, CargoFuelServicePhase.ToProvider,
            CargoFuelServiceReason.None, context.Tick));
        return false;
    }

    private bool TryChooseProvider(SimulationContext context, EntityId truck, PlayerId owner,
        in UnitFuelState fuel, double available, out EntityId selected,
        out CargoFuelServiceReason failure)
    {
        EntityRegistry entities = context.Entities;
        selected = EntityId.Invalid;
        failure = CargoFuelServiceReason.NoEligibleProvider;
        if (!entities.TryGetComponent(truck, out WorldTransform truckTransform))
            return false;
        double bestCost = double.PositiveInfinity;
        bool providerExists = false;
        foreach (EntityId candidate in entities.Query<SupplyProvider>(QueryIterationOrder.StableByEntityIndex))
        {
            var provider = entities.GetComponent<SupplyProvider>(candidate);
            if (provider.Owner != owner || !provider.Enabled || !inventories.Contains(provider.InventoryId) ||
                inventories.GetAvailableQuantity(provider.InventoryId, ResourceIds.Fuel) <= Epsilon ||
                !entities.TryGetComponent(candidate, out CompletedBuilding completed) || completed.Owner != owner ||
                !entities.TryGetComponent(candidate, out BuildingLoadingDock dock) ||
                !entities.TryGetComponent(candidate, out WorldTransform providerTransform) ||
                !VehicleDockingService.Operational(entities, candidate) ||
                entities.TryGetComponent(candidate, out SupplyDepot depot) &&
                    depot.State != SupplyDepotState.Operational)
                continue;
            providerExists = true;
            if (!VehicleDockPose.TryCreate(dock, providerTransform, truckTransform, out VehicleDockPose pose))
                continue;
            Vector3 entry = pose.Entry;
            if (entities.TryGetComponent(truck, out GroundMovement movement))
                entry.Y += movement.HeightOffset;
            double length;
            if (navigation is not null && entities.TryGetComponent(truck, out NavigationAgent agent))
            {
                // A real navigable route, not straight-line distance, determines service feasibility.
                if (_pathfinder!.World.Version != navigation.World.Version)
                    _pathfinder.UpdateWorld(navigation.World);
                var result = _pathfinder.FindPath(truckTransform.Position, entry, agent.Capabilities,
                    projectBlockedEndpoints: true);
                if (!result.Succeeded || result.Path is null)
                {
                    failure = CargoFuelServiceReason.UnreachableProvider;
                    continue;
                }
                length = result.Path.Diagnostics.RouteLengthMeters;
            }
            else
            {
                // Without a navigation owner only an already-local, settled truck can service.
                length = Math.Sqrt(VehicleDockPose.HorizontalDistanceSquared(truckTransform.Position, pose.Service));
                if (length > 2)
                {
                    failure = CargoFuelServiceReason.UnreachableProvider;
                    continue;
                }
            }

            // Exit and maneuvering reserve cannot be spent on the initial approach.
            double required = (length + Vector3.Distance(pose.Entry, pose.Service) + pose.HalfLength * 3 + 10) *
                fuel.ConsumptionPerMeter;
            if (!double.IsFinite(required) || available + Epsilon < required)
            {
                failure = CargoFuelServiceReason.InsufficientTravelFuel;
                continue;
            }
            if (length < bestCost || length == bestCost && candidate.CompareTo(selected) < 0)
            {
                selected = candidate;
                bestCost = length;
            }
        }
        if (!providerExists && failure == CargoFuelServiceReason.NoEligibleProvider)
            return false;
        return selected.IsValid;
    }

    private void ExecuteService(SimulationContext context, EntityId truck,
        in CargoTransport cargo, in UnitFuelState fuel, in CargoFuelServiceState service)
    {
        EntityRegistry entities = context.Entities;
        if (service.Phase == CargoFuelServicePhase.Exiting)
        {
            if (!docks.Exit(context, truck))
            {
                CargoDeliveryFuelPolicy.PrepareDockMovement(context, inventories, truck);
                Mark(context, truck, service with { Reason = CargoFuelServiceReason.ExitBlocked });
                return;
            }
            ClearServiceMovement(entities, truck);
            entities.RemoveComponent<CargoFuelServiceState>(truck);
            return;
        }

        if (!ValidProvider(entities, cargo.Owner, service.Provider, out InventoryId source))
        {
            if (entities.HasComponent<VehicleDockVisit>(truck))
            {
                Mark(context, truck, service with { Phase = CargoFuelServicePhase.Exiting,
                    Reason = CargoFuelServiceReason.ProviderUnavailable });
                return;
            }
            AwaitRescue(context, truck, CargoFuelServiceReason.ProviderUnavailable);
            return;
        }

        double needed = Math.Max(0, fuel.Capacity -
            inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel));
        if (needed <= Epsilon)
        {
            Mark(context, truck, service with { Phase = CargoFuelServicePhase.Exiting });
            return;
        }

        if (entities.HasComponent<NavigationFailureState>(truck))
        {
            if (entities.HasComponent<VehicleDockVisit>(truck))
                Mark(context, truck, service with { Phase = CargoFuelServicePhase.Exiting,
                    Reason = CargoFuelServiceReason.UnreachableProvider });
            else
                AwaitRescue(context, truck, CargoFuelServiceReason.UnreachableProvider);
            return;
        }

        bool docked = docks.Approach(context, truck, service.Provider, cargo.Owner);
        CargoDeliveryFuelPolicy.PrepareDockMovement(context, inventories, truck);
        if (!docked) return;
        if (!entities.TryGetComponent(truck, out VehicleDockVisit visit) ||
            visit.Building != service.Provider || visit.Generation == 0) return;

        double quantity = Math.Min(BatchSize,
            Math.Min(needed, Math.Min(inventories.GetAvailableQuantity(source, ResourceIds.Fuel),
                inventories.GetAddableQuantity(fuel.InventoryId, ResourceIds.Fuel, needed))));
        if (quantity <= Epsilon)
        {
            Mark(context, truck, service with { Phase = CargoFuelServicePhase.Exiting,
                Reason = CargoFuelServiceReason.SourceEmpty });
            return;
        }
        if (!docks.CanTransfer(context, truck, visit.Generation, quantity)) return;
        InventoryOperationResult transfer =
            inventories.Transfer(source, fuel.InventoryId, ResourceIds.Fuel, quantity);
        EngineInvariant.Require(transfer.Succeeded, DiagnosticCategory.Simulation,
            "CARGO_DRIVE_FUEL_TRANSFER_FAILED", "Validated physical cargo-truck fuel transfer failed.");
        VehicleDockingService.RecordTransfer(context, truck, visit.Generation);
        if (inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel) + Epsilon >= fuel.Capacity)
            Mark(context, truck, service with { Phase = CargoFuelServicePhase.Exiting,
                Reason = CargoFuelServiceReason.None });
    }

    private bool ValidProvider(EntityRegistry entities, PlayerId owner, EntityId providerId,
        out InventoryId source)
    {
        source = InventoryId.None;
        if (!providerId.IsValid || !entities.IsAlive(providerId) ||
            !entities.TryGetComponent(providerId, out SupplyProvider provider) ||
            !provider.Enabled || provider.Owner != owner ||
            !entities.TryGetComponent(providerId, out CompletedBuilding completed) ||
            completed.Owner != owner ||
            !entities.HasComponent<BuildingLoadingDock>(providerId) ||
            !VehicleDockingService.Operational(entities, providerId) ||
            entities.TryGetComponent(providerId, out SupplyDepot depot) &&
                depot.State != SupplyDepotState.Operational ||
            !inventories.Contains(provider.InventoryId)) return false;
        source = provider.InventoryId;
        return true;
    }

    private static void Mark(SimulationContext context, EntityId truck,
        in CargoFuelServiceState service) =>
        VehicleDockingService.Put(context.Entities, truck, service);

    private static void AwaitRescue(SimulationContext context, EntityId truck,
        CargoFuelServiceReason reason)
    {
        ClearServiceMovement(context.Entities, truck);
        Mark(context, truck, new(EntityId.Invalid, CargoFuelServicePhase.AwaitingFuelRescue,
            reason, context.Tick, context.Tick.Value + RetryTicks));
    }

    private static void ClearServiceMovement(EntityRegistry entities, EntityId truck)
    {
        if (entities.HasComponent<MovementOrder>(truck)) entities.RemoveComponent<MovementOrder>(truck);
        if (entities.HasComponent<NavigationPendingPath>(truck)) entities.RemoveComponent<NavigationPendingPath>(truck);
        if (entities.HasComponent<NavigationRouteState>(truck)) entities.RemoveComponent<NavigationRouteState>(truck);
        if (entities.HasComponent<NavigationFailureState>(truck)) entities.RemoveComponent<NavigationFailureState>(truck);
        if (entities.HasComponent<CargoTransportMovementTarget>(truck))
            entities.RemoveComponent<CargoTransportMovementTarget>(truck);
        CargoDeliveryFuelPolicy.ClearTravelState(entities, truck);
    }
}
