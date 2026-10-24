using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ScenarioPersistenceTests
{
    [Fact]
    public void SaveReplayKeepQualifiedIdentityAndRejectUnqualifiedLegacyLayout()
    {
        using var source = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 2026, enableDebugCapture: true), TestContext.Current.CancellationToken);
        source.Simulation.RunTicks(20, TestContext.Current.CancellationToken);
        var save = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(source)));
        var replay = MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(source)));
        Assert.Equal(7, save.SchemaVersion);
        Assert.Equal(source.RuntimeSettings.Composition.Identity, save.Configuration.Identity);
        Assert.Equal(save.Configuration.Identity, replay.Configuration.Identity);
        using var restored = MatchPersistenceService.Restore(save);
        using var playback = MatchPersistenceService.PlayReplay(replay);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        var legacy = save with { SchemaVersion = 6, Configuration = save.Configuration with { Identity = null } };
        legacy = MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(legacy));
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(legacy)).Reason);
    }

    [Fact]
    public void ChangedMapOrContentAndMissingNewIdentityFailBeforeWorldAllocation()
    {
        using var source = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation), TestContext.Current.CancellationToken);
        var save = MatchPersistenceService.CaptureSave(source);
        bool allocated = false;
        var wrong = source.RuntimeSettings.Composition with
        {
            Identity = save.Configuration.Identity! with { ContentSha256 = new string('0', 64) },
            CreateTerrain = _ => { allocated = true; throw new InvalidOperationException("Must not allocate terrain."); }
        };
        var error = Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(save, _ => wrong));
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion, error.Reason);
        Assert.False(allocated);
        var changed = save with { Configuration = save.Configuration with { Identity = save.Configuration.Identity! with { MapRevision = 2 } } };
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(changed)).Reason);
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(save with { Configuration = save.Configuration with { Identity = null } }))).Reason);
        var legacy = save with { SchemaVersion = 6, Configuration = save.Configuration with { Identity = null } };
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(legacy, _ => wrong)).Reason);
        Assert.False(allocated);
    }
}
