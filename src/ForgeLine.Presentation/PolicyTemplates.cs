using System.Globalization;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum PolicyTemplateKind : byte { Stock, Production }
public enum PolicyTemplateRowState : byte { Ready, Skipped, Pending, Confirmed, Rejected, Cancelled }

/// <summary>Local player intent only. No source facility, inventory or economic ownership is stored.</summary>
public readonly record struct PolicyTemplate(PolicyTemplateKind Kind, ResourceId Resource, RecipeId Recipe,
    double Minimum, double Target, double Maximum, LogisticsStockPriority StockPriority, bool Enabled,
    ProductionPriority ProductionPriority, ProductionRequestMode Mode, ResourceId Output, double OutputTarget, string Label)
{
    public const int MaximumSlots = 4, MaximumTargets = 32;
    public const double MaximumQuantity = 1_000_000;
    public bool Valid => Enum.IsDefined(Kind) && Enum.IsDefined(StockPriority) &&
        Enum.IsDefined(ProductionPriority) && Enum.IsDefined(Mode) &&
        (Kind == PolicyTemplateKind.Stock
            ? Resource.IsSpecified && double.IsFinite(Minimum) && double.IsFinite(Target) &&
              double.IsFinite(Maximum) && Minimum >= 0 && Minimum <= Target && Target <= Maximum &&
              Target >= LogisticsQuantityEditor.Minimum && Maximum <= MaximumQuantity
            : Recipe.IsSpecified && (Mode != ProductionRequestMode.DesiredStock ||
              Output.IsSpecified && double.IsFinite(OutputTarget) && OutputTarget >= LogisticsQuantityEditor.Minimum &&
              OutputTarget <= MaximumQuantity));
}

public readonly record struct PolicyTemplateStockFact(ResourceId Resource, string Name, EntityId Policy,
    int PolicyCount, double Minimum, double Target, double Maximum, LogisticsStockPriority Priority,
    bool Enabled, double Quantity, double Reserved, double Capacity, double OtherTargets);

public readonly record struct PolicyTemplateOutputLimit(ResourceId Resource, double Capacity);

public sealed record PolicyTemplateTargetSnapshot(EntityId Entity, string Name, string Unavailable,
    double InputCapacity, double OutputCapacity, IReadOnlyList<PolicyTemplateStockFact> Stocks,
    PlayerProductionFacilityActionReadModel? Production, IReadOnlyList<OperationsRuleReadModel> Rules)
{
    public IReadOnlyList<PolicyTemplateOutputLimit> OutputLimits { get; init; } = [];
}

public sealed record PolicyTemplateSelectionSnapshot(SimulationSessionId Session, SimulationTick Tick,
    IReadOnlyList<PolicyTemplateTargetSnapshot> Targets, int Omitted);

public sealed record PolicyTemplatePreviewRow(EntityId Entity, string Name, string Before, string After,
    string Reason, string Basis, PlayerActionRequest? Request, PolicyTemplateRowState State);

