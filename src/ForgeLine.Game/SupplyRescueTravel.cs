using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct SupplyRescueAssignment(
    EntityId Recipient,
    MovementOrder Movement,
    bool HasFuelBudget,
    double AvailableFuel,
    double FuelPerMeter,
    SimulationTick BudgetUpdatedAtTick);

public readonly record struct SupplyRescueRejection(
    EntityId Recipient,
    string Reason,
    Vector3 ProviderPosition,
    Vector3 RecipientPosition,
    double RequiredFuel,
    double AvailableFuel,
    SimulationTick RejectedAtTick);

internal static class SupplyRescueTravel
{
    private const ulong RetryDelayTicks = 20;

    public static void Assign(
        SimulationContext context,
        InventoryStore? inventories,
        EntityId provider,
        EntityId recipient,
        MovementOrder movement)
    {
        var assignment = new SupplyRescueAssignment(
            recipient, movement, false, 0.0, 0.0, SimulationTick.Zero);
        if (context.Entities.HasComponent<SupplyRescueAssignment>(provider))
        {
            context.Entities.SetComponent(provider, assignment);
        }
        else
        {
            context.Entities.AddComponent(provider, assignment);
        }

        if (inventories is not null)
        {
            RefreshBudget(context, inventories, provider);
        }
    }

    public static void RefreshBudget(
        SimulationContext context, InventoryStore inventories, EntityId provider)
    {
        if (!context.Entities.TryGetComponent(provider, out SupplyRescueAssignment assignment))
        {
            return;
        }

        double available = 0.0;
        double consumption = 0.0;
        if (context.Entities.TryGetComponent(provider, out UnitFuelState fuel))
        {
            consumption = fuel.ConsumptionPerMeter;
            available = inventories.Contains(fuel.InventoryId)
                ? inventories.GetAvailableQuantity(fuel.InventoryId, ResourceIds.Fuel)
                : 0.0;
        }

        context.Entities.SetComponent(provider, assignment with
        {
            HasFuelBudget = true,
            AvailableFuel = available,
            FuelPerMeter = consumption,
            BudgetUpdatedAtTick = context.Tick
        });
    }

    public static void Release(
        SimulationContext context, EntityId provider, EntityId recipient)
    {
        if (!context.Entities.TryGetComponent(provider, out SupplyRescueAssignment assignment) ||
            assignment.Recipient != recipient)
        {
            return;
        }

        // Only the movement owned by this assignment may be canceled.
        // A later player, refueling, or logistics order must survive cleanup.
        if (OwnsMovement(context, provider, assignment))
        {
            TacticalCommandUtilities.ClearMovementIntent(context, provider);
        }

        context.Entities.RemoveComponent<SupplyRescueAssignment>(provider);
    }

    public static bool HasUnrelatedMovement(
        SimulationContext context, EntityId provider, EntityId recipient)
    {
        if (context.Entities.TryGetComponent(provider, out SupplyRescueAssignment assignment) &&
            assignment.Recipient == recipient && OwnsMovement(context, provider, assignment))
        {
            return false;
        }

        return TacticalCommandUtilities.TryGetMovementIntent(context, provider, out _);
    }

    private static bool OwnsMovement(
        SimulationContext context, EntityId provider, SupplyRescueAssignment assignment)
    {
        if (context.Entities.TryGetComponent(provider, out MovementOrder movement))
        {
            if (movement == assignment.Movement)
            {
                return true;
            }

            // A routed local waypoint is owned only while it matches the route.
            // Do not mistake an overlapping newer strategic order for that waypoint.
            return context.Entities.TryGetComponent(provider, out NavigationRouteState active) &&
                active.OriginalOrder == assignment.Movement && active.HasActiveWaypoint &&
                movement.AcceptedAtTick == active.ActiveWaypointTick &&
                Vector3.DistanceSquared(movement.WorldTarget,
                    active.Path.Waypoints[active.NextWaypointIndex - 1]) <= 0.0001f;
        }

        return TacticalCommandUtilities.TryGetMovementIntent(context, provider, out MovementOrder intent) &&
            intent == assignment.Movement;
    }

    public static void RedirectOwnedOrder(
        SimulationContext context, EntityId provider, MovementOrder previous, MovementOrder replacement)
    {
        if (context.Entities.TryGetComponent(provider, out SupplyRescueAssignment assignment) &&
            assignment.Movement == previous)
        {
            context.Entities.SetComponent(provider, assignment with { Movement = replacement });
        }
    }

