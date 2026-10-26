#if FORGELINE_DEVELOPER_TOOLS
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed partial class ClientSimulationHostTests
{
    private static void RefreshMenu(DeveloperMenuModel model, ClientHostFixture fixture, DeveloperSessionAccess access)
    {
        Assert.True(fixture.Snapshots.TryReadLatest(out var snapshot));
        model.Refresh(access, snapshot, new SelectionSet(), fixture.Scenario.RuntimeSettings.Participants,
            fixture.Host.IsPaused, true);
    }

    [Fact]
    public void IntegratedMenuCanStepAQueuedParameterAndRestartItsPendingProfile()
    {
        using var fixture = ClientHostFixture.Create(timeProvider: new ControlledHostTime());
        var access = ActivateControls(fixture.Host);
        string root = Path.Combine(AppContext.BaseDirectory, "qualification-presets", Guid.NewGuid().ToString("N"));
        try
        {
            MatchRuntimeSettings? restart = null;
            var presets = new ClientDeveloperPresetSessionAdapter(new DeveloperPresetStore(root: root));
            using var backend = new ClientDeveloperMenuBackend(fixture.Scenario, fixture.Host, access,
                settings => { restart = settings; return true; }, presets);
            var model = new DeveloperMenuModel(backend.Providers, new DebugOverlayController(), backend);
            RefreshMenu(model, fixture, access);
            Assert.True(model.Open());
            Assert.True(SpinWait.SpinUntil(() => { RefreshMenu(model, fixture, access); return model.OwnsMenuPause; }, TestTimeout));
            Assert.All(new[] { backend.Providers.Sessions, backend.Providers.Actions, (IDeveloperProvider)backend.Providers.Parameters, backend.Providers.Presets },
                provider => Assert.Equal(DeveloperProviderAvailability.Available, provider.Status.Availability));

            var id = DeveloperParameterId.ConveyorThroughputMultiplier;
            ulong start = fixture.Host.CaptureControlStatus().CompletedTick.Value;
            model.Adjust(id, DeveloperTarget.Global, 1);
            model.ApplyParameter(id, DeveloperTarget.Global);
            RefreshMenu(model, fixture, access);
            Assert.True(model.WriteBlocked);
            Assert.False(model.SessionWriteBlocked);
            model.SessionCommand(DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(1));
            Assert.True(SpinWait.SpinUntil(() => { RefreshMenu(model, fixture, access); return !model.WriteBlocked; }, TestTimeout));
            Assert.Equal(start + 1, fixture.Host.CaptureControlStatus().CompletedTick.Value);
            Assert.Equal(1.25, model.Find(id, DeveloperTarget.Global)!.Observed.Value.AsDouble());

            var opponent = DeveloperTarget.ForPlayer(new PlayerId(2));
            double original = model.Find(DeveloperParameterId.OpponentAggression, opponent)!.Observed.Value.AsDouble();
            model.Adjust(DeveloperParameterId.OpponentAggression, opponent, 1);
            model.ApplyParameter(DeveloperParameterId.OpponentAggression, opponent);
            model.SessionCommand(DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(1));
            Assert.True(SpinWait.SpinUntil(() => { RefreshMenu(model, fixture, access); return !model.WriteBlocked; }, TestTimeout));
            Assert.Equal(original, fixture.Scenario.RuntimeSettings.Scenario.OpponentConfigurations[2].Aggression);
            Assert.Contains(model.Observation!.Parameters, row => row.Id == DeveloperParameterId.OpponentAggression && row.Pending.HasValue);
            model.PresetCommand(DeveloperPresetIntent.SaveCurrentValues);
            RefreshMenu(model, fixture, access);
            Assert.NotNull(model.Preset);
            model.PresetCommand(DeveloperPresetIntent.RestartSelected);
            Assert.NotNull(restart);
            using var loaded = ClientSessionFactory.CreateOwned(ClientSessionRequest.Restart(restart!), _ => { }, TestContext.Current.CancellationToken);
            Assert.NotEqual(fixture.Host.SessionId, loaded.Runtime.Simulation.SessionId);
            Assert.Equal(fixture.Scenario.RuntimeSettings.Composition.Identity, loaded.Runtime.RuntimeSettings.Composition.Identity);
            Assert.Equal(fixture.Scenario.RuntimeSettings.Seed, loaded.Runtime.RuntimeSettings.Seed);
            Assert.Equal(original + .01, loaded.Runtime.RuntimeSettings.Scenario.OpponentConfigurations[2].Aggression, 8);
            model.Close();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static DeveloperResult ApplyThroughHost(ClientHostFixture fixture, ClientDeveloperMenuBackend backend,
        DeveloperSessionAccess access, DeveloperOperation operation, DeveloperTarget target, DeveloperValue[] values,
        DeveloperParameterId parameter = DeveloperParameterId.None)
    {
        var domain = parameter == DeveloperParameterId.None ? DeveloperRevisionDomain.Simulation : DeveloperRevisionDomain.Parameters;
        var revision = fixture.Host.RuntimeDeveloperRevision(domain);
        var request = new DeveloperRequest(fixture.Host.SessionId, Guid.NewGuid(), revision, operation, target, parameter, values);
        var receipt = backend.Submit(access, new(fixture.Host.SessionId, revision, _ => true, _ => true), request);
        Assert.Equal(DeveloperRequestStatus.Accepted, receipt.Status);
        Assert.True(SpinWait.SpinUntil(() => fixture.Host.CaptureRuntimeDeveloperResults().Any(r => r.Correlation == request.Correlation), TestTimeout));
        ExecuteControl(fixture.Host, access, DeveloperOperation.SingleStep, DeveloperValue.FromUInt32(1));
        return Assert.Single(fixture.Host.CaptureRuntimeDeveloperResults(), r => r.Correlation == request.Correlation);
    }

    [Fact]
    public void OwnerTransportRecordsInitialProfileActionsResetAndRecoveryFixture()
    {
        var basis = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 742026);
        var metadata = DeveloperRestartBuilder.ForExisting(basis).InitialOverrides.Metadata;
        Assert.True(DeveloperRestartBuilder.TryBuild(basis, metadata,
            [new(DeveloperParameterId.ConveyorThroughputMultiplier, DeveloperTarget.Global, DeveloperValue.FromDouble(1.25))], out var plan, out _));
        using var fixture = ClientHostFixture.Create(runtimeSettings: plan!.Settings, timeProvider: new ControlledHostTime());
        var access = ActivateControls(fixture.Host);
        ExecuteControl(fixture.Host, access, DeveloperOperation.Pause);
        using var backend = new ClientDeveloperMenuBackend(fixture.Scenario, fixture.Host, access, _ => false);
        var core = fixture.Scenario.GetBase(new PlayerId(1)).CommandCore;
        var resource = ApplyThroughHost(fixture, backend, access, DeveloperOperation.GrantResource, DeveloperTarget.ForEntity(core),
            [DeveloperValue.FromUInt32(ResourceIds.RareElements.Value), DeveloperValue.FromUInt32((uint)DeveloperInventoryRole.BuildingStock), DeveloperValue.FromDouble(1)]);
        Assert.Equal(DeveloperRequestStatus.Applied, resource.Status);
        var changed = ApplyThroughHost(fixture, backend, access, DeveloperOperation.SetParameter, DeveloperTarget.Global,
            [DeveloperValue.FromDouble(2)], DeveloperParameterId.ConveyorThroughputMultiplier);
        Assert.Equal(DeveloperRequestStatus.Applied, changed.Status);
        var reset = ApplyThroughHost(fixture, backend, access, DeveloperOperation.ResetParameter, DeveloperTarget.Global,
            [], DeveloperParameterId.ConveyorThroughputMultiplier);
        Assert.Equal(DeveloperRequestStatus.Applied, reset.Status);
        Assert.True(resource.AppliedTick < changed.AppliedTick && changed.AppliedTick < reset.AppliedTick);
        fixture.Host.Dispose();

        var save = MatchPersistenceService.CaptureSave(fixture.Scenario);
        var replay = MatchPersistenceService.CaptureReplay(fixture.Scenario);
        string hash = MatchAuthoritativeSnapshot.Capture(fixture.Scenario).ComputeSha256();
        using var restored = MatchPersistenceService.RestoreDeveloper(save, DeveloperToolsCapability.Current, cancellationToken: TestContext.Current.CancellationToken);
        using var played = MatchPersistenceService.PlayDeveloperReplay(replay, DeveloperToolsCapability.Current);
        Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(played).ComputeSha256());
        Assert.True(save.DeveloperModified);
        Assert.Equal(2, save.ParameterChanges!.Length);
        Assert.Equal(1.25, Assert.Single(save.Configuration.DeveloperInitialProfile!.Parameters!).Value.ToValue().AsDouble());
        string output = Environment.GetEnvironmentVariable("FORGELINE_QUALIFICATION_OUTPUT") ?? Path.Combine(AppContext.BaseDirectory, "qualification-fixture");
        Directory.CreateDirectory(output);
        MatchPersistenceSerializer.WriteSave(Path.Combine(output, "developer.save.json"), save);
        MatchPersistenceSerializer.WriteReplay(Path.Combine(output, "developer.replay.json"), replay);
        File.WriteAllBytes(Path.Combine(output, "developer.preset.json"), DeveloperPresetCodec.Serialize(
            DeveloperPresetCodec.Create("qualification", "Qualification initial profile", plan.InitialOverrides)));
        File.WriteAllText(Path.Combine(output, "identity.json"), JsonSerializer.Serialize(new
        { SchemaVersion = 1, Seed = 742026, Variant = DeveloperToolsCapability.Current.BuildVariant,
            BaseIdentity = plan.Settings.Composition.Identity, ExpectedStateSha256 = hash, Tick = save.SavedTick }));
    }
}
#endif
