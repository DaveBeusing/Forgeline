using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum OperationsRuleTemplate : byte
{
    EnsureInputStockPolicy = 1,
    PauseUntilOutputAvailable = 2
}

public enum OperationsRuleOperation : byte
{
    Upsert = 1,
    SetEnabled = 2,
    Remove = 3
}

public enum OperationsRuleResult : byte
{
    None,
    Pending,
    WaitingForTrigger,
    Applied,
    ExistingPolicy,
    Recovered,
    Disabled,
    ManualOverride,
    TargetUnavailable,
    InvalidDefinition,
    RevisionMismatch,
    LimitReached,
    Rejected,
    Cooldown,
    ActionLimit
}

/// <summary>One explicit, owned facility target; material policies and production remain authoritative.</summary>
public readonly record struct OperationsRuleDefinition(
    ulong Id, PlayerId Owner, EntityId Facility, OperationsRuleTemplate Template,
    ResourceId Resource, double Minimum, double Target, double Maximum,
    LogisticsStockPriority Priority, uint CooldownTicks = 20);

public readonly record struct OperationsRuleReadModel(
    OperationsRuleDefinition Definition, uint Revision, bool Enabled,
    OperationsRuleResult Result, SimulationTick LastEvaluated,
    SimulationTick LastFired, int ActionCount, bool Pending,
    EntityId ManagedProductionRequest);

public sealed class OperationsRuleCommand : ISimulationCommand
{
    private readonly OperationsRuleSystem _rules;

    public OperationsRuleCommand(
        OperationsRuleSystem rules, OperationsRuleOperation operation,
        OperationsRuleDefinition definition, uint expectedRevision,
        bool enabled, SimulationTick submittedAtTick)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        Operation = operation;
        Definition = definition;
        ExpectedRevision = expectedRevision;
        Enabled = enabled;
        SubmittedAtTick = submittedAtTick;
    }

    public OperationsRuleOperation Operation { get; }
    public OperationsRuleDefinition Definition { get; }
    public uint ExpectedRevision { get; }
    public bool Enabled { get; }
    public SimulationTick SubmittedAtTick { get; }
    public SimulationTick ExecutedAtTick { get; private set; }
    public OperationsRuleResult Result { get; private set; }
    public bool Accepted { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ExecutedAtTick = context.Tick;
        Result = _rules.Apply(context, Operation, Definition, ExpectedRevision, Enabled);
        Accepted = Result == OperationsRuleResult.Applied;
    }
}

/// <summary>
/// Bounded deterministic rule evaluation. It may execute only existing, owner-checked
/// production and stock-policy commands, never manipulate inventories or transport.
/// </summary>
public sealed class OperationsRuleSystem : ISimulationSystem, IDisposable
{
    public const int MaximumRulesPerPlayer = 32;
    public const int MaximumRulesPerFacility = 8;
    public const int MaximumRulesPerTick = 64;
    public const int MaximumActionsPerRule = 16;
    private const int HysteresisTicks = 3;

    private sealed class Entry
    {
        public required OperationsRuleDefinition Definition;
        public uint Revision;
        public bool Enabled;
        public OperationsRuleResult Result = OperationsRuleResult.Pending;
        public SimulationTick LastEvaluated;
        public SimulationTick LastFired;
        public int ActionCount;
        public int HeldTicks;
        public bool InputPolicyHandled;
        public bool Suspended;
        public EntityId ManagedRequest;
    }

    private readonly SimulationCoordinator _simulation;
    private readonly InventoryStore _inventories;
    private readonly ProductionRecipeCatalog _recipes;
    private readonly List<Entry> _entries = [];
    private readonly List<(SimulationTick Tick, EntityId Request)> _manualPauses = [];
    private bool _disposed;

    public OperationsRuleSystem(
        SimulationCoordinator simulation, InventoryStore inventories,
        ProductionRecipeCatalog recipes)
    {
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _inventories = inventories ?? throw new ArgumentNullException(nameof(inventories));
        _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
        _simulation.CommandSubmitted += OnCommandSubmitted;
    }

    public SimulationPhase Phase => SimulationPhase.AiDecisions;

    public bool Matches(SimulationCoordinator simulation) => ReferenceEquals(_simulation, simulation);

