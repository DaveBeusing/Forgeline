#if FORGELINE_DEVELOPER_TOOLS
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class DeveloperMenuTests
{
    [Fact]
    public void AcceptedAndUnrelatedResultsNeverBecomeApplied()
    {
        var f = new Fixture(); f.Model.Adjust(Id, DeveloperTarget.Global, 1); f.Model.ApplyParameter(Id, DeveloperTarget.Global);
        var request = Assert.Single(f.Provider.Requests);
        Assert.True(f.Model.WriteBlocked); Assert.Empty(f.Model.History);
        Assert.Equal(1, f.Model.Find(Id, DeveloperTarget.Global)!.Observed.Value.AsDouble());
        var unrelated = new DeveloperRequest(f.Session, Guid.NewGuid(), request.ExpectedRevision, request.Operation, request.Target, request.Parameter, request.Values.ToArray());
        f.Model.Observe(DeveloperResult.Applied(unrelated, Parameters(1), new(8), [DeveloperAppliedValue.ForParameter(Id, request.Target, DeveloperValue.FromDouble(1.25))]));
        Assert.True(f.Model.WriteBlocked); Assert.Empty(f.Model.History);
        f.Complete(request, 1.25);
        Assert.False(f.Model.WriteBlocked); Assert.Single(f.Model.History);
        Assert.Equal(1.25, f.Model.Find(Id, request.Target)!.Observed.Value.AsDouble());
        Assert.Contains("Applied at tick 8", f.Model.Feedback, StringComparison.Ordinal);
    }
    [Fact]
    public void ConflictRetainsDraftAndRequiresAnotherExplicitApplyAtReloadedRevision()
    {
        var f = new Fixture(); f.Model.Adjust(Id, DeveloperTarget.Global, 1); f.Model.ApplyParameter(Id, DeveloperTarget.Global);
        var request = Assert.Single(f.Provider.Requests);
        f.Model.Observe(DeveloperResult.Rejected(request, Parameters(1), DeveloperFailureReason.StaleRevision));
        f.Provider.Revision = 1; f.Refresh();
        var draft = f.Model.Find(Id, DeveloperTarget.Global)!;
        Assert.True(draft.NeedsReview); Assert.Equal(1.25, draft.Value.AsDouble()); Assert.Equal(Parameters(1), draft.Observed.Revision);
        Assert.Single(f.Provider.Requests);
        f.Model.ApplyParameter(Id, DeveloperTarget.Global);
        Assert.Equal(2, f.Provider.Requests.Count); Assert.Equal(Parameters(1), f.Provider.Requests[1].ExpectedRevision);
    }
    [Fact]
    public void CapabilityLossClosesMenuClearsDraftsAndDisablesDiagnostics()
    {
        var f = new Fixture(); Assert.True(f.Model.Open()); f.Debug.SetEnabled(true);
        f.Model.Adjust(Id, DeveloperTarget.Global, 1);
        f.Refresh(active: false);
        Assert.False(f.Model.Visible); Assert.False(f.Model.Available); Assert.Empty(f.Model.Drafts); Assert.False(f.Debug.Enabled);
        Assert.False(f.Model.Open()); Assert.Empty(f.Provider.Requests);
    }
    [Fact]
    public void NewSessionAndForeignSnapshotCannotReuseTargetsOrResults()
    {
        var f = new Fixture(); f.Model.ApplyParameter(Id, DeveloperTarget.Global); var old = Assert.Single(f.Provider.Requests);
        f.Session = new(43); f.Refresh(snapshot: Snapshot(new(42)));
        Assert.False(f.Model.Available); Assert.False(f.Model.Open());
        f.Refresh(); Assert.True(f.Model.Available); Assert.False(f.Model.WriteBlocked);
        f.Model.Observe(DeveloperResult.Applied(old, Parameters(1), new(8), []));
        Assert.Empty(f.Model.History);
    }
    [Theory]
    [InlineData("resize")]
    [InlineData("dpi")]
    [InlineData("focus")]
    [InlineData("session")]
    public void LifecycleChangesSuppressHeldPointerAndEscapeDoesNotLeak(string change)
    {
        var f = new Fixture(); var controller = new DeveloperMenuController(f.Model); var input = new InputState();
        var layout = FrontendDesign.ResolveLayout(1600, 900); controller.Open(input); controller.Update(input, layout, true);
        input.BeginFrame(); input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, 4, 4));
        if (change == "resize") layout = FrontendDesign.ResolveLayout(1280, 720);
        if (change == "dpi") layout = FrontendDesign.ResolveLayout(1600, 900, 1, 144);
        if (change == "session") { f.Session = new(44); f.Refresh(); }
        controller.Update(input, layout, change != "focus");
        Assert.False(input.IsMouseButtonDown(PlatformMouseButton.Left));
        Assert.False(input.TryGetMousePressPosition(PlatformMouseButton.Left, out _)); Assert.Empty(f.Provider.Requests);
        if (!controller.Visible) controller.Open(input);
        controller.Update(input, layout, true); input.BeginFrame();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Escape));
        controller.Update(input, layout, true);
        Assert.False(controller.Visible); Assert.False(input.WasKeyPressed(PlatformKey.Escape));
    }
    [Fact]
    public void HeldApplySubmitsOnceEvenAfterFirstRequestCompletes()
    {
        var f = new Fixture(); var input = new InputState(); var layout = FrontendDesign.ResolveLayout(1600, 900);
        var controller = new DeveloperMenuController(f.Model); controller.Open(input); f.Model.ChangeSection(1); controller.Update(input, layout, true);
        var rows = DeveloperMenuView.Build(f.Model); int apply = Enumerable.Range(0, rows.Count).First(i => rows[i].Line.Label.StartsWith("APPLY ", StringComparison.Ordinal));
        for (int i = 0; i < apply; i++)
        {
            input.BeginFrame(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Down));
            controller.Update(input, layout, true); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.Down));
        }
        input.BeginFrame(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Enter));
        controller.Update(input, layout, true); var request = Assert.Single(f.Provider.Requests); f.Complete(request, 1);
        input.BeginFrame(); controller.Update(input, layout, true); Assert.Single(f.Provider.Requests);
    }
    [Fact]
    public void RecycledEntityAndMultipleSelectionCannotRemainActionTargets()
    {
        var f = new Fixture(); var entity = new EntityId(7, 1); f.Selection.Replace([entity]);
        f.Refresh(snapshot: Snapshot(f.Session, new RenderInstance(entity, RenderTransform.Identity, default, default, RenderVisibilityMask.World,
            Selectable: new(new PlayerId(1), ControllableEntityCategory.Unit))));
        for (int i = 0; i < 3; i++) f.Model.CycleTarget(1);
        Assert.Equal(DeveloperTarget.ForEntity(entity), f.Model.Target); f.Model.CycleAction(1); Assert.NotNull(f.Model.Action);
        f.Refresh(snapshot: Snapshot(f.Session, new RenderInstance(new(7, 2), RenderTransform.Identity, default, default, RenderVisibilityMask.World)));
        Assert.Equal(DeveloperTarget.Global, f.Model.Target); Assert.Null(f.Model.Action); Assert.False(f.Model.Inspector.Entity.IsValid);
        f.Selection.Replace([entity, new(8, 1)]); f.Refresh();
        Assert.Contains("Multiple selection", f.Model.Inspector.Status, StringComparison.Ordinal);
    }
    [Fact]
    public void ExplicitActionUsesAdapterPayloadAndObservedSimulationRevision()
    {
        var f = new Fixture(); var entity = new EntityId(9, 3); f.Selection.Replace([entity]);
        f.Refresh(snapshot: Snapshot(f.Session, new RenderInstance(entity, RenderTransform.Identity, default, default, RenderVisibilityMask.World,
            Selectable: new(new PlayerId(1), ControllableEntityCategory.Unit))));
        for (int i = 0; i < 3; i++) f.Model.CycleTarget(1);
        f.Model.CycleAction(1); f.Model.AdjustAction(0, 1); f.Model.ApplyAction();
        var request = Assert.Single(f.Provider.Requests);
        Assert.Equal(DeveloperOperation.SpawnUnit, request.Operation); Assert.Equal(DeveloperTarget.ForEntity(entity), request.Target);
        Assert.Equal(new DeveloperStateRevision(DeveloperRevisionDomain.Simulation, 0), request.ExpectedRevision);
        Assert.Equal(2u, Assert.Single(request.Values).AsUInt32()); Assert.Empty(f.Model.History);
    }
    [Fact]
    public void AdmissionDoesNotTransferPauseAndWindowBlockingIsReportedToActualOwner()
    {
        var f = new Fixture(); Assert.True(f.Model.Open()); f.Model.SessionCommand(DeveloperOperation.Resume);
        Assert.Single(f.Provider.Requests); Assert.False(f.Model.OwnsMenuPause);
        f.Backend.Transferred = true; f.Refresh(); Assert.True(f.Model.OwnsMenuPause);
        f.Refresh(focused: false); Assert.True(f.Backend.WindowBlocked); Assert.True(f.Model.WriteBlocked);
        f.Model.Close(); Assert.False(f.Model.OwnsMenuPause); Assert.Equal(1, f.Backend.Releases);
    }
    [Fact]
    public void RenderingChoiceUsesSameControllerWithoutSimulationCapture()
    {
        var f = new Fixture(); f.Debug.SetCategories(DebugOverlayCategory.Rendering); f.Model.ChangeSection(3);
        var toggle = DeveloperMenuView.Build(f.Model).Single(r => r.Line.Label == "DEBUG MASTER"); toggle.Activate!();
        Assert.True(f.Debug.Enabled);
        Assert.False(DebugOverlayPolicy.RequiresPresentationDebugSnapshot(f.Debug.View.EffectiveCategories, StrategicOverlayMode.None));
        Assert.Empty(f.Provider.Requests);
    }
    [Fact]
    public void PresetSubmissionRequiresMatchingCompletionAndHistoryIsBounded()
    {
        var f = new Fixture(); f.Model.PresetCommand(DeveloperPresetIntent.Import); Assert.True(f.Model.WriteBlocked);
        f.Backend.Outcome = new(Guid.NewGuid(), true, "Unrelated"); f.Refresh(); Assert.True(f.Model.WriteBlocked);
        f.Backend.Outcome = new(f.Backend.PresetCorrelation, false, "Invalid schema"); f.Refresh();
        Assert.False(f.Model.WriteBlocked); Assert.Contains("Failed: Invalid schema", f.Model.Feedback, StringComparison.Ordinal);
        for (int i = 0; i < 70; i++) { f.Model.ApplyParameter(Id, DeveloperTarget.Global, true); f.Complete(f.Provider.Requests[^1], 1); }
        Assert.Equal(64, f.Model.History.Count);
    }
    [Fact]
    public void PresetServiceRevalidatesBeforeRestartEvenWhenEntryClaimsCompatibility()
    {
        var f = new Fixture();
        var metadata = new DeveloperPresetMetadata(1, CentralDivideScenario.CompositionKey, ScenarioRegistry.CentralDivideDescriptor.Identity,
            17, MatchScenarioProfile.Gameplay, Participants, true);
        f.Backend.Presets = [new("selected", "Selected", metadata, DeveloperFailureReason.None)];
        f.Backend.Validation = DeveloperFailureReason.InvalidValue; f.Refresh();
        Assert.Equal(DeveloperFailureReason.InvalidValue, f.Model.PresetCompatibility);
        f.Model.PresetCommand(DeveloperPresetIntent.RestartSameSeed);
        Assert.Equal(Guid.Empty, f.Backend.PresetCorrelation); Assert.False(f.Model.WriteBlocked);
        Assert.Contains("compatible preset", f.Model.Feedback, StringComparison.Ordinal);
    }
    [Fact]
    public void MissingProvidersHaveNoEnabledSimulationControlsOrInventedConfirmedValues()
    {
        var model = new DeveloperMenuModel(new(DeveloperToolsCapability.Current, new DeveloperMenuCatalog()), new());
        var session = new SimulationSessionId(77); model.Refresh(DeveloperToolsCapability.Current.BindSession(session, true), Snapshot(session), new(), Participants, true, true);
        Assert.True(model.Open()); var rows = DeveloperMenuView.Build(model);
        Assert.Null(rows.Single(r => r.Line.Label == "RESUME").Activate);
        model.ChangeSection(1); rows = DeveloperMenuView.Build(model);
        Assert.Contains(rows, r => r.Line.Value == "Confirmed value unavailable");
        Assert.DoesNotContain(rows, r => r.Line.Label.StartsWith("APPLY ", StringComparison.Ordinal) && r.Activate is not null);
    }

    private const DeveloperParameterId Id = DeveloperParameterId.ConveyorThroughputMultiplier;
    private static DeveloperStateRevision Parameters(ulong revision) => new(DeveloperRevisionDomain.Parameters, revision);
    private static readonly MatchParticipantConfiguration[] Participants = [new(new(1), new(1), 0, false), new(new(2), new(2), 1, true)];
    private static PresentationSnapshot Snapshot(SimulationSessionId session, params RenderInstance[] instances) => new(new(8), TimeSpan.FromSeconds(.05), instances.Length, instances, sessionId: session);
    private sealed class Fixture
    {
        public SimulationSessionId Session = new(42);
        public readonly Provider Provider = new(); public readonly Backend Backend = new(); public readonly DebugOverlayController Debug = new();
        public readonly SelectionSet Selection = new(); public readonly DeveloperMenuModel Model;
        public Fixture() { Model = new(new(DeveloperToolsCapability.Current, new Catalog(), sessions: Provider, actions: Provider, parameters: Provider, presets: Backend), Debug, Backend); Refresh(); }
        public void Refresh(bool active = true, PresentationSnapshot? snapshot = null, bool focused = true)
        { Backend.Session = Session; Model.Refresh(DeveloperToolsCapability.Current.BindSession(Session, active), snapshot ?? Snapshot(Session), Selection, Participants, true, focused); }
        public void Complete(DeveloperRequest request, double value)
        {
            Provider.Revision++; Provider.Value = value;
            Model.Observe(DeveloperResult.Applied(request, Parameters(Provider.Revision), new(8), [DeveloperAppliedValue.ForParameter(Id, request.Target, DeveloperValue.FromDouble(value))]));
        }
    }
    private sealed class Provider : IDeveloperParameterProvider, IDeveloperSessionController, IDeveloperActionDispatcher
    {
        public ulong Revision; public double Value = 1; public List<DeveloperRequest> Requests { get; } = [];
        public DeveloperProviderStatus Status => new(DeveloperProviderAvailability.Available, DeveloperFailureReason.None);
        public IReadOnlyList<DeveloperParameterDescriptor> Descriptors { get; } = DeveloperGameplayTuningSchema.Entries.Where(e => e.Contract.Id == Id).Select(e => e.Contract).ToArray();
        public DeveloperParameterReadResult Capture() => new(Status, [new(Id, DeveloperTarget.Global, DeveloperValue.FromDouble(Value), Parameters(Revision))]);
        public DeveloperResult Submit(DeveloperSessionAccess permission, DeveloperExecutionContext context, DeveloperRequest request)
        { Requests.Add(request); return DeveloperResult.Accepted(request, context.Revision); }
    }
    private sealed class Catalog : IDeveloperMenuCatalog
    {
        public DeveloperProviderStatus Status => new(DeveloperProviderAvailability.Available, DeveloperFailureReason.None);
        public IReadOnlyList<DeveloperOperationDescriptor> Operations { get; } = [new(DeveloperOperation.SpawnUnit, DeveloperTargetKinds.Entity, "Spawn")];
    }
    private sealed class Backend : IDeveloperMenuBackend, IDeveloperPresetService
    {
        public SimulationSessionId Session; public Guid PresetCorrelation; public DeveloperMenuPresetOutcome? Outcome;
        public bool Transferred, WindowBlocked; public int Releases;
        public DeveloperMenuPreset[] Presets = []; public DeveloperFailureReason Validation;
        public DeveloperProviderStatus Status => new(DeveloperProviderAvailability.Available, DeveloperFailureReason.None);
        public DeveloperMenuBackendSnapshot Capture() => new(Session, new(8), [new(DeveloperRevisionDomain.Simulation, 0), new(DeveloperRevisionDomain.Session, 0)], [], Presets, menuPauseTransferred: Transferred, presetOutcome: Outcome);
        public IReadOnlyList<DeveloperMenuActionForm> ActionForms { get; } = [new(DeveloperOperation.SpawnUnit, [new("Unit kind", DeveloperValue.FromUInt32(1), DeveloperValue.FromUInt32(1), DeveloperValue.FromUInt32(2), DeveloperValue.FromUInt32(1),
            [new("First", DeveloperValue.FromUInt32(1)), new("Second", DeveloperValue.FromUInt32(2))])])];
        public void SetWindowBlocked(bool blocked) => WindowBlocked = blocked;
        public void ReleaseMenuOwnership() => Releases++;
        public bool TrySubmitPreset(Guid correlation, DeveloperPresetIntent intent, DeveloperMenuPreset? preset, out string feedback)
        { PresetCorrelation = correlation; feedback = "Waiting for import"; return true; }
        public DeveloperFailureReason Validate(DeveloperPresetMetadata metadata) => Validation;
    }
}
#endif
