#if !FORGELINE_DEVELOPER_TOOLS
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class DeveloperPublicDistributionTests
{
    [Fact]
    public void PublicDiagnosticSettingsCannotCreateDeveloperComposition()
    {
        var basis = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 742026,
            enableDiagnostics: true, enableDebugCapture: true);
        var metadata = new DeveloperPresetMetadata(1, basis.Composition.Key, basis.Composition.Identity!, basis.Seed,
            basis.Scenario.Profile, basis.Participants.ToArray(), true);
        var profile = new DeveloperInitialProfile(metadata, []);
        Assert.False(DeveloperToolsCapability.Current.IsCompiled);
        Assert.Null(typeof(ClientApplication).Assembly.GetType("ForgeLine.Client.ClientDeveloperMenuBackend"));
        Assert.Null(typeof(MatchRuntime).Assembly.GetType("ForgeLine.Game.DeveloperCommandGateway"));
        Assert.Throws<InvalidOperationException>(() => MatchRuntime.Create(basis with { InitialDeveloperProfile = profile }));
        using var runtime = MatchRuntime.Create(basis);
        string before = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        var revision = new DeveloperStateRevision(DeveloperRevisionDomain.Simulation, 0);
        var request = new DeveloperRequest(runtime.Simulation.SessionId, Guid.NewGuid(), revision,
            DeveloperOperation.RepairEntity, DeveloperTarget.ForEntity(runtime.GetBase(new(1)).CommandCore), DeveloperParameterId.None,
            [DeveloperValue.FromDouble(1)]);
        var result = runtime.Services.DeveloperActions.Submit(DeveloperToolsCapability.Current.BindSession(runtime.Simulation.SessionId, true),
            new(runtime.Simulation.SessionId, revision, _ => true, _ => true), request);
        Assert.Equal(DeveloperRequestStatus.Rejected, result.Status);
        Assert.Equal(DeveloperFailureReason.CapabilityUnavailable, result.Failure);
        Assert.Equal(before, MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256());
    }

    [Fact]
    public void MarkedDeveloperSaveAndReplayAreRejectedWithoutChangingTheSourceMatch()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 742026));
        string hash = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        var save = MatchPersistenceService.CaptureSave(runtime) with { DeveloperModified = true };
        var replay = MatchPersistenceService.CaptureReplay(runtime) with { DeveloperModified = true };
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(save));
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplay(replay));
        Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256());
    }
}
#endif