    public IReadOnlyList<OperationsRuleReadModel> Capture(PlayerId owner)
    {
        var rows = new List<OperationsRuleReadModel>();
        foreach (Entry e in _entries)
            if (e.Definition.Owner == owner)
                rows.Add(new(e.Definition, e.Revision, e.Enabled, e.Result,
                    e.LastEvaluated, e.LastFired, e.ActionCount,
                    e.Enabled && e.Result == OperationsRuleResult.Pending, e.ManagedRequest));
        return Array.AsReadOnly(rows.ToArray());
    }

    internal OperationsRuleResult Apply(
        SimulationContext context, OperationsRuleOperation operation,
        OperationsRuleDefinition definition, uint expectedRevision, bool enabled)
    {
        if (!definition.Owner.IsSpecified || definition.Id == 0 || !Enum.IsDefined(operation))
            return OperationsRuleResult.InvalidDefinition;

        int index = _entries.FindIndex(e => e.Definition.Owner == definition.Owner &&
            e.Definition.Id == definition.Id);
        if (operation != OperationsRuleOperation.Upsert)
        {
            if (index < 0) return OperationsRuleResult.TargetUnavailable;
            Entry current = _entries[index];
            if (current.Revision != expectedRevision) return OperationsRuleResult.RevisionMismatch;
            if (operation == OperationsRuleOperation.Remove)
            {
                // A rule-created pause must not be stranded by disable/removal.
                ReleaseManagedPause(context, current);
                _entries.RemoveAt(index);
                return OperationsRuleResult.Applied;
            }
            if (operation == OperationsRuleOperation.SetEnabled)
            {
                if (!enabled) ReleaseManagedPause(context, current);
                current.Enabled = enabled;
                current.Suspended = false;
                current.InputPolicyHandled = false;
                current.HeldTicks = 0;
                current.ActionCount = 0;
                current.Revision++;
                current.Result = enabled ? OperationsRuleResult.Pending : OperationsRuleResult.Disabled;
                return OperationsRuleResult.Applied;
            }
            return OperationsRuleResult.InvalidDefinition;
        }

        if (!ValidDefinition(definition) || !OwnedFacility(context.Entities, definition))
            return OperationsRuleResult.InvalidDefinition;
        if (index >= 0 && _entries[index].Revision != expectedRevision)
            return OperationsRuleResult.RevisionMismatch;
        if (index < 0)
        {
            if (expectedRevision != 0) return OperationsRuleResult.RevisionMismatch;
            if (_entries.Count(e => e.Definition.Owner == definition.Owner) >= MaximumRulesPerPlayer ||
                _entries.Count(e => e.Definition.Owner == definition.Owner &&
                    e.Definition.Facility == definition.Facility) >= MaximumRulesPerFacility)
                return OperationsRuleResult.LimitReached;
            _entries.Add(new Entry { Definition = definition, Revision = 1, Enabled = enabled,
                Result = enabled ? OperationsRuleResult.Pending : OperationsRuleResult.Disabled });
            _entries.Sort(static (a, b) =>
            {
                int owner = a.Definition.Owner.Value.CompareTo(b.Definition.Owner.Value);
                return owner != 0 ? owner : a.Definition.Id.CompareTo(b.Definition.Id);
            });
            return OperationsRuleResult.Applied;
        }

        Entry entry = _entries[index];
        ReleaseManagedPause(context, entry);
        entry.Definition = definition;
        entry.Enabled = enabled;
        entry.Revision++;
        entry.HeldTicks = 0;
        entry.ActionCount = 0;
        entry.InputPolicyHandled = false;
        entry.Suspended = false;
        entry.LastFired = SimulationTick.Zero;
        entry.Result = enabled ? OperationsRuleResult.Pending : OperationsRuleResult.Disabled;
        return OperationsRuleResult.Applied;
    }

    private static bool ValidDefinition(in OperationsRuleDefinition d) =>
        d.Facility.IsValid && d.CooldownTicks is >= 5 and <= 10_000 &&
        Enum.IsDefined(d.Template) && Enum.IsDefined(d.Priority) &&
        (d.Template == OperationsRuleTemplate.PauseUntilOutputAvailable ||
         (d.Resource.IsSpecified && double.IsFinite(d.Minimum) &&
          double.IsFinite(d.Target) && double.IsFinite(d.Maximum) &&
          d.Minimum >= 0 && d.Minimum <= d.Target && d.Target <= d.Maximum && d.Target > 0));

