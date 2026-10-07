using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TechnologyPersistenceTests
{
    [Fact]
    public void InProgressResearchReconstructsDeterministicallyFromRecordedCommand()
    {
        using VerticalSliceScenario original =
            CreateScenario(
                seed: 4412);

        PlayerTechnologyActionCommand command =
            PlayerTechnologyActionCommand.Start(
                original.West.Player,
                TechnologyIds.IndustrialStandardization,
                original.West.CommandCore,
                original.West.CommandCore,
                original.Simulation.CurrentTick,
                original.Services.TechnologyDefinitions);

        original.Simulation.SubmitCommand(
            command,
            original.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(
                original.West.Player.Value));
        original.Simulation.RunTicks(
            20,
            TestContext.Current.CancellationToken);

        Assert.True(
            TechnologyStateQueries.TryGetActiveResearch(
                original.Simulation.Entities,
                original.West.Player,
                out _,
                out TechnologyResearchRequest before));
        Assert.True(
            before.ProgressTicks >
            0);

        MatchSaveData save =
            MatchPersistenceService.CaptureSave(
                original);

        using VerticalSliceScenario restored =
            MatchPersistenceService.Restore(
                save);

        Assert.True(
            TechnologyStateQueries.TryGetActiveResearch(
                restored.Simulation.Entities,
                restored.West.Player,
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
            VerticalSliceAuthoritativeSnapshot
                .Capture(restored)
                .ComputeSha256());
    }

    private static VerticalSliceScenario CreateScenario(
        ulong seed)
    {
        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateHeadless(
                VerticalSliceScenarioProfile.Gameplay,
                seed) with
            {
                Participants =
                    VerticalSliceRuntimeSettings
                        .CreateDefaultParticipants(
                            westComputerControlled: false,
                            eastComputerControlled: false)
            };

        return VerticalSliceScenario.Create(
            runtime,
            TestContext.Current.CancellationToken);
    }
}
