using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game.Tests;

internal sealed class SkirmishScenarioHarness
{
    // Isolated policy cases author already researched capabilities. Normal-stock
    // research/payment and fleet progression are covered by TechnologyNormalMatchTests.
    internal static void CompleteResearchForPolicyFixture(MatchRuntime scenario, PlayerId player)
    {
        foreach (TechnologyId id in new[] { TechnologyIds.IndustrialStandardization,
                     TechnologyIds.MechanizedSystems, TechnologyIds.SensorFusion })
        {
            EntityId completed = scenario.Simulation.Entities.CreateEntity();
            scenario.Simulation.Entities.AddComponent(completed,
                new CompletedTechnology(player, id, scenario.Simulation.CurrentTick));
            foreach (TechnologyCapabilityId capability in scenario.Services.TechnologyDefinitions[id].Unlocks)
            {
                EntityId unlock = scenario.Simulation.Entities.CreateEntity();
                scenario.Simulation.Entities.AddComponent(unlock,
                    new TechnologyCapabilityUnlock(player, capability, id, scenario.Simulation.CurrentTick));
            }
        }
    }

    internal static void EnsureCargoForPolicyFixture(MatchRuntime scenario, PlayerId player, int minimum)
    {
        // These fixtures isolate construction policy. Paid fleet growth remains
        // covered by the unmodified strategic-loop and terminal-match gates.
        Vector3 home = scenario.Simulation.Entities.GetComponent<WorldTransform>(scenario.GetBase(player).CommandCore).Position;
        for (int count = scenario.CountUnits(player, UnitIds.CargoTruck); count < minimum; count++)
            scenario.UnitFactory.Create(DirectorateContent.CreateUnitCatalog()[UnitIds.CargoTruck],
                home + new Vector3(0, 0, 100 + count * 20), player);
    }

    internal static bool HasExpansionFundingPrerequisites(MatchRuntime scenario, PlayerId player)
    {
        BuildingId[] industry = [BuildingIds.PowerPlant, BuildingIds.StorageDepot, BuildingIds.Smelter,
            BuildingIds.Refinery, BuildingIds.ElectronicsPlant, BuildingIds.LogisticsHub, BuildingIds.SupplyDepot,
            BuildingIds.Barracks, BuildingIds.VehicleFactory, BuildingIds.AmmunitionPlant, BuildingIds.Radar];
        if (industry.Any(id => scenario.CountBuildings(player, id) == 0)) return false;
        var debug = scenario.Opponents.DebugSnapshot.Single(item => item.Player == player);
        if (debug.Economy.PowerGeneration < debug.Economy.PowerDemand + 20 ||
            debug.Economy.ActiveConstructionSites != 0 || debug.Force.CargoTrucks < 3 ||
            scenario.Simulation.CurrentTick.Value < debug.LastDecisionTick.Value + 2) return false;
        foreach (var entity in scenario.Simulation.Entities.Query<ConstructionSite>())
            if (scenario.Simulation.Entities.GetComponent<ConstructionSite>(entity).Owner == player) return false;
        return true;
    }

    private readonly MatchRuntime _scenario;

    private SkirmishScenarioHarness(MatchRuntime scenario)
    {
        _scenario = scenario;
    }

    public SkirmishStartingBase GetBase(PlayerId player) => _scenario.GetBase(player);

    public SimulationCoordinator Simulation => _scenario.Simulation;
    public MatchRuntimeServices Services => _scenario.Services;

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
