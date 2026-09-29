using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Jobs;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class VerticalSliceScenario : IDisposable
{
    private readonly JobScheduler? _ownedScheduler;
    private bool _disposed;

    private VerticalSliceScenario(
        VerticalSliceRuntimeSettings runtimeSettings,
        MatchConfiguration matchConfiguration,
        JobScheduler? scheduler,
        bool ownsScheduler,
        VerticalSliceRuntimeServices services,
        SimulationCoordinator simulation,
        PrototypeBattlefieldDefinition battlefield,
        TerrainWorld terrain,
        PrototypeBattlefieldRuntime battlefieldRuntime,
        InventoryStore inventories,
        LogisticsNetwork logistics,
        CargoTransportSystem cargoTransport,
        AutomatedDistributionSystem automatedDistribution,
        PowerNetworkSystem power,
        ProductionSystem production,
        UnitProductionSystem unitProduction,
        ResourceExtractionSystem extraction,
        BattlefieldSupplySystem battlefieldSupply,
        ArtilleryFireMissionSystem artillery,
        CombatReadinessSystem readiness,
        FactionIntelligenceStore intelligence,
        SkirmishOpponentSystem opponents,
        UnitFactory unitFactory,
        SkirmishStartingBase west,
        SkirmishStartingBase east)
    {
        RuntimeSettings = runtimeSettings;
        MatchConfiguration = matchConfiguration;
        Scheduler = scheduler;
        _ownedScheduler =
            ownsScheduler
                ? scheduler
                : null;
        Services = services;
        Simulation = simulation;
        Battlefield = battlefield;
        Terrain = terrain;
        BattlefieldRuntime = battlefieldRuntime;
        Inventories = inventories;
        Logistics = logistics;
        CargoTransport = cargoTransport;
        AutomatedDistribution = automatedDistribution;
        Power = power;
        Production = production;
        UnitProduction = unitProduction;
        Extraction = extraction;
        BattlefieldSupply = battlefieldSupply;
        Artillery = artillery;
        Readiness = readiness;
        Intelligence = intelligence;
        Opponents = opponents;
        UnitFactory = unitFactory;
        West = west;
        East = east;
    }

    public VerticalSliceRuntimeSettings RuntimeSettings { get; }

    public MatchConfiguration MatchConfiguration { get; }

    public JobScheduler? Scheduler { get; }

    public bool OwnsScheduler => _ownedScheduler is not null;

    public VerticalSliceRuntimeServices Services { get; }

    public SimulationCoordinator Simulation { get; }

    public PrototypeBattlefieldDefinition Battlefield { get; }

    public TerrainWorld Terrain { get; }

    public PrototypeBattlefieldRuntime BattlefieldRuntime { get; }

    public InventoryStore Inventories { get; }

    public LogisticsNetwork Logistics { get; }

    public CargoTransportSystem CargoTransport { get; }

    public AutomatedDistributionSystem AutomatedDistribution { get; }

    public PowerNetworkSystem Power { get; }

    public ProductionSystem Production { get; }

    public UnitProductionSystem UnitProduction { get; }

    public ResourceExtractionSystem Extraction { get; }

    public BattlefieldSupplySystem BattlefieldSupply { get; }

    public ArtilleryFireMissionSystem Artillery { get; }

    public CombatReadinessSystem Readiness { get; }

    public FactionIntelligenceStore Intelligence { get; }

    public SkirmishOpponentSystem Opponents { get; }

    public UnitFactory UnitFactory { get; }

    public SkirmishStartingBase West { get; }

    public SkirmishStartingBase East { get; }

    public static VerticalSliceScenario Create(
        VerticalSliceScenarioSettings settings,
        ulong seed = 17,
        bool enableDiagnostics = false)
    {
        ArgumentNullException.ThrowIfNull(settings);

        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateHeadless(
                settings.Profile,
                seed,
                enableDiagnostics,
                enableDebugCapture: enableDiagnostics) with
            {
                Scenario = settings
            };

        return Create(runtime);
    }

    public static VerticalSliceScenario Create(
        ulong seed = 17,
        SkirmishOpponentConfiguration? westConfiguration = null,
        SkirmishOpponentConfiguration? eastConfiguration = null,
        SkirmishStartingStock? startingStock = null,
        bool enableDiagnostics = false)
    {
        VerticalSliceScenarioSettings gameplay =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Gameplay);

        VerticalSliceScenarioSettings configured =
            gameplay with
            {
                WestOpponent =
                    westConfiguration ??
                    gameplay.WestOpponent,
                EastOpponent =
                    eastConfiguration ??
                    gameplay.EastOpponent,
                StartingStock =
                    startingStock ??
                    gameplay.StartingStock
            };

        return Create(
            VerticalSliceRuntimeSettings.CreateHeadless(
                configured.Profile,
                seed,
                enableDiagnostics,
                enableDebugCapture: enableDiagnostics) with
            {
                Scenario = configured
            });
    }

    public static VerticalSliceScenario Create(
        VerticalSliceRuntimeSettings runtimeSettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeSettings);
        runtimeSettings.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        JobScheduler? scheduler =
            runtimeSettings.SchedulerOwnership switch
            {
                VerticalSliceSchedulerOwnership.None =>
                    null,
                VerticalSliceSchedulerOwnership.Host =>
                    runtimeSettings.Scheduler,
                VerticalSliceSchedulerOwnership.Runtime =>
                    new JobScheduler(),
                _ =>
                    throw new InvalidOperationException(
                        "Vertical-slice scheduler ownership is invalid.")
            };
        bool ownsScheduler =
            runtimeSettings.SchedulerOwnership ==
            VerticalSliceSchedulerOwnership.Runtime;

        try
        {
            return CreateCore(
                runtimeSettings,
                scheduler,
                ownsScheduler,
                cancellationToken);
        }
        catch
        {
            if (ownsScheduler)
            {
                scheduler?.Dispose();
            }

            throw;
        }
    }

    private static VerticalSliceScenario CreateCore(
        VerticalSliceRuntimeSettings runtimeSettings,
        JobScheduler? scheduler,
        bool ownsScheduler,
        CancellationToken cancellationToken)
    {
        VerticalSliceScenarioSettings settings =
            runtimeSettings.Scenario;

        PrototypeBattlefieldDefinition battlefield =
            PrototypeBattlefieldDefinition.Create();
        MatchConfiguration matchConfiguration =
            runtimeSettings.CreateMatchConfiguration(
                battlefield);
        TerrainWorld terrain =
            PrototypeBattlefieldTerrainFactory.Create(
                battlefield);

        cancellationToken.ThrowIfCancellationRequested();

        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                seed: runtimeSettings.Seed,
                initialEntityCapacity:
                    runtimeSettings.InitialEntityCapacity,
                jobScheduler: scheduler,
                diagnosticsOptions:
                    new SimulationDiagnosticsOptions
                    {
                        Enabled =
                            runtimeSettings.EnableDiagnostics
                    });

        var spatialIndex =
            new SpatialGridIndex(
                new SpatialGridSettings
                {
                    World = terrain.Settings,
                    CellSizeMeters =
                        SpatialGridSettings.DefaultCellSizeMeters,
                    EnableQueryTiming =
                        runtimeSettings.EnableSpatialQueryTiming
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
        var gridSettings =
            new NavigationGridSettings
            {
                CellSizeMeters =
                    settings.NavigationCellSizeMeters,
                StaticObstacleClearanceMeters = 0.5f
            };
        var sectorSettings =
            new NavigationSectorSettings
            {
                SectorSizeCells =
                    settings.NavigationSectorSizeCells
            };
        NavigationWorld navigationWorld =
            NavigationWorld.Build(
                terrain,
                battlefield.StaticNavigationObstacles,
                gridSettings,
                sectorSettings);
        var pathfinder =
            new HierarchicalPathfinder(
                navigationWorld);
        var navigation =
            new HierarchicalNavigationSystem(
                pathfinder);
        var cargoTransport =
            new CargoTransportSystem(
                logistics,
                inventories,
                navigation);
        var automatedDistribution =
            new AutomatedDistributionSystem(
                logistics,
                inventories,
                cargoTransport,
                retryDelayTicks:
                    settings.DistributionRetryDelayTicks,
                maximumTransportAttempts:
                    settings.DistributionMaximumTransportAttempts,
                fairnessAgingTicks:
                    settings.DistributionFairnessAgingTicks);
        var battlefieldSupply =
            new BattlefieldSupplySystem(
                inventories)
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };

        var intelligence =
            new FactionIntelligenceStore(
                new IntelligenceGridSettings
                {
                    CellSizeMeters = 32.0f
                });
        var battlefieldIntelligence =
            new BattlefieldIntelligenceSystem(
                intelligence,
                spatialIndex)
            {
                TimingEnabled =
                    runtimeSettings.EnableDebugCapture,
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };
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

        SkirmishMatchInitialization initialization =
            SkirmishMatchInitializer.Initialize(
                simulation.Entities,
                inventories,
                unitFactory,
                terrain,
                battlefield,
                battlefieldRuntime,
                matchConfiguration,
                settings.StartingStock);
        SkirmishStartingBase west =
            initialization.GetBase(
                new PlayerId(1));
        SkirmishStartingBase east =
            initialization.GetBase(
                new PlayerId(2));

        AxisAlignedBounds[] entityObstacles =
            CollectStaticNavigationObstacles(
                simulation);
        var navigationObstacles =
            new List<AxisAlignedBounds>(
                battlefield.StaticNavigationObstacles.Count +
                entityObstacles.Length);
        navigationObstacles.AddRange(
            battlefield.StaticNavigationObstacles);
        navigationObstacles.AddRange(
            entityObstacles);

        navigation.UpdateWorld(
            NavigationWorld.Build(
                terrain,
                navigationObstacles,
                gridSettings,
                sectorSettings));

        cancellationToken.ThrowIfCancellationRequested();

        var formationMovement =
            new FormationMovementSystem(
                pathfinder)
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };
        var groundMovement =
            new GroundMovementSystem(
                terrain,
                spatialIndex)
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };
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
                intelligenceAvailability)
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };
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
                spatialIndex)
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };
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
        var combatDebugSnapshots =
            new CombatDebugSnapshotSystem(
                weapons,
                combatRuntime,
                targetAcquisition,
                damage)
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };
        var tacticalPreparation =
            new TacticalOrderPreparationSystem();
        var tacticalOpponent =
            new TacticalTestOpponentSystem(
                intelligence,
                weapons);
        var automaticResupply =
            new AutomaticResupplyDecisionSystem(
                inventories);
        var tacticalCombat =
            new TacticalCombatSystem(
                weapons,
                intelligence)
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };
        var readiness =
            new CombatReadinessSystem(
                inventories,
                weapons,
                artilleryWeapons)
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };
        var logisticsRegistration =
            new BuildingLogisticsRegistrationSystem(
                logistics);
        var roadAccess =
            new PrototypeRoadAccessSystem(
                logistics,
                battlefieldRuntime.RoadNodes);

        var configurations =
            new Dictionary<
                PlayerId,
                SkirmishOpponentConfiguration>();

        for (int index = 0;
             index < matchConfiguration.Participants.Count;
             index++)
        {
            MatchParticipantConfiguration participant =
                matchConfiguration.Participants[index];

            if (!participant.IsComputerControlled)
            {
                continue;
            }

            configurations.Add(
                participant.Player,
                participant.Player == west.Player
                    ? settings.WestOpponent
                    : settings.EastOpponent);
        }

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
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };

        var matchObjectives =
            new MatchObjectiveSystem(
                battlefieldRuntime.MatchStateEntity);

        ISimulationSystem[] systems =
        [
            buildingCommands,
            logisticsDisruption,
            strategicInfrastructure,
            opponents,
            tacticalOpponent,
            tacticalPreparation,
            automaticResupply,
            formationMovement,
            navigation,
            groundMovement,
            new SpatialIndexSystem(
                spatialSynchronizer),
            battlefieldIntelligence,
            targetAcquisition,
            tacticalCombat,
            artillery,
            combatExecution,
            damage,
            battlefieldSupply,
            automatedDistribution,
            cargoTransport,
            power,
            production,
            unitProduction,
            buildingConstruction,
            extraction,
            logisticsRegistration,
            roadAccess,
            lifecycle,
            new SpatialIndexCleanupSystem(
                spatialSynchronizer),
            readiness,
            matchObjectives,
            combatDebugSnapshots
        ];

        var registeredSystemTypes =
            new Type[systems.Length];

        for (int index = 0;
             index < systems.Length;
             index++)
        {
            ISimulationSystem system =
                systems[index];
            simulation.RegisterSystem(system);
            registeredSystemTypes[index] =
                system.GetType();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var services =
            new VerticalSliceRuntimeServices(
                resources,
                buildingDefinitions,
                unitDefinitions,
                spatialIndex,
                buildingPlacement,
                buildingCommands,
                buildingConstruction,
                unitProduction,
                groundMovement,
                formationMovement,
                navigation,
                battlefieldIntelligence,
                targetAcquisition,
                tacticalCombat,
                automaticResupply,
                combatDebugSnapshots,
                registeredSystemTypes);

        return new VerticalSliceScenario(
            runtimeSettings,
            matchConfiguration,
            scheduler,
            ownsScheduler,
            services,
            simulation,
            battlefield,
            terrain,
            battlefieldRuntime,
            inventories,
            logistics,
            cargoTransport,
            automatedDistribution,
            power,
            production,
            unitProduction,
            extraction,
            battlefieldSupply,
            artillery,
            readiness,
            intelligence,
            opponents,
            unitFactory,
            west,
            east);
    }

    private static AxisAlignedBounds[] CollectStaticNavigationObstacles(
        SimulationCoordinator simulation)
    {
        var obstacles =
            new List<AxisAlignedBounds>();

        foreach (EntityId entity in
                 simulation.Entities.Query<
                     WorldTransform,
                     SpatialPresence>())
        {
            SpatialPresence presence =
                simulation.Entities.GetComponent<
                    SpatialPresence>(
                        entity);

            if (presence.Metadata.Mobility !=
                SpatialMobility.Static)
            {
                continue;
            }

            WorldTransform transform =
                simulation.Entities.GetComponent<
                    WorldTransform>(
                        entity);
            obstacles.Add(
                presence.CreateEntry(
                    entity,
                    transform).Bounds);
        }

        return obstacles.ToArray();
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ownedScheduler?.Dispose();
    }

    public bool RunUntil(
        Func<VerticalSliceScenario, bool> condition,
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
}
