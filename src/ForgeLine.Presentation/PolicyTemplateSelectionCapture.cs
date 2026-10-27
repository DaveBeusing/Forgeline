using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

internal static class PolicyTemplateSelectionCapture
{
    internal static PolicyTemplateSelectionSnapshot Capture(SimulationContext context,
        PresentationExtractionContext extraction, IReadOnlyList<EntityId> selection)
    {
        var entities = context.Entities;
        var scenario = extraction.Scenario;
        var stock = scenario.Inventories;
        var rules = scenario.OperationsRules.Capture(extraction.Player);
        var rows = new List<PolicyTemplateTargetSnapshot>(PolicyTemplate.MaximumTargets);
        foreach (EntityId entity in selection.Take(PolicyTemplate.MaximumTargets).Distinct())
        {
            string unavailable = !entities.TryGetComponent(entity, out ControllableEntity owner) ||
                owner.Owner != extraction.Player || !owner.IsControllable ? "NOT OWNED OR CONTROLLABLE" :
                !entities.TryGetComponent(entity, out CompletedBuilding _) ? "SELECT COMPLETED FACILITY" :
                !entities.TryGetComponent(entity, out HealthState health) || health.IsDepleted ? "DESTROYED FACILITY" : "";
            if (unavailable.Length > 0)
            {
                rows.Add(new(entity, "UNAVAILABLE TARGET", unavailable, 0, 0, [], null, []));
                continue;
            }
            var building = entities.GetComponent<CompletedBuilding>(entity);
            string name = scenario.Services.BuildingDefinitions.TryGet(building.BuildingId, out var definition)
                ? definition.DisplayName : building.BuildingId.ToString();
            name = name[..Math.Min(96, name.Length)];
            if (entities.TryGetComponent(entity, out WorldTransform pose))
                name += FormattableString.Invariant($" ({pose.Position.X:0},{pose.Position.Z:0})");
            InventoryId input = ConveyorSimulationSystem.ResolveInventory(entities, entity, true);
            var facts = new List<PolicyTemplateStockFact>(32);
            var policies = new List<(EntityId Entity, LogisticsStockPolicy Policy)>(32);
            bool policyOverflow = false;
            foreach (EntityId policyEntity in entities.Query<LogisticsStockPolicy>(QueryIterationOrder.StableByEntityIndex))
            {
                var policy = entities.GetComponent<LogisticsStockPolicy>(policyEntity);
                if (policy.TargetEntity != entity) continue;
                if (policies.Count == 32) { policyOverflow = true; continue; }
                policies.Add((policyEntity, policy));
            }
            if (stock.Contains(input))
                foreach (var resource in scenario.Services.Resources.Definitions.Take(32))
                {
                    var matching = policies.Where(item => item.Policy.ResourceId == resource.Id).ToArray();
                    var first = matching.Length > 0 ? matching[0] : default;
                    double quantity = stock.GetQuantity(input, resource.Id);
                    double capacity = stock.GetResourceCapacity(input, resource.Id);
                    double others = policies.Where(item => item.Policy.ResourceId != resource.Id && item.Policy.Enabled)
                        .Sum(item => item.Policy.DesiredTarget);
                    facts.Add(new(resource.Id, resource.DisplayName[..Math.Min(96, resource.DisplayName.Length)], first.Entity, matching.Length,
                        first.Policy.DesiredMinimum, first.Policy.DesiredTarget, first.Policy.DesiredMaximum,
                        first.Policy.Priority, matching.Length == 0 || first.Policy.Enabled, quantity,
                        stock.GetReservedQuantity(input, resource.Id), capacity, others));
                }
            PlayerProductionFacilityActionReadModel? production = null;
            double outputCapacity = 0;
            var outputLimits = new List<PolicyTemplateOutputLimit>(32);
            if (entities.TryGetComponent(entity, out ProductionFacility facility))
            {
                var recipes = new List<PlayerProductionRecipeActionReadModel>(16);
                foreach (var recipe in scenario.Services.ProductionRecipes.Definitions)
                {
                    if (!facility.Supports(recipe.RequiredCapability) || recipes.Count == 16) continue;
                    recipes.Add(new(recipe.Id, recipe.DisplayName[..Math.Min(96, recipe.DisplayName.Length)],
                        recipe.Inputs.Take(9).Select(cost => Amount(facility.InputInventory, cost.ResourceId, cost.Quantity)).ToArray(),
                        recipe.Outputs.Take(9).Select(cost => Amount(facility.OutputInventory, cost.ResourceId, cost.Quantity)).ToArray()));
                }
                var requests = new List<PlayerProductionRequestReadModel>(32);
                foreach (EntityId requestEntity in entities.Query<ProductionRequest>(QueryIterationOrder.StableByEntityIndex))
                {
                    var request = entities.GetComponent<ProductionRequest>(requestEntity);
                    if (request.Facility != entity || requests.Count == 32) continue;
                    requests.Add(new(requestEntity, request.RecipeId, request.RecipeId.ToString(), request.Priority,
                        request.Mode, request.Paused, facility.ActiveRequest == requestEntity)
                    { DesiredStockResource = request.DesiredStockResourceId, DesiredStockQuantity = request.DesiredStockQuantity });
                }
                production = new(entity, facility.ActiveRequest, facility.ActiveRecipe, facility.Status,
                    facility.BlockReason, 0, recipes, requests,
                    AutomaticProcessing.RecipeFor(facility.Capabilities).IsSpecified);
                if (stock.Contains(facility.OutputInventory))
                {
                    outputCapacity = stock.GetTotalCapacity(facility.OutputInventory);
                    foreach (var output in recipes.SelectMany(recipe => recipe.Outputs).Select(output => output.ResourceId).Distinct().Take(32))
                        outputLimits.Add(new(output, stock.GetResourceCapacity(facility.OutputInventory, output)));
                }
            }
            rows.Add(new(entity, name, policyOverflow ? "POLICY LIMIT / USE INDIVIDUAL CONTROLS" : "",
                stock.Contains(input) ? stock.GetTotalCapacity(input) : 0, outputCapacity,
                facts.AsReadOnly(), production, Array.AsReadOnly(rules.Where(rule =>
                    rule.Definition.Facility == entity).Take(OperationsRuleSystem.MaximumRulesPerFacility).ToArray()))
            { OutputLimits = outputLimits.AsReadOnly() });
        }
        return new(scenario.Simulation.SessionId, context.Tick, rows.AsReadOnly(),
            Math.Max(0, selection.Count - PolicyTemplate.MaximumTargets));

        PlayerActionResourceAmount Amount(InventoryId inventory, ResourceId resource, double required) =>
            new(resource, scenario.Services.Resources.TryGet(resource, out var value) ? value.DisplayName[..Math.Min(96, value.DisplayName.Length)] : resource.ToString(),
                required, stock.Contains(inventory) ? stock.GetAvailableQuantity(inventory, resource) : 0);
    }
}
