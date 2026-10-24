using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class GameplayPacingProgressTests
{
    [Fact]
    public void PaidBeltsConfirmBuildingHandoffAndCommittedOutputRatherThanStartingStock()
    {
        var fixture = new ConveyorTestFixture();
        var journal = new GameplayProgressJournal(fixture.Entities, fixture.Simulation.SessionId);
        fixture.Conveyor.Progress = journal;
        fixture.Simulation.RegisterTickObserver(journal);
        var source = fixture.AddStorage(new Vector3(24, 0, 112));
        var smelter = fixture.AddProduction(BuildingIds.Smelter, RecipeIds.Steel, new Vector3(64, 0, 112));
        fixture.Give(source, ResourceIds.FerrousOre, 20);
        fixture.Give(smelter, ResourceIds.Steel, 10);
        fixture.Step();
        Assert.Empty(journal.Capture(fixture.Player, fixture.Simulation.CurrentTick));
        fixture.Connect(source, smelter);
        fixture.Simulation.SubmitCommand(PlayerProductionActionCommand.Queue(fixture.Player, smelter, RecipeIds.Steel,
            fixture.Simulation.CurrentTick), fixture.Simulation.CurrentTick.Next());
        fixture.RunUntil(() => journal.Capture(fixture.Player, fixture.Simulation.CurrentTick)
            .Any(item => item.Kind == GameplayProgressKind.ProductionCompleted), 500);
        var delivery = Assert.Single(journal.CaptureFirst(fixture.Player, fixture.Simulation.CurrentTick),
            item => item.Kind == GameplayProgressKind.ConveyorDelivered);
        Assert.Equal(smelter, delivery.Target);
        Assert.Equal(ResourceIds.FerrousOre, delivery.Resource);
        Assert.True(delivery.Quantity > 0);
        Assert.Equal(10, Assert.Single(journal.Capture(fixture.Player, fixture.Simulation.CurrentTick),
            item => item.Kind == GameplayProgressKind.ProductionCompleted).Quantity);
        fixture.Step(20);
        Assert.Equal(delivery, Assert.Single(journal.CaptureFirst(fixture.Player, fixture.Simulation.CurrentTick),
            item => item.Kind == GameplayProgressKind.ConveyorDelivered));
        Assert.Empty(journal.CaptureFirst(fixture.OtherPlayer, fixture.Simulation.CurrentTick));
        Assert.Empty(journal.CaptureFirst(fixture.Player, fixture.Simulation.CurrentTick.Next()));
    }

    [Fact]
    public void ResearchEvidenceRequiresPaidCompletionAndKeepsFirstTickAcrossMissedReads()
    {
        var fixture = new ConveyorTestFixture();
        var journal = new GameplayProgressJournal(fixture.Entities, fixture.Simulation.SessionId);
        fixture.Simulation.RegisterTickObserver(journal);
        var research = new TechnologyResearchSystem(DirectorateTechnologyDefinitions.CreateCatalog(), fixture.Inventories)
            { Progress = journal };
        fixture.Simulation.RegisterSystem(research);
        var facility = fixture.AddBuilding(BuildingIds.CommandCore, new Vector3(64, 0, 112));
        fixture.Give(facility, ResourceIds.Steel, 80);
        fixture.Give(facility, ResourceIds.Electronics, 20);
        var definition = DirectorateTechnologyDefinitions.CreateCatalog()[TechnologyIds.IndustrialStandardization];
        // The existing research command selects the authored required facility and real local stock.
        Assert.Equal(BuildingIds.CommandCore, definition.RequiredFacility);
        var command = PlayerTechnologyActionCommand.Start(fixture.Player, definition.Id, facility,
            facility, fixture.Simulation.CurrentTick, DirectorateTechnologyDefinitions.CreateCatalog());
        fixture.Simulation.SubmitCommand(command, fixture.Simulation.CurrentTick.Next());
        fixture.Step();
        Assert.DoesNotContain(journal.Capture(fixture.Player, fixture.Simulation.CurrentTick), item => item.Kind == GameplayProgressKind.ResearchCompleted);
        Assert.Equal(0, fixture.Stock(facility, ResourceIds.Steel));
        fixture.Step((int)definition.ResearchTicks);
        var proof = Assert.Single(journal.CaptureFirst(fixture.Player, fixture.Simulation.CurrentTick), item => item.Kind == GameplayProgressKind.ResearchCompleted);
        Assert.Equal(definition.Id.Value, proof.DefinitionId);
        Assert.Equal(facility, proof.Target);
        Assert.True(TechnologyStateQueries.IsCompleted(fixture.Entities, fixture.Player, definition.Id));
        fixture.Step(10);
        Assert.Equal(proof, Assert.Single(journal.CaptureFirst(fixture.Player, fixture.Simulation.CurrentTick), item => item.Kind == GameplayProgressKind.ResearchCompleted));
    }

    [Fact]
    public void TelemetryOnAndOffKeepIdenticalGameplayHashesAndUnreachedMilestonesStayNull()
    {
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 581);
        using var observed = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        using var plain = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        var telemetry = new GameplayTelemetryCollector(observed);
        for (int tick = 0; tick < 80; tick++)
        {
            observed.Simulation.AdvanceOneTick();
            telemetry.Observe();
            plain.Simulation.AdvanceOneTick();
        }
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(plain).ComputeSha256(), MatchAuthoritativeSnapshot.Capture(observed).ComputeSha256());
        var snapshot = telemetry.Capture();
        Assert.NotNull(snapshot.Pacing);
        Assert.Null(Assert.Single(snapshot.Pacing, item => item.Name == GameplayPacingNames.MobileSupply && item.Owner == "player:1").Tick);
        var analysis = GameplayTelemetryAnalysis.Aggregate([new(1, 581, snapshot)]);
        var missing = Assert.Single(analysis.Pacing!, item => item.Name == GameplayPacingNames.MobileSupply && item.Owner == "player:1");
        Assert.Equal(0, missing.ReachedMatches);
        Assert.Equal(1, missing.UnreachedMatches);
        Assert.Null(missing.MinimumTick);
        Assert.Null(missing.MeanTick);
    }
}
