using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
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
        BuildResearchPowerSupply(original);

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

        Assert.True(command.Accepted,
            $"Research command was rejected for facility {command.Facility} and source {command.SourceInventory}.");

        Assert.True(
            TechnologyStateQueries.TryGetActiveResearch(
                original.Simulation.Entities,
                original.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                out _,
                out TechnologyResearchRequest before),
            $"Research state missing at tick {original.Simulation.CurrentTick}; completed={TechnologyStateQueries.IsCompleted(original.Simulation.Entities, command.Issuer, command.TechnologyId)}.");
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

    private static void BuildResearchPowerSupply(MatchRuntime runtime)
    {
        SkirmishStartingBase side = runtime.GetBase(new ForgeLine.Game.PlayerId(1));
        Vector3 center = runtime.Simulation.Entities.GetComponent<WorldTransform>(side.CommandCore).Position;
        for (int radius = 2; radius <= 9; radius++)
        for (int z = -radius; z <= radius; z++)
        for (int x = -radius; x <= radius; x++)
        {
            if (Math.Abs(x) != radius && Math.Abs(z) != radius) continue;
            BuildingPlacementPreview preview = runtime.Services.BuildingPlacement.CreatePreview(
                runtime.Simulation.Entities, side.Player, BuildingIds.PowerPlant,
                center + new Vector3(x * 36.0f, 0, z * 36.0f), BuildingOrientation.North);
            if (!preview.IsValid) continue;
            var build = new BuildCommand(side.Player, BuildingIds.PowerPlant, preview.GroundPosition,
                BuildingOrientation.North, side.CommandCore, runtime.Simulation.CurrentTick);
            runtime.Simulation.SubmitCommand(build, runtime.Simulation.CurrentTick.Next(),
                new SimulationCommandSource(side.Player.Value));
            runtime.Simulation.RunTicks(runtime.Services.BuildingDefinitions[BuildingIds.PowerPlant].ConstructionTicks + 1UL,
                TestContext.Current.CancellationToken);
            Assert.Equal(BuildCommandRejectionReason.None, runtime.Services.BuildingCommands.Metrics.LastRejection);
            Assert.Equal(1.0, runtime.Simulation.Entities.GetComponent<PowerConsumer>(side.CommandCore).SupplyFraction);
            return;
        }
        throw new InvalidOperationException("No valid placement for the research power supply.");
    }
}
