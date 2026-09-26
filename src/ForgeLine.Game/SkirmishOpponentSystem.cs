using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class SkirmishOpponentSystem : ISimulationSystem
{
    private static readonly ResourceId[] RawResources =
    [
        ResourceIds.FerrousOre,
        ResourceIds.Volatiles,
        ResourceIds.Silicates
    ];

    private static readonly BuildingId[] IndustrialPlan =
    [
        BuildingIds.StorageDepot,
        BuildingIds.Smelter,
        BuildingIds.Refinery,
        BuildingIds.ElectronicsPlant,
        BuildingIds.LogisticsHub,
        BuildingIds.SupplyDepot,
        BuildingIds.Barracks,
        BuildingIds.VehicleFactory,
        BuildingIds.AmmunitionPlant,
        BuildingIds.Radar
    ];

    private readonly BuildingDefinitionCatalog _buildings;
    private readonly UnitDefinitionCatalog _units;
    private readonly InventoryStore _inventories;
    private readonly BuildingPlacementService _placement;
    private readonly FactionIntelligenceStore _intelligence;
    private readonly PrototypeBattlefieldDefinition _battlefield;
    private readonly IReadOnlyDictionary<PlayerId, SkirmishOpponentConfiguration>
        _configurations;
    private readonly List<EntityId> _controllers = new();
    private readonly List<SkirmishOpponentDebugReadModel> _debug = new();

    public SkirmishOpponentSystem(
        BuildingDefinitionCatalog buildings,
        UnitDefinitionCatalog units,
        InventoryStore inventories,
        BuildingPlacementService placement,
        FactionIntelligenceStore intelligence,
        PrototypeBattlefieldDefinition battlefield,
        IReadOnlyDictionary<PlayerId, SkirmishOpponentConfiguration>? configurations = null)
    {
        _buildings = buildings ??
            throw new ArgumentNullException(nameof(buildings));
        _units = units ??
            throw new ArgumentNullException(nameof(units));
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
        _placement = placement ??
            throw new ArgumentNullException(nameof(placement));
        _intelligence = intelligence ??
            throw new ArgumentNullException(nameof(intelligence));
        _battlefield = battlefield ??
            throw new ArgumentNullException(nameof(battlefield));
        _configurations =
            configurations ??
            new Dictionary<PlayerId, SkirmishOpponentConfiguration>();

        foreach (SkirmishOpponentConfiguration configuration in
                 _configurations.Values)
        {
            configuration.Validate();
        }
    }

    public SimulationPhase Phase =>
        SimulationPhase.AiDecisions;

    public bool DebugCaptureEnabled { get; set; }

    public IReadOnlyList<SkirmishOpponentDebugReadModel> DebugSnapshot =>
        _debug;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _controllers.Clear();
        _debug.Clear();

        foreach (EntityId controller in
                 context.Entities.Query<
                     SkirmishOpponentController,
                     SkirmishOpponentState>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            _controllers.Add(controller);
        }

        for (int index = 0;
             index < _controllers.Count;
             index++)
        {
            EvaluateController(
                context,
                _controllers[index]);
        }
    }

    private void EvaluateController(
        SimulationContext context,
        EntityId controllerEntity)
    {
        SkirmishOpponentController controller =
            context.Entities.GetComponent<SkirmishOpponentController>(
                controllerEntity);
        SkirmishOpponentState state =
            context.Entities.GetComponent<SkirmishOpponentState>(
                controllerEntity);
        SkirmishOpponentConfiguration configuration =
            ResolveConfiguration(controller.Player);

        OwnedState owned =
            CaptureOwnedState(
                context,
                controller);
        FactionIntelligenceSnapshot intelligence =
            _intelligence.Capture(
                controller.Faction);
        SkirmishEconomyAssessment economy =
            AssessEconomy(
                context,
                controller,
                owned);
        SkirmishForceAssessment force =
            AssessForce(
                context,
                owned,
                intelligence);

        if (!IsDecisionDue(
                context.Tick,
                state.LastDecisionTick,
                configuration.ReactionCadenceTicks))
        {
            CaptureDebug(
                controllerEntity,
                controller,
                state,
                economy,
                force,
                hasObjective: false,
                objective: Vector3.Zero);
            return;
        }

        EnsureTacticalBehavior(
            context,
            owned,
            configuration);
        EnsureEconomyPolicies(
            context,
            controller,
            owned);
        EnsureProductionPrograms(
            context,
            owned,
            configuration);

        SkirmishStrategicState strategicState;
        SkirmishStrategicGoal goal;
        Vector3 objective = Vector3.Zero;
        bool hasObjective = false;

        if (TryRespondToCriticalEconomy(
                context,
                controller,
                owned,
                economy,
                out goal))
        {
            strategicState =
                SkirmishStrategicState.Recovering;
        }
        else if (TryAdvanceBootstrap(
                     context,
                     controller,
                     owned,
                     economy,
                     out goal))
        {
            strategicState =
                SkirmishStrategicState.Bootstrap;
        }
        else if (TryDefend(
                     context,
                     controller,
                     owned,
                     intelligence,
                     configuration,
                     out objective))
        {
            strategicState =
                SkirmishStrategicState.Defending;
            goal =
                SkirmishStrategicGoal.Defend;
            hasObjective = true;
        }
        else if (TryExpand(
                     context,
                     controller,
                     owned,
                     economy,
                     force,
                     configuration,
                     ref state,
                     out objective))
        {
            strategicState =
                SkirmishStrategicState.Expanding;
            goal =
                SkirmishStrategicGoal.Expand;
            hasObjective = true;
        }
        else if (TryRecoverForce(
                     context,
                     controller,
                     owned,
                     force,
                     configuration))
        {
            strategicState =
                SkirmishStrategicState.Resupplying;
            goal =
                SkirmishStrategicGoal.RecoverSupply;
            objective =
                ResolveRecoveryPoint(
                    context,
                    controller,
                    owned);
            hasObjective = true;
        }
        else if (TryScout(
                     context,
                     controller,
                     owned,
                     intelligence,
                     ref state,
                     out objective))
        {
            strategicState =
                SkirmishStrategicState.Mobilizing;
            goal =
                SkirmishStrategicGoal.Scout;
            hasObjective = true;
        }
        else if (TryAttack(
                     context,
                     controller,
                     owned,
                     intelligence,
                     force,
                     configuration,
                     out objective))
        {
            strategicState =
                SkirmishStrategicState.Attacking;
            goal =
                SkirmishStrategicGoal.AttackObjective;
            hasObjective = true;
        }
        else
        {
            strategicState =
                SkirmishStrategicState.Mobilizing;
            goal =
                SkirmishStrategicGoal.PrepareOffensive;
        }

        TryIssueArtilleryMission(
            context,
            controller,
            owned,
            intelligence,
            configuration,
            ref state);

        state =
            state with
            {
                StrategicState = strategicState,
                ActiveGoal = goal,
                LastDecisionTick = context.Tick,
                DecisionsTaken =
                    checked(state.DecisionsTaken + 1)
            };

        context.Entities.SetComponent(
            controllerEntity,
            state);

        CaptureDebug(
            controllerEntity,
            controller,
            state,
            economy,
            force,
            hasObjective,
            objective);
    }

    private SkirmishOpponentConfiguration ResolveConfiguration(
        PlayerId player)
    {
        if (_configurations.TryGetValue(
                player,
                out SkirmishOpponentConfiguration? configuration))
        {
            return configuration;
        }

        var defaultConfiguration =
            new SkirmishOpponentConfiguration();
        defaultConfiguration.Validate();
        return defaultConfiguration;
    }

    private static OwnedState CaptureOwnedState(
        SimulationContext context,
        SkirmishOpponentController controller)
    {
        var owned =
            new OwnedState(
                controller.Player,
                controller.Faction);

        foreach (EntityId entity in
                 context.Entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CompletedBuilding building =
                context.Entities.GetComponent<CompletedBuilding>(
                    entity);

            if (building.Owner != controller.Player)
            {
                continue;
            }

            owned.Buildings.Add(entity);
            owned.BuildingCounts.TryGetValue(
                building.BuildingId,
                out int count);
            owned.BuildingCounts[building.BuildingId] =
                count + 1;

            AddBuildingInventories(
                context,
                entity,
                owned.InventoryIds);

            if (context.Entities.TryGetComponent(
                    entity,
                    out PowerGenerator generator) &&
                generator.Enabled &&
                generator.State ==
                PowerGeneratorState.Generating)
            {
                owned.PowerGeneration +=
                    generator.MaximumGeneration;
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out PowerConsumer consumer) &&
                consumer.Enabled)
            {
                owned.PowerDemand +=
                    consumer.Demand;

                if (consumer.State !=
                    PowerOperationalState.Powered)
                {
                    owned.OfflineConsumers++;
                }
            }

            if (context.Entities.HasComponent<ProductionFacility>(
                    entity))
            {
                owned.ProductionFacilities.Add(
                    entity);
            }

            if (context.Entities.HasComponent<UnitProductionFacility>(
                    entity))
            {
                owned.UnitProductionFacilities.Add(
                    entity);
            }

            if (building.BuildingId ==
                    BuildingIds.SupplyDepot &&
                context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform supplyTransform))
            {
                owned.SupplyDepots.Add(
                    (entity, supplyTransform.Position));
            }
        }

        foreach (EntityId site in
                 context.Entities.Query<ConstructionSite>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ConstructionSite construction =
                context.Entities.GetComponent<ConstructionSite>(
                    site);

            if (construction.Owner ==
                controller.Player)
            {
                owned.ConstructionSites.Add(
                    site);
                owned.PendingBuildings.Add(
                    construction.BuildingId);
            }
        }

        foreach (EntityId entity in
                 context.Entities.Query<ControllableEntity, UnitIdentity>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ControllableEntity controllable =
                context.Entities.GetComponent<ControllableEntity>(
                    entity);

            if (controllable.Owner !=
                controller.Player)
            {
                continue;
            }

            UnitIdentity identity =
                context.Entities.GetComponent<UnitIdentity>(
                    entity);

            owned.Units.Add(entity);
            owned.UnitByEntity[entity] =
                identity.UnitId;
            owned.UnitCounts.TryGetValue(
                identity.UnitId,
                out int count);
            owned.UnitCounts[identity.UnitId] =
                count + 1;

            UnitId unitId =
                identity.UnitId;

            if (unitId == UnitIds.CargoTruck ||
                unitId == UnitIds.SupplyTruck)
            {
                continue;
            }

            if (context.Entities.HasComponent<Combatant>(
                    entity) &&
                context.Entities.HasComponent<HealthState>(
                    entity))
            {
                owned.CombatUnits.Add(
                    entity);
            }
        }

        foreach (EntityId requestEntity in
                 context.Entities.Query<UnitProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            UnitProductionRequest request =
                context.Entities.GetComponent<UnitProductionRequest>(
                    requestEntity);

            if (!context.Entities.TryGetComponent(
                    request.Facility,
                    out UnitProductionFacility facility) ||
                facility.Owner !=
                controller.Player)
            {
                continue;
            }

            owned.PendingUnitCounts.TryGetValue(
                request.UnitId,
                out int count);
            owned.PendingUnitCounts[request.UnitId] =
                count + 1;
            owned.PendingRequestsPerFacility.TryGetValue(
                request.Facility,
                out int facilityRequests);
            owned.PendingRequestsPerFacility[
                request.Facility] =
                facilityRequests + 1;
        }

        return owned;
    }

    private static void AddBuildingInventories(
        SimulationContext context,
        EntityId entity,
        HashSet<InventoryId> inventories)
    {
        if (context.Entities.TryGetComponent(
                entity,
                out InventoryStorage storage))
        {
            inventories.Add(
                storage.InventoryId);
        }

        if (context.Entities.TryGetComponent(
                entity,
                out ProductionFacility production))
        {
            inventories.Add(
                production.InputInventory);
            inventories.Add(
                production.OutputInventory);
        }

        if (context.Entities.TryGetComponent(
                entity,
                out UnitProductionFacility unitProduction))
        {
            inventories.Add(
                unitProduction.InputInventory);
        }
    }

    private SkirmishEconomyAssessment AssessEconomy(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned)
    {
        double ferrous = 0.0;
        double volatiles = 0.0;
        double silicates = 0.0;
        double steel = 0.0;
        double fuel = 0.0;
        double electronics = 0.0;
        double ammunition = 0.0;

        foreach (InventoryId inventory in
                 owned.InventoryIds)
        {
            if (!_inventories.Contains(
                    inventory))
            {
                continue;
            }

            ferrous +=
                _inventories.GetQuantity(
                    inventory,
                    ResourceIds.FerrousOre);
            volatiles +=
                _inventories.GetQuantity(
                    inventory,
                    ResourceIds.Volatiles);
            silicates +=
                _inventories.GetQuantity(
                    inventory,
                    ResourceIds.Silicates);
            steel +=
                _inventories.GetQuantity(
                    inventory,
                    ResourceIds.Steel);
            fuel +=
                _inventories.GetQuantity(
                    inventory,
                    ResourceIds.Fuel);
            electronics +=
                _inventories.GetQuantity(
                    inventory,
                    ResourceIds.Electronics);
            ammunition +=
                _inventories.GetQuantity(
                    inventory,
                    ResourceIds.Ammunition);
        }

        double rawScore =
            Math.Clamp(
                Math.Min(
                    ferrous / 350.0,
                    Math.Min(
                        volatiles / 220.0,
                        silicates / 220.0)),
                0.0,
                1.0);
        double processedScore =
            Math.Clamp(
                Math.Min(
                    steel / 220.0,
                    Math.Min(
                        electronics / 100.0,
                        Math.Min(
                            fuel / 160.0,
                            ammunition / 220.0))),
                0.0,
                1.0);
        double powerScore =
            owned.PowerDemand <= 0.0
                ? 1.0
                : Math.Clamp(
                    owned.PowerGeneration /
                    owned.PowerDemand,
                    0.0,
                    1.0);
        double healthScore =
            (rawScore +
             processedScore +
             powerScore) /
            3.0;

        return new SkirmishEconomyAssessment(
            ferrous,
            volatiles,
            silicates,
            steel,
            fuel,
            electronics,
            ammunition,
            owned.PowerGeneration,
            owned.PowerDemand,
            owned.OfflineConsumers,
            owned.ConstructionSites.Count,
            owned.ProductionFacilities.Count,
            owned.UnitProductionFacilities.Count,
            healthScore);
    }

    private static SkirmishForceAssessment AssessForce(
        SimulationContext context,
        OwnedState owned,
        FactionIntelligenceSnapshot intelligence)
    {
        double readinessTotal = 0.0;
        double minimumSupply =
            owned.CombatUnits.Count > 0
                ? 1.0
                : 0.0;
        int readinessCount = 0;

        for (int index = 0;
             index < owned.CombatUnits.Count;
             index++)
        {
            EntityId unit =
                owned.CombatUnits[index];

            if (context.Entities.TryGetComponent(
                    unit,
                    out UnitCombatReadiness readiness))
            {
                readinessTotal +=
                    readiness.OverallReadiness;
                minimumSupply =
                    Math.Min(
                        minimumSupply,
                        Math.Min(
                            readiness.Fuel,
                            readiness.Ammunition));
                readinessCount++;
            }
        }

        int currentContacts = 0;

        for (int index = 0;
             index < intelligence.Contacts.Count;
             index++)
        {
            if (intelligence.Contacts[index].IsCurrent)
            {
                currentContacts++;
            }
        }

        return new SkirmishForceAssessment(
            owned.Units.Count,
            owned.CombatUnits.Count,
            GetUnitCount(
                owned,
                UnitIds.ScoutVehicle),
            GetUnitCount(
                owned,
                UnitIds.MainBattleTank),
            GetUnitCount(
                owned,
                UnitIds.MobileArtillery),
            GetUnitCount(
                owned,
                UnitIds.CargoTruck),
            GetUnitCount(
                owned,
                UnitIds.SupplyTruck),
            readinessCount > 0
                ? readinessTotal /
                  readinessCount
                : owned.CombatUnits.Count > 0
                    ? 1.0
                    : 0.0,
            minimumSupply,
            intelligence.Contacts.Count,
            currentContacts);
    }

    private static bool IsDecisionDue(
        SimulationTick tick,
        SimulationTick lastDecisionTick,
        uint cadence)
    {
        if (lastDecisionTick ==
            SimulationTick.Zero)
        {
            return true;
        }

        return tick.Value >=
            checked(
                lastDecisionTick.Value +
                cadence);
    }

    private bool TryRespondToCriticalEconomy(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        in SkirmishEconomyAssessment economy,
        out SkirmishStrategicGoal goal)
    {
        if (economy.PowerConstrained ||
            economy.OfflineConsumers > 0)
        {
            if (GetBuildingCount(
                    owned,
                    BuildingIds.PowerPlant) < 4 &&
                TryIssueBuilding(
                    context,
                    controller,
                    owned,
                    BuildingIds.PowerPlant,
                    requestedPosition: null))
            {
                goal =
                    SkirmishStrategicGoal.EstablishPower;
                return true;
            }
        }

        if (economy.RawResourceConstrained)
        {
            for (int index = 0;
                 index < RawResources.Length;
                 index++)
            {
                ResourceId resource =
                    RawResources[index];

                if (HasOperationalExtractor(
                        context,
                        owned,
                        resource) ||
                    HasPendingExtractor(
                        context,
                        owned,
                        resource))
                {
                    continue;
                }

                if (TryIssueExtractor(
                        context,
                        controller,
                        owned,
                        resource,
                        contestedAllowed: false))
                {
                    goal =
                        SkirmishStrategicGoal.SecureResources;
                    return true;
                }
            }

            goal =
                SkirmishStrategicGoal.RecoverEconomy;
            return false;
        }

        goal =
            SkirmishStrategicGoal.RecoverEconomy;
        return false;
    }

    private bool TryAdvanceBootstrap(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        in SkirmishEconomyAssessment economy,
        out SkirmishStrategicGoal goal)
    {
        if (GetBuildingCount(
                owned,
                BuildingIds.PowerPlant) == 0 &&
            !HasPendingBuilding(
                owned,
                BuildingIds.PowerPlant))
        {
            goal =
                SkirmishStrategicGoal.EstablishPower;
            return TryIssueBuilding(
                context,
                controller,
                owned,
                BuildingIds.PowerPlant,
                requestedPosition: null);
        }

        for (int index = 0;
             index < RawResources.Length;
             index++)
        {
            ResourceId resource =
                RawResources[index];

            if (HasOperationalExtractor(
                    context,
                    owned,
                    resource) ||
                HasPendingExtractor(
                    context,
                    owned,
                    resource))
            {
                continue;
            }

            goal =
                SkirmishStrategicGoal.SecureResources;
            return TryIssueExtractor(
                context,
                controller,
                owned,
                resource,
                contestedAllowed: false);
        }

        for (int index = 0;
             index < IndustrialPlan.Length;
             index++)
        {
            BuildingId building =
                IndustrialPlan[index];

            if (GetBuildingCount(
                    owned,
                    building) > 0 ||
                HasPendingBuilding(
                    owned,
                    building))
            {
                continue;
            }

            if (economy.PowerDemand > 0.0 &&
                economy.PowerGeneration <
                economy.PowerDemand + 20.0 &&
                GetBuildingCount(
                    owned,
                    BuildingIds.PowerPlant) < 4 &&
                !HasPendingBuilding(
                    owned,
                    BuildingIds.PowerPlant))
            {
                goal =
                    SkirmishStrategicGoal.EstablishPower;
                return TryIssueBuilding(
                    context,
                    controller,
                    owned,
                    BuildingIds.PowerPlant,
                    requestedPosition: null);
            }

            goal =
                building is
                    var id &&
                (id == BuildingIds.Barracks ||
                 id == BuildingIds.VehicleFactory)
                    ? SkirmishStrategicGoal.EstablishProduction
                    : SkirmishStrategicGoal.EstablishIndustry;

            return TryIssueBuilding(
                context,
                controller,
                owned,
                building,
                requestedPosition: null);
        }

        goal =
            SkirmishStrategicGoal.PrepareOffensive;
        return false;
    }

    private bool TryDefend(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        FactionIntelligenceSnapshot intelligence,
        SkirmishOpponentConfiguration configuration,
        out Vector3 objective)
    {
        objective = Vector3.Zero;

        if (owned.CombatUnits.Count == 0)
        {
            return false;
        }

        IntelligenceContact? threat =
            FindClosestCurrentContact(
                intelligence,
                controller.HomePosition,
                configuration.DefensiveRadiusMeters);

        if (!threat.HasValue)
        {
            return false;
        }

        objective =
            threat.Value.LastKnownPosition;

        if (threat.Value.State ==
                IntelligenceState.Identified &&
            _intelligence.TryResolveCurrentlyIdentifiedEntity(
                controller.Faction,
                threat.Value.ContactKey,
                out EntityId target) &&
            context.Entities.IsAlive(target))
        {
            var command =
                new AttackCommand(
                    controller.Player,
                    owned.CombatUnits.ToArray(),
                    target,
                    context.Tick,
                    configuration.ObjectivePressureLeashMeters);
            command.Execute(context);
        }
        else
        {
            var command =
                new AttackMoveCommand(
                    controller.Player,
                    owned.CombatUnits.ToArray(),
                    objective,
                    context.Tick,
                    FormationTemplate.Line,
                    configuration.ObjectivePressureLeashMeters);
            command.Execute(context);
        }

        return true;
    }

    private static bool TryRecoverForce(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        in SkirmishForceAssessment force,
        SkirmishOpponentConfiguration configuration)
    {
        if (owned.CombatUnits.Count == 0)
        {
            return false;
        }

        var retreatUnits =
            new List<EntityId>();
        int recoveringUnits = 0;

        for (int index = 0;
             index < owned.CombatUnits.Count;
             index++)
        {
            EntityId unit =
                owned.CombatUnits[index];

            if (!context.Entities.TryGetComponent(
                    unit,
                    out UnitCombatReadiness readiness))
            {
                continue;
            }

            double supply =
                Math.Min(
                    readiness.Fuel,
                    readiness.Ammunition);

            if (readiness.OverallReadiness >=
                    configuration.RetreatThreshold &&
                supply >=
                    configuration.ResupplyThreshold)
            {
                continue;
            }

            recoveringUnits++;

            if (!context.Entities.HasComponent<ResupplyOrder>(
                    unit))
            {
                retreatUnits.Add(
                    unit);
            }
        }

        if (recoveringUnits == 0)
        {
            return false;
        }

        if (retreatUnits.Count > 0)
        {
            Vector3 recovery =
                ResolveRecoveryPoint(
                    context,
                    controller,
                    owned);

            var command =
                new RetreatCommand(
                    controller.Player,
                    retreatUnits.ToArray(),
                    recovery,
                    context.Tick,
                    FormationTemplate.Column);
            command.Execute(context);
        }

        bool attackForceEstablished =
            force.CombatUnits >=
            configuration.MinimumAttackUnits;
        bool forceWideRecovery =
            attackForceEstablished &&
            (
                force.AverageReadiness <
                    configuration.RetreatThreshold ||
                recoveringUnits ==
                    owned.CombatUnits.Count
            );

        return forceWideRecovery;
    }

    private bool TryExpand(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        in SkirmishEconomyAssessment economy,
        in SkirmishForceAssessment force,
        SkirmishOpponentConfiguration configuration,
        ref SkirmishOpponentState state,
        out Vector3 objective)
    {
        objective = Vector3.Zero;

        bool economyCanSupportExpansion =
            economy.HealthScore >=
                configuration.ExpansionReadinessThreshold ||
            economy.RawResourceConstrained;

        if (!IsBootstrapComplete(owned) ||
            !economyCanSupportExpansion ||
            force.TotalUnits < 4 ||
            HasPendingBuilding(
                owned,
                BuildingIds.LogisticsHub) ||
            HasPendingBuilding(
                owned,
                BuildingIds.SupplyDepot))
        {
            return false;
        }

        Vector3? unsupportedRemoteHub =
            SelectUnsupportedRemoteHub(
                context,
                owned,
                controller.HomePosition,
                minimumDistanceMeters: 500.0f,
                supportRadiusMeters: 260.0f);

        if (unsupportedRemoteHub.HasValue)
        {
            objective =
                unsupportedRemoteHub.Value;

            if (TryIssueBuilding(
                    context,
                    controller,
                    owned,
                    BuildingIds.SupplyDepot,
                    unsupportedRemoteHub.Value))
            {
                return true;
            }
        }

        int remoteHubs =
            CountRemoteBuildings(
                context,
                owned,
                BuildingIds.LogisticsHub,
                controller.HomePosition,
                minimumDistanceMeters: 500.0f);

        if (remoteHubs >= 2)
        {
            return false;
        }

        BattlefieldSiteDefinition? site =
            SelectExpansionSite(
                controller,
                state.ExpansionSiteCursor);

        if (!site.HasValue)
        {
            return false;
        }

        objective =
            site.Value.Position;

        if (!TryIssueBuilding(
                context,
                controller,
                owned,
                BuildingIds.LogisticsHub,
                site.Value.Position))
        {
            if (TryIssueNearestContestedExtractor(
                    context,
                    controller,
                    owned,
                    site.Value.Position))
            {
                state =
                    state with
                    {
                        ExpansionSiteCursor =
                            checked(
                                state.ExpansionSiteCursor + 1)
                    };
                return true;
            }

            return false;
        }

        state =
            state with
            {
                ExpansionSiteCursor =
                    checked(
                        state.ExpansionSiteCursor + 1)
            };

        return true;
    }

    private bool TryScout(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        FactionIntelligenceSnapshot intelligence,
        ref SkirmishOpponentState state,
        out Vector3 objective)
    {
        objective = Vector3.Zero;

        if (intelligence.Contacts.Any(
                static contact =>
                    contact.IsCurrent))
        {
            return false;
        }

        EntityId scout =
            FindIdleUnit(
                context,
                owned,
                UnitIds.ScoutVehicle);

        if (!scout.IsValid)
        {
            return false;
        }

        BattlefieldSiteDefinition[] sites =
            GetOpponentFacingSites(
                controller);

        if (sites.Length == 0)
        {
            return false;
        }

        int index =
            Math.Abs(
                state.ScoutSiteCursor) %
            sites.Length;
        objective =
            sites[index].Position;

        var command =
            new AttackMoveCommand(
                controller.Player,
                [scout],
                objective,
                context.Tick,
                FormationTemplate.Column,
                pursuitLeashMeters: 90.0f);
        command.Execute(context);

        state =
            state with
            {
                ScoutSiteCursor =
                    checked(
                        state.ScoutSiteCursor + 1)
            };

        return true;
    }

    private bool TryAttack(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        FactionIntelligenceSnapshot intelligence,
        in SkirmishForceAssessment force,
        SkirmishOpponentConfiguration configuration,
        out Vector3 objective)
    {
        objective = Vector3.Zero;

        if (owned.CombatUnits.Count <
            configuration.MinimumAttackUnits)
        {
            return false;
        }

        EntityId[] attackers =
            owned.CombatUnits
                .Where(
                    unit =>
                    {
                        if (owned.UnitByEntity.TryGetValue(
                                unit,
                                out UnitId unitId) &&
                            unitId == UnitIds.ScoutVehicle)
                        {
                            return false;
                        }

                        if (!context.Entities.TryGetComponent(
                                unit,
                                out UnitCombatReadiness readiness))
                        {
                            return true;
                        }

                        return
                            readiness.OverallReadiness >=
                                configuration.OffensiveReadinessThreshold &&
                            Math.Min(
                                readiness.Fuel,
                                readiness.Ammunition) >=
                                configuration.ResupplyThreshold;
                    })
                .Take(
                    configuration.MaximumAttackUnits)
                .ToArray();

        if (attackers.Length <
            configuration.MinimumAttackUnits)
        {
            return false;
        }

        IntelligenceContact? identified = null;
        EntityId identifiedTarget = EntityId.Invalid;
        bool identifiedIsCommandCore = false;
        float identifiedDistance = float.PositiveInfinity;

        for (int index = 0;
             index < intelligence.Contacts.Count;
             index++)
        {
            IntelligenceContact contact =
                intelligence.Contacts[index];

            if (!contact.IsCurrent ||
                contact.State != IntelligenceState.Identified ||
                !_intelligence.TryResolveCurrentlyIdentifiedEntity(
                    controller.Faction,
                    contact.ContactKey,
                    out EntityId candidate) ||
                !context.Entities.IsAlive(candidate))
            {
                continue;
            }

            bool isCommandCore =
                context.Entities.TryGetComponent(
                    candidate,
                    out CompletedBuilding objectiveBuilding) &&
                objectiveBuilding.BuildingId ==
                    BuildingIds.CommandCore;
            float distance =
                HorizontalDistanceSquared(
                    controller.HomePosition,
                    contact.LastKnownPosition);

            if (!identified.HasValue ||
                (isCommandCore && !identifiedIsCommandCore) ||
                (isCommandCore == identifiedIsCommandCore &&
                 (distance < identifiedDistance ||
                  (distance == identifiedDistance &&
                   contact.ContactKey <
                   identified.Value.ContactKey))))
            {
                identified = contact;
                identifiedTarget = candidate;
                identifiedIsCommandCore = isCommandCore;
                identifiedDistance = distance;
            }
        }

        if (identified.HasValue &&
            identifiedTarget.IsValid)
        {
            objective =
                identified.Value.LastKnownPosition;

            var attack =
                new AttackCommand(
                    controller.Player,
                    attackers,
                    identifiedTarget,
                    context.Tick,
                    configuration.ObjectivePressureLeashMeters);
            attack.Execute(context);
            return true;
        }

        objective =
            SelectOffensiveWaypoint(
                controller,
                configuration.Aggression);

        var advance =
            new AttackMoveCommand(
                controller.Player,
                attackers,
                objective,
                context.Tick,
                FormationTemplate.Line,
                configuration.ObjectivePressureLeashMeters);
        advance.Execute(context);

        return true;
    }

    private static void TryIssueArtilleryMission(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        FactionIntelligenceSnapshot intelligence,
        SkirmishOpponentConfiguration configuration,
        ref SkirmishOpponentState state)
    {
        if (state.LastArtilleryTick !=
                SimulationTick.Zero &&
            context.Tick.Value <
            checked(
                state.LastArtilleryTick.Value +
                configuration.ArtilleryCadenceTicks))
        {
            return;
        }

        EntityId[] artillery =
            owned.UnitByEntity
                .Where(
                    pair =>
                        pair.Value ==
                        UnitIds.MobileArtillery &&
                        context.Entities.IsAlive(
                            pair.Key))
                .Select(
                    static pair =>
                        pair.Key)
                .OrderBy(
                    static entity =>
                        entity)
                .ToArray();

        if (artillery.Length == 0)
        {
            return;
        }

        IntelligenceContact? contact =
            intelligence.Contacts
                .Where(
                    static candidate =>
                        candidate.IsCurrent &&
                        candidate.State is
                            IntelligenceState.Detected or
                            IntelligenceState.Identified)
                .OrderBy(
                    static candidate =>
                        candidate.ContactKey)
                .Cast<IntelligenceContact?>()
                .FirstOrDefault();

        if (!contact.HasValue)
        {
            return;
        }

        var mission =
            new FireMissionCommand(
                controller.Player,
                artillery,
                contact.Value.ContactKey,
                requestedRounds: 2,
                context.Tick);
        mission.Execute(context);

        if (mission.AcceptedTargetCount > 0)
        {
            state =
                state with
                {
                    LastArtilleryTick =
                        context.Tick
                };
        }
    }

    private static void EnsureTacticalBehavior(
        SimulationContext context,
        OwnedState owned,
        SkirmishOpponentConfiguration configuration)
    {
        var behavior =
            new TacticalTestOpponent(
                configuration.ResupplyThreshold,
                configuration.RetreatThreshold,
                configuration.ObjectivePressureLeashMeters);
        var combatResupplyPolicy =
            new AutomaticResupplyPolicy(
                configuration.ResupplyThreshold,
                configuration.ResupplyThreshold,
                enabled: true);
        var logisticsResupplyPolicy =
            new AutomaticResupplyPolicy(
                configuration.ResupplyThreshold,
                fuelThreshold:
                    Math.Max(
                        configuration.ResupplyThreshold,
                        0.8),
                enabled: true);

        for (int index = 0;
             index < owned.Units.Count;
             index++)
        {
            EntityId unit =
                owned.Units[index];
            bool isLogisticsVehicle =
                owned.UnitByEntity.TryGetValue(
                    unit,
                    out UnitId unitId) &&
                (unitId == UnitIds.CargoTruck ||
                 unitId == UnitIds.SupplyTruck);
            AutomaticResupplyPolicy resupplyPolicy =
                isLogisticsVehicle
                    ? logisticsResupplyPolicy
                    : combatResupplyPolicy;

            if (context.Entities.HasComponent<AutomaticResupplyPolicy>(
                    unit))
            {
                context.Entities.SetComponent(
                    unit,
                    resupplyPolicy);
            }
            else
            {
                context.Entities.AddComponent(
                    unit,
                    resupplyPolicy);
            }
        }

        for (int index = 0;
             index < owned.CombatUnits.Count;
             index++)
        {
            EntityId unit =
                owned.CombatUnits[index];

            if (owned.UnitByEntity.TryGetValue(
                    unit,
                    out UnitId unitId) &&
                unitId == UnitIds.ScoutVehicle)
            {
                if (context.Entities.HasComponent<TacticalTestOpponent>(
                        unit))
                {
                    context.Entities.RemoveComponent<TacticalTestOpponent>(
                        unit);
                }

                continue;
            }

            if (context.Entities.HasComponent<TacticalTestOpponent>(
                    unit))
            {
                context.Entities.SetComponent(
                    unit,
                    behavior);
            }
            else
            {
                context.Entities.AddComponent(
                    unit,
                    behavior);
            }
        }
    }

    private static void EnsureEconomyPolicies(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned)
    {
        if (context.Entities.IsAlive(
                controller.PreferredConstructionSource))
        {
            SetStockPolicy(
                context,
                controller.PreferredConstructionSource,
                ResourceIds.FerrousOre,
                220.0,
                650.0,
                1_100.0,
                LogisticsStockPriority.High);
            SetStockPolicy(
                context,
                controller.PreferredConstructionSource,
                ResourceIds.Volatiles,
                140.0,
                450.0,
                800.0,
                LogisticsStockPriority.High);
            SetStockPolicy(
                context,
                controller.PreferredConstructionSource,
                ResourceIds.Silicates,
                140.0,
                450.0,
                800.0,
                LogisticsStockPriority.High);
            SetStockPolicy(
                context,
                controller.PreferredConstructionSource,
                ResourceIds.Steel,
                220.0,
                700.0,
                1_200.0,
                LogisticsStockPriority.High);
            SetStockPolicy(
                context,
                controller.PreferredConstructionSource,
                ResourceIds.Electronics,
                160.0,
                360.0,
                600.0,
                LogisticsStockPriority.High);
            SetStockPolicy(
                context,
                controller.PreferredConstructionSource,
                ResourceIds.Fuel,
                360.0,
                600.0,
                900.0,
                LogisticsStockPriority.High);
        }

        for (int index = 0;
             index < owned.ProductionFacilities.Count;
             index++)
        {
            EntityId entity =
                owned.ProductionFacilities[index];
            ProductionFacility facility =
                context.Entities.GetComponent<ProductionFacility>(
                    entity);

            if (facility.Supports(
                    ProductionCapability.SteelProcessing))
            {
                SetStockPolicy(
                    context,
                    entity,
                    ResourceIds.FerrousOre,
                    80.0,
                    240.0,
                    500.0,
                    LogisticsStockPriority.High);
            }

            if (facility.Supports(
                    ProductionCapability.FuelProcessing))
            {
                SetStockPolicy(
                    context,
                    entity,
                    ResourceIds.Volatiles,
                    80.0,
                    220.0,
                    450.0,
                    LogisticsStockPriority.High);
            }

            if (facility.Supports(
                    ProductionCapability.ElectronicsProcessing))
            {
                SetStockPolicy(
                    context,
                    entity,
                    ResourceIds.Silicates,
                    80.0,
                    220.0,
                    450.0,
                    LogisticsStockPriority.High);
            }

            if (facility.Supports(
                    ProductionCapability.AmmunitionProcessing))
            {
                SetStockPolicy(
                    context,
                    entity,
                    ResourceIds.Steel,
                    100.0,
                    260.0,
                    520.0,
                    LogisticsStockPriority.Critical);
                SetStockPolicy(
                    context,
                    entity,
                    ResourceIds.Electronics,
                    40.0,
                    120.0,
                    260.0,
                    LogisticsStockPriority.Critical);
            }
        }

        for (int index = 0;
             index < owned.UnitProductionFacilities.Count;
             index++)
        {
            EntityId entity =
                owned.UnitProductionFacilities[index];
            UnitProductionFacility facility =
                context.Entities.GetComponent<UnitProductionFacility>(
                    entity);
            const LogisticsStockPriority priority =
                LogisticsStockPriority.Critical;

            SetStockPolicy(
                context,
                entity,
                ResourceIds.Steel,
                80.0,
                240.0,
                420.0,
                priority);
            SetStockPolicy(
                context,
                entity,
                ResourceIds.Electronics,
                30.0,
                80.0,
                160.0,
                priority);
            SetStockPolicy(
                context,
                entity,
                ResourceIds.Fuel,
                80.0,
                160.0,
                280.0,
                priority);
            SetStockPolicy(
                context,
                entity,
                ResourceIds.Ammunition,
                50.0,
                120.0,
                240.0,
                priority);
        }

        for (int index = 0;
             index < owned.SupplyDepots.Count;
             index++)
        {
            EntityId entity =
                owned.SupplyDepots[index].Entity;

            SetStockPolicy(
                context,
                entity,
                ResourceIds.Fuel,
                250.0,
                600.0,
                900.0,
                LogisticsStockPriority.High);
            SetStockPolicy(
                context,
                entity,
                ResourceIds.Ammunition,
                350.0,
                800.0,
                1_200.0,
                LogisticsStockPriority.High);
        }
    }

    private static void SetStockPolicy(
        SimulationContext context,
        EntityId entity,
        ResourceId resource,
        double minimum,
        double target,
        double maximum,
        LogisticsStockPriority priority)
    {
        var command =
            new SetLogisticsStockPolicyCommand(
                entity,
                resource,
                minimum,
                target,
                maximum,
                context.Tick,
                priority);
        command.Execute(context);
    }

    private void EnsureProductionPrograms(
        SimulationContext context,
        OwnedState owned,
        SkirmishOpponentConfiguration configuration)
    {
        for (int index = 0;
             index < owned.ProductionFacilities.Count;
             index++)
        {
            EntityId entity =
                owned.ProductionFacilities[index];

            if (HasProductionRequest(
                    context,
                    entity))
            {
                continue;
            }

            ProductionFacility facility =
                context.Entities.GetComponent<ProductionFacility>(
                    entity);

            if (facility.Supports(
                    ProductionCapability.SteelProcessing))
            {
                QueueDesiredStock(
                    context,
                    entity,
                    RecipeIds.Steel,
                    ResourceIds.Steel,
                    600.0);
            }
            else if (facility.Supports(
                         ProductionCapability.FuelProcessing))
            {
                QueueDesiredStock(
                    context,
                    entity,
                    RecipeIds.Fuel,
                    ResourceIds.Fuel,
                    500.0);
            }
            else if (facility.Supports(
                         ProductionCapability.ElectronicsProcessing))
            {
                QueueDesiredStock(
                    context,
                    entity,
                    RecipeIds.Electronics,
                    ResourceIds.Electronics,
                    400.0);
            }
            else if (facility.Supports(
                         ProductionCapability.AmmunitionProcessing))
            {
                QueueDesiredStock(
                    context,
                    entity,
                    RecipeIds.Ammunition,
                    ResourceIds.Ammunition,
                    900.0);
            }
        }

        EnsureUnitProduction(
            context,
            owned,
            configuration);
    }

    private static bool HasProductionRequest(
        SimulationContext context,
        EntityId facility)
    {
        foreach (EntityId entity in
                 context.Entities.Query<ProductionRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ProductionRequest request =
                context.Entities.GetComponent<ProductionRequest>(
                    entity);

            if (request.Facility ==
                facility)
            {
                return true;
            }
        }

        return false;
    }

    private static void QueueDesiredStock(
        SimulationContext context,
        EntityId facility,
        RecipeId recipe,
        ResourceId output,
        double desiredStock)
    {
        var command =
            new QueueProductionCommand(
                facility,
                recipe,
                context.Tick,
                ProductionPriority.Normal,
                ProductionRequestMode.DesiredStock,
                output,
                desiredStock);
        command.Execute(context);
    }

    private void EnsureUnitProduction(
        SimulationContext context,
        OwnedState owned,
        SkirmishOpponentConfiguration configuration)
    {
        for (int index = 0;
             index < owned.UnitProductionFacilities.Count;
             index++)
        {
            EntityId facilityEntity =
                owned.UnitProductionFacilities[index];
            UnitProductionFacility facility =
                context.Entities.GetComponent<UnitProductionFacility>(
                    facilityEntity);

            int queued =
                owned.PendingRequestsPerFacility.TryGetValue(
                    facilityEntity,
                    out int pending)
                    ? pending
                    : 0;

            if (queued >=
                configuration.MaximumQueuedUnitsPerFacility)
            {
                continue;
            }

            UnitId candidate =
                SelectUnitProductionGoal(
                    owned,
                    facility);

            if (!candidate.IsSpecified)
            {
                continue;
            }

            var command =
                new QueueUnitProductionCommand(
                    facility.Owner,
                    facilityEntity,
                    candidate,
                    context.Tick,
                    ProductionPriority.Normal);
            command.Execute(context);

            if (command.Accepted)
            {
                owned.PendingUnitCounts.TryGetValue(
                    candidate,
                    out int candidatePending);
                owned.PendingUnitCounts[candidate] =
                    candidatePending + 1;
                owned.PendingRequestsPerFacility[
                    facilityEntity] =
                    queued + 1;
            }
        }
    }

    private UnitId SelectUnitProductionGoal(
        OwnedState owned,
        in UnitProductionFacility facility)
    {
        UnitId[] priority =
        [
            UnitIds.RifleSquad,
            UnitIds.CargoTruck,
            UnitIds.SupplyTruck,
            UnitIds.ScoutVehicle,
            UnitIds.MainBattleTank,
            UnitIds.MobileArtillery,
            UnitIds.CombatEngineer
        ];

        var targets =
            new Dictionary<UnitId, int>
            {
                [UnitIds.RifleSquad] = 6,
                [UnitIds.CombatEngineer] = 1,
                [UnitIds.ScoutVehicle] = 2,
                [UnitIds.CargoTruck] = 2,
                [UnitIds.SupplyTruck] = 1,
                [UnitIds.MainBattleTank] = 4,
                [UnitIds.MobileArtillery] = 2
            };

        for (int index = 0;
             index < priority.Length;
             index++)
        {
            UnitId unitId =
                priority[index];
            UnitDefinition definition =
                _units[unitId];

            if (!facility.Supports(
                    definition.RequiredProductionCapability))
            {
                continue;
            }

            int current =
                GetUnitCount(
                    owned,
                    unitId);
            int pending =
                owned.PendingUnitCounts.TryGetValue(
                    unitId,
                    out int value)
                    ? value
                    : 0;

            if (current + pending <
                targets[unitId])
            {
                return unitId;
            }
        }

        return UnitId.None;
    }

    private bool TryIssueBuilding(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        BuildingId buildingId,
        Vector3? requestedPosition)
    {
        if (owned.ConstructionSites.Count > 0 ||
            HasPendingBuildRequest(
                context,
                controller.Player))
        {
            return false;
        }

        BuildingDefinition definition =
            _buildings[buildingId];

        if (!TrySelectConstructionSource(
                context,
                controller,
                owned,
                definition,
                out EntityId source))
        {
            return false;
        }

        if (!TryFindPlacement(
                context,
                controller,
                buildingId,
                requestedPosition,
                out Vector3 position))
        {
            return false;
        }

        var command =
            new BuildCommand(
                controller.Player,
                buildingId,
                position,
                BuildingOrientation.North,
                source,
                context.Tick);
        command.Execute(context);

        return command.RequestEntity.IsValid;
    }

    private bool TryIssueExtractor(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        ResourceId resource,
        bool contestedAllowed)
    {
        BattlefieldResourceDepositDefinition? deposit =
            _battlefield.Resources
                .Where(
                    candidate =>
                        candidate.ResourceId ==
                        resource &&
                        (contestedAllowed ||
                         !candidate.Contested))
                .OrderBy(
                    candidate =>
                        HorizontalDistanceSquared(
                            controller.HomePosition,
                            candidate.Center))
                .Cast<BattlefieldResourceDepositDefinition?>()
                .FirstOrDefault();

        return deposit.HasValue &&
               TryIssueBuilding(
                   context,
                   controller,
                   owned,
                   BuildingIds.Extractor,
                   deposit.Value.Center);
    }

    private bool TryIssueNearestContestedExtractor(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        Vector3 origin)
    {
        BattlefieldResourceDepositDefinition? deposit =
            _battlefield.Resources
                .Where(
                    static candidate =>
                        candidate.Contested)
                .OrderBy(
                    candidate =>
                        HorizontalDistanceSquared(
                            origin,
                            candidate.Center))
                .Cast<BattlefieldResourceDepositDefinition?>()
                .FirstOrDefault();

        return deposit.HasValue &&
               TryIssueBuilding(
                   context,
                   controller,
                   owned,
                   BuildingIds.Extractor,
                   deposit.Value.Center);
    }

    private bool TrySelectConstructionSource(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned,
        BuildingDefinition definition,
        out EntityId source)
    {
        source = EntityId.Invalid;

        var candidates =
            new List<(EntityId Entity, double Stock)>();

        for (int index = 0;
             index < owned.Buildings.Count;
             index++)
        {
            EntityId entity =
                owned.Buildings[index];

            if (!context.Entities.TryGetComponent(
                    entity,
                    out InventoryStorage storage) ||
                !_inventories.Contains(
                    storage.InventoryId) ||
                !CanPay(
                    storage.InventoryId,
                    definition))
            {
                continue;
            }

            candidates.Add(
                (
                    entity,
                    _inventories.GetTotalQuantity(
                        storage.InventoryId)));
        }

        if (context.Entities.IsAlive(
                controller.PreferredConstructionSource) &&
            context.Entities.TryGetComponent(
                controller.PreferredConstructionSource,
                out InventoryStorage preferredStorage) &&
            _inventories.Contains(
                preferredStorage.InventoryId) &&
            CanPay(
                preferredStorage.InventoryId,
                definition))
        {
            source =
                controller.PreferredConstructionSource;
            return true;
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        source =
            candidates
                .OrderByDescending(
                    static candidate =>
                        candidate.Stock)
                .ThenBy(
                    static candidate =>
                        candidate.Entity)
                .First()
                .Entity;
        return true;
    }

    private bool CanPay(
        InventoryId inventory,
        BuildingDefinition definition)
    {
        for (int index = 0;
             index < definition.Costs.Count;
             index++)
        {
            BuildingResourceCost cost =
                definition.Costs[index];

            if (_inventories.GetAvailableQuantity(
                    inventory,
                    cost.ResourceId) <
                cost.Quantity)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryFindPlacement(
        SimulationContext context,
        SkirmishOpponentController controller,
        BuildingId buildingId,
        Vector3? requestedPosition,
        out Vector3 placement)
    {
        if (requestedPosition.HasValue)
        {
            BuildingPlacementResult direct =
                _placement.Evaluate(
                    context.Entities,
                    controller.Player,
                    buildingId,
                    requestedPosition.Value,
                    BuildingOrientation.North);

            if (direct.IsValid)
            {
                placement =
                    direct.GroundPosition;
                return true;
            }

            if (buildingId ==
                BuildingIds.Extractor)
            {
                placement = default;
                return false;
            }

            Vector2[] localOffsets =
            [
                new(60.0f, 0.0f),
                new(-60.0f, 0.0f),
                new(0.0f, 60.0f),
                new(0.0f, -60.0f),
                new(90.0f, 90.0f),
                new(90.0f, -90.0f),
                new(-90.0f, 90.0f),
                new(-90.0f, -90.0f),
                new(140.0f, 0.0f),
                new(-140.0f, 0.0f),
                new(0.0f, 140.0f),
                new(0.0f, -140.0f)
            ];

            for (int index = 0;
                 index < localOffsets.Length;
                 index++)
            {
                Vector2 offset =
                    localOffsets[index];
                Vector3 candidate =
                    requestedPosition.Value +
                    new Vector3(
                        offset.X,
                        0.0f,
                        offset.Y);

                BuildingPlacementResult nearby =
                    _placement.Evaluate(
                        context.Entities,
                        controller.Player,
                        buildingId,
                        candidate,
                        BuildingOrientation.North);

                if (nearby.IsValid)
                {
                    placement =
                        nearby.GroundPosition;
                    return true;
                }
            }
        }

        float direction =
            controller.HomePosition.X <
            _battlefield.Metadata.WidthMeters *
            0.5f
                ? 1.0f
                : -1.0f;
        Vector2[] offsets =
        [
            new(70.0f, -90.0f),
            new(70.0f, 90.0f),
            new(130.0f, -140.0f),
            new(130.0f, 140.0f),
            new(190.0f, -80.0f),
            new(190.0f, 80.0f),
            new(240.0f, -150.0f),
            new(240.0f, 150.0f),
            new(290.0f, -40.0f),
            new(290.0f, 40.0f),
            new(-70.0f, -90.0f),
            new(-70.0f, 90.0f),
            new(-130.0f, -150.0f),
            new(-130.0f, 150.0f),
            new(-180.0f, -60.0f),
            new(-180.0f, 60.0f),
            new(110.0f, -210.0f),
            new(110.0f, 210.0f),
            new(210.0f, -210.0f),
            new(210.0f, 210.0f),
            new(-110.0f, -210.0f),
            new(-110.0f, 210.0f),
            new(-190.0f, -210.0f),
            new(-190.0f, 210.0f)
        ];

        for (int index = 0;
             index < offsets.Length;
             index++)
        {
            Vector2 offset =
                offsets[
                    (index +
                     (int)buildingId.Value) %
                    offsets.Length];

            Vector3 candidate =
                controller.HomePosition +
                new Vector3(
                    offset.X * direction,
                    0.0f,
                    offset.Y);

            BuildingPlacementResult result =
                _placement.Evaluate(
                    context.Entities,
                    controller.Player,
                    buildingId,
                    candidate,
                    BuildingOrientation.North);

            if (result.IsValid)
            {
                placement =
                    result.GroundPosition;
                return true;
            }
        }

        placement = default;
        return false;
    }

    private static bool HasPendingBuildRequest(
        SimulationContext context,
        PlayerId owner)
    {
        foreach (EntityId entity in
                 context.Entities.Query<BuildingBuildRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            BuildingBuildRequest request =
                context.Entities.GetComponent<BuildingBuildRequest>(
                    entity);

            if (request.Issuer ==
                owner)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPendingBuilding(
        OwnedState owned,
        BuildingId building) =>
        owned.PendingBuildings.Contains(
            building);

    private static bool HasOperationalExtractor(
        SimulationContext context,
        OwnedState owned,
        ResourceId resource)
    {
        for (int index = 0;
             index < owned.Buildings.Count;
             index++)
        {
            EntityId entity =
                owned.Buildings[index];

            if (context.Entities.TryGetComponent(
                    entity,
                    out ResourceExtractor extractor) &&
                extractor.ResourceId ==
                resource &&
                extractor.Enabled)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPendingExtractor(
        SimulationContext context,
        OwnedState owned,
        ResourceId resource)
    {
        for (int index = 0;
             index < owned.ConstructionSites.Count;
             index++)
        {
            ConstructionSite site =
                context.Entities.GetComponent<ConstructionSite>(
                    owned.ConstructionSites[index]);

            if (site.BuildingId ==
                    BuildingIds.Extractor &&
                site.ExtractedResourceId ==
                    resource)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBootstrapComplete(
        OwnedState owned)
    {
        BuildingId[] required =
        [
            BuildingIds.PowerPlant,
            BuildingIds.StorageDepot,
            BuildingIds.Smelter,
            BuildingIds.Refinery,
            BuildingIds.ElectronicsPlant,
            BuildingIds.LogisticsHub,
            BuildingIds.Barracks,
            BuildingIds.VehicleFactory,
            BuildingIds.AmmunitionPlant,
            BuildingIds.SupplyDepot
        ];

        for (int index = 0;
             index < required.Length;
             index++)
        {
            if (GetBuildingCount(
                    owned,
                    required[index]) == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int CountRemoteBuildings(
        SimulationContext context,
        OwnedState owned,
        BuildingId building,
        Vector3 home,
        float minimumDistanceMeters)
    {
        float minimumSquared =
            minimumDistanceMeters *
            minimumDistanceMeters;
        int count = 0;

        for (int index = 0;
             index < owned.Buildings.Count;
             index++)
        {
            EntityId entity =
                owned.Buildings[index];

            if (!context.Entities.TryGetComponent(
                    entity,
                    out CompletedBuilding completed) ||
                completed.BuildingId !=
                building ||
                !context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform) ||
                HorizontalDistanceSquared(
                    home,
                    transform.Position) <
                minimumSquared)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static Vector3? SelectUnsupportedRemoteHub(
        SimulationContext context,
        OwnedState owned,
        Vector3 home,
        float minimumDistanceMeters,
        float supportRadiusMeters)
    {
        float minimumSquared =
            minimumDistanceMeters *
            minimumDistanceMeters;
        float supportSquared =
            supportRadiusMeters *
            supportRadiusMeters;

        return owned.Buildings
            .Where(
                entity =>
                    context.Entities.TryGetComponent(
                        entity,
                        out CompletedBuilding completed) &&
                    completed.BuildingId ==
                        BuildingIds.LogisticsHub &&
                    context.Entities.TryGetComponent(
                        entity,
                        out WorldTransform transform) &&
                    HorizontalDistanceSquared(
                        home,
                        transform.Position) >=
                        minimumSquared &&
                    !owned.SupplyDepots.Any(
                        depot =>
                            HorizontalDistanceSquared(
                                transform.Position,
                                depot.Position) <=
                            supportSquared))
            .Select(
                entity =>
                    context.Entities.GetComponent<WorldTransform>(
                        entity).Position)
            .OrderBy(
                position =>
                    HorizontalDistanceSquared(
                        home,
                        position))
            .Cast<Vector3?>()
            .FirstOrDefault();
    }

    private BattlefieldSiteDefinition? SelectExpansionSite(
        SkirmishOpponentController controller,
        int cursor)
    {
        BattlefieldSiteDefinition[] candidates =
            _battlefield.Sites
                .Where(
                    site =>
                        site.Kind ==
                            BattlefieldSiteKind.Expansion &&
                        IsOnHomeSideOrCentral(
                            controller,
                            site.Position))
                .OrderBy(
                    site =>
                        HorizontalDistanceSquared(
                            controller.HomePosition,
                            site.Position))
                .ToArray();

        if (candidates.Length == 0)
        {
            candidates =
                _battlefield.Sites
                    .Where(
                        static site =>
                            site.Kind ==
                            BattlefieldSiteKind.Expansion)
                    .OrderBy(
                        site =>
                            HorizontalDistanceSquared(
                                controller.HomePosition,
                                site.Position))
                    .ToArray();
        }

        if (candidates.Length == 0)
        {
            return null;
        }

        return candidates[
            Math.Abs(cursor) %
            candidates.Length];
    }

    private BattlefieldSiteDefinition[] GetOpponentFacingSites(
        SkirmishOpponentController controller)
    {
        float center =
            _battlefield.Metadata.WidthMeters *
            0.5f;
        bool homeWest =
            controller.HomePosition.X <
            center;

        return _battlefield.Sites
            .Where(
                site =>
                    site.Kind is
                        BattlefieldSiteKind.ForwardOperatingBase or
                        BattlefieldSiteKind.Expansion or
                        BattlefieldSiteKind.MiningOutpost)
            .OrderBy(
                site =>
                    HorizontalDistanceSquared(
                        controller.HomePosition,
                        site.Position))
            .ThenBy(
                site =>
                    homeWest
                        ? site.Position.X
                        : -site.Position.X)
            .ThenBy(
                static site =>
                    site.Key,
                StringComparer.Ordinal)
            .ToArray();
    }

    private bool IsOnHomeSideOrCentral(
        SkirmishOpponentController controller,
        Vector3 position)
    {
        float center =
            _battlefield.Metadata.WidthMeters *
            0.5f;

        return controller.HomePosition.X <
               center
            ? position.X <= center
            : position.X >= center;
    }

    private Vector3 SelectOffensiveWaypoint(
        SkirmishOpponentController controller,
        double aggression)
    {
        if (aggression >= 0.70)
        {
            return SelectDeepOffensiveWaypoint(
                controller);
        }

        BattlefieldSiteDefinition[] sites =
            GetOpponentFacingSites(
                controller);

        if (sites.Length > 0)
        {
            int index =
                sites.Length == 1
                    ? 0
                    : Math.Min(
                        1,
                        sites.Length - 1);

            return sites[index].Position;
        }

        return new Vector3(
            _battlefield.Metadata.WidthMeters * 0.5f,
            controller.HomePosition.Y,
            _battlefield.Metadata.HeightMeters * 0.5f);
    }

    private Vector3 SelectDeepOffensiveWaypoint(
        SkirmishOpponentController controller)
    {
        BattlefieldStartPosition? opposingStart =
            _battlefield.Starts
                .Where(
                    start =>
                        start.Player !=
                        controller.Player)
                .OrderBy(
                    start =>
                        HorizontalDistanceSquared(
                            controller.HomePosition,
                            start.Position))
                .ThenBy(
                    static start =>
                        start.Player.Value)
                .Cast<BattlefieldStartPosition?>()
                .FirstOrDefault();

        if (opposingStart.HasValue)
        {
            return opposingStart.Value.Position;
        }

        float center =
            _battlefield.Metadata.WidthMeters *
            0.5f;
        bool homeWest =
            controller.HomePosition.X <
            center;
        float stagingX =
            _battlefield.Metadata.WidthMeters *
            (homeWest
                ? 0.90f
                : 0.10f);

        return new Vector3(
            stagingX,
            controller.HomePosition.Y,
            _battlefield.Metadata.HeightMeters * 0.5f);
    }

    private static IntelligenceContact? FindClosestCurrentContact(
        FactionIntelligenceSnapshot intelligence,
        Vector3 origin,
        float maximumDistanceMeters)
    {
        float maximumSquared =
            maximumDistanceMeters *
            maximumDistanceMeters;

        IntelligenceContact? best = null;
        float bestDistance =
            float.PositiveInfinity;

        for (int index = 0;
             index < intelligence.Contacts.Count;
             index++)
        {
            IntelligenceContact contact =
                intelligence.Contacts[index];

            if (!contact.IsCurrent)
            {
                continue;
            }

            float distance =
                HorizontalDistanceSquared(
                    origin,
                    contact.LastKnownPosition);

            if (distance >
                    maximumSquared ||
                distance >=
                    bestDistance)
            {
                continue;
            }

            best =
                contact;
            bestDistance =
                distance;
        }

        return best;
    }

    private static EntityId FindIdleUnit(
        SimulationContext context,
        OwnedState owned,
        UnitId unitId)
    {
        foreach (var pair in
                 owned.UnitByEntity
                     .Where(
                         pair =>
                             pair.Value ==
                             unitId)
                     .OrderBy(
                         static pair =>
                             pair.Key))
        {
            EntityId entity =
                pair.Key;

            if (!context.Entities.IsAlive(
                    entity))
            {
                continue;
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out CombatOrderState order) &&
                order.Kind == CombatOrderKind.Retreat)
            {
                continue;
            }

            if (order.Kind == CombatOrderKind.AttackMove &&
                context.Entities.HasComponent<MovementOrder>(
                    entity))
            {
                continue;
            }

            if (context.Entities.HasComponent<ResupplyOrder>(
                    entity))
            {
                continue;
            }

            return entity;
        }

        return EntityId.Invalid;
    }

    private static Vector3 ResolveRecoveryPoint(
        SimulationContext context,
        SkirmishOpponentController controller,
        OwnedState owned)
    {
        if (owned.SupplyDepots.Count == 0)
        {
            return controller.HomePosition;
        }

        return owned.SupplyDepots
            .OrderBy(
                depot =>
                    HorizontalDistanceSquared(
                        controller.HomePosition,
                        depot.Position))
            .ThenBy(
                static depot =>
                    depot.Entity)
            .First()
            .Position;
    }

    private void CaptureDebug(
        EntityId controllerEntity,
        SkirmishOpponentController controller,
        in SkirmishOpponentState state,
        in SkirmishEconomyAssessment economy,
        in SkirmishForceAssessment force,
        bool hasObjective,
        Vector3 objective)
    {
        if (!DebugCaptureEnabled)
        {
            return;
        }

        _debug.Add(
            new SkirmishOpponentDebugReadModel(
                controllerEntity,
                controller.Player,
                state.StrategicState,
                state.ActiveGoal,
                economy,
                force,
                controller.HomePosition,
                objective,
                hasObjective,
                state.LastDecisionTick,
                state.DecisionsTaken));
    }

    private static int GetBuildingCount(
        OwnedState owned,
        BuildingId building) =>
        owned.BuildingCounts.TryGetValue(
            building,
            out int count)
            ? count
            : 0;

    private static int GetUnitCount(
        OwnedState owned,
        UnitId unit) =>
        owned.UnitCounts.TryGetValue(
            unit,
            out int count)
            ? count
            : 0;

    private static float HorizontalDistanceSquared(
        Vector3 left,
        Vector3 right)
    {
        float x =
            left.X - right.X;
        float z =
            left.Z - right.Z;
        return x * x +
               z * z;
    }

    private sealed class OwnedState
    {
        public OwnedState(
            PlayerId player,
            FactionId faction)
        {
            Player = player;
            Faction = faction;
        }

        public PlayerId Player { get; }

        public FactionId Faction { get; }

        public List<EntityId> Buildings { get; } = new();

        public Dictionary<BuildingId, int> BuildingCounts { get; } = new();

        public HashSet<BuildingId> PendingBuildings { get; } = new();

        public List<EntityId> ConstructionSites { get; } = new();

        public HashSet<InventoryId> InventoryIds { get; } = new();

        public List<EntityId> ProductionFacilities { get; } = new();

        public List<EntityId> UnitProductionFacilities { get; } = new();

        public List<(EntityId Entity, Vector3 Position)> SupplyDepots { get; } =
            new();

        public List<EntityId> Units { get; } = new();

        public List<EntityId> CombatUnits { get; } = new();

        public Dictionary<EntityId, UnitId> UnitByEntity { get; } = new();

        public Dictionary<UnitId, int> UnitCounts { get; } = new();

        public Dictionary<UnitId, int> PendingUnitCounts { get; } = new();

        public Dictionary<EntityId, int> PendingRequestsPerFacility { get; } =
            new();

        public double PowerGeneration { get; set; }

        public double PowerDemand { get; set; }

        public int OfflineConsumers { get; set; }
    }
}