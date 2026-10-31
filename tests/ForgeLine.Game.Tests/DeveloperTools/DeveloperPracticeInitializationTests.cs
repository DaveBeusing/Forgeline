using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class DeveloperPracticeInitializationTests
{
    [Theory]
    [InlineData(PracticeLessons.ReconKey)]
    [InlineData(PracticeLessons.IndustryKey)]
    [InlineData(PracticeLessons.SupplyKey)]
    public void PracticeKeepsUnsupportedTuningUnavailableAndRecoversItsActualState(string key)
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(
            MatchScenarioProfile.Validation, 2026, enableDebugCapture: true,
            composition: ScenarioRegistry.Default.Resolve(key)), TestContext.Current.CancellationToken);
        runtime.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        Assert.Null(runtime.InitialDeveloperProfile);
        Assert.Equal(DeveloperProviderStatus.Missing, runtime.Services.DeveloperParameters.Status);
        Assert.Empty(runtime.Services.DeveloperParameters.Capture().Values);
        Assert.NotNull(runtime.Practice);
        Assert.Equal(PracticeStatus.Active, runtime.Practice.Capture(new PlayerId(1), runtime.Simulation.CurrentTick)!.Status);
#if FORGELINE_DEVELOPER_TOOLS
        var gateway = Assert.IsType<DeveloperCommandGateway>(runtime.Services.DeveloperActions);
        gateway.BindAccess(DeveloperToolsCapability.Current.BindSession(runtime.Simulation.SessionId, true));
        Assert.Equal(DeveloperProviderAvailability.Available, gateway.Status.Availability);
        Assert.NotEmpty(gateway.Operations);
        Assert.Equal(DeveloperProviderStatus.Missing, runtime.Services.DeveloperParameters.Status);
#else
        Assert.Equal(DeveloperProviderStatus.Missing, runtime.Services.DeveloperActions.Status);
#endif
        var save = MatchPersistenceSerializer.DeserializeSave(
            MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(runtime)));
        var replay = MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(runtime)));
        using var restored = MatchPersistenceService.Restore(save);
        using var playback = MatchPersistenceService.PlayReplay(replay);
        foreach (var recovered in new[] { restored, playback })
        {
            Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(recovered).ComputeSha256());
            Assert.Equal(key, recovered.RuntimeSettings.Composition.Key);
            Assert.NotNull(recovered.Practice);
            Assert.Equal(DeveloperProviderStatus.Missing, recovered.Services.DeveloperParameters.Status);
        }
    }

    [Theory]
    [InlineData(CentralDivideScenario.CompositionKey)]
    [InlineData(SplitBasinBattlefield.CompositionKey)]
    public void CanonicalScenariosKeepTheirExistingBuildSpecificTuningProvider(string key)
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(
            MatchScenarioProfile.Validation, 2026, composition: ScenarioRegistry.Default.Resolve(key)),
            TestContext.Current.CancellationToken);
#if FORGELINE_DEVELOPER_TOOLS
        var gateway = Assert.IsType<DeveloperCommandGateway>(runtime.Services.DeveloperActions);
        gateway.BindAccess(DeveloperToolsCapability.Current.BindSession(runtime.Simulation.SessionId, true));
        Assert.IsType<DeveloperGameplayTuningProvider>(runtime.Services.DeveloperParameters);
        Assert.Equal(DeveloperProviderAvailability.Available, runtime.Services.DeveloperParameters.Status.Availability);
        Assert.NotEmpty(runtime.Services.DeveloperParameters.Capture().Values);
#else
        Assert.Equal(DeveloperProviderStatus.Missing, runtime.Services.DeveloperActions.Status);
        Assert.Equal(DeveloperProviderStatus.Missing, runtime.Services.DeveloperParameters.Status);
        Assert.Empty(runtime.Services.DeveloperParameters.Capture().Values);
#endif
    }

    [Theory]
    [InlineData(PracticeLessons.ReconKey)]
    [InlineData(PracticeLessons.IndustryKey)]
    [InlineData(PracticeLessons.SupplyKey)]
    [InlineData("unregistered.composition")]
    public void PracticeAndUnknownKeysStillCannotCreateCanonicalPresetMetadata(string key)
    {
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 2026);
        Assert.Throws<ArgumentException>(() => new DeveloperPresetMetadata(
            DeveloperPresetMetadata.CurrentSchema, key, settings.Composition.Identity!, settings.Seed,
            settings.Scenario.Profile, settings.Participants.ToArray(), requiresDeveloperTools: true));
    }
}
