using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct CargoDeliveryFuelBudget(
    MovementOrder Movement,
    double AvailableFuel,
    double FuelPerMeter,
    SimulationTick CapturedAtTick);

public readonly record struct CargoDeliveryFuelDeferral(
    MovementOrder Movement,
    double RequiredFuel,
    double AvailableFuel,
    NavigationVersion NavigationVersion,
    SimulationTick DeferredAtTick);

/// <summary>
/// Keeps cargo movement inside its physical propulsion budget. Logistics-network
/// distances remain planning estimates; once navigation resolves the current
/// physical leg, that exact path must be affordable before its first waypoint is
/// exposed to movement. Optional early refueling retains the normal delivery
/// reserve but never grants Fuel or consumes carried cargo.
/// </summary>
internal static class CargoDeliveryFuelPolicy
{
    private const double QuantityEpsilon = 0.000000001;
    private const double ReserveFraction = 0.20;

    public static bool RequiresRefueling(
        EntityRegistry entities,
        InventoryStore inventories,
        EntityId entity)
    {
        if (!entities.TryGetComponent(entity, out CargoDeliveryFuelDeferral deferral) ||
            !entities.TryGetComponent(entity, out UnitFuelState fuel) ||
            !inventories.Contains(fuel.InventoryId))
        {
            return false;
        }

        double available =
            inventories.GetAvailableQuantity(
                fuel.InventoryId,
                ResourceIds.Fuel);
        return available + QuantityEpsilon < deferral.RequiredFuel;
    }

    public static bool ShouldDeferRefueling(
        EntityRegistry entities,
        InventoryStore inventories,
        EntityId entity)
    {
        if (RequiresRefueling(entities, inventories, entity))
        {
            return false;
        }

        if (entities.HasComponent<SupplyTruck>(entity) ||
            entities.HasComponent<ResupplyOrder>(entity) ||
            entities.HasComponent<NavigationFailureState>(entity) ||
            !entities.TryGetComponent(entity, out CargoTransport transport) ||
            !entities.TryGetComponent(entity, out CargoTransportOrder order) ||
            !entities.TryGetComponent(entity, out CargoTransportRuntimeState state) ||
            state.Lifecycle is not (CargoTransportLifecycleState.ToDestination or CargoTransportLifecycleState.Unloading) ||
            !entities.TryGetComponent(entity, out UnitFuelState fuel) ||
            !entities.TryGetComponent(entity, out WorldTransform transform) ||
            !inventories.Contains(fuel.InventoryId) ||
            !inventories.Contains(transport.CargoInventory) ||
            inventories.GetQuantity(transport.CargoInventory, order.ResourceId) <= QuantityEpsilon)
        {
            return false;
        }

        double available =
            inventories.GetAvailableQuantity(
                fuel.InventoryId,
                ResourceIds.Fuel);
        double reserve = fuel.Capacity * ReserveFraction;
        if (available <= reserve)
        {
            return false;
        }

        // Arrival must be allowed to commit the physical unload before an
        // optional top-up takes ownership of movement again.
        if (state.Lifecycle == CargoTransportLifecycleState.Unloading)
        {
            return true;
        }

        if (!entities.TryGetComponent(entity, out CargoTransportRouteState cargoRoute) ||
            cargoRoute.Route is null ||
            cargoRoute.NextSegmentIndex < 0 ||
            cargoRoute.NextSegmentIndex > cargoRoute.Route.Segments.Count ||
            cargoRoute.Route.Version != state.ObservedNetworkVersion)
        {
            return false;
        }

        double remaining = 0.0;
        for (int index = cargoRoute.NextSegmentIndex;
             index < cargoRoute.Route.Segments.Count;
             index++)
        {
            remaining += cargoRoute.Route.Segments[index].DistanceMeters;
        }

        if (cargoRoute.NextSegmentIndex < cargoRoute.Route.Segments.Count &&
            entities.TryGetComponent(entity, out CargoTransportMovementTarget target) &&
            target.NodeId == cargoRoute.Route.Segments[cargoRoute.NextSegmentIndex].To)
        {
            double physicalDistance;
            if (entities.TryGetComponent(entity, out NavigationRouteState navigation) &&
                SamePosition(navigation.OriginalOrder.WorldTarget, target.WorldPosition))
            {
                physicalDistance = RemainingPhysicalDistance(
                    transform.Position,
                    navigation.Path,
                    Math.Max(0, navigation.NextWaypointIndex - 1),
                    target.WorldPosition);
            }
            else if (!entities.HasComponent<NavigationAgent>(entity) &&
                     entities.TryGetComponent(entity, out MovementOrder movement) &&
                     SamePosition(movement.WorldTarget, target.WorldPosition))
            {
                physicalDistance =
                    HorizontalDistance(
                        transform.Position,
                        target.WorldPosition);
            }
            else
            {
                // Between legs or while a path is pending, retain the full edge
                // estimate. Do not search a second path from the supply phase.
                physicalDistance =
                    cargoRoute.Route.Segments[
                        cargoRoute.NextSegmentIndex].DistanceMeters;
            }

            remaining +=
                physicalDistance -
                cargoRoute.Route.Segments[
                    cargoRoute.NextSegmentIndex].DistanceMeters;
        }

        double required =
            remaining * fuel.ConsumptionPerMeter +
            reserve;
        return double.IsFinite(required) &&
               available + QuantityEpsilon >= required;
    }