    private static bool OwnedFacility(EntityRegistry entities, in OperationsRuleDefinition d) =>
        entities.IsAlive(d.Facility) &&
        entities.TryGetComponent(d.Facility, out ControllableEntity owner) &&
        owner.Owner == d.Owner &&
        entities.HasComponent<ProductionFacility>(d.Facility);

    private void OnCommandSubmitted(SimulationCommandEnvelope command)
    {
        if (command.Command is PlayerProductionActionCommand
            { Operation: PlayerProductionOperation.SetPaused } manual)
            _manualPauses.Add((command.TargetTick, manual.RequestEntity));
    }

    public void Execute(SimulationContext context)
    {
        // Manual requests are recognized at execution tick, including commands
        // pre-scheduled while reconstructing replay history.
        for (int i = _manualPauses.Count - 1; i >= 0; i--)
        {
            var manual = _manualPauses[i];
            if (manual.Tick > context.Tick) continue;
            if (manual.Tick == context.Tick)
                foreach (Entry entry in _entries)
                    if (entry.ManagedRequest == manual.Request ||
                        (context.Entities.TryGetComponent(manual.Request, out ProductionRequest request) &&
                         request.Facility == entry.Definition.Facility))
                    {
                        entry.Suspended = true;
                        entry.ManagedRequest = EntityId.Invalid;
                        entry.Result = OperationsRuleResult.ManualOverride;
                    }
            _manualPauses.RemoveAt(i);
        }

        int evaluated = 0;
        foreach (Entry entry in _entries)
        {
            if (!entry.Enabled || evaluated++ >= MaximumRulesPerTick) continue;
            if (entry.LastEvaluated == context.Tick) continue;
            entry.LastEvaluated = context.Tick;
            if (entry.Suspended) continue;
            if (!OwnedFacility(context.Entities, entry.Definition))
            {
                entry.Result = OperationsRuleResult.TargetUnavailable;
                entry.Enabled = false;
                continue;
            }
            ProductionFacility facility = context.Entities.GetComponent<ProductionFacility>(entry.Definition.Facility);
            if (entry.Definition.Template == OperationsRuleTemplate.EnsureInputStockPolicy)
                EvaluateInputPolicy(context, entry, facility);
            else
                EvaluateOutputPause(context, entry, facility);
        }
    }

    private void EvaluateInputPolicy(SimulationContext context, Entry entry, ProductionFacility facility)
    {
        if (entry.InputPolicyHandled) return;
        if (facility.BlockReason != ProductionBlockReason.NoInput ||
            !context.Entities.TryGetComponent(facility.ActiveRequest, out ProductionRequest request) ||
            !_recipes.TryGet(request.RecipeId, out ProductionRecipeDefinition? recipe) ||
            !_inventories.Contains(facility.InputInventory) ||
            !recipe.Inputs.Any(ingredient => ingredient.ResourceId == entry.Definition.Resource &&
                _inventories.GetAvailableQuantity(facility.InputInventory, ingredient.ResourceId) < ingredient.Quantity))
        {
            entry.HeldTicks = 0;
            entry.Result = OperationsRuleResult.WaitingForTrigger;
            return;
        }
        if (++entry.HeldTicks < HysteresisTicks) return;
        if (!Ready(entry, context.Tick)) return;

        // Existing policies are always authoritative, including manually disabled ones.
        foreach (EntityId entity in context.Entities.Query<LogisticsStockPolicy>(QueryIterationOrder.StableByEntityIndex))
        {
            LogisticsStockPolicy policy = context.Entities.GetComponent<LogisticsStockPolicy>(entity);
            if (policy.TargetEntity != entry.Definition.Facility || policy.ResourceId != entry.Definition.Resource)
                continue;
            entry.InputPolicyHandled = true;
            entry.Result = policy.Enabled ? OperationsRuleResult.ExistingPolicy :
                OperationsRuleResult.ManualOverride;
            return;
        }

        var d = entry.Definition;
        var action = PlayerLogisticsActionCommand.SetStockPolicy(
            d.Owner, d.Facility, d.Resource, d.Minimum, d.Target, d.Maximum,
            d.Priority, true, context.Tick);
        action.Execute(context);
        entry.InputPolicyHandled = true; // Never synthesize another demand on subsequent ticks.
        entry.Result = action.Accepted ? OperationsRuleResult.Applied : OperationsRuleResult.Rejected;
        if (action.Accepted) Fired(entry, context.Tick);
    }

