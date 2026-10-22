using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

internal static class CargoConvoyCommands
{
    public static bool TryCreate(
        SimulationContext context,
        PlayerId issuer,
        ReadOnlySpan<EntityId> requestedTrucks,
        ReadOnlySpan<EntityId> requestedEscorts,
        out EntityId created,
        out PlayerLogisticsActionFailureReason failure)
    {
        created = EntityId.Invalid;
        failure = PlayerLogisticsActionFailureReason.ConvoyIneligible;
        if (requestedTrucks.Length is < 2 or > 100 || requestedEscorts.Length > 100)
            return false;

        var unique = new HashSet<EntityId>();
        var trucks = new List<EntityId>(requestedTrucks.Length);
        foreach (EntityId truck in requestedTrucks)
        {
            if (!unique.Add(truck) ||
                !context.Entities.TryGetComponent(truck, out CargoTransportOrder order) ||
                !context.Entities.TryGetComponent(truck, out CargoTransportRouteState currentRoute) ||
                currentRoute.Route is null ||
                context.Entities.HasComponent<CargoConvoyMember>(truck))
                return false;
            if (!CargoConvoyEligibility.TryGetTruck(
                    context.Entities, issuer, truck, order.Origin,
                    order.Destination, currentRoute.Route.Version, out _))
                return false;
            trucks.Add(truck);
        }

        trucks.Sort();
        var primary = context.Entities.GetComponent<CargoTransportOrder>(trucks[0]);
        var leaderRoute = context.Entities.GetComponent<CargoTransportRouteState>(trucks[0]);
        foreach (EntityId truck in trucks)
        {
            var route = context.Entities.GetComponent<CargoTransportRouteState>(truck);
            var order = context.Entities.GetComponent<CargoTransportOrder>(truck);
            if (order.Origin != primary.Origin || order.Destination != primary.Destination ||
                route.Route is null || leaderRoute.Route is null ||
                route.NextSegmentIndex != leaderRoute.NextSegmentIndex ||
                route.Route.Segments.Count != leaderRoute.Route.Segments.Count)
                return false;
            for (int index = 0; index < route.Route.Segments.Count; index++)
                if (route.Route.Segments[index].EdgeId !=
                    leaderRoute.Route.Segments[index].EdgeId)
                    return false;
        }

        var escorts = new List<EntityId>(requestedEscorts.Length);
        foreach (EntityId escort in requestedEscorts)
        {
            if (!unique.Add(escort) ||
                !CargoConvoyEligibility.IsEscort(context.Entities, issuer, escort) ||
                context.Entities.HasComponent<CargoConvoyEscort>(escort) ||
                context.Entities.HasComponent<CombatOrderState>(escort) ||
                context.Entities.HasComponent<ResupplyOrder>(escort) ||
                context.Entities.HasComponent<SupplyRescueAssignment>(escort))
                return false;
            escorts.Add(escort);
        }

        created = context.Entities.CreateEntity();
        context.Entities.AddComponent(created, new CargoConvoyGroup(
            issuer, trucks[0], primary.Origin, primary.Destination,
            leaderRoute.Route!.Version, CargoConvoyStatus.Rendezvous,
            context.Tick, context.Tick, context.Tick));
        foreach (EntityId truck in trucks)
            context.Entities.AddComponent(truck, new CargoConvoyMember(created));
        foreach (EntityId escort in escorts)
            context.Entities.AddComponent(escort, new CargoConvoyEscort(
                created, SimulationTick.Zero, default));

        failure = PlayerLogisticsActionFailureReason.None;
        return true;
    }

    public static bool TryDissolve(
        SimulationContext context, PlayerId issuer, EntityId group,
        out PlayerLogisticsActionFailureReason failure)
    {
        if (!context.Entities.TryGetComponent(group, out CargoConvoyGroup convoy))
        {
            failure = PlayerLogisticsActionFailureReason.InvalidTarget;
            return false;
        }

        if (convoy.Owner != issuer)
        {
            failure = PlayerLogisticsActionFailureReason.ForeignOwnership;
            return false;
        }

        CargoConvoySystem.Dissolve(context, group);
        failure = PlayerLogisticsActionFailureReason.None;
        return true;
    }
}