    public static void PrepareMovementBudget(
        EntityRegistry entities,
        InventoryStore inventories,
        EntityId entity,
        MovementOrder movement)
    {
        if (!IsCargoTravel(entities, entity) ||
            !entities.TryGetComponent(entity, out UnitFuelState fuel) ||
            !inventories.Contains(fuel.InventoryId) ||
            fuel.ConsumptionPerMeter <= QuantityEpsilon)
        {
            RemoveBudget(entities, entity);
            return;
        }

        var budget = new CargoDeliveryFuelBudget(
            movement,
            inventories.GetAvailableQuantity(
                fuel.InventoryId,
                ResourceIds.Fuel),
            fuel.ConsumptionPerMeter,
            SimulationTick.Zero);

        if (entities.HasComponent<CargoDeliveryFuelBudget>(entity))
        {
            entities.SetComponent(entity, budget);
        }
        else
        {
            entities.AddComponent(entity, budget);
        }
    }

    public static bool ValidateCompletedRoute(
        SimulationContext context,
        EntityId entity,
        MovementOrder movement,
        NavigationPath path)
    {
        if (!context.Entities.TryGetComponent(
                entity,
                out CargoDeliveryFuelBudget budget) ||
            !SameTravel(budget.Movement, movement))
        {
            return true;
        }

        RemoveBudget(context.Entities, entity);

        if (!context.Entities.TryGetComponent(
                entity,
                out WorldTransform transform))
        {
            SetDeferral(
                context,
                entity,
                movement,
                double.MaxValue,
                budget.AvailableFuel,
                path.Version);
            return false;
        }

        double distance =
            RemainingPhysicalDistance(
                transform.Position,
                path,
                firstWaypoint: 0,
                movement.WorldTarget);
        double required =
            distance * budget.FuelPerMeter;

        if (double.IsFinite(required) &&
            budget.AvailableFuel + QuantityEpsilon >= required)
        {
            RemoveDeferral(context.Entities, entity);
            return true;
        }

        SetDeferral(
            context,
            entity,
            movement,
            double.IsFinite(required)
                ? required
                : double.MaxValue,
            budget.AvailableFuel,
            path.Version);
        return false;
    }

