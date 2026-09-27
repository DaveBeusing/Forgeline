using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>
/// Defers optional early refueling while a loaded delivery can retain the normal
/// dispatch reserve. Future logistics edges are estimates; the current physical
/// path replaces its edge estimate as soon as navigation resolves it. This policy
/// neither grants fuel nor replaces navigation or zero-fuel movement constraints.
/// </summary>
internal static class CargoDeliveryFuelPolicy
{
    private const double ReserveFraction = 0.20;

    public static bool ShouldDeferRefueling(
        EntityRegistry entities,
        InventoryStore inventories,
        EntityId entity)
    {
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
            inventories.GetQuantity(transport.CargoInventory, order.ResourceId) <= 0.0)
        {
            return false;
        }

        double available = inventories.GetAvailableQuantity(fuel.InventoryId, ResourceIds.Fuel);
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
            cargoRoute.Route is null || cargoRoute.NextSegmentIndex < 0 ||
            cargoRoute.NextSegmentIndex > cargoRoute.Route.Segments.Count ||
            cargoRoute.Route.Version != state.ObservedNetworkVersion)
        {
            return false;
        }

        double remaining = 0.0;
        for (int index = cargoRoute.NextSegmentIndex; index < cargoRoute.Route.Segments.Count; index++)
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
                physicalDistance = 0.0;
                Vector3 previous = transform.Position;
                int first = Math.Max(0, navigation.NextWaypointIndex - 1);
                for (int index = first; index < navigation.Path.Waypoints.Count; index++)
                {
                    Vector3 waypoint = navigation.Path.Waypoints[index];
                    physicalDistance += HorizontalDistance(previous, waypoint);
                    previous = waypoint;
                }

                physicalDistance += HorizontalDistance(previous, target.WorldPosition);
            }
            else if (!entities.HasComponent<NavigationAgent>(entity) &&
                     entities.TryGetComponent(entity, out MovementOrder movement) &&
                     SamePosition(movement.WorldTarget, target.WorldPosition))
            {
                physicalDistance = HorizontalDistance(transform.Position, target.WorldPosition);
            }
            else
            {
                // Between legs or while a path is pending, retain the full edge
                // estimate. Do not search a second path from the supply phase.
                physicalDistance = cargoRoute.Route.Segments[cargoRoute.NextSegmentIndex].DistanceMeters;
            }

            remaining += physicalDistance - cargoRoute.Route.Segments[cargoRoute.NextSegmentIndex].DistanceMeters;
        }

        double required = remaining * fuel.ConsumptionPerMeter + reserve;
        return double.IsFinite(required) && available >= required;
    }

    private static bool SamePosition(Vector3 left, Vector3 right) =>
        Vector3.DistanceSquared(left, right) <= 0.0001f;

    private static double HorizontalDistance(Vector3 left, Vector3 right)
    {
        double x = (double)left.X - right.X;
        double z = (double)left.Z - right.Z;
        return Math.Sqrt(x * x + z * z);
    }
}
