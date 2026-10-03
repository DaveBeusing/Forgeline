using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;

namespace ForgeLine.Game;

public static class GameplayMetricNames
{
    public const string ResourceIncomeQuantity =
        "economy.resource_income.quantity";
    public const string ProcessingOutputQuantity =
        "economy.processing.output.quantity";
    public const string ProcessingThroughput =
        "economy.processing.throughput_per_second";
    public const string ProductionUtilization =
        "economy.production.utilization";
    public const string PowerGenerationAverage =
        "power.generation.average";
    public const string PowerDemandAverage =
        "power.demand.average";
    public const string PowerShortageDuration =
        "power.shortage.duration_seconds";
    public const string StorageUtilizationAverage =
        "storage.utilization.average";
    public const string StorageUtilizationPeak =
        "storage.utilization.peak";
    public const string CargoDeliveredQuantity =
        "logistics.cargo.delivered.quantity";
    public const string CargoCompletedOrders =
        "logistics.cargo.completed_orders";
    public const string CargoRouteFailures =
        "logistics.cargo.route_failures";
    public const string CargoFailedTransports =
        "logistics.cargo.failed_transports";
    public const string CargoTravelAverage =
        "logistics.cargo.travel_time.average_ticks";
    public const string CargoTravelMaximum =
        "logistics.cargo.travel_time.maximum_ticks";
    public const string DistributionCompletedRequests =
        "logistics.distribution.completed_requests";
    public const string DistributionFailedRequests =
        "logistics.distribution.failed_requests";
    public const string SupplyFuelTransferred =
        "supply.fuel.transferred.quantity";
    public const string SupplyAmmunitionTransferred =
        "supply.ammunition.transferred.quantity";
    public const string SupplyShortageDuration =
        "supply.shortage.duration_seconds";
    public const string UnitProductionCompleted =
        "units.production.completed";
    public const string UnitLosses =
        "units.losses";
    public const string CombatDamageApplied =
        "combat.damage.applied";
    public const string CombatDestructions =
        "combat.destructions";
    public const string ExpansionCompletedBuildings =
        "expansion.completed_buildings";
    public const string MatchDurationTicks =
        "match.duration.ticks";
    public const string MatchDurationSeconds =
        "match.duration.seconds";
    public const string MatchTerminal =
        "match.terminal";
    public const string MatchWinner =
        "match.winner";
}

public static class GameplayMilestoneNames
{
    public const string FirstExtractorExpansion =
        "progression.first_extractor_expansion";
    public const string FirstSupplyDepotExpansion =
        "progression.first_supply_depot_expansion";
    public const string FirstScoutProduced =
        "progression.first_scout_produced";
    public const string FirstMainBattleTankProduced =
        "progression.first_main_battle_tank_produced";
    public const string FirstArtilleryProduced =
        "progression.first_artillery_produced";
    public const string FirstOffensiveCommitment =
        "progression.first_offensive_commitment";
}

public sealed record GameplayMetric(
    string Name,
    string Owner,
    string Dimension,
    double Value,
    string Unit);

public sealed record GameplayMilestone(
    string Name,
    string Owner,
    string Dimension,
    ulong Tick);

public sealed record GameplaySupplyDebugSummary(
    int SuppliedUnits,
    int LowSupplyUnits,
    int CriticalUnits,
    int UnsuppliedUnits,
    int Providers,
    double FuelTransferred,
    double AmmunitionTransferred);

public sealed record GameplayProductionDebugSummary(
    int ProcessingFacilities,
    int RunningProcessingFacilities,
    int BlockedProcessingFacilities,
    long CompletedProcessingCycles,
    int UnitProductionFacilities,
    int RunningUnitProductionFacilities,
    int BlockedUnitProductionFacilities,
    long CompletedUnits);

public sealed record GameplayFrontDebugSummary(
    ulong Player,
    int CombatUnits,
    Vector3 Centroid);

public sealed record GameplayObjectiveDebugSummary(
    ulong Player,
    string StrategicState,
    string ActiveGoal);

public sealed record GameplayTelemetryDebugSummary(
    GameplaySupplyDebugSummary Supply,
    GameplayProductionDebugSummary Production,
    IReadOnlyList<GameplayFrontDebugSummary> Fronts,
    IReadOnlyList<GameplayObjectiveDebugSummary> Objectives);

