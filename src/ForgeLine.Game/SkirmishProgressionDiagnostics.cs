using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed record SkirmishInputDiagnostic(
    string Resource, double Required, double Quantity,
    double Available, double Reserved, double Missing);

public sealed record SkirmishProductionDiagnostic(
    string Entity, string ActiveUnit, string Status, string BlockReason,
    bool InputsReserved, IReadOnlyList<SkirmishInputDiagnostic> Inputs);

public sealed record SkirmishUnitDecisionDiagnostic(
    string Entity, string Unit, string Position, bool Eligible, string Exclusions,
    bool HasReadiness, double Readiness, double FuelFraction, double AmmunitionFraction,
    double MovementFuel, double FuelPerMeter, string CombatOrder, string TacticalStatus,
    string MovementStatus, string NavigationFailure, string ResupplyProvider);

public sealed record SkirmishProviderDiagnostic(
    string Entity, string Position, bool Enabled, string DepotState,
    double CargoFuel, double CargoAmmunition, double MovementFuel, double FuelPerMeter,
    string MovementTarget, IReadOnlyList<string> Recipients);

public sealed record SkirmishObjectiveDiagnostic(
    string EnemyCommandCore,
    double EnemyCommandCoreHealth,
    double EnemyCommandCoreMaximumHealth,
    bool EnemyCommandCoreDetected,
    bool EnemyCommandCoreIdentified,
    double ClosestCombatUnitDistanceMeters);

public sealed record SkirmishDecisionDiagnostic(
    ulong Tick, ulong Player, string StrategicState, string Goal,
    int CombatCandidates, int EligibleAttackers, int AttackGroupCapacity, int MinimumAttackers,
    int ScoutExclusions, int ReadinessExclusions, int FuelExclusions, int AmmunitionExclusions,
    int ActiveResupplyOrders, int OmittedUnitDetails, int OmittedFacilityDetails, int OmittedProviderDetails,
    IReadOnlyList<SkirmishUnitDecisionDiagnostic> Units,
    IReadOnlyList<SkirmishProductionDiagnostic> Production,
    IReadOnlyList<SkirmishProviderDiagnostic> Providers,
    SkirmishEconomyDiagnostic Economy)
{
    public SkirmishObjectiveDiagnostic? Objective { get; init; }
}

public sealed record SkirmishEligibilityLossDiagnostic(
    SkirmishDecisionDiagnostic Before, SkirmishDecisionDiagnostic After);

/// <summary>
/// Read-only, opt-in observation at the end of AiDecisions, after strategic and
/// automatic resupply decisions but before movement, supply, and production.
/// Readiness is still the value available to the strategic decision in this tick.
/// This observer never issues commands or changes authoritative state.
/// </summary>
public sealed class SkirmishProgressionDiagnostics : ISimulationSystem
{
    public const int MaximumHistoryEntries = 128;
    public const int MaximumUnitDetails = 128;
    public const int MaximumFacilityDetails = 32;
    public const int MaximumProviderDetails = 64;
    private const int RetainedOpeningEntries = 32;
    private const ulong PeriodicCaptureTicks = 1_000;

    private readonly InventoryStore _inventories;
    private readonly UnitDefinitionCatalog _units;
    private readonly IReadOnlyDictionary<PlayerId, SkirmishOpponentConfiguration> _configurations;
    private readonly FactionIntelligenceStore? _intelligence;
    private readonly List<SkirmishDecisionDiagnostic> _history = new(MaximumHistoryEntries);
    private readonly List<SkirmishEligibilityLossDiagnostic> _firstLosses = new();
    private readonly Dictionary<PlayerId, SkirmishDecisionDiagnostic> _latest = new();
    private readonly Dictionary<PlayerId, (string Key, ulong Tick)> _recorded = new();

