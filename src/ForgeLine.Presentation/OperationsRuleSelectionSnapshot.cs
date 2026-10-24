using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Detached rule facts for exactly one authorized processing selection.</summary>
public sealed class OperationsRuleSelectionSnapshot
{
    public OperationsRuleSelectionSnapshot(PlayerId player, EntityId facility, double inputCapacity,
        TimeSpan tickDuration, RecipeId contextRecipe, IReadOnlyList<PlayerActionResourceAmount> inputs,
        IReadOnlyList<OperationsRuleReadModel> ownerRules,
        IReadOnlyList<PlayerProductionRequestReadModel>? requests = null)
    {
        Player = player;
        Facility = facility;
        InputCapacity = inputCapacity;
        TickDuration = tickDuration;
        ContextRecipe = contextRecipe;
        Inputs = Array.AsReadOnly(inputs.Take(32).ToArray());
        UsedRuleIds = Array.AsReadOnly(ownerRules.Where(rule => rule.Definition.Owner == player).Take(OperationsRuleSystem.MaximumRulesPerPlayer)
            .Select(rule => rule.Definition.Id).ToArray());
        Rules = Array.AsReadOnly(ownerRules.Where(rule => rule.Definition.Owner == player &&
            rule.Definition.Facility == facility).Take(OperationsRuleSystem.MaximumRulesPerFacility).ToArray());
        ManagedRequests = Array.AsReadOnly((requests ?? []).Where(request =>
            Rules.Any(rule => rule.ManagedProductionRequest == request.RequestEntity))
            .Take(OperationsRuleSystem.MaximumRulesPerFacility).ToArray());
    }

    public PlayerId Player { get; }
    public EntityId Facility { get; }
    public double InputCapacity { get; }
    public TimeSpan TickDuration { get; }
    public RecipeId ContextRecipe { get; }
    public IReadOnlyList<PlayerActionResourceAmount> Inputs { get; }
    public IReadOnlyList<ulong> UsedRuleIds { get; }
    public IReadOnlyList<OperationsRuleReadModel> Rules { get; }
    public IReadOnlyList<PlayerProductionRequestReadModel> ManagedRequests { get; }
}
