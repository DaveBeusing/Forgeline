using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TechnologyNormalMatchTests
{
    [Fact]
    public void NormalOpponentPaysForResearchAndReconstructsWhileProductionContinues()
    {
        using var original = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(
            MatchScenarioProfile.Gameplay, 2026) with
        {
            EnableDebugCapture = true,
            Participants = CentralDivideScenario.CreateDefaultParticipants(westComputerControlled: false, eastComputerControlled: true)
        }, TestContext.Current.CancellationToken);
        PlayerId player = new(2);
        bool sawActive = false;
        for (int step = 0; step < 600; step++)
        {
            original.Simulation.RunTicks(100, TestContext.Current.CancellationToken);
            if (step % 100 == 0)
                foreach (var diagnostic in original.Opponents.DebugSnapshot)
                    TestContext.Current.TestOutputHelper?.WriteLine($"{original.Simulation.CurrentTick}: {diagnostic.StrategicState}, {diagnostic.Economy}, {diagnostic.Force}, {diagnostic.Research}");
            if (TechnologyStateQueries.TryGetActiveResearch(original.Simulation.Entities, player, out _, out var active) &&
                active.MaterialsConsumed && active.ProgressTicks > 0 && !sawActive)
            {
                sawActive = true;
                var save = MatchPersistenceService.CaptureSave(original);
                var replay = MatchPersistenceService.CaptureReplay(original);
                using var restored = MatchPersistenceService.Restore(MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(save)));
                using var playback = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(replay)));
                foreach (ulong ticks in new ulong[] { 0, 1, 9, 30 })
                {
                    original.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
                    restored.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
                    playback.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
                    string hash = MatchAuthoritativeSnapshot.Capture(original).ComputeSha256();
                    Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
                    Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
                }
            }
            if (TechnologyStateQueries.IsCompleted(original.Simulation.Entities, player, TechnologyIds.MechanizedSystems) &&
                HasUnit(UnitIds.MainBattleTank) && HasUnit(UnitIds.MobileArtillery)) break;
        }
        Assert.True(sawActive);
        Assert.True(TechnologyStateQueries.IsCompleted(original.Simulation.Entities, player, TechnologyIds.IndustrialStandardization));
        Assert.True(TechnologyStateQueries.IsCompleted(original.Simulation.Entities, player, TechnologyIds.MechanizedSystems));
        Assert.True(HasUnit(UnitIds.MainBattleTank), $"Paid MBT missing at tick {original.Simulation.CurrentTick}.");
        Assert.True(HasUnit(UnitIds.MobileArtillery), $"Paid artillery missing at tick {original.Simulation.CurrentTick}.");
        bool HasUnit(UnitId id)
        {
            foreach (EntityId entity in original.Simulation.Entities.Query<UnitIdentity, ControllableEntity>())
                if (original.Simulation.Entities.GetComponent<UnitIdentity>(entity).UnitId == id &&
                    original.Simulation.Entities.GetComponent<ControllableEntity>(entity).Owner == player) return true;
            return false;
        }
        var schemaSevenSave = MatchPersistenceService.CaptureSave(original);
        var oldState = schemaSevenSave.State with { SchemaVersion = 7 };
        schemaSevenSave = schemaSevenSave with { State = oldState, StateSha256 = oldState.ComputeSha256() };
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(schemaSevenSave));
        var oldReplay = MatchPersistenceService.CaptureReplay(original) with { AuthoritativeSchemaVersion = 7 };
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplay(oldReplay));
    }
}