internal static class PolicyTemplatePreview
{
    internal static PolicyTemplatePreviewRow Create(PolicyTemplate draft, PolicyTemplateTargetSnapshot target)
    {
        string reason = !draft.Valid ? "INVALID LOCAL TEMPLATE" : target.Unavailable;
        string before = "", after = "", basis = "";
        PlayerActionRequest? request = null;
        if (draft.Kind == PolicyTemplateKind.Stock)
        {
            var stock = target.Stocks.FirstOrDefault(item => item.Resource == draft.Resource);
            basis = FormattableString.Invariant($"{target.InputCapacity}/{stock.Policy}/{stock.PolicyCount}/{stock.Minimum}/{stock.Target}/{stock.Maximum}/{stock.Priority}/{stock.Enabled}/{stock.Capacity}/{stock.OtherTargets}");
            before = stock.Policy.IsValid
                ? FormattableString.Invariant($"{stock.Name}: {stock.Minimum}/{stock.Target}/{stock.Maximum} {stock.Priority} {(stock.Enabled ? "ON" : "OFF")}")
                : draft.Label + ": NO POLICY";
            after = FormattableString.Invariant($"{draft.Minimum}/{draft.Target}/{draft.Maximum} {draft.StockPriority} {(draft.Enabled ? "ON" : "OFF")}; local {stock.Quantity}, reserved {stock.Reserved}");
            if (reason.Length == 0)
                reason = !stock.Resource.IsSpecified || stock.Capacity <= 0 ? "RESOURCE NOT ACCEPTED" :
                    stock.PolicyCount > 1 ? "MULTIPLE POLICIES / USE INDIVIDUAL CONTROLS" :
                    stock.Policy.IsValid && !stock.Enabled ? "MANUALLY DISABLED POLICY PRESERVED" :
                    target.Rules.Any(rule => rule.Enabled && rule.Definition.Template == OperationsRuleTemplate.EnsureInputStockPolicy &&
                        rule.Definition.Resource == draft.Resource) ? "ACTIVE INPUT RULE / USE INDIVIDUAL CONTROLS" :
                    draft.Maximum > stock.Capacity || draft.Target + stock.OtherTargets > target.InputCapacity
                        ? "CAPACITY OR COMBINED TARGETS EXCEEDED" : "";
            if (reason.Length == 0)
                request = PlayerActionRequest.SetStockPolicy(target.Entity, draft.Resource, draft.Minimum,
                    draft.Target, draft.Maximum, draft.StockPriority, draft.Enabled);
        }
        else
        {
            var production = target.Production;
            var recipe = production?.Recipes.FirstOrDefault(item => item.RecipeId == draft.Recipe);
            basis = target.OutputCapacity.ToString("G", CultureInfo.InvariantCulture) + "/" +
                string.Join(";", production?.Requests.Select(item => item.RequestEntity.ToString()) ?? []);
            before = production is null ? "NO PRODUCTION FACILITY" :
                production.IsAutomatic ? "AUTOMATIC PROCESSOR" : $"EXISTING QUEUE {production.Requests.Count}";
            after = $"{draft.Label}: {draft.Mode} {draft.ProductionPriority}";
            if (draft.Mode == ProductionRequestMode.DesiredStock)
                after += FormattableString.Invariant($" / target {draft.OutputTarget}");
            if (recipe is not null)
                after += " / input available/needed: " + string.Join(", ", recipe.Inputs.Take(8).Select(input =>
                    FormattableString.Invariant($"{input.DisplayName} {input.AvailableQuantity:0.##}/{input.RequiredQuantity:0.##}")));
            if (reason.Length == 0)
                reason = production is null || recipe is null ? "RECIPE NOT SUPPORTED" :
                    production.IsAutomatic ? "AUTOMATIC PROCESSOR / USE EXISTING CONTROLS" :
                    production.Requests.Count > 0 ? "EXISTING QUEUE PRESERVED" :
                    target.Rules.Any(rule => rule.Enabled) ? "ACTIVE OPERATIONS RULE / USE INDIVIDUAL CONTROLS" :
                    recipe.Inputs.Count > 8 || recipe.Outputs.Count > 8 ? "RECIPE EXCEEDS TEMPLATE LIMIT" :
                    recipe.Inputs.Sum(input => input.RequiredQuantity) > target.InputCapacity ||
                        recipe.Inputs.Any(input => target.Stocks.FirstOrDefault(stock => stock.Resource == input.ResourceId).Capacity < input.RequiredQuantity)
                        ? "RECIPE INPUTS EXCEED CAPACITY" :
                    draft.Mode == ProductionRequestMode.DesiredStock &&
                        (!recipe.Outputs.Any(output => output.ResourceId == draft.Output) ||
                         draft.OutputTarget > target.OutputCapacity ||
                         draft.OutputTarget > target.OutputLimits.FirstOrDefault(limit => limit.Resource == draft.Output).Capacity) ? "OUTPUT TARGET NOT SUPPORTED" : "";
            if (reason.Length == 0)
                request = PlayerActionRequest.QueueProduction(target.Entity, draft.Recipe, draft.ProductionPriority,
                    draft.Mode, draft.Output, draft.OutputTarget);
        }
        basis += "/" + string.Join(";", target.Rules.Select(rule =>
            $"{rule.Definition.Id}:{rule.Revision}:{rule.Enabled}"));
        return new(target.Entity, target.Name, before, after, reason, basis, request,
            reason.Length == 0 ? PolicyTemplateRowState.Ready : PolicyTemplateRowState.Skipped);
    }
}
