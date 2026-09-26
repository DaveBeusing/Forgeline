using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game.Tests;

internal sealed class SkirmishScenarioHarness
{
    private SkirmishScenarioHarness(
        SimulationCoordinator simulation,
        PrototypeBattlefieldDefinition battlefield,
        TerrainWorld terrain,
        PrototypeBattlefieldRuntime battlefieldRuntime,
        InventoryStore inventories,
        LogisticsNetwork logistics,
        CargoTransportSystem cargoTransport,
        AutomatedDistributionSystem automatedDistribution,
        FactionIntelligenceStore intelligence,
        SkirmishOpponentSystem opponents,
        UnitFactory unitFactory,
        SkirmishStartingBase west,
        SkirmishStartingBase east)
    {
        Simulation = simulation;
        Battlefield = battlefield;
        Terrain = terrain;
        BattlefieldRuntime = battlefieldRuntime;
        Inventories = inventories;
        Logistics = logistics;
        CargoTransport = cargoTransport;
        AutomatedDistribution = automatedDistribution;
        Intelligence = intelligence;
        Opponents = opponents;
        UnitFactory = unitFactory;
        West = west;
        East = east;
    }

    public SimulationCoordinator Simulation { get; }

    public PrototypeBattlefieldDefinition Battlefield { get; }

    public TerrainWorld Terrain { get; }

    public PrototypeBattlefieldRuntime BattlefieldRuntime { get; }

    public InventoryStore Inventories { get; }

    public LogisticsNetwork Logistics { get; }

    public CargoTransportSystem CargoTransport { get; }

    public AutomatedDistributionSystem AutomatedDistribution { get; }

    public FactionIntelligenceStore Intelligence { get; }

    public SkirmishOpponentSystem Opponents { get; }

    public UnitFactory UnitFactory { get; }

    public SkirmishStartingBase West { get; }

    public SkirmishStartingBase East { get; }

    public static SkirmishScenarioHarness Create(
        ulong seed = 17,
        SkirmishOpponentConfiguration? westConfiguration = null,
        SkirmishOpponentConfiguration? eastConfiguration = null,
        SkirmishStartingStock? startingStock = null)
    {
        PrototypeBattlefieldDefinition battlefield =
            PrototypeBattlefieldDefinition.Create();
        TerrainWorld terrain =
            PrototypeBattlefieldTerrainFactory.Create(
                battlefield);

        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                seed: seed,
                initialEntityCapacity: 8_192);

        var spatialIndex =
            new SpatialGridIndex(
                new SpatialGridSettings
                {
                    World = terrain.Settings,
                    CellSizeMeters =
                        SpatialGridSettings.DefaultCellSizeMeters
                });
        var spatialSynchronizer =
            new SpatialIndexSynchronizer(
                spatialIndex);

        BuildingDefinitionCatalog buildingDefinitions =
            DirectorateContent.CreateBuildingCatalog();
        UnitDefinitionCatalog unitDefinitions =
            DirectorateContent.CreateUnitCatalog();
        ResourceCatalog resources =
            InitialResourceDefinitions.CreateCatalog();
        ProductionRecipeCatalog recipes =
            InitialProductionRecipes.CreateCatalog();
        WeaponCatalog weapons =
            DirectorateContent.CreateWeaponCatalog();
        ArmorCatalog armor =
            DirectorateContent.CreateArmorCatalog();
        ArtilleryWeaponCatalog artilleryWeapons =
            DirectorateContent.CreateArtilleryWeaponCatalog();
        FactionContentDefinition faction =
            DirectorateContent.CreateFactionDefinition();

        GameContentValidator.ValidateDirectorate(
            faction,
            resources,
            buildingDefinitions,
            unitDefinitions,
            recipes,
            weapons,
            armor,
            artilleryWeapons);

        var inventories =
            new InventoryStore();
        var buildingPlacement =
            new BuildingPlacementService(
                buildingDefinitions,
                terrain,
                spatialIndex);
        var buildingCommands =
            new BuildingCommandProcessingSystem(
                buildingDefinitions,
                buildingPlacement,
                inventories,
                spatialIndex);
        var buildingConstruction =
            new BuildingConstructionSystem(
                buildingDefinitions,
                inventories,
                spatialIndex);
        var power =
            new PowerNetworkSystem();
        var production =
            new ProductionSystem(
                recipes,
                inventories);
        var extraction =
            new ResourceExtractionSystem(
                inventories: inventories);

        var logistics =
            new LogisticsNetwork();
        PrototypeBattlefieldRuntime battlefieldRuntime =
            PrototypeBattlefieldRuntime.Load(
                simulation.Entities,
                battlefield,
                terrain,
                logistics);
        var logisticsDisruption =
            new LogisticsDisruptionSystem(
                logistics);
        var cargoTransport =
            new CargoTransportSystem(
                logistics,
                inventories);
        var automatedDistribution =
            new AutomatedDistributionSystem(
                logistics,
                inventories,
                cargoTransport,
                retryDelayTicks: 10,
                maximumTransportAttempts: 8,
                fairnessAgingTicks: 100);
        var battlefieldSupply =
            new BattlefieldSupplySystem(
                inventories);

