using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game.Tests;

internal sealed class SkirmishScenarioHarness
{
    private readonly VerticalSliceScenario _scenario;

    private SkirmishScenarioHarness(VerticalSliceScenario scenario)
    {
        _scenario = scenario;
    }

    public SimulationCoordinator Simulation => _scenario.Simulation;

    public PrototypeBattlefieldDefinition Battlefield => _scenario.Battlefield;

    public TerrainWorld Terrain => _scenario.Terrain;

    public PrototypeBattlefieldRuntime BattlefieldRuntime =>
        _scenario.BattlefieldRuntime;

    public InventoryStore Inventories => _scenario.Inventories;

    public LogisticsNetwork Logistics => _scenario.Logistics;

    public CargoTransportSystem CargoTransport => _scenario.CargoTransport;

    public AutomatedDistributionSystem AutomatedDistribution =>
        _scenario.AutomatedDistribution;

    public FactionIntelligenceStore Intelligence => _scenario.Intelligence;

    public SkirmishOpponentSystem Opponents => _scenario.Opponents;

    public UnitFactory UnitFactory => _scenario.UnitFactory;

    public SkirmishStartingBase West => _scenario.West;

    public SkirmishStartingBase East => _scenario.East;

    public static SkirmishScenarioHarness Create(
        ulong seed = 17,
        SkirmishOpponentConfiguration? westConfiguration = null,
        SkirmishOpponentConfiguration? eastConfiguration = null,
        SkirmishStartingStock? startingStock = null)
    {
        SkirmishOpponentConfiguration validationWest =
            westConfiguration ??
            CreateTestConfiguration();
        SkirmishOpponentConfiguration validationEast =
            eastConfiguration ??
            CreateTestConfiguration();

        return new SkirmishScenarioHarness(
            VerticalSliceScenario.Create(
                seed,
                validationWest,
                validationEast,
                startingStock));
    }

    public MatchState GetMatchState() =>
        _scenario.GetMatchState();

    public SkirmishOpponentState GetOpponentState(
        PlayerId player) =>
        _scenario.GetOpponentState(player);

    public int CountBuildings(
        PlayerId owner,
        BuildingId buildingId) =>
        _scenario.CountBuildings(
            owner,
            buildingId);

    public int CountUnits(
        PlayerId owner,
        UnitId unitId) =>
        _scenario.CountUnits(
            owner,
            unitId);

    public bool RunUntil(
        Func<SkirmishScenarioHarness, bool> condition,
        ulong maximumTicks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);

        return _scenario.RunUntil(
            _ => condition(this),
            maximumTicks,
            cancellationToken);
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
