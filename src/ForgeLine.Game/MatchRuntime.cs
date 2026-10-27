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

public sealed class MatchRuntime : IDisposable
{
    private readonly JobScheduler? _ownedScheduler;
    private bool _disposed;

    private MatchRuntime(
        MatchRuntimeSettings runtimeSettings,
        MatchConfiguration matchConfiguration,
        JobScheduler? scheduler,
        bool ownsScheduler,
        MatchRuntimeServices services,
        SimulationCoordinator simulation,
        BattlefieldDefinition battlefield,
        TerrainWorld terrain,
        BattlefieldRuntime battlefieldRuntime,
        InventoryStore inventories,
        LogisticsNetwork logistics,
        CargoTransportSystem cargoTransport,
        CargoConvoySystem cargoConvoys,
        AutomatedDistributionSystem automatedDistribution,
        OperationsRuleSystem operationsRules,
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
        SkirmishMatchInitialization initialization, GameplayProgressJournal progress)
    {
        RuntimeSettings = runtimeSettings;
        PersistenceBaseSettings = runtimeSettings;
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
        CargoConvoys = cargoConvoys;
        AutomatedDistribution = automatedDistribution;
        OperationsRules = operationsRules;
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
        Replay = new MatchReplayRecorder(simulation);
        Initialization = initialization;
        Progress = progress;
    }

    public GameplayProgressJournal Progress { get; }

    public MatchRuntimeSettings RuntimeSettings { get; }
    internal MatchRuntimeSettings PersistenceBaseSettings { get; private set; }
    public DeveloperInitialProfile? InitialDeveloperProfile { get; private set; }

    public MatchConfiguration MatchConfiguration { get; }

    public JobScheduler? Scheduler { get; }

    public bool OwnsScheduler => _ownedScheduler is not null;

    public MatchRuntimeServices Services { get; }

    public SimulationCoordinator Simulation { get; }

    public BattlefieldDefinition Battlefield { get; }

    public TerrainWorld Terrain { get; }

    public BattlefieldRuntime BattlefieldRuntime { get; }

    public InventoryStore Inventories { get; }

    public LogisticsNetwork Logistics { get; }

    public CargoTransportSystem CargoTransport { get; }

    public CargoConvoySystem CargoConvoys { get; }

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

    public MatchReplayRecorder Replay { get; }

    public OperationsRuleSystem OperationsRules { get; }

    public SkirmishMatchInitialization Initialization { get; }

    public SkirmishStartingBase GetBase(PlayerId player) => Initialization.GetBase(player);

    public static MatchRuntime Create(MatchRuntimeSettings runtimeSettings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeSettings);
        if (runtimeSettings.InitialDeveloperProfile is { } initial)
        {
#if FORGELINE_DEVELOPER_TOOLS
            return CreateDeveloper(runtimeSettings, initial, DeveloperToolsCapability.Current, cancellationToken);
#else
            throw new InvalidOperationException("Developer preset profiles are unavailable in Public builds.");
#endif
        }
        return CreateInternal(runtimeSettings, null, cancellationToken);
    }

    public static MatchRuntime CreateDeveloper(MatchRuntimeSettings basis, DeveloperInitialProfile profile,
        DeveloperToolsCapability capability, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capability); ArgumentNullException.ThrowIfNull(profile);
        if (!capability.IsCompiled || !ReferenceEquals(capability, DeveloperToolsCapability.Current))
            throw new InvalidOperationException("An approved compiled host is required for initial developer overrides.");
        return CreateInternal(basis with { InitialDeveloperProfile = null }, profile, cancellationToken);
    }
    private static MatchRuntime CreateInternal(MatchRuntimeSettings runtimeSettings, DeveloperInitialProfile? initial,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtimeSettings);
        runtimeSettings.Validate();