        var intelligence =
            new FactionIntelligenceStore(
                new IntelligenceGridSettings
                {
                    CellSizeMeters = 32.0f
                });
        var battlefieldIntelligence =
            new BattlefieldIntelligenceSystem(
                intelligence,
                spatialIndex);
        var intelligenceAvailability =
            new IntelligenceTargetAvailabilityPolicy(
                simulation.Entities,
                intelligence);

        var unitFactory =
            new UnitFactory(
                simulation.Entities,
                inventories,
                cargoTransport);
        var unitProduction =
            new UnitProductionSystem(
                unitDefinitions,
                inventories,
                unitFactory);

        BattlefieldStartPosition westStart =
            battlefield.Starts.Single(
                static start =>
                    start.Player ==
                    new PlayerId(1));
        BattlefieldStartPosition eastStart =
            battlefield.Starts.Single(
                static start =>
                    start.Player ==
                    new PlayerId(2));
        SkirmishStartingBase west =
            SkirmishStartingBaseFactory.Create(
                simulation.Entities,
                inventories,
                unitFactory,
                terrain,
                westStart,
                startingStock);
        SkirmishStartingBase east =
            SkirmishStartingBaseFactory.Create(
                simulation.Entities,
                inventories,
                unitFactory,
                terrain,
                eastStart,
                startingStock);

        _ = battlefieldRuntime.AttachCommandCoreObjectives(
            simulation.Entities,
            new Dictionary<PlayerId, EntityId>
            {
                [west.Player] =
                    west.CommandCore,
                [east.Player] =
                    east.CommandCore
            });

        var navigationObstacles =
            new List<AxisAlignedBounds>(
                battlefield.StaticNavigationObstacles);

        foreach (EntityId entity in
                 simulation.Entities.Query<
                     WorldTransform,
                     SpatialPresence>())
        {
            SpatialPresence presence =
                simulation.Entities.GetComponent<SpatialPresence>(
                    entity);

            if (presence.Metadata.Mobility !=
                SpatialMobility.Static)
            {
                continue;
            }

            WorldTransform transform =
                simulation.Entities.GetComponent<WorldTransform>(
                    entity);
            navigationObstacles.Add(
                presence.CreateEntry(
                    entity,
                    transform).Bounds);
        }

        var gridSettings =
            new NavigationGridSettings
            {
                CellSizeMeters = 32.0f,
                StaticObstacleClearanceMeters = 0.5f
            };
        var sectorSettings =
            new NavigationSectorSettings
            {
                SectorSizeCells = 4
            };
        NavigationWorld navigationWorld =
            NavigationWorld.Build(
                terrain,
                navigationObstacles,
                gridSettings,
                sectorSettings);
        var pathfinder =
            new HierarchicalPathfinder(
                navigationWorld);
        var navigation =
            new HierarchicalNavigationSystem(
                pathfinder);
        var formationMovement =
            new FormationMovementSystem(
                pathfinder);
        var groundMovement =
            new GroundMovementSystem(
                terrain,
                spatialIndex);
        var strategicInfrastructure =
            new StrategicInfrastructureSystem(
                logistics,
                terrain,
                navigation,
                navigationObstacles,
                gridSettings,
                sectorSettings);

        var combatRuntime =
            new CombatRuntime();
        var targetAcquisition =
            new TargetAcquisitionSystem(
                weapons,
                spatialIndex,
                intelligenceAvailability);
        var combatExecution =
            new CombatExecutionSystem(
                weapons,
                inventories,
                combatRuntime,
                spatialIndex,
                intelligenceAvailability);
        var artillery =
            new ArtilleryFireMissionSystem(
                artilleryWeapons,
                inventories,
                combatRuntime,
                intelligence,
                terrain,
                spatialIndex);
        var damage =
            new CombatDamageResolutionSystem(
                combatRuntime,
                weapons,
                armor,
                artilleryWeapons);
        var lifecycle =
            new CombatEntityLifecycleSystem(
                combatRuntime,
                spatialIndex);
        var tacticalPreparation =
            new TacticalOrderPreparationSystem();
        var tacticalOpponent =
            new TacticalTestOpponentSystem(
                intelligence);
        var automaticResupply =
            new AutomaticResupplyDecisionSystem(
                inventories);
        var tacticalCombat =
            new TacticalCombatSystem(
                weapons,
                intelligence);
        var readiness =
            new CombatReadinessSystem(
                inventories,
                weapons,
                artilleryWeapons);
        var logisticsRegistration =
            new BuildingLogisticsRegistrationSystem(
                logistics);
        var roadAccess =
            new PrototypeRoadAccessSystem(
                logistics,
                battlefieldRuntime.RoadNodes);

