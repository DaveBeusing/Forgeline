using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class BuildingDemolitionTests
{
    private static readonly PlayerId Player = new(1);

    [Fact]
    public void CompletedBuildingRefundsFortyPercentExactlyOnce()
    {
        using MatchRuntime scenario = CreateScenario(6201);
        EntityId core = scenario.GetBase(Player).CommandCore;
        InventoryId inventory = scenario.Simulation.Entities
            .GetComponent<InventoryStorage>(core).InventoryId;
        double originalSteel = scenario.Inventories.GetQuantity(inventory, ResourceIds.Steel);
        double originalElectronics = scenario.Inventories.GetQuantity(inventory, ResourceIds.Electronics);
        EntityId target = CreatePaidBuilding(scenario, Player,
            [new BuildingResourceCost(ResourceIds.Steel, 125.0),
             new BuildingResourceCost(ResourceIds.Electronics, 17.5)]);

        PlayerCommandGateway gateway = CreateGateway(scenario);
        PlayerCommandSubmissionReceipt accepted = gateway.SubmitDemolition(
            Player, [target], scenario.Simulation.CurrentTick);
        Assert.True(accepted.Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.False(scenario.Simulation.Entities.IsAlive(target));
        Assert.Equal(originalSteel + 50.0,
            scenario.Inventories.GetQuantity(inventory, ResourceIds.Steel), 6);
        Assert.Equal(originalElectronics + 7.0,
            scenario.Inventories.GetQuantity(inventory, ResourceIds.Electronics), 6);
        Assert.True(gateway.Results.TryRead(out PlayerCommandResultReadModel first));
        Assert.Equal(PlayerCommandKind.Demolition, first.Kind);
        Assert.Equal(1, first.AcceptedTargets);
        Assert.Equal(2, Assert.Single(first.DemolitionTargets!).Refunded.Length);

        gateway.SubmitDemolition(Player, [target], scenario.Simulation.CurrentTick);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out PlayerCommandResultReadModel second));
        Assert.Equal(0, second.AcceptedTargets);
        Assert.Equal(BuildingDemolitionFailure.InvalidTarget,
            Assert.Single(second.DemolitionTargets!).Failure);
        Assert.Equal(originalSteel + 50.0,
            scenario.Inventories.GetQuantity(inventory, ResourceIds.Steel), 6);
    }

    [Fact]
    public void CommandCoreAndForeignTargetsCannotBeDemolished()
    {
        using MatchRuntime scenario = CreateScenario(6202);
        EntityId core = scenario.GetBase(Player).CommandCore;
        EntityId foreign = CreatePaidBuilding(scenario, new PlayerId(2),
            [new BuildingResourceCost(ResourceIds.Steel, 10)]);
        PlayerCommandGateway gateway = CreateGateway(scenario);

        gateway.SubmitDemolition(Player, [core, foreign], scenario.Simulation.CurrentTick);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(gateway.Results.TryRead(out PlayerCommandResultReadModel result));
        Assert.Equal(0, result.AcceptedTargets);
        Assert.Equal(2, result.RejectedTargets);
        Assert.Contains(result.DemolitionTargets!, x =>
            x.Failure == BuildingDemolitionFailure.ProtectedCommandCore);
        Assert.Contains(result.DemolitionTargets!, x =>
            x.Failure == BuildingDemolitionFailure.NotOwned);
        Assert.True(scenario.Simulation.Entities.IsAlive(core));
        Assert.True(scenario.Simulation.Entities.IsAlive(foreign));
    }

    [Fact]
    public void MissingPaymentsAndExcessRefundsLeaveBuildingsIntact()
    {
        using MatchRuntime scenario = CreateScenario(6203);
        EntityId missing = CreatePaidBuilding(scenario, Player, []);
        scenario.Simulation.Entities.RemoveComponent<PaidBuildingMaterials>(missing);
        EntityId expensive = CreatePaidBuilding(scenario, Player,
            [new BuildingResourceCost(ResourceIds.Steel, 1_000_000)]);
        PlayerCommandGateway gateway = CreateGateway(scenario);

        gateway.SubmitDemolition(Player, [missing, expensive], scenario.Simulation.CurrentTick);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(gateway.Results.TryRead(out PlayerCommandResultReadModel result));
        Assert.Equal(0, result.AcceptedTargets);
        Assert.Contains(result.DemolitionTargets!, x =>
            x.Failure == BuildingDemolitionFailure.MissingPaymentRecord);
        Assert.Contains(result.DemolitionTargets!, x =>
            x.Failure == BuildingDemolitionFailure.InsufficientRefundCapacity);
        Assert.True(scenario.Simulation.Entities.IsAlive(missing));
        Assert.True(scenario.Simulation.Entities.IsAlive(expensive));
    }

    [Fact]
    public void DemolitionReplayCodecPreservesTargetsAndRefundCommandIdentity()
    {
        var command = new DemolishBuildingsCommand(
            Player,
            [new EntityId(15, 1), new EntityId(15, 1), new EntityId(3, 2)],
            new SimulationTick(5),
            new PlayerCommandCorrelationId(77));
        var envelope = new SimulationCommandEnvelope(
            new SimulationTick(6), 14, new SimulationCommandSource(Player.Value), command);

        Assert.True(ReplayCommandCodec.TryEncode(envelope, out RecordedSimulationCommand? recorded));
        Assert.NotNull(recorded);
        Assert.Equal(ReplayCommandKind.Demolition, recorded.Kind);
        Assert.Equal(2, recorded.Entities.Length);
        Assert.Equal(new EntityId(3, 2), recorded.Entities[0]);

        using MatchRuntime scenario = CreateScenario(6204);
        var decoded = Assert.IsType<DemolishBuildingsCommand>(
            ReplayCommandCodec.Decode(recorded, scenario));
        Assert.Equal(command.Targets, decoded.Targets);
        Assert.Equal(command.CorrelationId, decoded.CorrelationId);
    }

    [Fact]
    public void ConstructionSiteCancellationReleasesReservationsWithoutRefund()
    {
        using MatchRuntime scenario = CreateScenario(6205);
        BuildingDefinition definition =
            scenario.Services.BuildingDefinitions[BuildingIds.PowerPlant];
        InventoryId source = scenario.Inventories.CreateInventory(
            new InventorySpecification(100_000));
        foreach (BuildingResourceCost cost in definition.Costs)
        {
            Assert.True(scenario.Inventories.Add(source, cost.ResourceId, cost.Quantity).Succeeded);
            Assert.True(scenario.Inventories.Reserve(source, cost.ResourceId, cost.Quantity).Succeeded);
        }

        EntityId site = scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            site, new ControllableEntity(Player, ControllableEntityCategory.Building));
        scenario.Simulation.Entities.AddComponent(
            site, new ConstructionSite(
                BuildingIds.PowerPlant, Player, source,
                scenario.Simulation.CurrentTick, 100, 0));

        PlayerCommandGateway gateway = CreateGateway(scenario);
        gateway.SubmitDemolition(Player, [site], scenario.Simulation.CurrentTick);
        scenario.Simulation.AdvanceOneTick();

        Assert.False(scenario.Simulation.Entities.IsAlive(site));
        Assert.True(gateway.Results.TryRead(out PlayerCommandResultReadModel result));
        Assert.Equal(1, result.AcceptedTargets);
        Assert.Empty(Assert.Single(result.DemolitionTargets!).Refunded);
        foreach (BuildingResourceCost cost in definition.Costs)
        {
            Assert.Equal(0, scenario.Inventories.GetReservedQuantity(source, cost.ResourceId));
            Assert.Equal(cost.Quantity,
                scenario.Inventories.GetQuantity(source, cost.ResourceId));
        }
    }

    [Fact]
    public void ConstructionCancellationCommandIsReplayable()
    {
        var cancel = new CancelConstructionCommand(
            Player, new EntityId(41, 2), new SimulationTick(9));
        var envelope = new SimulationCommandEnvelope(
            new SimulationTick(10), 21, new SimulationCommandSource(Player.Value), cancel);
        Assert.True(ReplayCommandCodec.TryEncode(envelope, out RecordedSimulationCommand? recorded));
        Assert.NotNull(recorded);
        Assert.Equal(ReplayCommandKind.CancelConstruction, recorded.Kind);
        using MatchRuntime scenario = CreateScenario(6206);
        var decoded = Assert.IsType<CancelConstructionCommand>(
            ReplayCommandCodec.Decode(recorded, scenario));
        Assert.Equal(cancel.ConstructionSite, decoded.ConstructionSite);
        Assert.Equal(cancel.SubmittedAtTick, decoded.SubmittedAtTick);
    }

    private static EntityId CreatePaidBuilding(
        MatchRuntime scenario, PlayerId owner, BuildingResourceCost[] costs)
    {
        EntityId entity = scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            entity, new ControllableEntity(owner, ControllableEntityCategory.Building));
        scenario.Simulation.Entities.AddComponent(
            entity, new CompletedBuilding(BuildingIds.PowerPlant, owner, new SimulationTick(12)));
        scenario.Simulation.Entities.AddComponent(
            entity, new PaidBuildingMaterials(costs));
        return entity;
    }

    private static MatchRuntime CreateScenario(ulong seed)
    {
        MatchRuntimeSettings settings = CentralDivideScenario.CreateHeadless(
            MatchScenarioProfile.Gameplay, seed) with
        {
            Participants = CentralDivideScenario.CreateDefaultParticipants(
                westComputerControlled: false, eastComputerControlled: false)
        };
        return CentralDivideScenario.Create(settings, TestContext.Current.CancellationToken);
    }

    private static PlayerCommandGateway CreateGateway(MatchRuntime scenario)
    {
        var gateway = new PlayerCommandGateway(
            scenario.Simulation,
            scenario.Services.BuildingCommands,
            scenario.BattlefieldRuntime.MatchStateEntity,
            demolition: scenario.Services.Demolition);
        scenario.Simulation.RegisterTickObserver(gateway);
        return gateway;
    }
}