    public SkirmishProgressionDiagnostics(
        InventoryStore inventories,
        UnitDefinitionCatalog units,
        IReadOnlyDictionary<PlayerId, SkirmishOpponentConfiguration> configurations,
        FactionIntelligenceStore? intelligence = null)
    {
        _inventories = inventories ?? throw new ArgumentNullException(nameof(inventories));
        _units = units ?? throw new ArgumentNullException(nameof(units));
        _configurations = configurations ?? throw new ArgumentNullException(nameof(configurations));
        _intelligence = intelligence;
    }

    public SimulationPhase Phase => SimulationPhase.AiDecisions;

    public IReadOnlyList<SkirmishDecisionDiagnostic> History => _history;

    public IReadOnlyList<SkirmishEligibilityLossDiagnostic> FirstEligibilityLosses => _firstLosses;

    public IReadOnlyList<SkirmishDecisionDiagnostic> Latest =>
        _latest.Values.OrderBy(static entry => entry.Player).ToArray();

    public ulong ObservedDecisions { get; private set; }

    public ulong DroppedHistoryEntries { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (EntityId entity in context.Entities.Query<SkirmishOpponentController, SkirmishOpponentState>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            SkirmishOpponentState state = context.Entities.GetComponent<SkirmishOpponentState>(entity);
            if (state.LastDecisionTick != context.Tick)
            {
                continue;
            }

            SkirmishOpponentController controller = context.Entities.GetComponent<SkirmishOpponentController>(entity);
            SkirmishOpponentConfiguration configuration = _configurations[controller.Player];
            SkirmishDecisionDiagnostic current = Capture(context, controller.Player, state, configuration);
            ObservedDecisions++;

            if (_latest.TryGetValue(controller.Player, out SkirmishDecisionDiagnostic? previous) &&
                previous.EligibleAttackers >= previous.MinimumAttackers &&
                current.EligibleAttackers < current.MinimumAttackers &&
                !_firstLosses.Any(loss => loss.After.Player == current.Player))
            {
                _firstLosses.Add(new SkirmishEligibilityLossDiagnostic(previous, current));
            }

            _latest[controller.Player] = current;
            string key = BuildTransitionKey(current);
            if (!_recorded.TryGetValue(controller.Player, out var recorded) ||
                recorded.Key != key || current.Tick - recorded.Tick >= PeriodicCaptureTicks)
            {
                if (_history.Count == MaximumHistoryEntries)
                {
                    // Retain the opening and a rolling tail. The first eligibility
                    // loss survives separately even when intermediate samples expire.
                    _history.RemoveAt(RetainedOpeningEntries);
                    DroppedHistoryEntries++;
                }

                _history.Add(current);
                _recorded[controller.Player] = (key, current.Tick);
            }
        }
    }

