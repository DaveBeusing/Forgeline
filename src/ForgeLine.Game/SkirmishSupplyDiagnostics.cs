using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed record SkirmishSupplyPlanningDiagnostic(
    string Entity, ulong EvaluatedAtTick, string SelectedProvider,
    int FriendlyProviders, int RejectedProviders, string Rejections);

public sealed record SkirmishRescueRouteDiagnostic(
    string Provider, string Recipient, string Reason, ulong RejectedAtTick,
    double RequiredFuel, double AvailableFuel);

public sealed record SkirmishSupplyDiagnosticSnapshot(
    IReadOnlyList<SkirmishSupplyPlanningDiagnostic> Planning,
    IReadOnlyList<SkirmishRescueRouteDiagnostic> RouteRejections,
    int OmittedPlanningDetails,
    int OmittedRouteDetails)
{
    public static SkirmishSupplyDiagnosticSnapshot Empty { get; } = new([], [], 0, 0);
}

internal static class SkirmishSupplyDiagnostics
{
    private const int MaximumPlanningDetails = 128;
    private const int MaximumRouteDetails = 64;

    public static SkirmishSupplyDiagnosticSnapshot Capture(SimulationContext context, PlayerId owner)
    {
        var planning = new List<SkirmishSupplyPlanningDiagnostic>();
        int planningCount = 0;
        foreach (EntityId entity in context.Entities.Query<ResupplyPlanningResult>(QueryIterationOrder.StableByEntityIndex))
        {
            if (!context.Entities.TryGetComponent(entity, out ControllableEntity controllable) ||
                controllable.Owner != owner)
            {
                continue;
            }

            planningCount++;
            if (planning.Count == MaximumPlanningDetails)
            {
                continue;
            }

            ResupplyPlanningResult result = context.Entities.GetComponent<ResupplyPlanningResult>(entity);
            planning.Add(new SkirmishSupplyPlanningDiagnostic(
                entity.ToString(), result.EvaluatedAtTick.Value,
                result.SelectedProvider.IsValid ? result.SelectedProvider.ToString() : "None",
                result.FriendlyProviders, result.RejectedProviders, result.Rejections.ToString()));
        }

        var routes = new List<SkirmishRescueRouteDiagnostic>();
        int routeCount = 0;
        foreach (EntityId entity in context.Entities.Query<SupplyRescueRejection>(QueryIterationOrder.StableByEntityIndex))
        {
            if (!context.Entities.TryGetComponent(entity, out SupplyProvider provider) || provider.Owner != owner)
            {
                continue;
            }

            routeCount++;
            if (routes.Count == MaximumRouteDetails)
            {
                continue;
            }

            SupplyRescueRejection rejection = context.Entities.GetComponent<SupplyRescueRejection>(entity);
            routes.Add(new SkirmishRescueRouteDiagnostic(
                entity.ToString(), rejection.Recipient.ToString(), rejection.Reason,
                rejection.RejectedAtTick.Value, rejection.RequiredFuel, rejection.AvailableFuel));
        }

        return new SkirmishSupplyDiagnosticSnapshot(
            planning, routes, Math.Max(0, planningCount - planning.Count), Math.Max(0, routeCount - routes.Count));
    }
}
