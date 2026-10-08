using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Jobs;
using ForgeLine.Ecs;
using ForgeLine.World;
using ForgeLine.Logistics;
namespace ForgeLine.Game;

public static class CentralDivideScenario
{
    public static MatchRuntime Create(MatchRuntimeSettings settings, CancellationToken cancellationToken = default) =>
        MatchRuntime.Create(settings, cancellationToken);

    public static SkirmishMatchInitialization InitializeMatch(
        EntityRegistry entities, InventoryStore inventories, UnitFactory unitFactory,
        TerrainWorld terrain, BattlefieldDefinition battlefield, BattlefieldRuntime battlefieldRuntime,
        MatchConfiguration configuration, SkirmishStartingStock? startingStock = null) =>
        SkirmishMatchInitializer.Initialize(CreateComposition(), entities, inventories, unitFactory, terrain,
            battlefield, battlefieldRuntime, configuration, startingStock);

    public const string CompositionKey = "central-divide.directorate.v1";

    public static MatchComposition CreateComposition()
    {
        var content = new MatchContent(
            InitialResourceDefinitions.CreateCatalog(),
            DirectorateContent.CreateBuildingCatalog(),
            DirectorateContent.CreateUnitCatalog(),
            DirectorateTechnologyDefinitions.CreateCatalog(),
            InitialProductionRecipes.CreateCatalog(),
            DirectorateContent.CreateWeaponCatalog(),
            DirectorateContent.CreateArmorCatalog(),
            DirectorateContent.CreateArtilleryWeaponCatalog());
        GameContentValidator.ValidateDirectorate(
            DirectorateContent.CreateFactionDefinition(), content.Resources,
            content.Buildings, content.Units, content.Recipes,
            content.Weapons, content.Armor, content.Artillery);
        return new MatchComposition(
            CompositionKey, CentralDivideBattlefield.Create(), content,
            static map => CentralDivideTerrainFactory.Create(map),
            static (entities, inventories, factory, terrain, start, catalogs, stock) =>
                CentralDivideStartingBaseFactory.Create(entities, inventories, factory, terrain, start, stock, catalogs.Units),
            static systems => systems);
    }

    public static MatchRuntime Create(
        MatchScenarioSettings settings,
        ulong seed = 17,
        bool enableDiagnostics = false)
    {
        ArgumentNullException.ThrowIfNull(settings);

        MatchRuntimeSettings runtime =
            CreateHeadless(
                settings.Profile,
                seed,
                enableDiagnostics,
                enableDebugCapture: true) with
            {
                Scenario = settings
            };

        return MatchRuntime.Create(runtime);
    }

    public static MatchRuntime Create(
        ulong seed = 17,
        SkirmishOpponentConfiguration? westConfiguration = null,
        SkirmishOpponentConfiguration? eastConfiguration = null,
        SkirmishStartingStock? startingStock = null,
        bool enableDiagnostics = false)
    {
        MatchScenarioSettings gameplay =
            CreateSettings(
                MatchScenarioProfile.Gameplay);

        MatchScenarioSettings configured =
            gameplay with
            {
                OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>
                {
                    [1] =
                    westConfiguration ??
                    gameplay.OpponentConfigurations[1],
                    [2] =
                    eastConfiguration ??
                    gameplay.OpponentConfigurations[2],
                },
                StartingStock =
                    startingStock ??
                    gameplay.StartingStock
            };

        return MatchRuntime.Create(
            CreateHeadless(
                configured.Profile,
                seed,
                enableDiagnostics,
                enableDebugCapture: enableDiagnostics) with
            {
                Scenario = configured
            });
    }

    public static MatchRuntimeSettings CreateHeadless(
        MatchScenarioProfile profile,
        ulong seed = 17,
        bool enableDiagnostics = false,
        bool enableDebugCapture = false) =>
        new()
        {
            Composition = CreateComposition(),
            Scenario =
                CreateSettings(profile),
            Seed = seed,
            Participants =
                CreateDefaultParticipants(
                    westComputerControlled: true,
                    eastComputerControlled: true),
            EnableDiagnostics = enableDiagnostics,
            EnableDebugCapture = enableDebugCapture,
            EnableSpatialQueryTiming = false,
            SchedulerOwnership =
                MatchSchedulerOwnership.None
        };

    public static MatchRuntimeSettings CreateClient(
        JobScheduler scheduler,
        ulong seed = 17,
        bool enableDiagnostics = true) =>
        new()
        {
            Composition = CreateComposition(),
            Scenario =
                CreateSettings(
                    MatchScenarioProfile.Gameplay),
            Seed = seed,
            Participants =
                CreateDefaultParticipants(
                    westComputerControlled: false,
                    eastComputerControlled: true),
            EnableDiagnostics = enableDiagnostics,
            EnableDebugCapture = false,
            EnableSpatialQueryTiming = true,
            Scheduler = scheduler ??
                throw new ArgumentNullException(nameof(scheduler)),
            SchedulerOwnership =
                MatchSchedulerOwnership.Host
        };

