using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TechnologyPersistenceTests
{
    [Fact]
    public void InProgressResearchReconstructsDeterministicallyFromRecordedCommand()
    {
        using MatchRuntime original =
            CreateScenario(
                seed: 4412);

        PlayerTechnologyActionCommand command =
            PlayerTechnologyActionCommand.Start(
                original.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                TechnologyIds.IndustrialStandardization,
                original.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                original.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                original.Simulation.CurrentTick,
                original.Services.TechnologyDefinitions);

        original.Simulation.SubmitCommand(
            command,
            original.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(
                original.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value));
        original.Simulation.RunTicks(
            20,
            TestContext.Current.CancellationToken);

        Assert.True(
            TechnologyStateQueries.TryGetActiveResearch(
                original.Simulation.Entities,
                original.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                out _,
                out TechnologyResearchRequest before));
        Assert.True(
            before.ProgressTicks >
            0);

        MatchSaveData save =
            MatchPersistenceService.CaptureSave(
                original);

        using MatchRuntime restored =
            MatchPersistenceService.Restore(
                save);

        Assert.True(
            TechnologyStateQueries.TryGetActiveResearch(
                restored.Simulation.Entities,
                restored.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                out _,
                out TechnologyResearchRequest after));
        Assert.Equal(
            before.TechnologyId,
            after.TechnologyId);
        Assert.Equal(
            before.ProgressTicks,
            after.ProgressTicks);
        Assert.Equal(
            before.MaterialsConsumed,
            after.MaterialsConsumed);
        Assert.Equal(
            save.StateSha256,
            MatchAuthoritativeSnapshot
                .Capture(restored)
                .ComputeSha256());
    }

    private static MatchRuntime CreateScenario(
        ulong seed)
    {
        MatchRuntimeSettings runtime =
            CentralDivideScenario.CreateHeadless(
                MatchScenarioProfile.Gameplay,
                seed) with
            {
                Participants =
                    CentralDivideScenario
                        .CreateDefaultParticipants(
                            westComputerControlled: false,
                            eastComputerControlled: false)
            };

        return CentralDivideScenario.Create(
            runtime,
            TestContext.Current.CancellationToken);
    }
}