    public static bool IsDeferred(
        SimulationContext context, InventoryStore? inventories, EntityId provider, EntityId recipient)
    {
        if (!context.Entities.TryGetComponent(provider, out SupplyRescueRejection rejection) ||
            rejection.Recipient != recipient ||
            context.Tick.Value - rejection.RejectedAtTick.Value >= RetryDelayTicks)
        {
            return false;
        }

        if (context.Entities.TryGetComponent(provider, out WorldTransform providerTransform) &&
            HorizontalDistance(providerTransform.Position, rejection.ProviderPosition) > 1.0)
        {
            return false;
        }

        if (context.Entities.TryGetComponent(recipient, out WorldTransform recipientTransform) &&
            HorizontalDistance(recipientTransform.Position, rejection.RecipientPosition) > 1.0)
        {
            return false;
        }

        if (rejection.RequiredFuel > 0.0 && inventories is not null &&
            context.Entities.TryGetComponent(provider, out UnitFuelState fuel) &&
            inventories.Contains(fuel.InventoryId) &&
            inventories.GetAvailableQuantity(fuel.InventoryId, ResourceIds.Fuel) >= rejection.RequiredFuel)
        {
            return false;
        }

        return true;
    }

    public static bool ValidateCompletedRoute(
        SimulationContext context, EntityId provider, MovementOrder order, NavigationPath path)
    {
        if (!context.Entities.TryGetComponent(provider, out SupplyRescueAssignment assignment) ||
            assignment.Movement != order)
        {
            return true;
        }

        if (!assignment.HasFuelBudget || assignment.BudgetUpdatedAtTick != context.Tick)
        {
            Reject(context, provider, assignment, "FuelBudgetUnavailable", 0.0);
            return false;
        }

        double required = RouteFuel(context, provider, assignment, path, 0);
        if (required > assignment.AvailableFuel)
        {
            Reject(context, provider, assignment, "RouteFuelInsufficient", required);
            return false;
        }

        return true;
    }

    public static bool ValidateRemainingRoute(
        SimulationContext context, EntityId provider, EntityId recipient)
    {
        if (!context.Entities.TryGetComponent(provider, out SupplyRescueAssignment assignment) ||
            assignment.Recipient != recipient ||
            !context.Entities.TryGetComponent(provider, out NavigationRouteState route) ||
            route.OriginalOrder != assignment.Movement)
        {
            return true;
        }

        int first = Math.Max(0, route.NextWaypointIndex - (route.HasActiveWaypoint ? 1 : 0));
        double required = RouteFuel(context, provider, assignment, route.Path, first);
        if (required > assignment.AvailableFuel)
        {
            Reject(context, provider, assignment, "RemainingRouteFuelInsufficient", required);
            return false;
        }

        return true;
    }

    public static void RecordNavigationFailure(
        SimulationContext context, EntityId provider, MovementOrder order, NavigationFailureReason reason)
    {
        if (context.Entities.TryGetComponent(provider, out SupplyRescueAssignment assignment) &&
            assignment.Movement == order)
        {
            Reject(context, provider, assignment, "Navigation:" + reason, 0.0);
        }
    }

    private static double RouteFuel(
        SimulationContext context, EntityId provider, SupplyRescueAssignment assignment,
        NavigationPath path, int firstWaypoint)
    {
        if (!context.Entities.TryGetComponent(provider, out WorldTransform transform))
        {
            return double.MaxValue;
        }

        Vector3 previous = transform.Position;
        double distance = 0.0;
        for (int index = firstWaypoint; index < path.Waypoints.Count; index++)
        {
            Vector3 waypoint = path.Waypoints[index];
            distance += HorizontalDistance(previous, waypoint);
            previous = waypoint;
        }

        // Include the final physical approach after projected grid endpoints.
        distance += HorizontalDistance(previous, assignment.Movement.WorldTarget);
        return distance * assignment.FuelPerMeter;
    }

    private static void Reject(
        SimulationContext context, EntityId provider, SupplyRescueAssignment assignment,
        string reason, double requiredFuel)
    {
        Vector3 start = context.Entities.TryGetComponent(provider, out WorldTransform providerTransform)
            ? providerTransform.Position : Vector3.Zero;
        Vector3 target = context.Entities.TryGetComponent(assignment.Recipient, out WorldTransform recipientTransform)
            ? recipientTransform.Position : Vector3.Zero;
        var rejection = new SupplyRescueRejection(
            assignment.Recipient, reason, start, target, requiredFuel,
            assignment.AvailableFuel, context.Tick);
        if (context.Entities.HasComponent<SupplyRescueRejection>(provider))
        {
            context.Entities.SetComponent(provider, rejection);
        }
        else
        {
            context.Entities.AddComponent(provider, rejection);
        }

        Release(context, provider, assignment.Recipient);
    }

    private static double HorizontalDistance(Vector3 left, Vector3 right)
    {
        double x = left.X - right.X;
        double z = left.Z - right.Z;
        return Math.Sqrt(x * x + z * z);
    }
}