    private void EvaluateOutputPause(SimulationContext context, Entry entry, ProductionFacility facility)
    {
        if (entry.ManagedRequest.IsValid)
        {
            if (!context.Entities.TryGetComponent(entry.ManagedRequest, out ProductionRequest managed) ||
                managed.Facility != entry.Definition.Facility || !managed.Paused)
            {
                entry.ManagedRequest = EntityId.Invalid;
                entry.Suspended = true;
                entry.Result = OperationsRuleResult.ManualOverride;
                return;
            }
            if (!_recipes.TryGet(managed.RecipeId, out ProductionRecipeDefinition? recipe) ||
                !_inventories.Contains(facility.OutputInventory)) return;
            foreach (ProductionIngredient output in recipe.Outputs)
                if (!_inventories.CanAdd(facility.OutputInventory, output.ResourceId, output.Quantity))
                    return;
            if (!Ready(entry, context.Tick)) return;
            var resume = PlayerProductionActionCommand.SetPaused(
                entry.Definition.Owner, entry.ManagedRequest, false, context.Tick);
            resume.Execute(context);
            entry.Result = resume.Accepted ? OperationsRuleResult.Recovered : OperationsRuleResult.Rejected;
            if (resume.Accepted) { Fired(entry, context.Tick); entry.ManagedRequest = EntityId.Invalid; }
            return;
        }

        if (facility.BlockReason != ProductionBlockReason.OutputFull ||
            !facility.ActiveRequest.IsValid ||
            !context.Entities.TryGetComponent(facility.ActiveRequest, out ProductionRequest request) ||
            request.Paused)
        {
            entry.HeldTicks = 0;
            entry.Result = OperationsRuleResult.WaitingForTrigger;
            return;
        }
        if (++entry.HeldTicks < HysteresisTicks || !Ready(entry, context.Tick)) return;
        if (!_recipes.TryGet(request.RecipeId, out ProductionRecipeDefinition? checkRecipe) ||
            !_inventories.Contains(facility.OutputInventory)) return;
        // Check that the observed block is still physically real.
        if (checkRecipe.Outputs.All(output =>
                _inventories.CanAdd(facility.OutputInventory, output.ResourceId, output.Quantity)))
        { entry.HeldTicks = 0; return; }

        var pause = PlayerProductionActionCommand.SetPaused(
            entry.Definition.Owner, facility.ActiveRequest, true, context.Tick);
        pause.Execute(context);
        entry.Result = pause.Accepted ? OperationsRuleResult.Applied : OperationsRuleResult.Rejected;
        if (pause.Accepted) { Fired(entry, context.Tick); entry.ManagedRequest = facility.ActiveRequest; }
    }

    private static void Fired(Entry entry, SimulationTick tick)
    {
        entry.LastFired = tick;
        entry.ActionCount++;
        entry.HeldTicks = 0;
    }

    private static bool Ready(Entry entry, SimulationTick tick)
    {
        if (entry.ActionCount >= MaximumActionsPerRule)
        {
            entry.Result = OperationsRuleResult.ActionLimit;
            return false;
        }
        if (entry.LastFired != SimulationTick.Zero &&
            tick.Value - entry.LastFired.Value < entry.Definition.CooldownTicks)
        {
            entry.Result = OperationsRuleResult.Cooldown;
            return false;
        }
        return true;
    }

    private static void ReleaseManagedPause(SimulationContext context, Entry entry)
    {
        if (!entry.ManagedRequest.IsValid ||
            !context.Entities.TryGetComponent(entry.ManagedRequest, out ProductionRequest request) ||
            request.Facility != entry.Definition.Facility || !request.Paused) return;
        var resume = PlayerProductionActionCommand.SetPaused(
            entry.Definition.Owner, entry.ManagedRequest, false, context.Tick);
        resume.Execute(context);
        entry.ManagedRequest = EntityId.Invalid;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _simulation.CommandSubmitted -= OnCommandSubmitted;
    }
}
