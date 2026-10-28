using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class OpponentDifficultyProfileTests
{
    private static MatchRuntimeSettings Settings(OpponentDifficultyId west, OpponentDifficultyId east, ulong seed = 17) =>
        CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, seed, enableDebugCapture: true) with
        {
            Scenario = OpponentDifficultyProfiles.Apply(CentralDivideScenario.CreateSettings(MatchScenarioProfile.Gameplay),
                new Dictionary<ulong, OpponentDifficultyId> { [1] = west, [2] = east })
        };

    [Fact]
    public void ProfilesAreStableValidatedAndMonotonicWithoutNewAuthority()
    {
        var easy = OpponentDifficultyProfiles.Get(OpponentDifficultyId.Easy);
        var standard = OpponentDifficultyProfiles.Get(OpponentDifficultyId.Standard);
        var hard = OpponentDifficultyProfiles.Get(OpponentDifficultyId.Hard);
        Assert.Equal("skirmish.easy.v1", easy.Key);
        Assert.Equal("skirmish.standard.v1", standard.Key);
        Assert.Equal("skirmish.hard.v1", hard.Key);
        Assert.Equal(new SkirmishOpponentConfiguration(), standard.Configuration);
        foreach (var profile in OpponentDifficultyProfiles.All)
        {
            profile.Configuration.Validate();
            Assert.Equal(profile.Key, OpponentDifficultyProfiles.Identify(profile.Configuration));
            Assert.InRange(profile.Configuration.Raids.MaximumContacts, 1, 32);
            Assert.InRange(profile.Configuration.Raids.MaximumUnits, 2, 3);
            Assert.Equal(standard.Configuration.OffensiveFuelThreshold, profile.Configuration.OffensiveFuelThreshold);
            Assert.Equal(standard.Configuration.RetreatThreshold, profile.Configuration.RetreatThreshold);
            Assert.Equal(standard.Configuration.Raids.FuelMargin, profile.Configuration.Raids.FuelMargin);
        }
        Assert.True(easy.Configuration.ReactionCadenceTicks > standard.Configuration.ReactionCadenceTicks &&
            standard.Configuration.ReactionCadenceTicks > hard.Configuration.ReactionCadenceTicks);
        Assert.True(easy.Configuration.MaximumQueuedUnitsPerFacility < standard.Configuration.MaximumQueuedUnitsPerFacility &&
            standard.Configuration.MaximumQueuedUnitsPerFacility < hard.Configuration.MaximumQueuedUnitsPerFacility);
        Assert.True(easy.Configuration.MaximumAttackUnits < standard.Configuration.MaximumAttackUnits &&
            standard.Configuration.MaximumAttackUnits < hard.Configuration.MaximumAttackUnits);
        Assert.True(easy.Configuration.Raids.MaximumContacts <= standard.Configuration.Raids.MaximumContacts &&
            standard.Configuration.Raids.MaximumContacts <= hard.Configuration.Raids.MaximumContacts);
        Assert.True(easy.Configuration.Raids.CooldownTicks > standard.Configuration.Raids.CooldownTicks &&
            standard.Configuration.Raids.CooldownTicks > hard.Configuration.Raids.CooldownTicks);
    }

    [Fact]
    public void ApplyingProfilesPreservesStartingStateAndCallerSettings()
    {
        var baseline = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Gameplay);
        string unchanged = JsonSerializer.Serialize(baseline);
        string? initialHash = null;
        foreach (var profile in OpponentDifficultyProfiles.All)
        {
            var settings = Settings(profile.Id, profile.Id);
            Assert.Equal(baseline.StartingStock, settings.Scenario.StartingStock);
            Assert.Equal(baseline.NavigationCellSizeMeters, settings.Scenario.NavigationCellSizeMeters);
            Assert.Equal(baseline.DistributionMaximumTransportAttempts, settings.Scenario.DistributionMaximumTransportAttempts);
            using var runtime = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
            string hash = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
            initialHash ??= hash;
            Assert.Equal(initialHash, hash);
        }
        Assert.Equal(unchanged, JsonSerializer.Serialize(baseline));
        Assert.Equal("custom", OpponentDifficultyProfiles.Identify(new() { ReactionCadenceTicks = 21 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => OpponentDifficultyProfiles.Get((OpponentDifficultyId)255));
        Assert.Throws<ArgumentException>(() => OpponentDifficultyProfiles.Apply(baseline,
            new Dictionary<ulong, OpponentDifficultyId> { [99] = OpponentDifficultyId.Hard }));
    }

    [Theory]
    [InlineData(OpponentDifficultyId.Easy, 4)]
    [InlineData(OpponentDifficultyId.Standard, 7)]
    [InlineData(OpponentDifficultyId.Hard, 13)]
    public void LegalDecisionCadenceChangesObservablePlanningWithoutGrantingStock(OpponentDifficultyId id, int decisions)
    {
        using var runtime = MatchRuntime.Create(Settings(id, id), TestContext.Current.CancellationToken);
        runtime.Simulation.RunTicks(128, TestContext.Current.CancellationToken);
        var diagnostics = runtime.Opponents.CaptureQualificationDecisions();
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d =>
        {
            Assert.Equal(decisions, d.DecisionsTaken);
            Assert.Equal(OpponentDifficultyProfiles.Get(id).Key, d.ProfileKey);
            Assert.Equal(128UL, d.ObservedTick);
            Assert.Equal(OpponentDecisionCause.CadenceWait, d.Cause);
            Assert.True(d.NextDecisionTick > d.ObservedTick);
        });
        runtime.Opponents.DebugCaptureEnabled = false;
        Assert.Empty(runtime.Opponents.CaptureQualificationDecisions());
        runtime.Opponents.DebugCaptureEnabled = true;
        runtime.Simulation.AdvanceOneTick();
        Assert.Equal(128UL, diagnostics[0].ObservedTick);
        Assert.Equal(129UL, runtime.Opponents.CaptureQualificationDecisions()[0].ObservedTick);
    }

    [Fact]
    public void ProfileConfigurationAndDecisionsSurviveSaveReplayWithoutSchemaChange()
    {
        using var runtime = MatchRuntime.Create(Settings(OpponentDifficultyId.Easy, OpponentDifficultyId.Hard),
            TestContext.Current.CancellationToken);
        runtime.Simulation.RunTicks(64, TestContext.Current.CancellationToken);
        var save = MatchPersistenceService.CaptureSave(runtime);
        using var restored = MatchPersistenceService.Restore(MatchPersistenceSerializer.DeserializeSave(
            MatchPersistenceSerializer.SerializeSave(save)));
        using var replay = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(runtime))));
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(replay).ComputeSha256());
        Assert.Equal("skirmish.easy.v1", OpponentDifficultyProfiles.Identify(save.Configuration.Scenario.OpponentConfigurations[1]));
        Assert.Equal("skirmish.hard.v1", OpponentDifficultyProfiles.Identify(save.Configuration.Scenario.OpponentConfigurations[2]));
        runtime.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        replay.Simulation.RunTicks(16, TestContext.Current.CancellationToken);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256(), MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256(), MatchAuthoritativeSnapshot.Capture(replay).ComputeSha256());
    }

    [Fact]
    public void RepeatedSeedAndDiagnosticCapturePreserveAuthoritativeOutcome()
    {
        using var first = MatchRuntime.Create(Settings(OpponentDifficultyId.Easy, OpponentDifficultyId.Hard, 2026), TestContext.Current.CancellationToken);
        using var second = MatchRuntime.Create(Settings(OpponentDifficultyId.Easy, OpponentDifficultyId.Hard, 2026), TestContext.Current.CancellationToken);
        for (int tick = 0; tick < 512; tick++)
        {
            first.Simulation.AdvanceOneTick();
            second.Simulation.AdvanceOneTick();
            _ = first.Opponents.CaptureQualificationDecisions();
        }
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(first).ComputeSha256(), MatchAuthoritativeSnapshot.Capture(second).ComputeSha256());
    }

    [Theory]
    [InlineData(OpponentDifficultyId.Easy)]
    [InlineData(OpponentDifficultyId.Standard)]
    [InlineData(OpponentDifficultyId.Hard)]
    public void PrecancelledQualificationDoesNotCreateRuntime(OpponentDifficultyId difficulty)
    {
        Assert.Throws<OperationCanceledException>(() => MatchRuntime.Create(Settings(difficulty, difficulty), new CancellationToken(true)));
    }
}
