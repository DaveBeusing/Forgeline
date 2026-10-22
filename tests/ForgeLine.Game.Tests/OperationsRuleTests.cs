using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class OperationsRuleTests
{
    private static readonly PlayerId Owner = new(1);

    [Fact]
    public void OwnedRuleCommandsHaveRevisionsAndExplicitRejection()
    {
        using MatchRuntime scenario = CreateScenario();
        var gateway = Gateway(scenario);
        EntityId facility = AddProcessingFacility(scenario, out _, out _);
        var definition = InputRule(facility);
        var receipt = gateway.SubmitOperationsRule(scenario.OperationsRules,
            OperationsRuleOperation.Upsert, definition, 0, true,
            scenario.Simulation.CurrentTick);
        Assert.True(receipt.Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out var accepted));
        Assert.Equal(PlayerCommandKind.OperationsRule, accepted.Kind);
        Assert.Equal(OperationsRuleResult.Applied, accepted.OperationsResult);
        Assert.Equal(1u, Assert.Single(scenario.OperationsRules.Capture(Owner)).Revision);

        gateway.SubmitOperationsRule(scenario.OperationsRules,
            OperationsRuleOperation.Upsert, definition, 0, true,
            scenario.Simulation.CurrentTick);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out var rejected));
        Assert.Equal(OperationsRuleResult.RevisionMismatch, rejected.OperationsResult);
        Assert.Equal(PlayerCommandFeedbackState.Rejected, rejected.State);

        gateway.SubmitOperationsRule(scenario.OperationsRules,
            OperationsRuleOperation.SetEnabled, definition, 1, false,
            scenario.Simulation.CurrentTick);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out var disabled));
        Assert.Equal(OperationsRuleResult.Applied, disabled.OperationsResult);
        Assert.False(Assert.Single(scenario.OperationsRules.Capture(Owner)).Enabled);
    }

    [Fact]
    public void ForeignAndDestroyedTargetsCannotCreateRules()
    {
        using MatchRuntime scenario = CreateScenario();
        EntityId foreign = AddProcessingFacility(scenario, out _, out _, new PlayerId(2));
        var gateway = Gateway(scenario);
        gateway.SubmitOperationsRule(scenario.OperationsRules,
            OperationsRuleOperation.Upsert, InputRule(foreign), 0, true,
            scenario.Simulation.CurrentTick);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out var denied));
        Assert.Equal(OperationsRuleResult.InvalidDefinition, denied.OperationsResult);

        EntityId target = AddProcessingFacility(scenario, out _, out _);
        Assert.True(scenario.Simulation.Entities.DestroyEntity(target));
        gateway.SubmitOperationsRule(scenario.OperationsRules,
            OperationsRuleOperation.Upsert, InputRule(target), 0, true,
            scenario.Simulation.CurrentTick);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out denied));
        Assert.Equal(OperationsRuleResult.InvalidDefinition, denied.OperationsResult);
        Assert.Empty(scenario.OperationsRules.Capture(Owner));
    }

    [Fact]
    public void InputShortageUsesOneRealPolicyWithoutDuplicatingInventoryOrDemand()
    {
        using MatchRuntime scenario = CreateScenario();
        EntityId facility = AddProcessingFacility(scenario, out InventoryId input, out _);
        QueueSteel(scenario, facility);
        scenario.Simulation.RunTicks(2);
        Assert.Equal(ProductionBlockReason.NoInput,
            scenario.Simulation.Entities.GetComponent<ProductionFacility>(facility).BlockReason);
        double initialStock = scenario.Inventories.GetTotalQuantity(input);
        var gateway = Gateway(scenario);
        gateway.SubmitOperationsRule(scenario.OperationsRules,
            OperationsRuleOperation.Upsert, InputRule(facility), 0, true,
            scenario.Simulation.CurrentTick);
        scenario.Simulation.RunTicks(20);
        var row = Assert.Single(scenario.OperationsRules.Capture(Owner));
        Assert.Equal(OperationsRuleResult.Applied, row.Result);
        Assert.Equal(1, row.ActionCount);
        Assert.Equal(initialStock, scenario.Inventories.GetTotalQuantity(input));
        var policies = scenario.Simulation.Entities.Query<LogisticsStockPolicy>(
            QueryIterationOrder.StableByEntityIndex).ToArray();
        Assert.Single(policies.Where(p =>
            scenario.Simulation.Entities.GetComponent<LogisticsStockPolicy>(p).TargetEntity == facility &&
            scenario.Simulation.Entities.GetComponent<LogisticsStockPolicy>(p).ResourceId == ResourceIds.FerrousOre));
        scenario.Simulation.RunTicks(40);
        Assert.Equal(1, Assert.Single(scenario.OperationsRules.Capture(Owner)).ActionCount);
    }

    [Fact]
    public void ExistingDisabledPolicyIsNeverEnabledByARecoveryRule()
    {
        using MatchRuntime scenario = CreateScenario();
        EntityId facility = AddProcessingFacility(scenario, out _, out _);
        QueueSteel(scenario, facility);
        var policy = PlayerLogisticsActionCommand.SetStockPolicy(
            Owner, facility, ResourceIds.FerrousOre, 20, 50, 100,
            LogisticsStockPriority.Normal, false, SimulationTick.Zero);
        scenario.Simulation.SubmitCommand(policy, SimulationTick.Zero.Next());
        scenario.Simulation.AdvanceOneTick();
        Assert.True(policy.Accepted);
        var gateway = Gateway(scenario);
        gateway.SubmitOperationsRule(scenario.OperationsRules,
            OperationsRuleOperation.Upsert, InputRule(facility), 0, true,
            scenario.Simulation.CurrentTick);
        scenario.Simulation.RunTicks(10);
        Assert.Equal(OperationsRuleResult.ManualOverride,
            Assert.Single(scenario.OperationsRules.Capture(Owner)).Result);
        Assert.All(scenario.Simulation.Entities.Query<LogisticsStockPolicy>(),
            p => Assert.False(scenario.Simulation.Entities.GetComponent<LogisticsStockPolicy>(p).Enabled));
    }

    [Fact]
    public void FullOutputIsPausedAndResumedOnlyAfterActualCapacityRecovers()
    {
        using MatchRuntime scenario = CreateScenario();
        EntityId facility = AddProcessingFacility(scenario, out InventoryId input,
            out InventoryId output, outputCapacity: 10);
        Assert.True(scenario.Inventories.Add(input, ResourceIds.FerrousOre, 200).Succeeded);
        Assert.True(scenario.Inventories.Add(output, ResourceIds.Steel, 10).Succeeded);
        QueueSteel(scenario, facility);
        var gateway = Gateway(scenario);
        gateway.SubmitOperationsRule(scenario.OperationsRules,
            OperationsRuleOperation.Upsert, OutputRule(facility), 0, true,
            scenario.Simulation.CurrentTick);
        scenario.Simulation.RunTicks(55);
        var paused = Assert.Single(scenario.OperationsRules.Capture(Owner));
        Assert.True(paused.ManagedProductionRequest.IsValid);
        Assert.True(scenario.Simulation.Entities.GetComponent<ProductionRequest>(
            paused.ManagedProductionRequest).Paused);
        Assert.Equal(1, paused.ActionCount);

        Assert.True(scenario.Inventories.Remove(output, ResourceIds.Steel, 10).Succeeded);
        scenario.Simulation.AdvanceOneTick();
        var recovered = Assert.Single(scenario.OperationsRules.Capture(Owner));
        Assert.False(recovered.ManagedProductionRequest.IsValid);
        Assert.Equal(2, recovered.ActionCount);
        Assert.Equal(OperationsRuleResult.Recovered, recovered.Result);
    }

    [Fact]
    public void ManualPauseCommandOverridesRuleControlledRecovery()
    {
        using MatchRuntime scenario = CreateScenario();
        EntityId facility = AddProcessingFacility(scenario, out InventoryId input,
            out InventoryId output, outputCapacity: 10);
        Assert.True(scenario.Inventories.Add(input, ResourceIds.FerrousOre, 200).Succeeded);
        Assert.True(scenario.Inventories.Add(output, ResourceIds.Steel, 10).Succeeded);
        QueueSteel(scenario, facility);
        var gateway = Gateway(scenario);
        gateway.SubmitOperationsRule(scenario.OperationsRules,
            OperationsRuleOperation.Upsert, OutputRule(facility), 0, true,
            scenario.Simulation.CurrentTick);
        scenario.Simulation.RunTicks(55);
        EntityId request = Assert.Single(scenario.OperationsRules.Capture(Owner)).ManagedProductionRequest;
        Assert.True(request.IsValid);
        gateway.SubmitProductionPaused(Owner, request, true, scenario.Simulation.CurrentTick);
        scenario.Simulation.AdvanceOneTick();
        Assert.Equal(OperationsRuleResult.ManualOverride,
            Assert.Single(scenario.OperationsRules.Capture(Owner)).Result);
        Assert.True(scenario.Inventories.Remove(output, ResourceIds.Steel, 10).Succeeded);
        scenario.Simulation.RunTicks(40);
        Assert.True(scenario.Simulation.Entities.GetComponent<ProductionRequest>(request).Paused);
        Assert.Equal(1, Assert.Single(scenario.OperationsRules.Capture(Owner)).ActionCount);
    }

    [Fact]
    public void OperationsCommandsAreReplayableWithoutASeparateStateOwner()
    {
        using MatchRuntime scenario = CreateScenario();
        var definition = InputRule(scenario.GetBase(Owner).CommandCore);
        var command = new OperationsRuleCommand(scenario.OperationsRules,
            OperationsRuleOperation.Upsert, definition, 0, true, SimulationTick.Zero);
        var envelope = new SimulationCommandEnvelope(new SimulationTick(1), 1,
            new SimulationCommandSource(Owner.Value), command);
        Assert.True(ReplayCommandCodec.TryEncode(envelope, out var encoded));
        Assert.NotNull(encoded);
        Assert.Equal(ReplayCommandKind.OperationsRule, encoded.Kind);
        var decoded = Assert.IsType<OperationsRuleCommand>(ReplayCommandCodec.Decode(encoded, scenario));
        Assert.Equal(command.Definition, decoded.Definition);
        Assert.Equal(command.Operation, decoded.Operation);
        Assert.Equal(command.ExpectedRevision, decoded.ExpectedRevision);
    }

    private static OperationsRuleDefinition InputRule(EntityId facility) =>
        new(1, Owner, facility, OperationsRuleTemplate.EnsureInputStockPolicy,
            ResourceIds.FerrousOre, 20, 50, 100, LogisticsStockPriority.Normal, 5);

    private static OperationsRuleDefinition OutputRule(EntityId facility) =>
        new(2, Owner, facility, OperationsRuleTemplate.PauseUntilOutputAvailable,
            ResourceId.None, 0, 0, 0, LogisticsStockPriority.Normal, 5);

    private static MatchRuntime CreateScenario()
    {
        var runtime = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, seed: 9921) with
        {
            Participants = CentralDivideScenario.CreateDefaultParticipants(
                westComputerControlled: false, eastComputerControlled: false)
        };
        return CentralDivideScenario.Create(runtime, TestContext.Current.CancellationToken);
    }

    private static PlayerCommandGateway Gateway(MatchRuntime scenario)
    {
        var gateway = new PlayerCommandGateway(scenario.Simulation,
            scenario.Services.BuildingCommands, scenario.BattlefieldRuntime.MatchStateEntity);
        scenario.Simulation.RegisterTickObserver(gateway);
        return gateway;
    }

    private static EntityId AddProcessingFacility(MatchRuntime scenario,
        out InventoryId input, out InventoryId output,
        PlayerId owner = default, double outputCapacity = 1000)
    {
        if (!owner.IsSpecified) owner = Owner;
        var e = scenario.Simulation.Entities;
        input = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
        output = scenario.Inventories.CreateInventory(new InventorySpecification(outputCapacity));
        EntityId facility = e.CreateEntity();
        e.AddComponent(facility, new ControllableEntity(owner, ControllableEntityCategory.Building));
        e.AddComponent(facility, new ProductionFacility(input, output,
            ProductionCapability.SteelProcessing, SimulationTick.Zero));
        return facility;
    }

    private static void QueueSteel(MatchRuntime scenario, EntityId facility)
    {
        var queue = PlayerProductionActionCommand.Queue(Owner, facility,
            RecipeIds.Steel, scenario.Simulation.CurrentTick, mode: ProductionRequestMode.Repeat);
        scenario.Simulation.SubmitCommand(queue, scenario.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(Owner.Value));
    }
}
