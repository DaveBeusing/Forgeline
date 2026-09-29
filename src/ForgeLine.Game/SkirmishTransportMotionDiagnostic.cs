using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed record SkirmishTransportMotionDiagnostic(
    string Velocity, int StalledTicks, ulong ObservedOrderTick,
    string CurrentWaypoint, string MovementKind, ulong AcceptedAtTick,
    string PendingDestination, int NextWaypointIndex, int WaypointCount,
    float RouteLengthMeters, IReadOnlyList<string> NextWaypoints)
{
    internal static SkirmishTransportMotionDiagnostic Capture(SimulationContext context, EntityId entity)
    {
        bool hasMovement = context.Entities.TryGetComponent(entity, out GroundMovementState movement);
        bool hasOrder = context.Entities.TryGetComponent(entity, out MovementOrder order);
        bool hasRoute = context.Entities.TryGetComponent(entity, out NavigationRouteState route);
        bool hasPending = context.Entities.TryGetComponent(entity, out NavigationPendingPath pending);
        var waypoints = new List<string>();
        if (hasRoute)
        {
            int first = Math.Max(0, route.NextWaypointIndex - 1);
            for (int index = first; index < route.Path.Waypoints.Count && waypoints.Count < 4; index++)
            {
                waypoints.Add(route.Path.Waypoints[index].ToString());
            }
        }

        return new SkirmishTransportMotionDiagnostic(
            hasMovement ? movement.Velocity.ToString() : "None",
            hasMovement ? movement.StalledTicks : 0,
            hasMovement ? movement.ObservedOrderTick.Value : 0,
            hasOrder ? order.WorldTarget.ToString() : "None",
            hasOrder ? order.Kind.ToString() : "None",
            hasOrder ? order.AcceptedAtTick.Value : 0,
            hasPending ? pending.OriginalOrder.WorldTarget.ToString() : "None",
            hasRoute ? route.NextWaypointIndex : 0,
            hasRoute ? route.Path.Waypoints.Count : 0,
            hasRoute ? route.Path.Diagnostics.RouteLengthMeters : 0.0f,
            waypoints);
    }
}
