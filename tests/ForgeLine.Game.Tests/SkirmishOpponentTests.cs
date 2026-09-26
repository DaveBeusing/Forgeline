using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
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

        EntityId westCargo =
            scenario.West.StartingUnits.Single(
                entity =>
                    scenario.Simulation.Entities.GetComponent<UnitIdentity>(
                        entity).UnitId ==
                    UnitIds.CargoTruck);
        EntityId eastCargo =
            scenario.East.StartingUnits.Single(
                entity =>
                    scenario.Simulation.Entities.GetComponent<UnitIdentity>(
                        entity).UnitId ==
                    UnitIds.CargoTruck);
        WorldTransform westCargoTransform =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                westCargo);
        WorldTransform eastCargoTransform =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                eastCargo);
        WorldTransform westCoreTransform =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                scenario.West.CommandCore);
        WorldTransform eastCoreTransform =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                scenario.East.CommandCore);

        float westCargoOffsetX =
            westCargoTransform.Position.X -
            westCoreTransform.Position.X;
        float eastCargoOffsetX =
            eastCargoTransform.Position.X -
            eastCoreTransform.Position.X;

        Assert.Equal(
            -westCargoOffsetX,
            eastCargoOffsetX,
            precision: 3);
        Assert.True(
            MathF.Abs(westCargoOffsetX) > 20.0f);
        Assert.True(
            MathF.Abs(eastCargoOffsetX) > 20.0f);

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
    public void CargoTrucksCanServiceExtractorDepositApproaches()
    {
        SkirmishScenarioHarness scenario =
            SkirmishScenarioHarness.Create();

        bool delivered =
            scenario.RunUntil(
                static current =>
                    current.CargoTransport.Metrics.DeliveredQuantity >=
                    700.0,
                maximumTicks: 12_000,
                TestContext.Current.CancellationToken);

        Assert.True(
            delivered,
            DescribeScenario(scenario));
        Assert.Equal(
            0L,
            scenario.CargoTransport.Metrics.FailedTransportCount);
        Assert.True(
            scenario.CargoTransport.Metrics.CompletedOrderCount >=
            4L,
            DescribeScenario(scenario));
    }

    [Fact]
    public void OpponentsRecoverPowerAndRawResourceShortageThroughConstruction()
    {
        var constrainedStock =
            new SkirmishStartingStock(
                FerrousOre: 500.0,
                Volatiles: 60.0,
                Silicates: 200.0,
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

        Assert.True(
            recovered,
            DescribeScenario(scenario));
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
                static current =>
                    HasCombatGroup(
                        current),
                maximumTicks: 30_000,
                TestContext.Current.CancellationToken);

        Assert.True(
            formedGroup,
            DescribeScenario(scenario));

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
                maximumTicks: 80_000,
                TestContext.Current.CancellationToken);

        Assert.True(
            completed,
            DescribeScenario(scenario));

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

    private static string DescribeScenario(
        SkirmishScenarioHarness scenario)
    {
        SkirmishOpponentState west =
            scenario.GetOpponentState(
                scenario.West.Player);
        SkirmishOpponentState east =
            scenario.GetOpponentState(
                scenario.East.Player);
        MatchState match =
            scenario.GetMatchState();

        return
            $"match={match.Status}; " +
            DescribePlayer(
                scenario,
                scenario.West,
                west) +
            "; " +
            DescribePlayer(
                scenario,
                scenario.East,
                east);
    }

    private static string DescribePlayer(
        SkirmishScenarioHarness scenario,
        SkirmishStartingBase side,
        SkirmishOpponentState state)
    {
        SkirmishOpponentDebugReadModel debug =
            scenario.Opponents.DebugSnapshot.Single(
                entry =>
                    entry.Player ==
                    side.Player);
        double coreSteel =
            scenario.Inventories.GetQuantity(
                side.StartingInventory,
                ResourceIds.Steel);
        double coreElectronics =
            scenario.Inventories.GetQuantity(
                side.StartingInventory,
                ResourceIds.Electronics);
        double coreFuel =
            scenario.Inventories.GetQuantity(
                side.StartingInventory,
                ResourceIds.Fuel);
        AutomatedDistributionMetrics distribution =
            scenario.AutomatedDistribution.Metrics;
        CargoTransportMetrics cargo =
            scenario.CargoTransport.Metrics;
        string distributionFailures =
            string.Join(
                ",",
                scenario.AutomatedDistribution.LastDebugSnapshot.Requests
                    .Where(static request =>
                        request.FailureReason !=
                        LogisticsTransportRequestFailureReason.None)
                    .Select(static request =>
                        request.FailureReason)
                    .Distinct()
                    .Order());
        string cargoStates =
            string.Join(
                ",",
                scenario.CargoTransport.LastDebugSnapshot.Transports
                    .OrderBy(static transport => transport.Entity)
                    .Select(transport =>
                    {
                        EntityId entity = transport.Entity;
                        string fuel = "na";
                        if (scenario.Simulation.Entities.TryGetComponent(
                                entity,
                                out UnitFuelState fuelState))
                        {
                            fuel =
                                $"{scenario.Inventories.GetQuantity(fuelState.InventoryId, ResourceIds.Fuel):F1}/{fuelState.Capacity:F0}";
                        }

                        string resupply =
                            scenario.Simulation.Entities.TryGetComponent(
                                entity,
                                out ResupplyOrder resupplyOrder)
                                ? resupplyOrder.Provider.ToString()
                                : "none";
                        string movement =
                            scenario.Simulation.Entities.TryGetComponent(
                                entity,
                                out MovementOrder movementOrder)
                                ? $"{movementOrder.WorldTarget.X:F0},{movementOrder.WorldTarget.Z:F0}"
                                : "none";

                        return
                            $"{entity}:{transport.Lifecycle}/{transport.FailureReason}" +
                            $"@{transport.WorldPosition.X:F0},{transport.WorldPosition.Z:F0}" +
                            (transport.HasMovementTarget
                                ? $" cargo->{transport.MovementTarget.X:F0},{transport.MovementTarget.Z:F0}"
                                : string.Empty) +
                            $" move->{movement} fuel={fuel} resupply={resupply}";
                    }));

        return
            $"{side.Player}={state.StrategicState}/{state.ActiveGoal} decisions={state.DecisionsTaken} " +
            $"power={scenario.CountBuildings(side.Player, BuildingIds.PowerPlant)} " +
            $"extractors={scenario.CountBuildings(side.Player, BuildingIds.Extractor)} " +
            $"storage={scenario.CountBuildings(side.Player, BuildingIds.StorageDepot)} " +
            $"smelter={scenario.CountBuildings(side.Player, BuildingIds.Smelter)} " +
            $"refinery={scenario.CountBuildings(side.Player, BuildingIds.Refinery)} " +
            $"electronics={scenario.CountBuildings(side.Player, BuildingIds.ElectronicsPlant)} " +
            $"hub={scenario.CountBuildings(side.Player, BuildingIds.LogisticsHub)} " +
            $"barracks={scenario.CountBuildings(side.Player, BuildingIds.Barracks)} " +
            $"factory={scenario.CountBuildings(side.Player, BuildingIds.VehicleFactory)} " +
            $"ammoPlant={scenario.CountBuildings(side.Player, BuildingIds.AmmunitionPlant)} " +
            $"supply={scenario.CountBuildings(side.Player, BuildingIds.SupplyDepot)} " +
            $"radar={scenario.CountBuildings(side.Player, BuildingIds.Radar)} " +
            $"coreSteel={coreSteel:F0} coreElectronics={coreElectronics:F0} coreFuel={coreFuel:F0} " +
            $"totalSteel={debug.Economy.Steel:F0} totalElectronics={debug.Economy.Electronics:F0} " +
            $"production={debug.Economy.ProductionFacilities} unitProduction={debug.Economy.UnitProductionFacilities} " +
            $"distribution=p{distribution.PendingRequestCount}/a{distribution.AssignedRequestCount}/t{distribution.InTransitRequestCount}/r{distribution.RetryPendingRequestCount}/c{distribution.CompletedRequestCount}/f{distribution.FailedRequestCount} " +
            $"distributionFailures={distributionFailures} " +
            $"cargo={cargo.TransportCount}/active{cargo.ActiveTransportCount}/wait{cargo.WaitingTransportCount}/failed{cargo.FailedTransportCount}/delivered{cargo.DeliveredQuantity:F0}/routeFail{cargo.RouteFailureCount} " +
            $"cargoStates={cargoStates} " +
            $"scouts={scenario.CountUnits(side.Player, UnitIds.ScoutVehicle)} " +
            $"tanks={scenario.CountUnits(side.Player, UnitIds.MainBattleTank)}";
    }

    private static bool HasCombatGroup(
        SkirmishScenarioHarness scenario)
    {
        foreach (EntityId _ in
                 scenario.Simulation.Entities.Query<CombatGroupIntent>())
        {
            return true;
        }

        return false;
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
