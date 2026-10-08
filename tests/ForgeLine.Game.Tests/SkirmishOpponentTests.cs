using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class SkirmishOpponentTests
{
    [Fact]
    public void OffensiveFuelReserveCannotUndercutGeneralResupplyThreshold()
    {
        var configuration =
            new SkirmishOpponentConfiguration
            {
                ResupplyThreshold = 0.40,
                OffensiveFuelThreshold = 0.30
            };

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                configuration.Validate);

        Assert.Contains(
            "Offensive fuel threshold",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 2, 2)]
    [InlineData(1, 3, 3)]
    [InlineData(1, 5, 3)]
    [InlineData(2, 1, 2)]
    [InlineData(2, 3, 3)]
    [InlineData(4, 3, 4)]
    public void MatureSupplyTargetScalesWithDepotNetwork(
        int minimumSupplyTrucks,
        int supplyDepotCount,
        int expected)
    {
        var configuration =
            new SkirmishOpponentConfiguration
            {
                MinimumSupplyTrucks =
                    minimumSupplyTrucks
            };

        Assert.Equal(
            expected,
            configuration.ResolveMatureSupplyTruckTarget(
                supplyDepotCount));
    }

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
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingInventory,
                    resource),
                scenario.Inventories.GetQuantity(
                    scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingInventory,
                    resource));
        }

        Assert.Equal(
            1,
            scenario.CountUnits(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                UnitIds.CombatEngineer));
        Assert.Equal(
            1,
            scenario.CountUnits(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Player,
                UnitIds.CombatEngineer));
        Assert.Equal(
            2,
            scenario.CountUnits(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                UnitIds.CargoTruck));
        Assert.Equal(
            2,
            scenario.CountUnits(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Player,
                UnitIds.CargoTruck));

        EntityId westCargo =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[1];
        EntityId eastCargo =
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[1];

        Assert.Equal(
            UnitIds.CargoTruck,
            scenario.Simulation.Entities.GetComponent<UnitIdentity>(
                westCargo).UnitId);
        Assert.Equal(
            UnitIds.CargoTruck,
            scenario.Simulation.Entities.GetComponent<UnitIdentity>(
                eastCargo).UnitId);
        WorldTransform westCargoTransform =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                westCargo);
        WorldTransform eastCargoTransform =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                eastCargo);
        WorldTransform westCoreTransform =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);
        WorldTransform eastCoreTransform =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore);

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
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player).StrategicState);
        Assert.Equal(
            SkirmishStrategicState.Bootstrap,
            scenario.GetOpponentState(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Player).StrategicState);
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
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player).DecisionsTaken > 0);
        Assert.True(
            scenario.GetOpponentState(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Player).DecisionsTaken > 0);
    }


    [Fact]
    public void PowerRecoveryCanAddCapacityWhenFourPlantsStillCannotMeetDemand()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                CentralDivideScenario.CreateSettings(
                    MatchScenarioProfile.Validation));
        EntityRegistry entities =
            scenario.Simulation.Entities;
        WorldTransform core =
            entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);

        int powerPlants = 0;

        foreach (EntityId entity in
                 entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CompletedBuilding building =
                entities.GetComponent<CompletedBuilding>(
                    entity);

            if (building.Owner ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player &&
                building.BuildingId ==
                    BuildingIds.PowerPlant)
            {
                powerPlants++;
            }
        }

        while (powerPlants < 4)
        {
            EntityId plant =
                entities.CreateEntity();
            entities.AddComponent(
                plant,
                new WorldTransform(
                    core.Position +
                        new Vector3(
                            80.0f + 24.0f * powerPlants,
                            0.0f,
                            80.0f),
                    Quaternion.Identity,
                    Vector3.One));
            entities.AddComponent(
                plant,
                new CompletedBuilding(
                    BuildingIds.PowerPlant,
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                    SimulationTick.Zero));
            entities.AddComponent(
                plant,
                new PowerGenerator(
                    100.0));
            powerPlants++;
        }

        EntityId overloadedConsumer =
            entities.CreateEntity();
        entities.AddComponent(
            overloadedConsumer,
            new CompletedBuilding(
                BuildingIds.StorageDepot,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                SimulationTick.Zero));
        entities.AddComponent(
            overloadedConsumer,
            new PowerConsumer(
                450.0,
                PowerPriority.Industrial,
                enabled: true));

        scenario.Simulation.RunTicks(
            2,
            TestContext.Current.CancellationToken);

        bool queuedAdditionalPower =
            false;

        foreach (EntityId site in
                 entities.Query<ConstructionSite>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            ConstructionSite construction =
                entities.GetComponent<ConstructionSite>(
                    site);

            if (construction.Owner ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player &&
                construction.BuildingId ==
                    BuildingIds.PowerPlant)
            {
                queuedAdditionalPower =
                    true;
                break;
            }
        }

        Assert.True(
            queuedAdditionalPower);
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
                intent.Issuer == scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player
                    ? scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Faction
                    : scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Faction;
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
    public void DebugSnapshotProjectsLayeredGroupSupplyAndRetreatState()
    {
        var configuration =
            new SkirmishOpponentConfiguration
            {
                ReactionCadenceTicks = 1_000
            };
        SkirmishScenarioHarness scenario =
            SkirmishScenarioHarness.Create(
                westConfiguration: configuration,
                eastConfiguration: configuration);
        scenario.Opponents.DebugCaptureEnabled =
            true;

        EntityId unit =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];
        EntityId group =
            scenario.Simulation.Entities.CreateEntity();
        Vector3 destination =
            new(1_250.0f, 0.0f, 1_100.0f);

        scenario.Simulation.Entities.AddComponent(
            group,
            new CombatGroupIntent(
                CombatOrderKind.AttackMove,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                destination,
                hasDestination: true,
                EntityId.Invalid,
                FormationTemplate.Column,
                initialMemberCount: 1,
                pursuitLeashMeters: 160.0f,
                scenario.Simulation.CurrentTick));

        if (scenario.Simulation.Entities.HasComponent<CombatGroupMember>(
                unit))
        {
            scenario.Simulation.Entities.SetComponent(
                unit,
                new CombatGroupMember(group));
        }
        else
        {
            scenario.Simulation.Entities.AddComponent(
                unit,
                new CombatGroupMember(group));
        }

        var readiness =
            new UnitCombatReadiness(
                Strength: 1.0,
                Health: 0.20,
                Fuel: 0.10,
                Ammunition: 0.10,
                Mobility: 1.0,
                WeaponAvailability: 1.0,
                SupplyCondition: 0.10,
                CombatCapability: 0.20,
                OverallReadiness: 0.20,
                scenario.Simulation.CurrentTick);

        if (scenario.Simulation.Entities.HasComponent<UnitCombatReadiness>(
                unit))
        {
            scenario.Simulation.Entities.SetComponent(
                unit,
                readiness);
        }
        else
        {
            scenario.Simulation.Entities.AddComponent(
                unit,
                readiness);
        }

        WorldTransform coreTransform =
            scenario.Simulation.Entities.GetComponent<WorldTransform>(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);
        var recovery =
            new RetreatRecoveryState(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                RetreatRecoveryReason.RepairAndSupply,
                coreTransform.Position,
                scenario.Simulation.CurrentTick);

        if (scenario.Simulation.Entities.HasComponent<RetreatRecoveryState>(
                unit))
        {
            scenario.Simulation.Entities.SetComponent(
                unit,
                recovery);
        }
        else
        {
            scenario.Simulation.Entities.AddComponent(
                unit,
                recovery);
        }

        scenario.Simulation.AdvanceOneTick();

        SkirmishOpponentDebugReadModel debug =
            Assert.Single(
                scenario.Opponents.DebugSnapshot,
                entry =>
                    entry.Player ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);

        Assert.Equal(
            SkirmishOperationalObjective.StabilizeEconomy,
            debug.OperationalObjective);
        Assert.True(debug.GroupObjective.IsSpecified);
        Assert.Equal(group, debug.GroupObjective.Group);
        Assert.Equal(
            CombatOrderKind.AttackMove,
            debug.GroupObjective.Order);
        Assert.Equal(destination, debug.GroupObjective.Destination);
        Assert.Equal(1, debug.GroupObjective.SurvivingMemberCount);
        Assert.True(
            debug.SupplyRequirement.HasFlag(
                SkirmishSupplyRequirement.Fuel));
        Assert.True(
            debug.SupplyRequirement.HasFlag(
                SkirmishSupplyRequirement.Ammunition));
        Assert.True(
            debug.SupplyRequirement.HasFlag(
                SkirmishSupplyRequirement.Repair));
        Assert.Equal(
            SkirmishRetreatReason.RepairAndSupply,
            debug.RetreatReason);
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
                first.GetBase(new ForgeLine.Game.PlayerId(1)).Player),
            second.GetOpponentState(
                second.GetBase(new ForgeLine.Game.PlayerId(1)).Player));
        Assert.Equal(
            first.GetOpponentState(
                first.GetBase(new ForgeLine.Game.PlayerId(2)).Player),
            second.GetOpponentState(
                second.GetBase(new ForgeLine.Game.PlayerId(2)).Player));
        Assert.Equal(
            first.GetMatchState(),
            second.GetMatchState());

        Assert.Equal(
            first.CountBuildings(
                first.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                BuildingIds.PowerPlant),
            second.CountBuildings(
                second.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                BuildingIds.PowerPlant));
        Assert.Equal(
            first.CountBuildings(
                first.GetBase(new ForgeLine.Game.PlayerId(2)).Player,
                BuildingIds.VehicleFactory),
            second.CountBuildings(
                second.GetBase(new ForgeLine.Game.PlayerId(2)).Player,
                BuildingIds.VehicleFactory));
    }

    [Fact]
    public void NonDecisionTicksSkipDecisionOnlyAssessmentWithoutDebugCapture()
    {
        using MatchRuntime scenario =
            CreateMeasuredScenario(
                enableOpponentDebugCapture: false);

        scenario.Simulation.RunTicks(
            3,
            TestContext.Current.CancellationToken);

        SkirmishOpponentWorkMetrics before =
            scenario.Opponents.WorkMetrics;

        scenario.Simulation.RunTicks(
            5,
            TestContext.Current.CancellationToken);

        SkirmishOpponentWorkMetrics after =
            scenario.Opponents.WorkMetrics;

        Assert.Equal(
            0,
            after.DecisionEvaluations -
            before.DecisionEvaluations);
        Assert.Equal(
            10,
            after.NonDecisionEvaluations -
            before.NonDecisionEvaluations);
        Assert.Equal(
            10,
            after.OwnedStateCaptures -
            before.OwnedStateCaptures);
        Assert.Equal(
            0,
            after.IntelligenceCaptures -
            before.IntelligenceCaptures);
        Assert.Equal(
            0,
            after.EconomyAssessments -
            before.EconomyAssessments);
        Assert.Equal(
            0,
            after.ForceAssessments -
            before.ForceAssessments);
        Assert.Equal(
            before.ScratchStatesCreated,
            after.ScratchStatesCreated);
    }

    [Fact]
    public void DebugCaptureKeepsNonDecisionAssessmentsFresh()
    {
        using MatchRuntime scenario =
            CreateMeasuredScenario(
                enableOpponentDebugCapture: true);

        scenario.Simulation.RunTicks(
            3,
            TestContext.Current.CancellationToken);

        SkirmishOpponentWorkMetrics before =
            scenario.Opponents.WorkMetrics;

        scenario.Simulation.RunTicks(
            5,
            TestContext.Current.CancellationToken);

        SkirmishOpponentWorkMetrics after =
            scenario.Opponents.WorkMetrics;

        Assert.Equal(
            10,
            after.NonDecisionEvaluations -
            before.NonDecisionEvaluations);
        Assert.Equal(
            10,
            after.IntelligenceCaptures -
            before.IntelligenceCaptures);
        Assert.Equal(
            10,
            after.EconomyAssessments -
            before.EconomyAssessments);
        Assert.Equal(
            10,
            after.ForceAssessments -
            before.ForceAssessments);
        Assert.Equal(
            2,
            scenario.Opponents.DebugSnapshot.Count);
    }

    [Fact]
    public void NonDecisionAssessmentSuppressionReducesCurrentThreadAllocations()
    {
        long assessmentEnabled =
            MeasureNonDecisionAllocations(
                enableOpponentDebugCapture: true);
        long assessmentSuppressed =
            MeasureNonDecisionAllocations(
                enableOpponentDebugCapture: false);

        Assert.True(
            assessmentSuppressed <
            assessmentEnabled,
            $"Expected cadence suppression to reduce current-thread allocation; enabled={assessmentEnabled}; suppressed={assessmentSuppressed}.");
    }

    [Fact]
    public void OpponentScratchStateFollowsControllerLifetime()
    {
        using MatchRuntime scenario =
            CreateMeasuredScenario(
                enableOpponentDebugCapture: false);

        scenario.Simulation.RunTicks(
            3,
            TestContext.Current.CancellationToken);

        SkirmishOpponentWorkMetrics before =
            scenario.Opponents.WorkMetrics;
        Assert.Equal(
            2,
            before.ScratchStatesCreated);
        Assert.Equal(
            0,
            before.ScratchStatesReleased);

        EntityId removedController =
            EntityId.Invalid;

        foreach (EntityId entity in
                 scenario.Simulation.Entities.Query<
                     SkirmishOpponentController,
                     SkirmishOpponentState>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            SkirmishOpponentController controller =
                scenario.Simulation.Entities
                    .GetComponent<SkirmishOpponentController>(
                        entity);

            if (controller.Player ==
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Player)
            {
                removedController = entity;
                break;
            }
        }

        Assert.True(removedController.IsValid);
        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(
                removedController));

        scenario.Simulation.AdvanceOneTick();

        SkirmishOpponentWorkMetrics after =
            scenario.Opponents.WorkMetrics;
        Assert.Equal(
            before.ScratchStatesCreated,
            after.ScratchStatesCreated);
        Assert.Equal(
            1,
            after.ScratchStatesReleased -
            before.ScratchStatesReleased);
    }

    [Fact]
    public void EastOffensiveObjectiveAdvancesIntoOpponentHalfAgainstStationaryBase()
    {
        SkirmishScenarioHarness scenario =
            SkirmishScenarioHarness.Create(
                seed: 2026);
        scenario.Opponents.DebugCaptureEnabled =
            true;

        // Isolate the eastern opponent's planning from the west controller winning the
        // competing match before the east force can make an offensive decision.
        // The west base, units, intelligence and combat remain authoritative.
        scenario.Simulation.Entities.RemoveComponent<SkirmishOpponentController>(
            scenario.West.Controller);

        bool reachedAttack =
            scenario.RunUntil(
                current =>
                    current.GetOpponentState(
                        current.East.Player).ActiveGoal ==
                    SkirmishStrategicGoal.AttackObjective &&
                    current.Opponents.DebugSnapshot.Any(
                        snapshot =>
                            snapshot.Player ==
                                current.East.Player &&
                            snapshot.HasChosenObjective),
                maximumTicks: 40_000,
                TestContext.Current.CancellationToken);

        Assert.True(
            reachedAttack,
            DescribePlayer(scenario, scenario.East, scenario.GetOpponentState(scenario.East.Player)));

        SkirmishOpponentDebugReadModel decision =
            Assert.Single(
                scenario.Opponents.DebugSnapshot,
                snapshot =>
                    snapshot.Player ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Player);

        float center =
            scenario.Battlefield.Metadata.WidthMeters *
            0.5f;

        Assert.True(
            decision.ChosenObjective.X <= center,
            $"East offensive objective remained on its own half: objective={decision.ChosenObjective}; center={center:F1}.");
    }

    [Fact]
    public void BoundedHeadlessSkirmishProgressesThroughStrategicLoop()
    {
        SkirmishScenarioHarness scenario =
            SkirmishScenarioHarness.Create(
                seed: 2026);

        bool progressed =
            scenario.RunUntil(
                current =>
                    HasPowerAndExtraction(
                        current,
                        current.West.Player) &&
                    HasPowerAndExtraction(
                        current,
                        current.East.Player) &&
                    current.CountBuildings(
                        current.West.Player,
                        BuildingIds.LogisticsHub) >= 2 &&
                    current.CountBuildings(
                        current.East.Player,
                        BuildingIds.LogisticsHub) >= 2 &&
                    HasIntegratedIndustry(
                        current,
                        current.West.Player) &&
                    HasIntegratedIndustry(
                        current,
                        current.East.Player) &&
                    current.CountUnits(
                        current.West.Player,
                        UnitIds.ScoutVehicle) > 0 &&
                    current.CountUnits(
                        current.East.Player,
                        UnitIds.ScoutVehicle) > 0 &&
                    HasCombatGroup(current) &&
                    (current.Intelligence.GetContactCount(current.West.Faction) > 0 ||
                     current.Intelligence.GetContactCount(current.East.Faction) > 0),
                maximumTicks: 40_000,
                TestContext.Current.CancellationToken);

        Assert.True(
            progressed,
            DescribeScenario(scenario));
        Assert.True(
            scenario.GetOpponentState(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player).DecisionsTaken > 20);
        Assert.True(
            scenario.GetOpponentState(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Player).DecisionsTaken > 20);
        Assert.True(
            scenario.CargoTransport.Metrics.DeliveredQuantity > 0.0);
        Assert.True(
            scenario.BattlefieldSupply.Metrics.TotalFuelTransferred > 0.0 ||
            scenario.BattlefieldSupply.Metrics.TotalAmmunitionTransferred > 0.0);
        Assert.True(
            scenario.Intelligence.GetContactCount(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Faction) > 0 ||
            scenario.Intelligence.GetContactCount(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Faction) > 0);
    }

    private static long MeasureNonDecisionAllocations(
        bool enableOpponentDebugCapture)
    {
        using MatchRuntime scenario =
            CreateMeasuredScenario(
                enableOpponentDebugCapture);

        scenario.Simulation.RunTicks(
            3,
            TestContext.Current.CancellationToken);

        long before =
            GC.GetAllocatedBytesForCurrentThread();

        scenario.Simulation.RunTicks(
            5,
            TestContext.Current.CancellationToken);

        return
            GC.GetAllocatedBytesForCurrentThread() -
            before;
    }

    private static MatchRuntime CreateMeasuredScenario(
        bool enableOpponentDebugCapture)
    {
        MatchScenarioSettings settings =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);
        MatchRuntimeSettings runtime =
            CentralDivideScenario.CreateHeadless(
                settings.Profile,
                seed: 7331,
                enableDiagnostics: false,
                enableDebugCapture: false) with
            {
                Scenario = settings
            };
        MatchRuntime scenario =
            CentralDivideScenario.Create(runtime);
        scenario.Opponents.DebugCaptureEnabled =
            enableOpponentDebugCapture;
        return scenario;
    }

    private static bool HasIntegratedIndustry(
        SkirmishScenarioHarness scenario,
        PlayerId player) =>
        scenario.CountBuildings(
            player,
            BuildingIds.Smelter) > 0 &&
        scenario.CountBuildings(
            player,
            BuildingIds.Refinery) > 0 &&
        scenario.CountBuildings(
            player,
            BuildingIds.ElectronicsPlant) > 0 &&
        scenario.CountBuildings(
            player,
            BuildingIds.VehicleFactory) > 0 &&
        scenario.CountBuildings(
            player,
            BuildingIds.AmmunitionPlant) > 0 &&
        scenario.CountBuildings(
            player,
            BuildingIds.SupplyDepot) > 0 &&
        scenario.CountBuildings(
            player,
            BuildingIds.Radar) > 0;

    private static string DescribeScenario(
        SkirmishScenarioHarness scenario)
    {
        SkirmishOpponentState west =
            scenario.GetOpponentState(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        SkirmishOpponentState east =
            scenario.GetOpponentState(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Player);
        MatchState match =
            scenario.GetMatchState();

        return
            $"match={match.Status}; " +
            DescribePlayer(
                scenario,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)),
                west) +
            "; " +
            DescribePlayer(
                scenario,
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)),
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

        var unitProductionEntries =
            new List<string>();

        foreach (EntityId entity in
                 scenario.Simulation.Entities.Query<UnitProductionFacility>())
        {
            if (!scenario.Simulation.Entities.TryGetComponent(
                    entity,
                    out CompletedBuilding building) ||
                building.Owner != side.Player)
            {
                continue;
            }

            UnitProductionFacility facility =
                scenario.Simulation.Entities.GetComponent<UnitProductionFacility>(
                    entity);
            PowerOperationalState powerState =
                scenario.Simulation.Entities.TryGetComponent(
                    entity,
                    out PowerConsumer consumer)
                    ? consumer.State
                    : PowerOperationalState.Offline;

            unitProductionEntries.Add(
                $"{entity}:{facility.ActiveUnit}/{facility.Status}/{facility.BlockReason}" +
                $" power={powerState}" +
                $" steel={scenario.Inventories.GetQuantity(facility.InputInventory, ResourceIds.Steel):F0}" +
                $" elec={scenario.Inventories.GetQuantity(facility.InputInventory, ResourceIds.Electronics):F0}" +
                $" fuel={scenario.Inventories.GetQuantity(facility.InputInventory, ResourceIds.Fuel):F0}" +
                $" ammo={scenario.Inventories.GetQuantity(facility.InputInventory, ResourceIds.Ammunition):F0}");
        }

        string unitProductionStates =
            string.Join(
                ",",
                unitProductionEntries);

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
            $"unitProductionStates={unitProductionStates} " +
            $"scouts={scenario.CountUnits(side.Player, UnitIds.ScoutVehicle)} " +
            $"tanks={scenario.CountUnits(side.Player, UnitIds.MainBattleTank)} " +
            $"force={debug.Force.CombatUnits}/ready{debug.Force.AverageReadiness:F2}/supply{debug.Force.MinimumSupply:F2}/currentContacts{debug.Force.CurrentHostileContacts} " +
            $"admission={debug.OffensiveAdmission.Reason}" +
            $"/eligible{debug.OffensiveAdmission.EligibleAttackerCount}" +
            $"/pressure{debug.OffensiveAdmission.ObjectivePressureUnitCount}" +
            $"/supply{debug.OffensiveAdmission.ForwardSupplyReady}";
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