#if FORGELINE_DEVELOPER_TOOLS
        DeveloperRestartPlan? developerPlan = null;
        if (initial is not null)
        {
            var overrides = initial.Parameters.Select(p => new DeveloperTuningOverride(p.Id, p.Target, p.Value)).ToArray();
            if (!DeveloperRestartBuilder.TryBuild(runtimeSettings, initial.Metadata, overrides, out developerPlan, out var failure))
                throw new ArgumentException("Invalid initial developer overrides: " + failure, nameof(initial));
            runtimeSettings = developerPlan!.Settings;
        }
        else if (runtimeSettings.Composition.Identity is not null && ScenarioRegistry.Default.Descriptors.Any(d => d.Key == runtimeSettings.Composition.Key))
            developerPlan = DeveloperRestartBuilder.ForExisting(runtimeSettings);
#else
        if (initial is not null) throw new InvalidOperationException("Developer initial overrides are unavailable publicly.");
#endif
        cancellationToken.ThrowIfCancellationRequested();

        JobScheduler? scheduler =
            runtimeSettings.SchedulerOwnership switch
            {
                MatchSchedulerOwnership.None =>
                    null,
                MatchSchedulerOwnership.Host =>
                    runtimeSettings.Scheduler,
                MatchSchedulerOwnership.Runtime =>
                    new JobScheduler(),
                _ =>
                    throw new InvalidOperationException(
                        "Match scheduler ownership is invalid.")
            };
        bool ownsScheduler =
            runtimeSettings.SchedulerOwnership ==
            MatchSchedulerOwnership.Runtime;

        try
        {
            var runtime = CreateCore(runtimeSettings, scheduler, ownsScheduler, cancellationToken);
#if FORGELINE_DEVELOPER_TOOLS
            try
            {
                runtime.PersistenceBaseSettings = developerPlan?.BaseSettings ?? runtimeSettings;
                runtime.InitialDeveloperProfile = developerPlan?.InitialOverrides.Parameters.Count > 0 ? developerPlan.InitialOverrides : null;
                var gateway = new DeveloperCommandGateway(runtime, developerPlan);
                runtime.Services.DeveloperActions = gateway; runtime.Services.DeveloperParameters = gateway.Parameters;
                runtime.Services.DeveloperCatalog = gateway;
            }
            catch { runtime.Dispose(); throw; }
#endif
            return runtime;
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

    private static MatchRuntime CreateCore(
        MatchRuntimeSettings runtimeSettings,
        JobScheduler? scheduler,
        bool ownsScheduler,
        CancellationToken cancellationToken)
    {
        MatchScenarioSettings settings =
            runtimeSettings.Scenario;

        BattlefieldDefinition battlefield =
            runtimeSettings.Composition.Battlefield;
        MatchConfiguration matchConfiguration =
            runtimeSettings.CreateMatchConfiguration(
                battlefield);
        TerrainWorld terrain =
            runtimeSettings.Composition.CreateTerrain(battlefield);

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
                            runtimeSettings.EnableDiagnostics,
                        TrackPhaseTiming = runtimeSettings.EnablePhaseTiming
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

        MatchContent content = runtimeSettings.Composition.Content;
        BuildingDefinitionCatalog buildingDefinitions = content.Buildings;
        UnitDefinitionCatalog unitDefinitions = content.Units;
        TechnologyDefinitionCatalog technologyDefinitions = content.Technologies;
        ResourceCatalog resources = content.Resources;
        ProductionRecipeCatalog recipes = content.Recipes;
        WeaponCatalog weapons = content.Weapons;
        ArmorCatalog armor = content.Armor;
        ArtilleryWeaponCatalog artilleryWeapons = content.Artillery;

        var inventories =
            new InventoryStore();
        var buildingPlacement =
            new BuildingPlacementService(
                buildingDefinitions,
                terrain,
                spatialIndex,
                new BattlefieldBuildableAreaQuery(
                    battlefield));
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
                inventories,
                automaticProcessing: true);
        var progress = new GameplayProgressJournal(simulation.Entities, simulation.SessionId);
        simulation.RegisterTickObserver(progress);
        var extraction =
            new ResourceExtractionSystem(
                sink: progress, inventories: inventories);

        var logistics =
            new LogisticsNetwork();
        BattlefieldRuntime battlefieldRuntime =
            BattlefieldRuntime.Load(
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
        var docking = new VehicleDockingService(terrain, spatialIndex, navigation);
        var cargoTransport =
            new CargoTransportSystem(
                logistics,
                inventories,
                navigation, docking) { Progress = progress };
        var operationsRules = new OperationsRuleSystem(simulation, inventories, recipes, battlefieldRuntime.MatchStateEntity);
        var cargoConvoys = new CargoConvoySystem(logistics);
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
                inventories, docking)
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture,
                Progress = progress
            };

        var intelligence =
            new FactionIntelligenceStore(
                new IntelligenceGridSettings
                {
                    CellSizeMeters = 32.0f
                });
        var tacticalAreas = battlefield.TacticalAreas;
        var visualOcclusion = new VisualOcclusionPolicy(terrain, tacticalAreas);
        var battlefieldIntelligence =
            new BattlefieldIntelligenceSystem(
                intelligence,
                spatialIndex, technologyDefinitions, visualOcclusion)
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
                cargoTransport, terrain);
        var unitProduction =
            new UnitProductionSystem(
                unitDefinitions,
                inventories,
                unitFactory) { Progress = progress };
        progress.Work = (entities, entity) => PlayerExperienceSnapshotFactory.CaptureWork(entities, entity, production, unitProduction, unitDefinitions, buildingDefinitions);
        var technologyResearch =
            new TechnologyResearchSystem(
                technologyDefinitions,
                inventories) { Progress = progress };

        SkirmishMatchInitialization initialization =
            SkirmishMatchInitializer.Initialize(
                runtimeSettings.Composition,
                simulation.Entities,
                inventories,
                unitFactory,
                terrain,
                battlefield,
                battlefieldRuntime,
                matchConfiguration,
                settings.StartingStock);
        BridgeCombatInitialization.Initialize(simulation.Entities, logistics,
            battlefieldRuntime.CrossingEntities.Values, matchConfiguration.Participants.Select(participant => participant.Faction));
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
        navigation.ConnectVehicleRouting(terrain, simulation, groundMovement.NavigationClearanceMeters, scheduler,
            runtimeSettings.Composition.Key + "|" + runtimeSettings.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "|" + System.Text.Json.JsonSerializer.Serialize(runtimeSettings.Composition.Identity));
        groundMovement.ConnectNavigation(navigation, terrain, battlefield.StaticNavigationObstacles);
        var strategicInfrastructure =
            new StrategicInfrastructureSystem(
                logistics,
                terrain,
                navigation,
                battlefield.StaticNavigationObstacles,
                gridSettings,
                sectorSettings);
        strategicInfrastructure.BindBridgeRepairs(simulation, inventories, intelligence);

        var conveyors = new ConveyorConstructionService(simulation, terrain, spatialIndex, inventories, battlefield)
        { NavigationChanged = strategicInfrastructure.RefreshConveyorNavigation };
        var demolition = new BuildingDemolitionSystem(
            inventories,
            spatialIndex,
            matchConfiguration.Participants.ToDictionary(
                participant => participant.Player,
                participant => initialization.GetBase(participant.Player).CommandCore),
            conveyors);

        var combatRuntime =
            new CombatRuntime();
        var terrainLineOfFire =
            new TerrainLineOfFirePolicy(
                terrain);
        var targetAcquisition =
            new TargetAcquisitionSystem(
                weapons,
                spatialIndex,
                intelligenceAvailability,
                terrainLineOfFire)
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
                intelligenceAvailability,
                terrainLineOfFire);
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
                artilleryWeapons, tacticalAreas);
        var buildingDestruction = new BuildingCombatDestruction(
            inventories, buildingConstruction, production, unitProduction,
            technologyResearch, cargoTransport, automatedDistribution);
        var lifecycle =
            new CombatEntityLifecycleSystem(
                combatRuntime,
                spatialIndex,
                buildingDestruction,
                strategicInfrastructure.RefreshRemovedStaticEntities,
                inventories,
                strategicInfrastructure.DisableBridgeFromCombat);
        var suppression =
            new SuppressionSystem
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };
        var combatDebugSnapshots =
            new CombatDebugSnapshotSystem(
                weapons,
                combatRuntime,
                targetAcquisition,
                damage,
                suppression)
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
        var repairRecovery =
            new RepairRecoverySystem(
                inventories)
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };
        var tacticalCombat =
            new TacticalCombatSystem(
                weapons,
                intelligence,
                intelligenceAvailability,
                terrainLineOfFire)
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
            new RoadAccessSystem(
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
                settings.OpponentConfigurations.TryGetValue(participant.Player.Value, out var policy)
                    ? policy
                    : throw new InvalidOperationException($"No opponent policy for player {participant.Player}."));
        }

        var opponents =
            new SkirmishOpponentSystem(
                buildingDefinitions,
                unitDefinitions,
                inventories,
                buildingPlacement,
                intelligence,
                battlefield,
                configurations, navigation, weapons, technologyDefinitions)
            {
                DebugCaptureEnabled =
                    runtimeSettings.EnableDebugCapture
            };

        var matchObjectives =
            new MatchObjectiveSystem(
                battlefieldRuntime.MatchStateEntity);

        var conveyorFlow = new ConveyorSimulationSystem(inventories, automatedDistribution, recipes, unitDefinitions) { Progress = progress };

        ISimulationSystem[] systems =
        [
            buildingCommands,
            demolition,
            logisticsDisruption,
            strategicInfrastructure,
            operationsRules,
            opponents,
            tacticalOpponent,
            new QueuedOrderSystem(intelligence, weapons, artilleryWeapons),
            tacticalPreparation,
            suppression,
            automaticResupply,
            formationMovement,
            cargoConvoys,
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
            repairRecovery,
            automatedDistribution,
            cargoTransport,
            power,
            production,
            unitProduction,
            technologyResearch,
            buildingConstruction,
            extraction,
            conveyorFlow,
            lifecycle,
            logisticsRegistration,
            roadAccess,
            new SpatialIndexCleanupSystem(
                spatialSynchronizer),
            readiness,
            matchObjectives,
            combatDebugSnapshots
        ];

        systems = runtimeSettings.Composition.ConfigureSystems(systems).ToArray();

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

        MatchObjectiveSystem.ActivateMatch(
            simulation.Entities,
            battlefieldRuntime.MatchStateEntity);

        var services =
            new MatchRuntimeServices(
                resources,
                buildingDefinitions,
                unitDefinitions,
                technologyDefinitions,
                recipes,
                weapons,
                artilleryWeapons,
                combatRuntime,
                spatialIndex,
                buildingPlacement,
                buildingCommands,
                buildingConstruction,
                demolition,
                unitProduction,
                technologyResearch,
                groundMovement,
                formationMovement,
                navigation,
                battlefieldIntelligence,
                targetAcquisition,
                tacticalCombat,
                suppression,
                automaticResupply,
                repairRecovery,
                combatDebugSnapshots,
                registeredSystemTypes);

        services.StrategicInfrastructure = strategicInfrastructure;
        services.Conveyors = conveyors;
        services.ConveyorFlow = conveyorFlow;

        return new MatchRuntime(
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
            cargoConvoys,
            automatedDistribution,
            operationsRules,
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
            initialization, progress);
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
        EntityId controller = GetBase(player).Controller;

        if (!Simulation.Entities.IsAlive(controller))
        {
            return SkirmishOpponentState.Initial;
        }

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
#if FORGELINE_DEVELOPER_TOOLS
        (Services.DeveloperActions as DeveloperCommandGateway)?.Dispose();
#endif
        Services.Navigation.Dispose();
        Replay.Dispose();
        OperationsRules.Dispose();
        _ownedScheduler?.Dispose();
    }

    public bool RunUntil(
        Func<MatchRuntime, bool> condition,
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