    public static MatchRuntimeSettings CreateOwned(
        MatchScenarioProfile profile,
        ulong seed,
        IReadOnlyList<MatchParticipantConfiguration> participants,
        bool enableDiagnostics = false,
        bool enableDebugCapture = false,
        bool enableSpatialQueryTiming = false) =>
        new()
        {
            Composition = CreateComposition(),
            Scenario =
                CreateSettings(profile),
            Seed = seed,
            Participants = participants,
            EnableDiagnostics = enableDiagnostics,
            EnableDebugCapture = enableDebugCapture,
            EnableSpatialQueryTiming = enableSpatialQueryTiming,
            SchedulerOwnership =
                MatchSchedulerOwnership.Runtime
        };

    public static IReadOnlyList<MatchParticipantConfiguration>
        CreateDefaultParticipants(
            bool westComputerControlled,
            bool eastComputerControlled) =>
        [
            new MatchParticipantConfiguration(
                new PlayerId(1),
                new FactionId(1),
                startIndex: 0,
                westComputerControlled),
            new MatchParticipantConfiguration(
                new PlayerId(2),
                new FactionId(2),
                startIndex: 1,
                eastComputerControlled)
        ];
    public static MatchScenarioSettings CreateSettings(
        MatchScenarioProfile profile) =>
        profile switch
        {
            MatchScenarioProfile.Gameplay =>
                CreateGameplay(),
            MatchScenarioProfile.Validation =>
                CreateValidation(),
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(profile))
        };

    private static MatchScenarioSettings CreateGameplay() =>
        new()
        {
            Profile = MatchScenarioProfile.Gameplay,
            StartingStock = SkirmishStartingStock.Standard,
            OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>
            {
                [1] = new(),
                [2] = new()
            },
            NavigationCellSizeMeters = 16.0f,
            NavigationSectorSizeCells = 8,
            DistributionRetryDelayTicks = 20,
            DistributionMaximumTransportAttempts = 4,
            DistributionFairnessAgingTicks = 200
        };

    private static MatchScenarioSettings CreateValidation() =>
        new()
        {
            Profile = MatchScenarioProfile.Validation,
            StartingStock =
                new SkirmishStartingStock(
                    FerrousOre: 1_200.0,
                    Volatiles: 6_000.0,
                    Silicates: 800.0,
                    Steel: 3_000.0,
                    Fuel: 18_000.0,
                    Electronics: 1_500.0,
                    Ammunition: 1_500.0),
            OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>
            {
                [1] =
                new SkirmishOpponentConfiguration
                {
                    ReactionCadenceTicks = 10,
                    Aggression = 1.0,
                    ExpansionReadinessThreshold = 0.40,
                    OffensiveReadinessThreshold = 0.45,
                    RetreatThreshold = 0.15,
                    ResupplyThreshold = 0.18,
                    OffensiveFuelThreshold = 0.40,
                    MinimumAttackUnits = 3,
                    MaximumAttackUnits = 12,
                    MinimumObjectivePressureUnits = 1,
                    MaximumQueuedUnitsPerFacility = 3,
                    MinimumCargoTrucks = 2,
                    MinimumSupplyTrucks = 2,
                    DefensiveRadiusMeters = 450.0f,
                    ObjectivePressureLeashMeters = 120.0f,
                    ArtilleryCadenceTicks = 50
                },
                [2] =
                new SkirmishOpponentConfiguration
                {
                    ReactionCadenceTicks = 10,
                    Aggression = 0.55,
                    ExpansionReadinessThreshold = 0.42,
                    OffensiveReadinessThreshold = 0.58,
                    RetreatThreshold = 0.22,
                    ResupplyThreshold = 0.22,
                    OffensiveFuelThreshold = 0.46,
                    MinimumAttackUnits = 3,
                    MaximumAttackUnits = 8,
                    MinimumObjectivePressureUnits = 2,
                    MaximumQueuedUnitsPerFacility = 2,
                    MinimumCargoTrucks = 2,
                    MinimumSupplyTrucks = 2,
                    DefensiveRadiusMeters = 600.0f,
                    ObjectivePressureLeashMeters = 240.0f,
                    ArtilleryCadenceTicks = 60
                },
            },
            NavigationCellSizeMeters = 32.0f,
            NavigationSectorSizeCells = 4,
            DistributionRetryDelayTicks = 10,
            DistributionMaximumTransportAttempts = 8,
            DistributionFairnessAgingTicks = 100
        };
}
