using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class WorldHoverExtractionTests
{
    [Fact]
    public void OwnedHoverIsCopiedWithoutSelectionAndClearsOnDestruction()
    {
        using var scenario = CreateScenario();
        var interaction = new PresentationInteractionState();
        var buffer = Observe(scenario, interaction);
        EntityId entity = scenario.GetBase(new PlayerId(1)).CommandCore;
        interaction.SetHover(entity);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var first));
        var hover = Assert.IsType<PlayerHoverSummary>(first.Hover);
        Assert.Equal(entity, hover.Entity);
        Assert.Equal(first.Tick, hover.Tick);
        Assert.Equal(first.SessionId, hover.SessionId);
        Assert.Equal(PlayerHoverCategory.Building, hover.Category);
        Assert.True(hover.OwnedDetails.HasHealth);
        Assert.Equal(0, first.PlayerExperience!.Value.Selection.Count);
        scenario.Simulation.Entities.DestroyEntity(entity);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var second));
        Assert.Null(second.Hover);
        Assert.Equal(hover, first.Hover);
    }

    [Fact]
    public void HiddenEnemyAndStaleGenerationCannotPublishFacts()
    {
        using var scenario = CreateScenario();
        var interaction = new PresentationInteractionState();
        var buffer = Observe(scenario, interaction);
        interaction.SetHover(scenario.GetBase(new PlayerId(2)).CommandCore);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var hidden));
        Assert.Null(hidden.Hover);
        var entity = scenario.GetBase(new PlayerId(1)).CommandCore;
        interaction.SetHover(new EntityId(entity.Index, entity.Generation + 1));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var stale));
        Assert.Null(stale.Hover);
    }

    internal static MatchRuntime CreateScenario() => CentralDivideScenario.Create(
        CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 4209) with
        {
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false)
        }, TestContext.Current.CancellationToken);

    internal static PresentationSnapshotBuffer Observe(MatchRuntime scenario, PresentationInteractionState interaction)
    {
        var buffer = new PresentationSnapshotBuffer();
        var gateway = new PlayerCommandGateway(scenario.Simulation, scenario.Services.BuildingCommands,
            scenario.BattlefieldRuntime.MatchStateEntity);
        scenario.Simulation.RegisterTickObserver(new PresentationExtractor(buffer,
            new PresentationExtractionContext(scenario, new PlayerId(1), interaction, gateway)));
        return buffer;
    }
}