    private SkirmishDecisionDiagnostic Capture(
        SimulationContext context,
        PlayerId owner,
        SkirmishOpponentState state,
        SkirmishOpponentConfiguration configuration)
    {
        var units = new List<SkirmishUnitDecisionDiagnostic>();
        int combat = 0;
        int eligible = 0;
        int scouts = 0;
        int readinessExcluded = 0;
        int fuelExcluded = 0;
        int ammunitionExcluded = 0;
        int activeResupply = 0;

        EntityId reconReserve =
            EntityId.Invalid;

        foreach (EntityId candidate in
                 context.Entities.Query<
                     ControllableEntity,
                     UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (context.Entities
                    .GetComponent<ControllableEntity>(
                        candidate)
                    .Owner != owner ||
                context.Entities
                    .GetComponent<UnitIdentity>(
                        candidate)
                    .UnitId !=
                    UnitIds.ScoutVehicle ||
                !context.Entities.HasComponent<Combatant>(
                    candidate) ||
                !context.Entities.HasComponent<HealthState>(
                    candidate))
            {
                continue;
            }

            reconReserve = candidate;
            break;
        }

        foreach (EntityId entity in context.Entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (context.Entities.GetComponent<ControllableEntity>(entity).Owner != owner)
            {
                continue;
            }

            bool hasOrder = context.Entities.TryGetComponent(entity, out ResupplyOrder resupply);
            if (hasOrder)
            {
                activeResupply++;
            }

            UnitId unit = context.Entities.GetComponent<UnitIdentity>(entity).UnitId;
            if (unit == UnitIds.CargoTruck || unit == UnitIds.SupplyTruck ||
                !context.Entities.HasComponent<Combatant>(entity) ||
                !context.Entities.HasComponent<HealthState>(entity))
            {
                continue;
            }

            combat++;
            bool hasReadiness = context.Entities.TryGetComponent(entity, out UnitCombatReadiness readiness);
            bool scoutReserved =
                entity == reconReserve;
            bool lowReadiness = hasReadiness && readiness.OverallReadiness < configuration.OffensiveReadinessThreshold;
            bool lowFuel = hasReadiness && readiness.Fuel < configuration.OffensiveFuelThreshold;
            bool lowAmmunition = hasReadiness && readiness.Ammunition < configuration.ResupplyThreshold;
            bool canAttack =
                !scoutReserved &&
                !lowReadiness &&
                !lowFuel &&
                !lowAmmunition;
            scouts += scoutReserved ? 1 : 0;
            readinessExcluded += !scoutReserved && lowReadiness ? 1 : 0;
            fuelExcluded += !scoutReserved && lowFuel ? 1 : 0;
            ammunitionExcluded += !scoutReserved && lowAmmunition ? 1 : 0;
            eligible += canAttack ? 1 : 0;

            if (units.Count == MaximumUnitDetails)
            {
                continue;
            }

            var exclusions = new List<string>();
            if (scoutReserved)
            {
                exclusions.Add("Scout");
            }
            else
            {
                if (lowReadiness)
                {
                    exclusions.Add("Readiness");
                }

                if (lowFuel)
                {
                    exclusions.Add("Fuel");
                }

                if (lowAmmunition)
                {
                    exclusions.Add("Ammunition");
                }
            }

            bool hasFuel = context.Entities.TryGetComponent(entity, out UnitFuelState fuel);
            units.Add(new SkirmishUnitDecisionDiagnostic(
                entity.ToString(), unit.ToString(), Position(context, entity), canAttack,
                string.Join("|", exclusions), hasReadiness,
                hasReadiness ? readiness.OverallReadiness : 1.0,
                hasReadiness ? readiness.Fuel : 1.0,
                hasReadiness ? readiness.Ammunition : 1.0,
                hasFuel ? Stock(fuel.InventoryId, ResourceIds.Fuel, available: true) : 0.0,
                hasFuel ? fuel.ConsumptionPerMeter : 0.0,
                context.Entities.TryGetComponent(entity, out CombatOrderState order) ? order.Kind.ToString() : "None",
                context.Entities.TryGetComponent(entity, out TacticalCombatState tactical) ? tactical.Status.ToString() : "None",
                context.Entities.TryGetComponent(entity, out GroundMovementState movement) ? movement.Status.ToString() : "None",
                context.Entities.TryGetComponent(entity, out NavigationFailureState failure) ? failure.ToString() : "None",
                hasOrder ? resupply.Provider.ToString() : "None"));
        }

        var production = new List<SkirmishProductionDiagnostic>();
        int facilityCount = 0;
        foreach (EntityId entity in context.Entities.Query<UnitProductionFacility>(QueryIterationOrder.StableByEntityIndex))
        {
            UnitProductionFacility facility = context.Entities.GetComponent<UnitProductionFacility>(entity);
            if (facility.Owner != owner)
            {
                continue;
            }

            facilityCount++;
            if (production.Count == MaximumFacilityDetails)
            {
                continue;
            }

            var inputs = new List<SkirmishInputDiagnostic>();
            if (_units.TryGet(facility.ActiveUnit, out UnitDefinition? definition))
            {
                foreach (UnitResourceCost cost in definition.Costs)
                {
                    double quantity = Stock(facility.InputInventory, cost.ResourceId, available: false);
                    double available = Stock(facility.InputInventory, cost.ResourceId, available: true);
                    inputs.Add(new SkirmishInputDiagnostic(
                        cost.ResourceId.ToString(), cost.Quantity, quantity, available,
                        Math.Max(0.0, quantity - available),
                        facility.InputsReserved ? 0.0 : Math.Max(0.0, cost.Quantity - available)));
                }
            }

            production.Add(new SkirmishProductionDiagnostic(
                entity.ToString(), facility.ActiveUnit.ToString(), facility.Status.ToString(),
                facility.BlockReason.ToString(), facility.InputsReserved, inputs));
        }

        var providers = new List<SkirmishProviderDiagnostic>();
        int providerCount = 0;
        foreach (EntityId entity in context.Entities.Query<SupplyProvider>(QueryIterationOrder.StableByEntityIndex))
        {
            SupplyProvider provider = context.Entities.GetComponent<SupplyProvider>(entity);
            if (provider.Owner != owner)
            {
                continue;
            }

            providerCount++;
            if (providers.Count == MaximumProviderDetails)
            {
                continue;
            }

            var recipients = new List<string>();
            foreach (EntityId recipient in context.Entities.Query<ResupplyOrder>(QueryIterationOrder.StableByEntityIndex))
            {
                if (context.Entities.GetComponent<ResupplyOrder>(recipient).Provider == entity &&
                    recipients.Count < MaximumUnitDetails)
                {
                    recipients.Add(recipient.ToString());
                }
            }

            bool hasFuel = context.Entities.TryGetComponent(entity, out UnitFuelState fuel);
            providers.Add(new SkirmishProviderDiagnostic(
                entity.ToString(), Position(context, entity), provider.Enabled,
                context.Entities.TryGetComponent(entity, out SupplyDepot depot) ? depot.State.ToString() : "NotDepot",
                Stock(provider.InventoryId, ResourceIds.Fuel, available: true),
                Stock(provider.InventoryId, ResourceIds.Ammunition, available: true),
                hasFuel ? Stock(fuel.InventoryId, ResourceIds.Fuel, available: true) : 0.0,
                hasFuel ? fuel.ConsumptionPerMeter : 0.0,
                TacticalCommandUtilities.TryGetMovementIntent(context, entity, out MovementOrder intent)
                    ? intent.WorldTarget.ToString() : "None",
                recipients));
        }

        return new SkirmishDecisionDiagnostic(
            context.Tick.Value, owner.Value, state.StrategicState.ToString(), state.ActiveGoal.ToString(),
            combat, eligible, Math.Min(eligible, configuration.MaximumAttackUnits), configuration.MinimumAttackUnits,
            scouts, readinessExcluded, fuelExcluded, ammunitionExcluded, activeResupply,
            Math.Max(0, combat - units.Count), Math.Max(0, facilityCount - production.Count),
            Math.Max(0, providerCount - providers.Count), units, production, providers,
            SkirmishIndustryDiagnostics.Capture(context, _inventories, owner))
        {
            Objective =
                CaptureObjective(
                    context,
                    owner)
        };
    }

