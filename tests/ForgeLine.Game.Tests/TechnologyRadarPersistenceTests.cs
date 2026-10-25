using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TechnologyRadarPersistenceTests
{
    [Fact]
    public void PaidFusionReconstructsDuringResearchAndWithItsRadarBenefit()
    {
        using var original = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(
            MatchScenarioProfile.Gameplay, 4418) with
        {
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false)
        }, TestContext.Current.CancellationToken);
        var side = original.GetBase(new PlayerId(1));
        var entities = original.Simulation.Entities;
        Build(BuildingIds.PowerPlant);
        EntityId radar = Build(BuildingIds.Radar);
        Assert.Equal(180, Range(original));
        Start(TechnologyIds.IndustrialStandardization, side.CommandCore);
        Advance(original, 121);
        Assert.True(TechnologyStateQueries.IsCompleted(entities, side.Player, TechnologyIds.IndustrialStandardization));
        Start(TechnologyIds.SensorFusion, radar);
        Advance(original, 20);
        Assert.True(TechnologyStateQueries.TryGetActiveResearch(entities, side.Player, out _, out var active));
        Assert.True(active.MaterialsConsumed);
        CompareReconstruction(12);
        Advance(original, 120);
        Assert.True(TechnologyStateQueries.IsCompleted(entities, side.Player, TechnologyIds.SensorFusion));
        Assert.Equal(225, Range(original));
        Assert.Equal(180, entities.GetComponent<RadarSensorState>(radar).IdentificationRangeMeters);
        CompareReconstruction(20);

        void CompareReconstruction(ulong ticks)
        {
            using var restored = MatchPersistenceService.Restore(MatchPersistenceSerializer.DeserializeSave(
                MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(original))));
            using var playback = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(
                MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(original))));
            foreach (ulong advance in new ulong[] { 0, ticks })
            {
                Advance(original, advance); Advance(restored, advance); Advance(playback, advance);
                string hash = MatchAuthoritativeSnapshot.Capture(original).ComputeSha256();
                Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
                Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
                Assert.Equal(Range(original), Range(restored));
                Assert.Equal(Range(original), Range(playback));
            }
        }
        float Range(MatchRuntime runtime) => RadarOperation.IdentificationRange(runtime.Simulation.Entities,
            radar, runtime.Simulation.Entities.GetComponent<RadarSensorState>(radar), runtime.Services.TechnologyDefinitions);
        void Start(TechnologyId id, EntityId facility)
        {
            var command = PlayerTechnologyActionCommand.Start(side.Player, id, facility, side.CommandCore,
                original.Simulation.CurrentTick, original.Services.TechnologyDefinitions);
            Submit(command); Advance(original, 1); Assert.True(command.Accepted);
        }
        EntityId Build(BuildingId id)
        {
            Vector3 center = entities.GetComponent<WorldTransform>(side.CommandCore).Position;
            for (int radius = 2; radius <= 9; radius++)
            for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            {
                if (Math.Abs(x) != radius && Math.Abs(z) != radius) continue;
                var preview = original.Services.BuildingPlacement.CreatePreview(entities, side.Player, id,
                    center + new Vector3(x * 36, 0, z * 36), BuildingOrientation.North);
                if (!preview.IsValid) continue;
                Submit(new BuildCommand(side.Player, id, preview.GroundPosition, BuildingOrientation.North,
                    side.CommandCore, original.Simulation.CurrentTick));
                Advance(original, original.Services.BuildingDefinitions[id].ConstructionTicks + 2UL);
                foreach (EntityId entity in entities.Query<CompletedBuilding>())
                    if (entities.GetComponent<CompletedBuilding>(entity) is var building &&
                        building.Owner == side.Player && building.BuildingId == id) return entity;
                throw new InvalidOperationException($"Paid construction did not complete: {id}.");
            }
            throw new InvalidOperationException($"No valid placement: {id}.");
        }
        void Submit(ISimulationCommand command) => original.Simulation.SubmitCommand(command,
            original.Simulation.CurrentTick.Next(), new SimulationCommandSource(side.Player.Value));
    }

    private static void Advance(MatchRuntime runtime, ulong ticks) =>
        runtime.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
}
