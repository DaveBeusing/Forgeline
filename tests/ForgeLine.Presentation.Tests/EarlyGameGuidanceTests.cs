using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class EarlyGameGuidanceTests
{
    [Fact]
    public void CanonicalStartSkipsCoreAndSupplyButStockIsNotProduction()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var buffer = WorldHoverExtractionTests.Observe(scenario, new PresentationInteractionState());
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var summary = Assert.IsType<PlayerGuidanceSummary>(snapshot.Guidance);
        Assert.True(summary.Observed.HasFlag(PlayerGuidanceMilestone.CommandCore));
        Assert.True(summary.Observed.HasFlag(PlayerGuidanceMilestone.Supply));
        Assert.False(summary.Observed.HasFlag(PlayerGuidanceMilestone.SteelProcessing));
        Assert.False(summary.Observed.HasFlag(PlayerGuidanceMilestone.Scout));
        Assert.Equal("ESTABLISH POWER", new EarlyGameGuidanceController().Update(snapshot, true).Objective);
    }

    [Fact]
    public void OutOfOrderCompletionIsObservedWhileHiddenAndResetsOnReplacement()
    {
        var guide = new EarlyGameGuidanceController();
        var first = Snapshot(PlayerGuidanceMilestone.CommandCore | PlayerGuidanceMilestone.VehicleFactory | PlayerGuidanceMilestone.Scout);
        Assert.False(guide.Update(first, false).Visible);
        Assert.True(guide.Observed.HasFlag(PlayerGuidanceMilestone.Scout));
        Assert.Equal("ESTABLISH POWER", guide.Update(first, true).Objective);
        var second = Snapshot(PlayerGuidanceMilestone.CommandCore | PlayerGuidanceMilestone.Power |
            PlayerGuidanceMilestone.FerrousExtraction | PlayerGuidanceMilestone.SteelProcessing, tick: 2);
        Assert.Equal("MAKE SUPPLY AVAILABLE", guide.Update(second, true).Objective);
        Assert.False(guide.Update(first, true).Visible); // Older publication cannot advance or rewind the guide.
        Assert.Equal("ESTABLISH COMMAND CORE", guide.Update(Snapshot(0, session: 2), true).Objective);
        Assert.Equal(PlayerGuidanceMilestone.None, guide.Observed);
    }

    [Fact]
    public void IncoherentOrTerminalSnapshotsCannotShowOrAdvanceGuidance()
    {
        var guide = new EarlyGameGuidanceController();
        var valid = Snapshot(PlayerGuidanceMilestone.CommandCore);
        var mismatched = new PresentationSnapshot(valid.Tick, TimeSpan.Zero, 0, [], sessionId: valid.SessionId,
            playerExperience: valid.PlayerExperience, guidance: valid.Guidance!.Value with { SessionId = new SimulationSessionId(2) });
        Assert.False(guide.Update(mismatched, true).Visible);
        Assert.Equal(PlayerGuidanceMilestone.None, guide.Observed);
        Assert.False(guide.Update(Snapshot((PlayerGuidanceMilestone)255, terminal: true), true).Visible);
        Assert.False(guide.Update(null, true).Visible);
        Assert.False(guide.Update(valid, true, blocked: true).Visible);
        Assert.Equal(PlayerGuidanceMilestone.CommandCore, guide.Observed);
    }

    [Fact]
    public void RealConstructionAndProductionAdvanceOnlyAfterCompletedState()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var player = new PlayerId(1);
        var gateway = new PlayerCommandGateway(scenario.Simulation, scenario.Services.BuildingCommands, scenario.BattlefieldRuntime.MatchStateEntity);
        scenario.Simulation.RegisterTickObserver(gateway);
        var buffer = WorldHoverExtractionTests.Observe(scenario, new PresentationInteractionState());
        foreach (var building in new[] { BuildingIds.VehicleFactory, BuildingIds.PowerPlant, BuildingIds.PowerPlant, BuildingIds.Smelter })
        {
            var position = FindPlacement(scenario, building);
            Assert.True(gateway.SubmitBuild(player, building, position, BuildingOrientation.North,
                scenario.GetBase(player).CommandCore, scenario.Simulation.CurrentTick).Accepted);
            scenario.Simulation.AdvanceOneTick();
            Assert.True(buffer.TryReadLatest(out var pending));
            Assert.False(pending.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.VehicleFactory));
        }
        scenario.Simulation.RunTicks(600, TestContext.Current.CancellationToken);
        Assert.True(buffer.TryReadLatest(out var built));
        Assert.True(built.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.Power));
        Assert.True(built.Guidance.Value.Observed.HasFlag(PlayerGuidanceMilestone.VehicleFactory));
        Assert.False(built.Guidance.Value.Observed.HasFlag(PlayerGuidanceMilestone.SteelProcessing));
        var saved = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(scenario)));
        using var restored = MatchPersistenceService.Restore(saved);
        var restoredBuffer = new PresentationSnapshotBuffer();
        var restoredGateway = new PlayerCommandGateway(restored.Simulation, restored.Services.BuildingCommands, restored.BattlefieldRuntime.MatchStateEntity);
        restored.Simulation.AttachTickObserver(new PresentationExtractor(restoredBuffer,
            new PresentationExtractionContext(restored, player, new PresentationInteractionState(), restoredGateway)));
        restored.Simulation.AdvanceOneTick();
        Assert.True(restoredBuffer.TryReadLatest(out var loaded));
        Assert.NotEqual(built.SessionId, loaded.SessionId);
        Assert.True(loaded.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.Power));
        Assert.True(loaded.Guidance.Value.Observed.HasFlag(PlayerGuidanceMilestone.VehicleFactory));
        var smelter = FindBuilding(scenario, BuildingIds.Smelter);
        var processor = scenario.Simulation.Entities.GetComponent<ProductionFacility>(smelter);
        Assert.True(scenario.Inventories.Transfer(scenario.GetBase(player).StartingInventory, processor.InputInventory,
            ResourceIds.FerrousOre, 20).Succeeded);
        Assert.True(gateway.SubmitProduction(player, smelter, RecipeIds.Steel, scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var queued));
        Assert.False(queued.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.SteelProcessing));
        scenario.Simulation.RunTicks(scenario.Services.ProductionRecipes[RecipeIds.Steel].DurationTicks + 2, TestContext.Current.CancellationToken);
        Assert.True(buffer.TryReadLatest(out var processed));
        Assert.True(processed.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.SteelProcessing));
        Assert.False(built.Guidance.Value.Observed.HasFlag(PlayerGuidanceMilestone.SteelProcessing)); // Retained copy is immutable.
        var factoryEntity = FindBuilding(scenario, BuildingIds.VehicleFactory);
        var factory = scenario.Simulation.Entities.GetComponent<UnitProductionFacility>(factoryEntity);
        var scout = scenario.Services.UnitDefinitions[UnitIds.ScoutVehicle];
        foreach (var cost in scout.Costs)
            Assert.True(scenario.Inventories.Transfer(scenario.GetBase(player).StartingInventory, factory.InputInventory,
                cost.ResourceId, cost.Quantity).Succeeded);
        Assert.True(gateway.SubmitUnitProduction(player, factoryEntity, UnitIds.ScoutVehicle, scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var scoutQueued));
        Assert.False(scoutQueued.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.Scout));
        scenario.Simulation.RunTicks(scout.ProductionTicks + 2, TestContext.Current.CancellationToken);
        Assert.True(buffer.TryReadLatest(out var scoutReady));
        Assert.True(scoutReady.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.Scout));
    }

    [Fact]
    public void ForeignIndustryDoesNotCompleteLocalMilestones()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var entities = scenario.Simulation.Entities;
        var foreign = entities.CreateEntity();
        entities.AddComponent(foreign, new ControllableEntity(new PlayerId(2), ControllableEntityCategory.Building));
        entities.AddComponent(foreign, new CompletedBuilding(BuildingIds.PowerPlant, new PlayerId(2), SimulationTick.Zero));
        entities.AddComponent(foreign, new PowerGenerator(500));
        var buffer = WorldHoverExtractionTests.Observe(scenario, new PresentationInteractionState());
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        Assert.False(snapshot.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.Power));
    }

    [Fact]
    public void ExtractionRequiresObservedLocalWorkAndScoutingRequiresCurrentPermittedContact()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var entities = scenario.Simulation.Entities;
        var deposit = entities.CreateEntity();
        entities.AddComponent(deposit, new ResourceDeposit(ResourceIds.FerrousOre,
            new ForgeLine.World.AxisAlignedBounds(Vector3.Zero, Vector3.One), 100, 1));
        var extractor = entities.CreateEntity();
        entities.AddComponent(extractor, new ControllableEntity(new PlayerId(1), ControllableEntityCategory.Building));
        entities.AddComponent(extractor, new ResourceExtractor(deposit, ResourceIds.FerrousOre, 10,
            new FactionId(1), outputInventory: scenario.GetBase(new PlayerId(1)).CommandCore));
        var enemy = scenario.GetBase(new PlayerId(2)).CommandCore;
        bool detected = false;
        scenario.Simulation.RegisterTickObserver(new ContactObserver(context =>
        {
            scenario.Intelligence.BeginTick(context.Tick);
            if (detected) scenario.Intelligence.Observe(new FactionId(1), enemy,
                entities.GetComponent<IntelligenceSignature>(enemy), Vector3.Zero, IntelligenceState.Detected, context.Tick);
        }));
        var buffer = WorldHoverExtractionTests.Observe(scenario, new PresentationInteractionState());
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var hidden));
        Assert.True(hidden.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.FerrousExtraction));
        Assert.False(hidden.Guidance.Value.Observed.HasFlag(PlayerGuidanceMilestone.OpponentContact));
        detected = true;
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var contact));
        Assert.True(contact.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.OpponentContact));
        Assert.Equal(0u, Assert.Single(contact.Intelligence!.Contacts).IdentityKey);
        detected = false;
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var lost));
        Assert.False(lost.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.OpponentContact));
    }

    [Fact]
    public void RejectedBuildingCommandCannotCompleteMilestone()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var gateway = new PlayerCommandGateway(scenario.Simulation, scenario.Services.BuildingCommands, scenario.BattlefieldRuntime.MatchStateEntity);
        scenario.Simulation.RegisterTickObserver(gateway);
        var buffer = WorldHoverExtractionTests.Observe(scenario, new PresentationInteractionState());
        Assert.True(gateway.SubmitBuild(new PlayerId(1), BuildingIds.PowerPlant, new Vector3(-100000), BuildingOrientation.North,
            scenario.GetBase(new PlayerId(1)).CommandCore, scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out var result));
        Assert.Equal(PlayerCommandFeedbackState.Rejected, result.State);
        Assert.True(buffer.TryReadLatest(out var snapshot));
        Assert.False(snapshot.Guidance!.Value.Observed.HasFlag(PlayerGuidanceMilestone.Power));
    }

    private sealed class ContactObserver(Action<SimulationContext> action) : ISimulationTickObserver
    {
        public void OnTickCompleted(SimulationContext context) => action(context);
    }

    internal static PresentationSnapshot Snapshot(PlayerGuidanceMilestone observed, ulong tick = 1, ulong session = 1, bool terminal = false) =>
        new(new SimulationTick(tick), TimeSpan.Zero, 0, [], sessionId: new SimulationSessionId(session),
            playerExperience: default(PlayerExperienceSnapshot) with { Player = new PlayerId(1), MatchStatus = terminal ? PlayerMatchStatus.Victory : PlayerMatchStatus.Active },
            guidance: new PlayerGuidanceSummary(new SimulationSessionId(session), new SimulationTick(tick), new PlayerId(1), observed));

    internal static EntityId FindBuilding(MatchRuntime scenario, BuildingId building)
    {
        foreach (var entity in scenario.Simulation.Entities.Query<CompletedBuilding>())
            if (scenario.Simulation.Entities.GetComponent<CompletedBuilding>(entity) is var completed &&
                completed.Owner == new PlayerId(1) && completed.BuildingId == building) return entity;
        throw new InvalidOperationException("Completed test building missing.");
    }

    internal static Vector3 FindPlacement(MatchRuntime scenario, BuildingId building)
    {
        var core = scenario.Simulation.Entities.GetComponent<WorldTransform>(scenario.GetBase(new PlayerId(1)).CommandCore).Position;
        for (int x = 60; x < 500; x += 35)
            for (int z = -300; z < 300; z += 35)
            {
                var position = core + new Vector3(x, 0, z);
                if (scenario.Services.BuildingPlacement.Evaluate(scenario.Simulation.Entities, new PlayerId(1), building, position, BuildingOrientation.North).IsValid)
                    return position;
            }
        throw new InvalidOperationException("No valid test placement found.");
    }
}