    private SkirmishObjectiveDiagnostic? CaptureObjective(
        SimulationContext context,
        PlayerId owner)
    {
        EntityId enemyCore =
            EntityId.Invalid;

        foreach (EntityId objectiveEntity in
                 context.Entities.Query<CommandCoreObjective>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CommandCoreObjective objective =
                context.Entities.GetComponent<CommandCoreObjective>(
                    objectiveEntity);

            if (objective.Owner != owner)
            {
                enemyCore =
                    objective.CommandCore;
                break;
            }
        }

        if (!enemyCore.IsValid ||
            !context.Entities.IsAlive(
                enemyCore) ||
            !context.Entities.TryGetComponent(
                enemyCore,
                out HealthState health))
        {
            return null;
        }

        FactionId observingFaction =
            owner.Value <= uint.MaxValue
                ? new FactionId(
                    checked((uint)owner.Value))
                : FactionId.None;
        bool detected =
            _intelligence is not null &&
            observingFaction.IsSpecified &&
            _intelligence.IsEntityCurrentlyDetected(
                observingFaction,
                enemyCore);
        bool identified =
            _intelligence is not null &&
            observingFaction.IsSpecified &&
            _intelligence.IsEntityCurrentlyIdentified(
                observingFaction,
                enemyCore);

        double closestDistance =
            double.PositiveInfinity;

        if (context.Entities.TryGetComponent(
                enemyCore,
                out WorldTransform coreTransform))
        {
            foreach (EntityId entity in
                     context.Entities.Query<
                         ControllableEntity,
                         UnitIdentity,
                         WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
            {
                if (context.Entities.GetComponent<ControllableEntity>(
                        entity).Owner != owner ||
                    !context.Entities.HasComponent<Combatant>(
                        entity) ||
                    !context.Entities.HasComponent<HealthState>(
                        entity))
                {
                    continue;
                }

                UnitId unitId =
                    context.Entities.GetComponent<UnitIdentity>(
                        entity).UnitId;

                if (unitId is var value &&
                    (value == UnitIds.CargoTruck ||
                     value == UnitIds.SupplyTruck))
                {
                    continue;
                }

                WorldTransform transform =
                    context.Entities.GetComponent<WorldTransform>(
                        entity);
                float deltaX =
                    transform.Position.X -
                    coreTransform.Position.X;
                float deltaZ =
                    transform.Position.Z -
                    coreTransform.Position.Z;
                double distance =
                    Math.Sqrt(
                        deltaX * deltaX +
                        deltaZ * deltaZ);

                closestDistance =
                    Math.Min(
                        closestDistance,
                        distance);
            }
        }

        return new SkirmishObjectiveDiagnostic(
            enemyCore.ToString(),
            health.Current,
            health.Maximum,
            detected,
            identified,
            closestDistance);
    }

    private double Stock(InventoryId inventory, ResourceId resource, bool available) =>
        !_inventories.Contains(inventory) ? 0.0 : available
            ? _inventories.GetAvailableQuantity(inventory, resource)
            : _inventories.GetQuantity(inventory, resource);

    private static string Position(SimulationContext context, EntityId entity) =>
        context.Entities.TryGetComponent(entity, out WorldTransform transform)
            ? transform.Position.ToString() : "Unavailable";

    private static string BuildTransitionKey(SkirmishDecisionDiagnostic entry) =>
        $"{entry.StrategicState}/{entry.Goal}/{entry.CombatCandidates}/{entry.EligibleAttackers}/" +
        $"{entry.ScoutExclusions}/{entry.ReadinessExclusions}/{entry.FuelExclusions}/{entry.AmmunitionExclusions}/" +
        $"{entry.ActiveResupplyOrders}/" +
        string.Join(";", entry.Production.Select(static facility =>
            $"{facility.Entity}:{facility.ActiveUnit}:{facility.Status}:" +
            string.Join(",", facility.Inputs.Where(static input => input.Missing > 0.0).Select(static input => input.Resource)))) +
        "/" + string.Join(";", entry.Providers.Select(static provider =>
            $"{provider.Entity}:{provider.Enabled}:{provider.DepotState}:{provider.CargoFuel > 0.0}:" +
            $"{provider.CargoAmmunition > 0.0}:{provider.MovementFuel > 0.0}:{provider.Recipients.Count}")) +
        "/" + string.Join(";", entry.Economy.Industry.Select(static facility =>
            $"{facility.Entity}:{facility.Status}:{facility.BlockReason}:{facility.PowerState}")) +
        "/" + string.Join(";", entry.Economy.Extractors.Select(static extractor =>
            $"{extractor.Entity}:{extractor.State}:{extractor.RemainingQuantity > 0.0}"));
}