        var configurations =
            new Dictionary<PlayerId, SkirmishOpponentConfiguration>
            {
                [west.Player] =
                    westConfiguration ??
                    CreateTestConfiguration(),
                [east.Player] =
                    eastConfiguration ??
                    CreateTestConfiguration()
            };
        var opponents =
            new SkirmishOpponentSystem(
                buildingDefinitions,
                unitDefinitions,
                inventories,
                buildingPlacement,
                intelligence,
                battlefield,
                configurations)
            {
                DebugCaptureEnabled = true
            };

        var matchObjectives =
            new MatchObjectiveSystem(
                battlefieldRuntime.MatchStateEntity);

        simulation.RegisterSystem(
            buildingCommands);
        simulation.RegisterSystem(
            logisticsDisruption);
        simulation.RegisterSystem(
            strategicInfrastructure);

        simulation.RegisterSystem(
            opponents);
        simulation.RegisterSystem(
            tacticalOpponent);
        simulation.RegisterSystem(
            tacticalPreparation);
        simulation.RegisterSystem(
            automaticResupply);

        simulation.RegisterSystem(
            formationMovement);
        simulation.RegisterSystem(
            navigation);
        simulation.RegisterSystem(
            groundMovement);
        simulation.RegisterSystem(
            new SpatialIndexSystem(
                spatialSynchronizer));

        simulation.RegisterSystem(
            battlefieldIntelligence);
        simulation.RegisterSystem(
            targetAcquisition);
        simulation.RegisterSystem(
            tacticalCombat);
        simulation.RegisterSystem(
            artillery);
        simulation.RegisterSystem(
            combatExecution);
        simulation.RegisterSystem(
            damage);

        simulation.RegisterSystem(
            battlefieldSupply);
        simulation.RegisterSystem(
            automatedDistribution);
        simulation.RegisterSystem(
            cargoTransport);
        simulation.RegisterSystem(
            power);
        simulation.RegisterSystem(
            production);
        simulation.RegisterSystem(
            unitProduction);
        simulation.RegisterSystem(
            buildingConstruction);
        simulation.RegisterSystem(
            extraction);

        simulation.RegisterSystem(
            logisticsRegistration);
        simulation.RegisterSystem(
            roadAccess);
        simulation.RegisterSystem(
            lifecycle);
        simulation.RegisterSystem(
            new SpatialIndexCleanupSystem(
                spatialSynchronizer));

        simulation.RegisterSystem(
            readiness);
        simulation.RegisterSystem(
            matchObjectives);

        return new SkirmishScenarioHarness(
            simulation,
            battlefield,
            terrain,
            battlefieldRuntime,
            inventories,
            logistics,
            cargoTransport,
            automatedDistribution,
            intelligence,
            opponents,
            unitFactory,
            west,
            east);
    }

    public MatchState GetMatchState() =>
        Simulation.Entities.GetComponent<MatchState>(
            BattlefieldRuntime.MatchStateEntity);

    public SkirmishOpponentState GetOpponentState(
        PlayerId player)
    {
        EntityId controller =
            player == West.Player
                ? West.Controller
                : player == East.Player
                    ? East.Controller
                    : EntityId.Invalid;

        if (!controller.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(player));
        }

        return Simulation.Entities.GetComponent<SkirmishOpponentState>(
            controller);
    }

    public int CountBuildings(
        PlayerId owner,
        BuildingId buildingId)
    {
        int count = 0;

        foreach (EntityId entity in
                 Simulation.Entities.Query<CompletedBuilding>())
        {
            CompletedBuilding building =
                Simulation.Entities.GetComponent<CompletedBuilding>(
                    entity);

            if (building.Owner == owner &&
                building.BuildingId == buildingId)
            {
                count++;
            }
        }

        return count;
    }

    public int CountUnits(
        PlayerId owner,
        UnitId unitId)
    {
        int count = 0;

        foreach (EntityId entity in
                 Simulation.Entities.Query<UnitIdentity>())
        {
            if (!Simulation.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) ||
                controllable.Owner != owner)
            {
                continue;
            }

            UnitIdentity identity =
                Simulation.Entities.GetComponent<UnitIdentity>(
                    entity);

            if (identity.UnitId == unitId)
            {
                count++;
            }
        }

        return count;
    }

    public bool RunUntil(
        Func<SkirmishScenarioHarness, bool> condition,
        ulong maximumTicks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);

        for (ulong tick = 0;
             tick < maximumTicks &&
             !cancellationToken.IsCancellationRequested;
             tick++)
        {
            Simulation.AdvanceOneTick();

            if (condition(this))
            {
                return true;
            }
        }

        return condition(this);
    }

    private static SkirmishOpponentConfiguration CreateTestConfiguration() =>
        new()
        {
            ReactionCadenceTicks = 10,
            Aggression = 0.8,
            ExpansionReadinessThreshold = 0.42,
            OffensiveReadinessThreshold = 0.58,
            RetreatThreshold = 0.22,
            ResupplyThreshold = 0.22,
            MinimumAttackUnits = 3,
            MaximumAttackUnits = 8,
            MaximumQueuedUnitsPerFacility = 2,
            DefensiveRadiusMeters = 600.0f,
            ObjectivePressureLeashMeters = 240.0f,
            ArtilleryCadenceTicks = 60
        };
}