public sealed record GameplayTelemetrySnapshot(
    int SchemaVersion,
    ulong ObservedTicks,
    int TickRate,
    IReadOnlyList<GameplayMetric> Metrics,
    IReadOnlyList<GameplayMilestone> Milestones,
    GameplayTelemetryDebugSummary Debug);

public sealed class GameplayTelemetryCollector
{
    public const int CurrentSchemaVersion = 1;

    private const string MatchOwner = "match";
    private readonly VerticalSliceScenario _scenario;
    private readonly Dictionary<EntityId, DepositObservation> _deposits =
        new();
    private readonly Dictionary<EntityId, UnitObservation> _units =
        new();
    private readonly Dictionary<EntityId, double> _productionOutputs =
        new();
    private readonly HashSet<EntityId> _knownBuildings =
        new();
    private readonly Dictionary<EntityId, CargoOrderObservation> _cargoOrders =
        new();
    private readonly Dictionary<MetricDimensionKey, double> _resourceIncome =
        new();
    private readonly Dictionary<string, double> _processingOutput =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _processingFacilityTicks =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _processingRunningTicks =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _powerGenerationTicks =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _powerDemandTicks =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _powerShortageTicks =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _storageUtilizationSum =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _storageSamples =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _storageUtilizationPeak =
        new(StringComparer.Ordinal);
    private readonly Dictionary<MetricDimensionKey, long> _producedUnits =
        new();
    private readonly Dictionary<MetricDimensionKey, long> _lostUnits =
        new();
    private readonly Dictionary<string, long> _expandedBuildings =
        new(StringComparer.Ordinal);
    private readonly Dictionary<MilestoneKey, GameplayMilestone> _milestones =
        new();

    private ulong _observedTicks;
    private ulong _supplyShortageTicks;
    private ulong _completedCargoTravelSamples;
    private double _totalCargoTravelTicks;
    private ulong _maximumCargoTravelTicks;

    public GameplayTelemetryCollector(VerticalSliceScenario scenario)
    {
        _scenario =
            scenario ??
            throw new ArgumentNullException(nameof(scenario));

        CaptureInitialDeposits();
        CaptureInitialUnits();
        CaptureInitialBuildings();
        CaptureInitialProduction();
    }

    public ulong ObservedTicks => _observedTicks;

    public void Observe()
    {
        _observedTicks++;

        ObserveExtraction();
        ObserveProcessing();
        ObservePower();
        ObserveStorage();
        ObserveCargoTravel();
        ObserveUnits();
        ObserveBuildings();
        ObserveSupplyShortage();
        ObserveObjectives();
    }

    public GameplayTelemetrySnapshot Capture()
    {
        var metrics =
            new List<GameplayMetric>();

        AddResourceIncomeMetrics(metrics);
        AddProcessingMetrics(metrics);
        AddPowerMetrics(metrics);
        AddStorageMetrics(metrics);
        AddLogisticsMetrics(metrics);
        AddUnitMetrics(metrics);
        AddCombatMetrics(metrics);
        AddExpansionMetrics(metrics);
        AddMatchMetrics(metrics);

        metrics.Sort(
            static (left, right) =>
            {
                int name =
                    string.CompareOrdinal(
                        left.Name,
                        right.Name);

                if (name != 0)
                {
                    return name;
                }

                int owner =
                    string.CompareOrdinal(
                        left.Owner,
                        right.Owner);

                return owner != 0
                    ? owner
                    : string.CompareOrdinal(
                        left.Dimension,
                        right.Dimension);
            });

        GameplayMilestone[] milestones =
            _milestones.Values
                .OrderBy(
                    static value =>
                        value.Name,
                    StringComparer.Ordinal)
                .ThenBy(
                    static value =>
                        value.Owner,
                    StringComparer.Ordinal)
                .ThenBy(
                    static value =>
                        value.Dimension,
                    StringComparer.Ordinal)
                .ToArray();

        return new GameplayTelemetrySnapshot(
            CurrentSchemaVersion,
            _observedTicks,
            _scenario.Simulation.Clock.TicksPerSecond,
            metrics,
            milestones,
            CaptureDebugSummary());
    }

