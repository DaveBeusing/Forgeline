using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct ConstructionTemplatePreviewRequest(ulong Id, PlayerId Issuer,
    int Template, Vector3 Anchor, BuildingOrientation Orientation, EntityId FirstSite, EntityId SecondSite);
public enum TemplateSiteState : byte { Unsent, Building, Complete, Unavailable }
public sealed record ConstructionTemplatePreview(ConstructionTemplatePreviewRequest Request, SimulationTick Tick,
    IReadOnlyList<BuildingPlacementPreview> Buildings, IReadOnlyList<IReadOnlyList<PlayerActionResourceAmount>> BuildingCosts,
    ConveyorPlan Link, IReadOnlyList<PlayerActionResourceAmount> TotalCosts, IReadOnlyList<TemplateSiteState> Sites)
{
    public bool HasCompleteQuote => Link.Pieces.Count > 0 && Link.Steel > 0;
    public bool CanStart => Buildings.All(b => b.IsValid) && Link.IsValid && TotalCosts.All(c => c.IsAvailable);
}

internal static class ConstructionTemplatePreviewFactory
{
    internal static ConstructionTemplatePreview? Capture(SimulationContext context, PresentationExtractionContext extraction)
    {
        if (extraction.Interaction.CaptureTemplateRequest() is not {} request ||
            request.Issuer != extraction.Player || !PaidConstructionTemplates.IsSupported(request.Template)) return null;
        var scenario = extraction.Scenario;
        var services = scenario.Services;
        var buildings = new BuildingPlacementPreview[PaidConstructionTemplates.BuildingCount];
        var costs = new IReadOnlyList<PlayerActionResourceAmount>[PaidConstructionTemplates.BuildingCount];
        var states = new TemplateSiteState[PaidConstructionTemplates.BuildingCount];
        var total = new Dictionary<ResourceId, double>();
        bool hasPayer = context.Entities.TryGetComponent(extraction.Side.CommandCore, out InventoryStorage storage) &&
            scenario.Inventories.Contains(storage.InventoryId);
        double Available(ResourceId resource) => hasPayer ? scenario.Inventories.GetAvailableQuantity(storage.InventoryId, resource) : 0;
        PlayerActionResourceAmount Amount(ResourceId resource, double quantity) =>
            new(resource, services.Resources[resource].DisplayName, quantity, Available(resource));
        for (int i = 0; i < buildings.Length; i++)
        {
            BuildingId id = PaidConstructionTemplates.Building(request.Template, i);
            buildings[i] = services.BuildingPlacement.CreatePreview(context.Entities, extraction.Player, id,
                PaidConstructionTemplates.Position(request.Anchor, request.Orientation, i), request.Orientation);
            costs[i] = Array.AsReadOnly(services.BuildingDefinitions[id].Costs.Select(c => Amount(c.ResourceId, c.Quantity)).ToArray());
            foreach (var cost in costs[i]) total[cost.ResourceId] = total.GetValueOrDefault(cost.ResourceId) + cost.RequiredQuantity;
            EntityId site = i == 0 ? request.FirstSite : request.SecondSite;
            states[i] = !site.IsValid ? TemplateSiteState.Unsent :
                !context.Entities.TryGetComponent(site, out ControllableEntity owner) || owner.Owner != extraction.Player ||
                context.Entities.TryGetComponent(site, out HealthState health) && health.IsDepleted ? TemplateSiteState.Unavailable :
                context.Entities.HasComponent<CompletedBuilding>(site) ? TemplateSiteState.Complete :
                context.Entities.HasComponent<ConstructionSite>(site) ? TemplateSiteState.Building : TemplateSiteState.Unavailable;
        }
        // Future structures must also keep each other's footprints and truck lanes clear.
        for (int i = 0; i < buildings.Length; i++)
            for (int j = 0; j < buildings.Length; j++)
                if (i != j && (ConveyorGeometry.Overlap(buildings[i].Bounds, buildings[j].Bounds) ||
                    buildings[i].LoadingCorridor is {} corridor && ConveyorGeometry.Overlap(corridor, buildings[j].Bounds)))
                    buildings[i] = buildings[i] with { IsValid = false, Failure = BuildingPlacementFailureReason.LoadingCorridorUnavailable };
        ConveyorPlan link = new(ConveyorFailure.PortMismatch, Array.Empty<ConveyorPlacement>(), 0);
        if (buildings[PaidConstructionTemplates.Source].ConveyorOutput is {} output &&
            buildings[PaidConstructionTemplates.Destination].ConveyorInput is {} input)
        {
            if (states.All(s => s == TemplateSiteState.Complete))
            {
                var route = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
                    output.Position - Vector3.UnitY * 5.8f, input.Position - Vector3.UnitY * 5.8f,
                    request.Orientation, 0, true, SourcePortBuilding: request.FirstSite,
                    DestinationPortBuilding: request.SecondSite, AutomaticRouting: true, AutomaticHeight: true);
                link = services.Conveyors.Preview(context.Entities, extraction.Player, route, extraction.Side.CommandCore);
            }
            else link = services.Conveyors.PreviewDesignRoute(context.Entities, extraction.Player, output, input, extraction.Side.CommandCore);
            foreach (var piece in link.Pieces)
                for (int i = 0; i < buildings.Length; i++)
                    if (ConveyorGeometry.Overlap(piece.Bounds, buildings[i].Bounds) ||
                        piece.IsSupport && buildings[i].LoadingCorridor is {} lane && ConveyorGeometry.Overlap(piece.Bounds, lane))
                        link = link with { Failure = ConveyorFailure.Obstructed, FailurePosition = piece.Bounds.Center };
        }
        total[ResourceIds.Steel] = total.GetValueOrDefault(ResourceIds.Steel) + link.Steel;
        return new(request, context.Tick, Array.AsReadOnly(buildings), Array.AsReadOnly(costs), link,
            Array.AsReadOnly(total.OrderBy(p => p.Key).Select(p => Amount(p.Key, p.Value)).ToArray()), Array.AsReadOnly(states));
    }
}
