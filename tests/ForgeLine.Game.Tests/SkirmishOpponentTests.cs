using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishOpponentTests
{
    [Fact]
    public void StartingBasesAreSymmetricAndUseNormalAuthoritativeState()
    {
        SkirmishScenarioHarness scenario =
            SkirmishScenarioHarness.Create();

        ResourceId[] resources =
        [
            ResourceIds.FerrousOre,
            ResourceIds.Volatiles,
            ResourceIds.Silicates,
            ResourceIds.Steel,
            ResourceIds.Fuel,
            ResourceIds.Electronics,
            ResourceIds.Ammunition
        ];

        for (int index = 0;
             index < resources.Length;
             index++)
        {
            ResourceId resource = resources[index];

            Assert.Equal(
                scenario.Inventories.GetQuantity(
                    scenario.West.StartingInventory,
                    resource),
                scenario.Inventories.GetQuantity(
                    scenario.East.StartingInventory,
                    resource));
        }

        Assert.Equal(
            1,
            scenario.CountUnits(
                scenario.West.Player,
                UnitIds.CombatEngineer));
        Assert.Equal(
            1,
            scenario.CountUnits(
                scenario.East.Player,
                UnitIds.CombatEngineer));
        Assert.Equal(
            1,
            scenario.CountUnits(
                scenario.West.Player,
                UnitIds.CargoTruck));
        Assert.Equal(
            1,
            scenario.CountUnits(
                scenario.East.Player,
                UnitIds.CargoTruck));

        Assert.Equal(
            SkirmishStrategicState.Bootstrap,
            scenario.GetOpponentState(
                scenario.West.Player).StrategicState);
        Assert.Equal(
            SkirmishStrategicState.Bootstrap,
            scenario.GetOpponentState(
                scenario.East.Player).StrategicState);
    }

    [Fact]
    public void OpponentsRecoverPowerAndRawResourceShortageThroughConstruction()
    {
        var constrainedStock =
            new SkirmishStartingStock(
                FerrousOre: 40.0,
                Volatiles: 40.0,
                Silicates: 40.0,
                Steel: 1_200.0,
                Fuel: 600.0,
                Electronics: 600.0,
                Ammunition: 600.0);
        SkirmishScenarioHarness scenario =
            SkirmishScenarioHarness.Create(
                startingStock: constrainedStock);

        bool recovered =
            scenario.RunUntil(
                current =>
                    HasPowerAndExtraction(
                        current,
                        current.West.Player) &&
                    HasPowerAndExtraction(
                        current,
                        current.East.Player),
                maximumTicks: 8_000,
                TestContext.Current.CancellationToken);

        Assert.True(recovered);
        Assert.True(
            scenario.GetOpponentState(
                scenario.West.Player).DecisionsTaken > 0);
        Assert.True(
            scenario.GetOpponentState(
                scenario.East.Player).DecisionsTaken > 0);
    }

    [Fact]
    public void DirectCombatTargetsRequireCurrentIdentifiedIntelligence()
    {
        SkirmishScenarioHarness scenario =
            SkirmishScenarioHarness.Create();

        bool formedGroup =
            scenario.RunUntil(
                current =>
                    current.Simulation.Entities
                        .Query<CombatGroupIntent>()
                        .Any(),
                maximumTicks: 12_000,
                TestContext.Current.CancellationToken);

        Assert.True(formedGroup);

        foreach (EntityId entity in
                 scenario.Simulation.Entities.Query<CombatGroupIntent>())
        {
            CombatGroupIntent intent =
                scenario.Simulation.Entities.GetComponent<CombatGroupIntent>(
                    entity);

            if (intent.Kind !=
                CombatOrderKind.Attack)
            {
                Assert.False(
                    intent.ExplicitTarget.IsValid);
                continue;
            }

            FactionId faction =
                intent.Issuer == scenario.West.Player
                    ? scenario.West.Faction
                    : scenario.East.Faction;
            FactionIntelligenceSnapshot intelligence =
                scenario.Intelligence.Capture(
                    faction);

            bool authorized =
                intelligence.Contacts.Any(
                    contact =>
                        contact.IsCurrent &&
                        contact.State ==
                            IntelligenceState.Identified &&
                        scenario.Intelligence.TryResolveCurrentlyIdentifiedEntity(
                            faction,
                            contact.ContactKey,
                            out EntityId target) &&
                        target ==
                            intent.ExplicitTarget);

            Assert.True(authorized);
        }
    }

    [Fact]
    public void SameSeedProducesDeterministicStrategicProgress()
    {
        SkirmishScenarioHarness first =
            SkirmishScenarioHarness.Create(
                seed: 1337);
        SkirmishScenarioHarness second =
            SkirmishScenarioHarness.Create(
                seed: 1337);

        first.Simulation.RunTicks(
            2_000,
            TestContext.Current.CancellationToken);
        second.Simulation.RunTicks(
            2_000,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            first.GetOpponentState(
                first.West.Player),
            second.GetOpponentState(
                second.West.Player));
        Assert.Equal(
            first.GetOpponentState(
                first.East.Player),
            second.GetOpponentState(
                second.East.Player));
        Assert.Equal(
            first.GetMatchState(),
            second.GetMatchState());

        Assert.Equal(
            first.CountBuildings(
                first.West.Player,
                BuildingIds.PowerPlant),
            second.CountBuildings(
                second.West.Player,
                BuildingIds.PowerPlant));
        Assert.Equal(
            first.CountBuildings(
                first.East.Player,
                BuildingIds.VehicleFactory),
            second.CountBuildings(
                second.East.Player,
                BuildingIds.VehicleFactory));
    }

    [Fact]
    public void BoundedHeadlessMatchProgressesToTerminalOutcome()
    {
        SkirmishScenarioHarness scenario =
            SkirmishScenarioHarness.Create(
                seed: 2026);

        bool completed =
            scenario.RunUntil(
                current =>
                    current.GetMatchState().Status !=
                    MatchStatus.Running,
                maximumTicks: 30_000,
                TestContext.Current.CancellationToken);

        Assert.True(completed);

        MatchState result =
            scenario.GetMatchState();

        Assert.True(
            result.Status is
                MatchStatus.Victory or
                MatchStatus.Draw);
        Assert.True(
            scenario.GetOpponentState(
                scenario.West.Player).DecisionsTaken > 20);
        Assert.True(
            scenario.GetOpponentState(
                scenario.East.Player).DecisionsTaken > 20);
    }

    private static bool HasPowerAndExtraction(
        SkirmishScenarioHarness scenario,
        PlayerId player) =>
        scenario.CountBuildings(
            player,
            BuildingIds.PowerPlant) > 0 &&
        scenario.CountBuildings(
            player,
            BuildingIds.Extractor) > 0;
}
