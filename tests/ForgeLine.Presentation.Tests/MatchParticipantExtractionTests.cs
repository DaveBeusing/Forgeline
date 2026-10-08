using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class MatchParticipantExtractionTests
{
    [Fact]
    public void ExtractionUsesConfiguredPlayerRatherThanPresetSideIds()
    {
        MatchComposition preset = CentralDivideScenario.CreateComposition();
        BattlefieldDefinition original = preset.Battlefield;
        var starts = original.Starts.Select((start, index) => start with
        {
            Player = new PlayerId(index == 0 ? 7UL : 11UL)
        }).ToArray();
        var map = new BattlefieldDefinition(original.Metadata with { Key = "test.remapped-starts" },
            starts, original.Resources.ToArray(), original.WorldObjects.ToArray(), original.Sites.ToArray(),
            original.RoadNodes.ToArray(), original.RoadEdges.ToArray(), original.Crossings.ToArray(),
            original.Objectives.Select((objective, index) => objective with { Owner = starts[index].Player }).ToArray(),
            original.StaticNavigationObstacles.ToArray(), original.TerrainVisual);
        MatchRuntimeSettings settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay) with
        {
            Composition = preset with { Key = "test.remapped-starts.v1", Battlefield = map },
            Participants = starts.Select((start, index) => new MatchParticipantConfiguration(
                start.Player, new FactionId((uint)start.Player.Value), index, false)).ToArray()
        };
        using MatchRuntime runtime = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        var gateway = new PlayerCommandGateway(runtime.Simulation, runtime.Services.BuildingCommands,
            runtime.BattlefieldRuntime.MatchStateEntity);
        var context = new PresentationExtractionContext(runtime, new PlayerId(11),
            new PresentationInteractionState(), gateway);
        Assert.Equal(runtime.GetBase(new PlayerId(11)), context.Side);
        var buffer = new PresentationSnapshotBuffer();
        runtime.Simulation.RegisterTickObserver(gateway);
        runtime.Simulation.RegisterTickObserver(new PresentationExtractor(buffer, context));
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out PresentationSnapshot snapshot));
        Assert.True(snapshot.PlayerExperience.HasValue);
        Assert.Equal(runtime.Simulation.SessionId, snapshot.SessionId);
    }
}
