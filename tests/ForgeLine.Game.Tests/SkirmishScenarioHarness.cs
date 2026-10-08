using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game.Tests;

internal sealed class SkirmishScenarioHarness
{
    private readonly MatchRuntime _scenario;

    private SkirmishScenarioHarness(MatchRuntime scenario)
    {
        _scenario = scenario;
    }

    public SkirmishStartingBase GetBase(PlayerId player) => _scenario.GetBase(player);

    public SimulationCoordinator Simulation => _scenario.Simulation;

    public BattlefieldDefinition Battlefield => _scenario.Battlefield;

    public TerrainWorld Terrain => _scenario.Terrain;

    public BattlefieldRuntime BattlefieldRuntime =>
        _scenario.BattlefieldRuntime;

    public InventoryStore Inventories => _scenario.Inventories;

    public LogisticsNetwork Logistics => _scenario.Logistics;

    public CargoTransportSystem CargoTransport => _scenario.CargoTransport;

    public AutomatedDistributionSystem AutomatedDistribution =>
        _scenario.AutomatedDistribution;

    public PowerNetworkSystem Power => _scenario.Power;

    public ProductionSystem Production => _scenario.Production;

    public UnitProductionSystem UnitProduction => _scenario.UnitProduction;

    public ResourceExtractionSystem Extraction => _scenario.Extraction;

    public BattlefieldSupplySystem BattlefieldSupply =>
        _scenario.BattlefieldSupply;

    public ArtilleryFireMissionSystem Artillery => _scenario.Artillery;

    public CombatReadinessSystem Readiness => _scenario.Readiness;

    public FactionIntelligenceStore Intelligence => _scenario.Intelligence;

    public SkirmishOpponentSystem Opponents => _scenario.Opponents;

    public UnitFactory UnitFactory => _scenario.UnitFactory;

    public SkirmishStartingBase West => _scenario.GetBase(new PlayerId(1));

    public SkirmishStartingBase East => _scenario.GetBase(new PlayerId(2));

    public static SkirmishScenarioHarness Create(
        ulong seed = 17,
        SkirmishOpponentConfiguration? westConfiguration = null,
        SkirmishOpponentConfiguration? eastConfiguration = null,
        SkirmishStartingStock? startingStock = null)
    {
        MatchScenarioSettings validation =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);

        MatchScenarioSettings configured =
            validation with
            {
                OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>
                {
                    [1] =
                    westConfiguration ??
                    validation.OpponentConfigurations[1],
                    [2] =
                    eastConfiguration ??
                    validation.OpponentConfigurations[2],
                },
                StartingStock =
                    startingStock ??
                    validation.StartingStock
            };

        return new SkirmishScenarioHarness(
            CentralDivideScenario.Create(
                configured,
                seed));
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

}