    private void CaptureInitialDeposits()
    {
        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<ResourceDeposit>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ResourceDeposit deposit =
                _scenario.Simulation.Entities.GetComponent<ResourceDeposit>(
                    entity);

            _deposits.Add(
                entity,
                new DepositObservation(
                    deposit.ResourceId,
                    deposit.RemainingQuantity));
        }
    }

    private void CaptureInitialUnits()
    {
        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<
                     ControllableEntity,
                     UnitIdentity>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            ControllableEntity controllable =
                _scenario.Simulation.Entities.GetComponent<ControllableEntity>(
                    entity);
            UnitIdentity identity =
                _scenario.Simulation.Entities.GetComponent<UnitIdentity>(
                    entity);

            _units.Add(
                entity,
                new UnitObservation(
                    controllable.Owner,
                    identity.UnitId));
        }
    }

    private void CaptureInitialBuildings()
    {
        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _knownBuildings.Add(entity);
        }
    }

    private void CaptureInitialProduction()
    {
        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<ProductionFacility>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ProductionFacility facility =
                _scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                    entity);

            _productionOutputs[entity] =
                facility.TotalOutputQuantity;
        }
    }

    private void ObserveExtraction()
    {
        var owners =
            new Dictionary<EntityId, string>();

        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<ResourceExtractor>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ResourceExtractor extractor =
                _scenario.Simulation.Entities.GetComponent<ResourceExtractor>(
                    entity);

            owners.TryAdd(
                extractor.Deposit,
                OwnerForFaction(extractor.Owner));
        }

        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<ResourceDeposit>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ResourceDeposit deposit =
                _scenario.Simulation.Entities.GetComponent<ResourceDeposit>(
                    entity);

            if (!_deposits.TryGetValue(
                    entity,
                    out DepositObservation previous))
            {
                _deposits.Add(
                    entity,
                    new DepositObservation(
                        deposit.ResourceId,
                        deposit.RemainingQuantity));
                continue;
            }

            double extracted =
                Math.Max(
                    0.0,
                    previous.RemainingQuantity -
                    deposit.RemainingQuantity);

            if (extracted > 0.0)
            {
                string resource =
                    ResourceDimension(
                        deposit.ResourceId);
                string owner =
                    owners.TryGetValue(
                        entity,
                        out string? value)
                        ? value
                        : MatchOwner;

                Add(
                    _resourceIncome,
                    new MetricDimensionKey(
                        owner,
                        resource),
                    extracted);
                Add(
                    _resourceIncome,
                    new MetricDimensionKey(
                        MatchOwner,
                        resource),
                    extracted);
            }

            _deposits[entity] =
                new DepositObservation(
                    deposit.ResourceId,
                    deposit.RemainingQuantity);
        }
    }

    private void ObserveProcessing()
    {
        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<ProductionFacility>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ProductionFacility facility =
                _scenario.Simulation.Entities.GetComponent<ProductionFacility>(
                    entity);
            string owner =
                ResolveBuildingOwner(entity);

            Add(
                _processingFacilityTicks,
                owner,
                1.0);
            Add(
                _processingFacilityTicks,
                MatchOwner,
                1.0);

            if (facility.Status ==
                ProductionStatus.Running)
            {
                Add(
                    _processingRunningTicks,
                    owner,
                    1.0);
                Add(
                    _processingRunningTicks,
                    MatchOwner,
                    1.0);
            }

            if (_productionOutputs.TryGetValue(
                    entity,
                    out double previousOutput))
            {
                double delta =
                    Math.Max(
                        0.0,
                        facility.TotalOutputQuantity -
                        previousOutput);

                if (delta > 0.0)
                {
                    Add(
                        _processingOutput,
                        owner,
                        delta);
                    Add(
                        _processingOutput,
                        MatchOwner,
                        delta);
                }
            }

            _productionOutputs[entity] =
                facility.TotalOutputQuantity;
        }
    }

    private void ObservePower()
    {
        double generation = 0.0;
        double demand = 0.0;
        bool shortage = false;

        foreach (PowerNetworkReadModel network in
                 _scenario.Power.Networks)
        {
            string owner =
                OwnerForNetwork(
                    network.NetworkId);

            Add(
                _powerGenerationTicks,
                owner,
                network.Generation);
            Add(
                _powerDemandTicks,
                owner,
                network.Demand);

            if (network.Deficit > 0.0)
            {
                Increment(
                    _powerShortageTicks,
                    owner);
                shortage = true;
            }

            generation +=
                network.Generation;
            demand +=
                network.Demand;
        }

        Add(
            _powerGenerationTicks,
            MatchOwner,
            generation);
        Add(
            _powerDemandTicks,
            MatchOwner,
            demand);

        if (shortage)
        {
            Increment(
                _powerShortageTicks,
                MatchOwner);
        }
    }

    private void ObserveStorage()
    {
        var byOwner =
            new Dictionary<string, StorageObservation>(
                StringComparer.Ordinal);

        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CompletedBuilding building =
                _scenario.Simulation.Entities.GetComponent<CompletedBuilding>(
                    entity);
            string owner =
                OwnerForPlayer(
                    building.Owner);
            var inventories =
                new HashSet<InventoryId>();

            if (_scenario.Simulation.Entities.TryGetComponent(
                    entity,
                    out InventoryStorage storage))
            {
                inventories.Add(
                    storage.InventoryId);
            }

            if (_scenario.Simulation.Entities.TryGetComponent(
                    entity,
                    out ProductionFacility production))
            {
                inventories.Add(
                    production.InputInventory);
                inventories.Add(
                    production.OutputInventory);
            }

            if (_scenario.Simulation.Entities.TryGetComponent(
                    entity,
                    out UnitProductionFacility unitProduction))
            {
                inventories.Add(
                    unitProduction.InputInventory);
            }

            foreach (InventoryId inventory in inventories)
            {
                if (!_scenario.Inventories.Contains(
                        inventory))
                {
                    continue;
                }

                double capacity =
                    _scenario.Inventories.GetTotalCapacity(
                        inventory);
                double quantity =
                    _scenario.Inventories.GetTotalQuantity(
                        inventory);

                if (!byOwner.TryGetValue(
                        owner,
                        out StorageObservation current))
                {
                    current =
                        default;
                }

                byOwner[owner] =
                    current with
                    {
                        Capacity =
                            current.Capacity +
                            capacity,
                        Quantity =
                            current.Quantity +
                            quantity
                    };
            }
        }

        double matchCapacity = 0.0;
        double matchQuantity = 0.0;

        foreach ((string owner, StorageObservation storage) in byOwner)
        {
            ObserveStorageUtilization(
                owner,
                storage);
            matchCapacity +=
                storage.Capacity;
            matchQuantity +=
                storage.Quantity;
        }

        ObserveStorageUtilization(
            MatchOwner,
            new StorageObservation(
                matchCapacity,
                matchQuantity));
    }

    private void ObserveStorageUtilization(
        string owner,
        in StorageObservation storage)
    {
        if (storage.Capacity <= 0.0)
        {
            return;
        }

        double utilization =
            Math.Clamp(
                storage.Quantity /
                storage.Capacity,
                0.0,
                1.0);

        Add(
            _storageUtilizationSum,
            owner,
            utilization);
        Increment(
            _storageSamples,
            owner);

        if (!_storageUtilizationPeak.TryGetValue(
                owner,
                out double peak) ||
            utilization > peak)
        {
            _storageUtilizationPeak[owner] =
                utilization;
        }
    }

    private void ObserveCargoTravel()
    {
        ulong currentTick =
            _scenario.Simulation.CurrentTick.Value;
        var active =
            new HashSet<EntityId>();

        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<CargoTransport>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (_scenario.Simulation.Entities.TryGetComponent(
                    entity,
                    out CargoTransportOrder order))
            {
                active.Add(entity);

                if (!_cargoOrders.TryGetValue(
                        entity,
                        out CargoOrderObservation observation) ||
                    observation.SubmittedAtTick !=
                        order.SubmittedAtTick.Value)
                {
                    _cargoOrders[entity] =
                        new CargoOrderObservation(
                            order.SubmittedAtTick.Value);
                }

                continue;
            }

            if (!_cargoOrders.Remove(
                    entity,
                    out CargoOrderObservation completed))
            {
                continue;
            }

            ulong duration =
                currentTick >=
                completed.SubmittedAtTick
                    ? currentTick -
                      completed.SubmittedAtTick
                    : 0;

            _completedCargoTravelSamples++;
            _totalCargoTravelTicks +=
                duration;
            _maximumCargoTravelTicks =
                Math.Max(
                    _maximumCargoTravelTicks,
                    duration);
        }

        EntityId[] stale =
            _cargoOrders.Keys
                .Where(
                    entity =>
                        !active.Contains(entity) &&
                        !_scenario.Simulation.Entities.IsAlive(entity))
                .ToArray();

        for (int index = 0;
             index < stale.Length;
             index++)
        {
            _cargoOrders.Remove(
                stale[index]);
        }
    }

    private void ObserveUnits()
    {
        var current =
            new Dictionary<EntityId, UnitObservation>();

        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<
                     ControllableEntity,
                     UnitIdentity>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            ControllableEntity controllable =
                _scenario.Simulation.Entities.GetComponent<ControllableEntity>(
                    entity);
            UnitIdentity identity =
                _scenario.Simulation.Entities.GetComponent<UnitIdentity>(
                    entity);
            var observation =
                new UnitObservation(
                    controllable.Owner,
                    identity.UnitId);

            current.Add(
                entity,
                observation);

            if (_units.ContainsKey(entity))
            {
                continue;
            }

            string owner =
                OwnerForPlayer(
                    observation.Owner);
            string unit =
                UnitDimension(
                    observation.UnitId);

            Increment(
                _producedUnits,
                new MetricDimensionKey(
                    owner,
                    unit));

            RecordUnitMilestone(
                owner,
                observation.UnitId);
        }

        foreach ((EntityId entity, UnitObservation previous) in _units)
        {
            if (current.ContainsKey(entity))
            {
                continue;
            }

            Increment(
                _lostUnits,
                new MetricDimensionKey(
                    OwnerForPlayer(
                        previous.Owner),
                    UnitDimension(
                        previous.UnitId)));
        }

        _units.Clear();

        foreach ((EntityId entity, UnitObservation observation) in current)
        {
            _units.Add(
                entity,
                observation);
        }
    }

    private void ObserveBuildings()
    {
        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (!_knownBuildings.Add(entity))
            {
                continue;
            }

            CompletedBuilding building =
                _scenario.Simulation.Entities.GetComponent<CompletedBuilding>(
                    entity);
            string owner =
                OwnerForPlayer(
                    building.Owner);

            Increment(
                _expandedBuildings,
                owner);

            if (building.BuildingId ==
                BuildingIds.Extractor)
            {
                RecordMilestone(
                    GameplayMilestoneNames.FirstExtractorExpansion,
                    owner,
                    "building.directorate.extractor");
            }
            else if (building.BuildingId ==
                     BuildingIds.SupplyDepot)
            {
                RecordMilestone(
                    GameplayMilestoneNames.FirstSupplyDepotExpansion,
                    owner,
                    "building.directorate.supply_depot");
            }
        }
    }

    private void ObserveSupplyShortage()
    {
        BattlefieldSupplyMetrics metrics =
            _scenario.BattlefieldSupply.Metrics;

        if (metrics.LowSupplyUnitCount +
            metrics.CriticalUnitCount +
            metrics.UnsuppliedUnitCount >
            0)
        {
            _supplyShortageTicks++;
        }
    }

    private void ObserveObjectives()
    {
        ObserveObjective(
            _scenario.West.Player);
        ObserveObjective(
            _scenario.East.Player);
    }

    private void ObserveObjective(PlayerId player)
    {
        SkirmishOpponentState state =
            _scenario.GetOpponentState(
                player);

        if (state.ActiveGoal ==
            SkirmishStrategicGoal.AttackObjective)
        {
            RecordMilestone(
                GameplayMilestoneNames.FirstOffensiveCommitment,
                OwnerForPlayer(player),
                "strategic_goal");
        }
    }

    private void RecordUnitMilestone(
        string owner,
        UnitId unitId)
    {
        string name =
            unitId == UnitIds.ScoutVehicle
                ? GameplayMilestoneNames.FirstScoutProduced
                : unitId == UnitIds.MainBattleTank
                    ? GameplayMilestoneNames.FirstMainBattleTankProduced
                    : unitId == UnitIds.MobileArtillery
                        ? GameplayMilestoneNames.FirstArtilleryProduced
                        : string.Empty;

        if (name.Length == 0)
        {
            return;
        }

        RecordMilestone(
            name,
            owner,
            UnitDimension(unitId));
    }

    private void RecordMilestone(
        string name,
        string owner,
        string dimension)
    {
        var key =
            new MilestoneKey(
                name,
                owner,
                dimension);

        if (_milestones.ContainsKey(key))
        {
            return;
        }

        _milestones.Add(
            key,
            new GameplayMilestone(
                name,
                owner,
                dimension,
                _scenario.Simulation.CurrentTick.Value));
    }

    private void AddResourceIncomeMetrics(
        ICollection<GameplayMetric> metrics)
    {
        foreach ((MetricDimensionKey key, double quantity) in
                 _resourceIncome)
        {
            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.ResourceIncomeQuantity,
                    key.Owner,
                    key.Dimension,
                    quantity,
                    "quantity"));
        }
    }

    private void AddProcessingMetrics(
        ICollection<GameplayMetric> metrics)
    {
        var owners =
            new HashSet<string>(
                _processingFacilityTicks.Keys,
                StringComparer.Ordinal);

        owners.UnionWith(
            _processingOutput.Keys);

        double logicalSeconds =
            LogicalSeconds();

        foreach (string owner in owners)
        {
            double output =
                Get(
                    _processingOutput,
                    owner);
            double facilityTicks =
                Get(
                    _processingFacilityTicks,
                    owner);
            double runningTicks =
                Get(
                    _processingRunningTicks,
                    owner);

            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.ProcessingOutputQuantity,
                    owner,
                    string.Empty,
                    output,
                    "quantity"));
            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.ProcessingThroughput,
                    owner,
                    string.Empty,
                    logicalSeconds > 0.0
                        ? output /
                          logicalSeconds
                        : 0.0,
                    "quantity_per_second"));
            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.ProductionUtilization,
                    owner,
                    string.Empty,
                    facilityTicks > 0.0
                        ? runningTicks /
                          facilityTicks
                        : 0.0,
                    "ratio"));
        }
    }

    private void AddPowerMetrics(
        ICollection<GameplayMetric> metrics)
    {
        var owners =
            new HashSet<string>(
                _powerGenerationTicks.Keys,
                StringComparer.Ordinal);

        owners.UnionWith(
            _powerDemandTicks.Keys);
        owners.UnionWith(
            _powerShortageTicks.Keys);

        foreach (string owner in owners)
        {
            double divisor =
                _observedTicks > 0
                    ? _observedTicks
                    : 1.0;

            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.PowerGenerationAverage,
                    owner,
                    string.Empty,
                    Get(
                        _powerGenerationTicks,
                        owner) /
                    divisor,
                    "power"));
            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.PowerDemandAverage,
                    owner,
                    string.Empty,
                    Get(
                        _powerDemandTicks,
                        owner) /
                    divisor,
                    "power"));
            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.PowerShortageDuration,
                    owner,
                    string.Empty,
                    TicksToSeconds(
                        Get(
                            _powerShortageTicks,
                            owner)),
                    "seconds"));
        }
    }

    private void AddStorageMetrics(
        ICollection<GameplayMetric> metrics)
    {
        var owners =
            new HashSet<string>(
                _storageSamples.Keys,
                StringComparer.Ordinal);

        owners.UnionWith(
            _storageUtilizationPeak.Keys);

        foreach (string owner in owners)
        {
            ulong samples =
                Get(
                    _storageSamples,
                    owner);

            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.StorageUtilizationAverage,
                    owner,
                    string.Empty,
                    samples > 0
                        ? Get(
                              _storageUtilizationSum,
                              owner) /
                          samples
                        : 0.0,
                    "ratio"));
            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.StorageUtilizationPeak,
                    owner,
                    string.Empty,
                    Get(
                        _storageUtilizationPeak,
                        owner),
                    "ratio"));
        }
    }

    private void AddLogisticsMetrics(
        ICollection<GameplayMetric> metrics)
    {
        CargoTransportMetrics cargo =
            _scenario.CargoTransport.Metrics;
        AutomatedDistributionMetrics distribution =
            _scenario.AutomatedDistribution.Metrics;
        BattlefieldSupplyMetrics supply =
            _scenario.BattlefieldSupply.Metrics;

        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.CargoDeliveredQuantity,
                MatchOwner,
                string.Empty,
                cargo.DeliveredQuantity,
                "quantity"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.CargoCompletedOrders,
                MatchOwner,
                string.Empty,
                cargo.CompletedOrderCount,
                "count"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.CargoRouteFailures,
                MatchOwner,
                string.Empty,
                cargo.RouteFailureCount,
                "count"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.CargoFailedTransports,
                MatchOwner,
                string.Empty,
                cargo.FailedTransportCount,
                "count"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.CargoTravelAverage,
                MatchOwner,
                string.Empty,
                _completedCargoTravelSamples > 0
                    ? _totalCargoTravelTicks /
                      _completedCargoTravelSamples
                    : 0.0,
                "ticks"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.CargoTravelMaximum,
                MatchOwner,
                string.Empty,
                _maximumCargoTravelTicks,
                "ticks"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.DistributionCompletedRequests,
                MatchOwner,
                string.Empty,
                distribution.CompletedRequestCount,
                "count"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.DistributionFailedRequests,
                MatchOwner,
                string.Empty,
                distribution.FailedRequestCount,
                "count"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.SupplyFuelTransferred,
                MatchOwner,
                string.Empty,
                supply.TotalFuelTransferred,
                "quantity"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.SupplyAmmunitionTransferred,
                MatchOwner,
                string.Empty,
                supply.TotalAmmunitionTransferred,
                "quantity"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.SupplyShortageDuration,
                MatchOwner,
                string.Empty,
                TicksToSeconds(
                    _supplyShortageTicks),
                "seconds"));
    }

    private void AddUnitMetrics(
        ICollection<GameplayMetric> metrics)
    {
        foreach ((MetricDimensionKey key, long count) in
                 _producedUnits)
        {
            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.UnitProductionCompleted,
                    key.Owner,
                    key.Dimension,
                    count,
                    "count"));
        }

        foreach ((MetricDimensionKey key, long count) in
                 _lostUnits)
        {
            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.UnitLosses,
                    key.Owner,
                    key.Dimension,
                    count,
                    "count"));
        }
    }

    private void AddCombatMetrics(
        ICollection<GameplayMetric> metrics)
    {
        CombatRuntimeMetrics combat =
            _scenario.Services.CombatRuntime.Metrics;

        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.CombatDamageApplied,
                MatchOwner,
                string.Empty,
                combat.TotalDamageApplied,
                "health"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.CombatDestructions,
                MatchOwner,
                string.Empty,
                combat.TotalDestructions,
                "count"));
    }

    private void AddExpansionMetrics(
        ICollection<GameplayMetric> metrics)
    {
        foreach ((string owner, long count) in
                 _expandedBuildings)
        {
            metrics.Add(
                new GameplayMetric(
                    GameplayMetricNames.ExpansionCompletedBuildings,
                    owner,
                    string.Empty,
                    count,
                    "count"));
        }
    }

    private void AddMatchMetrics(
        ICollection<GameplayMetric> metrics)
    {
        MatchState match =
            _scenario.GetMatchState();
        ulong duration =
            match.IsCompleted
                ? match.CompletedAtTick.Value
                : _scenario.Simulation.CurrentTick.Value;

        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.MatchDurationTicks,
                MatchOwner,
                string.Empty,
                duration,
                "ticks"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.MatchDurationSeconds,
                MatchOwner,
                string.Empty,
                duration /
                (double)_scenario.Simulation.Clock.TicksPerSecond,
                "seconds"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.MatchTerminal,
                MatchOwner,
                string.Empty,
                match.IsCompleted
                    ? 1.0
                    : 0.0,
                "boolean"));
        metrics.Add(
            new GameplayMetric(
                GameplayMetricNames.MatchWinner,
                MatchOwner,
                string.Empty,
                match.Winner.Value,
                "player_id"));
    }

    private GameplayTelemetryDebugSummary CaptureDebugSummary()
    {
        BattlefieldSupplyMetrics supply =
            _scenario.BattlefieldSupply.Metrics;
        ProductionMetrics processing =
            _scenario.Production.Metrics;
        UnitProductionMetrics units =
            _scenario.UnitProduction.Metrics;

        GameplayFrontDebugSummary[] fronts =
        [
            CaptureFront(
                _scenario.West.Player),
            CaptureFront(
                _scenario.East.Player)
        ];

        GameplayObjectiveDebugSummary[] objectives =
        [
            CaptureObjective(
                _scenario.West.Player),
            CaptureObjective(
                _scenario.East.Player)
        ];

        return new GameplayTelemetryDebugSummary(
            new GameplaySupplyDebugSummary(
                supply.SuppliedUnitCount,
                supply.LowSupplyUnitCount,
                supply.CriticalUnitCount,
                supply.UnsuppliedUnitCount,
                supply.ProviderCount,
                supply.TotalFuelTransferred,
                supply.TotalAmmunitionTransferred),
            new GameplayProductionDebugSummary(
                processing.FacilityCount,
                processing.RunningFacilityCount,
                processing.BlockedFacilityCount,
                processing.CompletedCycles,
                units.FacilityCount,
                units.RunningFacilityCount,
                units.BlockedFacilityCount,
                units.CompletedUnits),
            fronts,
            objectives);
    }

    private GameplayFrontDebugSummary CaptureFront(
        PlayerId player)
    {
        Vector3 sum =
            Vector3.Zero;
        int count = 0;

        foreach (EntityId entity in
                 _scenario.Simulation.Entities.Query<
                     ControllableEntity,
                     UnitIdentity,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            ControllableEntity controllable =
                _scenario.Simulation.Entities.GetComponent<ControllableEntity>(
                    entity);

            if (controllable.Owner != player ||
                !_scenario.Simulation.Entities.HasComponent<Combatant>(
                    entity))
            {
                continue;
            }

            WorldTransform transform =
                _scenario.Simulation.Entities.GetComponent<WorldTransform>(
                    entity);
            sum +=
                transform.Position;
            count++;
        }

        return new GameplayFrontDebugSummary(
            player.Value,
            count,
            count > 0
                ? sum /
                  count
                : Vector3.Zero);
    }

    private GameplayObjectiveDebugSummary CaptureObjective(
        PlayerId player)
    {
        SkirmishOpponentState state =
            _scenario.GetOpponentState(
                player);

        return new GameplayObjectiveDebugSummary(
            player.Value,
            state.StrategicState.ToString(),
            state.ActiveGoal.ToString());
    }

    private string ResolveBuildingOwner(EntityId entity)
    {
        if (_scenario.Simulation.Entities.TryGetComponent(
                entity,
                out CompletedBuilding building))
        {
            return OwnerForPlayer(
                building.Owner);
        }

        return MatchOwner;
    }

    private string ResourceDimension(ResourceId resource)
    {
        return _scenario.Services.Resources.TryGet(
                resource,
                out ResourceDefinition? definition)
            ? definition.Key
            : resource.ToString();
    }

    private string UnitDimension(UnitId unit)
    {
        return _scenario.Services.UnitDefinitions.TryGet(
                unit,
                out UnitDefinition? definition)
            ? definition.Key
            : unit.ToString();
    }

    private string OwnerForNetwork(PowerNetworkId network)
    {
        if (network.Value ==
            _scenario.West.Player.Value)
        {
            return OwnerForPlayer(
                _scenario.West.Player);
        }

        if (network.Value ==
            _scenario.East.Player.Value)
        {
            return OwnerForPlayer(
                _scenario.East.Player);
        }

        return $"network:{network.Value}";
    }

    private static string OwnerForPlayer(PlayerId player) =>
        $"player:{player.Value}";

    private static string OwnerForFaction(FactionId faction) =>
        faction.IsSpecified
            ? $"player:{faction.Value}"
            : MatchOwner;

    private double LogicalSeconds() =>
        _observedTicks /
        (double)_scenario.Simulation.Clock.TicksPerSecond;

    private double TicksToSeconds(ulong ticks) =>
        ticks /
        (double)_scenario.Simulation.Clock.TicksPerSecond;

    private static void Add<TKey>(
        IDictionary<TKey, double> values,
        TKey key,
        double value)
        where TKey : notnull
    {
        values.TryGetValue(
            key,
            out double existing);
        values[key] =
            existing +
            value;
    }

    private static void Increment<TKey>(
        IDictionary<TKey, long> values,
        TKey key)
        where TKey : notnull
    {
        values.TryGetValue(
            key,
            out long existing);
        values[key] =
            checked(
                existing +
                1);
    }

    private static void Increment<TKey>(
        IDictionary<TKey, ulong> values,
        TKey key)
        where TKey : notnull
    {
        values.TryGetValue(
            key,
            out ulong existing);
        values[key] =
            checked(
                existing +
                1);
    }

    private static double Get(
        IReadOnlyDictionary<string, double> values,
        string key) =>
        values.TryGetValue(
            key,
            out double value)
            ? value
            : 0.0;

    private static ulong Get(
        IReadOnlyDictionary<string, ulong> values,
        string key) =>
        values.TryGetValue(
            key,
            out ulong value)
            ? value
            : 0;

    private readonly record struct DepositObservation(
        ResourceId Resource,
        double RemainingQuantity);

    private readonly record struct UnitObservation(
        PlayerId Owner,
        UnitId UnitId);

    private readonly record struct CargoOrderObservation(
        ulong SubmittedAtTick);

    private readonly record struct StorageObservation(
        double Capacity,
        double Quantity);

    private readonly record struct MetricDimensionKey(
        string Owner,
        string Dimension);

    private readonly record struct MilestoneKey(
        string Name,
        string Owner,
        string Dimension);
}