    public static bool ShouldDeferMovement(
        EntityRegistry entities,
        InventoryStore inventories,
        EntityId entity,
        MovementOrder movement,
        NavigationVersion navigationVersion)
    {
        if (!entities.TryGetComponent(
                entity,
                out CargoDeliveryFuelDeferral deferral))
        {
            return false;
        }

        if (!SameTravel(deferral.Movement, movement) ||
            deferral.NavigationVersion != navigationVersion)
        {
            RemoveDeferral(entities, entity);
            return false;
        }

        if (!entities.TryGetComponent(entity, out UnitFuelState fuel) ||
            !inventories.Contains(fuel.InventoryId))
        {
            return true;
        }

        double available =
            inventories.GetAvailableQuantity(
                fuel.InventoryId,
                ResourceIds.Fuel);
        if (available + QuantityEpsilon < deferral.RequiredFuel)
        {
            return true;
        }

        RemoveDeferral(entities, entity);
        return false;
    }

    public static void ClearTravelState(
        EntityRegistry entities,
        EntityId entity)
    {
        RemoveBudget(entities, entity);
        RemoveDeferral(entities, entity);
    }

    private static bool IsCargoTravel(
        EntityRegistry entities,
        EntityId entity) =>
        !entities.HasComponent<SupplyTruck>(entity) &&
        !entities.HasComponent<ResupplyOrder>(entity) &&
        entities.HasComponent<CargoTransport>(entity) &&
        entities.HasComponent<CargoTransportOrder>(entity) &&
        entities.TryGetComponent(
            entity,
            out CargoTransportRuntimeState state) &&
        state.Lifecycle is
            CargoTransportLifecycleState.ToOrigin or
            CargoTransportLifecycleState.ToDestination;

    private static void SetDeferral(
        SimulationContext context,
        EntityId entity,
        MovementOrder movement,
        double requiredFuel,
        double availableFuel,
        NavigationVersion navigationVersion)
    {
        var deferral = new CargoDeliveryFuelDeferral(
            movement,
            requiredFuel,
            availableFuel,
            navigationVersion,
            context.Tick);

        if (context.Entities.HasComponent<CargoDeliveryFuelDeferral>(entity))
        {
            context.Entities.SetComponent(entity, deferral);
        }
        else
        {
            context.Entities.AddComponent(entity, deferral);
        }
    }

    private static double RemainingPhysicalDistance(
        Vector3 start,
        NavigationPath path,
        int firstWaypoint,
        Vector3 destination)
    {
        double distance = 0.0;
        Vector3 previous = start;

        for (int index = firstWaypoint;
             index < path.Waypoints.Count;
             index++)
        {
            Vector3 waypoint = path.Waypoints[index];
            distance += HorizontalDistance(previous, waypoint);
            previous = waypoint;
        }

        distance += HorizontalDistance(previous, destination);
        return distance;
    }

    private static bool SameTravel(
        MovementOrder left,
        MovementOrder right) =>
        left.Issuer == right.Issuer &&
        SamePosition(left.WorldTarget, right.WorldTarget);

    private static bool SamePosition(
        Vector3 left,
        Vector3 right) =>
        Vector3.DistanceSquared(left, right) <= 0.0001f;

    private static void RemoveBudget(
        EntityRegistry entities,
        EntityId entity)
    {
        if (entities.HasComponent<CargoDeliveryFuelBudget>(entity))
        {
            entities.RemoveComponent<CargoDeliveryFuelBudget>(entity);
        }
    }

    private static void RemoveDeferral(
        EntityRegistry entities,
        EntityId entity)
    {
        if (entities.HasComponent<CargoDeliveryFuelDeferral>(entity))
        {
            entities.RemoveComponent<CargoDeliveryFuelDeferral>(entity);
        }
    }

    private static double HorizontalDistance(
        Vector3 left,
        Vector3 right)
    {
        double x = (double)left.X - right.X;
        double z = (double)left.Z - right.Z;
        return Math.Sqrt(x * x + z * z);
    }
}
