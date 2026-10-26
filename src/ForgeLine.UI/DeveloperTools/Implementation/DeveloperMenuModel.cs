using System.Globalization;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.UI;

public sealed class DeveloperParameterDraft
{
    internal DeveloperParameterDraft(DeveloperEffectiveParameter observed) { Observed = observed; Value = observed.Value; }
    public DeveloperEffectiveParameter Observed { get; internal set; }
    public DeveloperValue Value { get; internal set; }
    public bool Dirty { get; internal set; }
    public bool NeedsReview { get; internal set; }
}

/// <summary>Local bounded drafts and correlated outcomes. This model never reads live ECS or advances simulation.</summary>
public sealed class DeveloperMenuModel
{
    private readonly DeveloperToolsProviders _providers;
    private readonly DebugOverlayController _debug;
    private readonly IDeveloperMenuBackend? _backend;
    private readonly Dictionary<(DeveloperParameterId, DeveloperTarget), DeveloperParameterDraft> _drafts = new();
    private readonly Queue<string> _history = new();
    private DeveloperRequest? _inFlight;
    private Guid? _presetInFlight;
    private DeveloperSessionAccess? _access;
    private SimulationSessionId _session;
    private MatchParticipantConfiguration[] _participants = [];
    private DeveloperMenuBackendSnapshot? _observation;
    private DeveloperInspector _inspector;
    private bool _focused;
    private bool _snapshotReady;
    private DeveloperStateRevision? _actionRevision;
    public SimulationTick? DebugDataTick { get; private set; }
    public string MatchStatus { get; private set; } = "Unavailable";
    private bool _hostPaused;
    private DeveloperTarget _target = DeveloperTarget.Global;
    private DeveloperOperationDescriptor? _action;
    private int _preset;
    private DeveloperMenuActionForm? _actionForm;
    private DeveloperValue[] _actionValues = [];
    public IReadOnlyList<DeveloperValue> ActionValues => Array.AsReadOnly(_actionValues);
    public DeveloperMenuActionForm? ActionForm => _actionForm;
    public DeveloperMenuModel(DeveloperToolsProviders providers, DebugOverlayController debug, IDeveloperMenuBackend? backend = null)
    { _providers = providers; _debug = debug; _backend = backend; }
    public bool Visible { get; private set; }
    public bool Available => _snapshotReady && _access is { IsActive: true } && _access.Capability.IsCompiled && _access.Session == _session;
    public bool OwnsMenuPause => Visible && _observation?.MenuPauseTransferred == true;
    public bool WriteBlocked => !Available || !_focused || _observation?.Loading == true || _inFlight is not null || _presetInFlight.HasValue;
    public DeveloperMenuSection Section { get; private set; }
    public string Feedback { get; private set; } = "";
    public IReadOnlyCollection<string> History => _history.ToArray();
    public IReadOnlyCollection<DeveloperParameterDraft> Drafts => _drafts.Values.ToArray();
    public DeveloperToolsProviders Providers => _providers;
    public DeveloperInspector Inspector => _inspector;
    public DeveloperMenuBackendSnapshot? Observation => _observation;
    public bool HostPaused => _hostPaused;
    public DebugOverlayController Debug => _debug;
    public DeveloperTarget Target => _target;
    public DeveloperOperationDescriptor? Action => _action;
    public IReadOnlyList<MatchParticipantConfiguration> Participants => _participants;
    public DeveloperMenuPreset? Preset => _observation is { Presets.Count: > 0 } data ? data.Presets[Math.Clamp(_preset, 0, data.Presets.Count - 1)] : null;

    public void Refresh(DeveloperSessionAccess access, PresentationSnapshot? snapshot, SelectionSet selection,
        IReadOnlyList<MatchParticipantConfiguration> participants, bool hostPaused, bool focused)
    {
        ArgumentNullException.ThrowIfNull(access);
        bool changed = _session != access.Session;
        if (changed || !access.IsActive || !access.Capability.IsCompiled || snapshot?.SessionId != access.Session)
        {
            Close(); _drafts.Clear(); _history.Clear(); _inFlight = null; _presetInFlight = null; _target = DeveloperTarget.Global;
            _action = null; _observation = null; _preset = 0;
        }
        _snapshotReady = snapshot?.SessionId == access.Session;
        DebugDataTick = _snapshotReady && snapshot?.Debug is not null ? snapshot.Tick : null;
        MatchStatus = _snapshotReady ? snapshot?.PlayerExperience?.MatchStatus.ToString() ?? "Unavailable" : "Unavailable";
        _session = access.Session; _access = access; _hostPaused = hostPaused; _focused = focused;
        _participants = participants.ToArray();
        _inspector = snapshot?.SessionId == _session ? DeveloperInspector.Capture(snapshot, selection) : default;
        if (_target.Kind == DeveloperTargetKinds.Entity && _target.Entity != _inspector.Entity)
        { _target = DeveloperTarget.Global; _action = null; _actionForm = null; _actionValues = []; Feedback = "Entity target changed; select and review the action again."; }
        if (!Available) { _debug.SetEnabled(false); return; }
        _backend?.SetWindowBlocked(!focused);
        var observation = _backend?.Capture();
        _observation = observation?.Session == _session ? observation : null;
        if (_observation is not null) foreach (var result in _observation.Results) Observe(result);
        if (_presetInFlight is { } pendingPreset && _observation?.PresetOutcome is { } outcome && outcome.Correlation == pendingPreset)
        {
            Feedback = (outcome.Succeeded ? "Completed: " : "Failed: ") + outcome.Message;
            if (_history.Count == 64) _history.Dequeue(); _history.Enqueue(Feedback); _presetInFlight = null;
        }
        var capture = _providers.Parameters.Capture();
        if (capture.Status.Availability != DeveloperProviderAvailability.Available) return;
        var keys = new HashSet<(DeveloperParameterId, DeveloperTarget)>();
        foreach (var value in capture.Values)
        {
            if (value.Target.Kind == DeveloperTargetKinds.Player && !_participants.Any(p => p.IsComputerControlled && p.Player == value.Target.Player)) continue;
            var key = (value.Id, value.Target); keys.Add(key);
            if (!_drafts.TryGetValue(key, out var draft)) _drafts.Add(key, new(value));
            else if (value.Revision != draft.Observed.Revision || value.Value != draft.Observed.Value)
            {
                draft.NeedsReview |= draft.Dirty; draft.Observed = value;
                if (!draft.Dirty) draft.Value = value.Value;
            }
        }
        foreach (var key in _drafts.Keys.Where(k => !keys.Contains(k)).ToArray()) _drafts.Remove(key);
    }
    public bool Open()
    {
        if (!Available) return false;
        Visible = true; Section = DeveloperMenuSection.Session; Feedback = "Opening preserves the current host pause state.";
        return true;
    }
    public void Close() { if (Visible) _backend?.ReleaseMenuOwnership(); Visible = false; }
    public void ChangeSection(int direction) => Section = (DeveloperMenuSection)(((int)Section + direction + 5) % 5);
    public void CycleTarget(int direction)
    {
        var targets = new List<DeveloperTarget> { DeveloperTarget.Global };
        targets.AddRange(_participants.Select(p => DeveloperTarget.ForPlayer(p.Player)));
        if (_inspector.Entity.IsValid) targets.Add(DeveloperTarget.ForEntity(_inspector.Entity));
        int i = targets.IndexOf(_target); _target = targets[(Math.Max(0, i) + direction + targets.Count) % targets.Count]; _action = null; _actionForm = null; _actionValues = [];
    }
    public void CycleAction(int direction)
    {
        var actions = _providers.Menu.Operations.Where(o => o.Operation is DeveloperOperation.SpawnUnit or DeveloperOperation.GrantResource &&
            (o.Targets & _target.Kind) != 0).ToArray();
        if (actions.Length == 0) { _action = null; return; }
        int i = _action.HasValue ? Array.IndexOf(actions, _action.Value) : -1;
        _action = actions[(i + direction + actions.Length) % actions.Length];
        _actionForm = _backend?.ActionForms.Take(32).FirstOrDefault(f => f.Operation == _action.Value.Operation);
        _actionValues = _actionForm?.Fields.Select(f => f.Default).ToArray() ?? [];
        _actionRevision = Revision(DeveloperRevisionDomain.Simulation);
    }
    public void CyclePreset(int direction)
    { int count = _observation?.Presets.Count ?? 0; if (count != 0) _preset = (_preset + direction + count) % count; }
    public DeveloperParameterDraft? Find(DeveloperParameterId id, DeveloperTarget target) => _drafts.GetValueOrDefault((id, target));
    public void Adjust(DeveloperParameterId id, DeveloperTarget target, int direction)
    {
        if (WriteBlocked || Find(id, target) is not { } draft) return;
        var descriptor = _providers.Parameters.Descriptors.Single(d => d.Id == id);
        draft.Value = Step(descriptor, draft.Value, direction); draft.Dirty = draft.Value != draft.Observed.Value;
    }
    public void ApplyParameter(DeveloperParameterId id, DeveloperTarget target, bool reset = false)
    {
        if (Find(id, target) is not { } draft) { Feedback = "Confirmed parameter observation unavailable."; return; }
        draft.NeedsReview = false;
        Send(reset ? DeveloperOperation.ResetParameter : DeveloperOperation.SetParameter, target, id,
            reset ? [] : [draft.Value], draft.Observed.Revision);
    }
    public void SessionCommand(DeveloperOperation operation, DeveloperValue? value = null) =>
        Send(operation, DeveloperTarget.Global, DeveloperParameterId.None, value.HasValue ? [value.Value] : [], Revision(DeveloperRevisionDomain.Session));
    public void AdjustAction(int index, int direction)
    {
        if (WriteBlocked || _actionForm is null || index < 0 || index >= _actionValues.Length) return;
        var f = _actionForm.Fields[index];
        if (f.Choices is { } choices)
        {
            int current = Enumerable.Range(0, choices.Count).First(i => choices[i].Value == _actionValues[index]);
            _actionValues[index] = choices[(current + direction + choices.Count) % choices.Count].Value; return;
        }
        double next = Math.Clamp(Number(_actionValues[index]) + direction * Number(f.Step), Number(f.Minimum), Number(f.Maximum));
        _actionValues[index] = FromNumber(f.Default.Kind, next);
    }
    public string ActionValueText(int index) => _actionForm?.Fields[index].Choices?.FirstOrDefault(c => c.Value == _actionValues[index])?.Label ?? Format(_actionValues[index]);
    public void ApplyAction()
    {
        if (_action is not { } action) { Feedback = "Select an available action and explicit target first."; return; }
        if (_actionForm is null || _actionForm.Operation != action.Operation)
        { Feedback = "Action payload adapter unavailable; no request submitted."; return; }
        Send(action.Operation, _target, DeveloperParameterId.None, _actionValues, _actionRevision);
    }
    public DeveloperFailureReason PresetCompatibility => Preset is { } preset
        ? preset.Compatibility != DeveloperFailureReason.None ? preset.Compatibility : _providers.Presets.Validate(preset.Metadata)
        : DeveloperFailureReason.ProviderUnavailable;
    public void PresetCommand(DeveloperPresetIntent intent)
    {
        if (WriteBlocked || _providers.Presets.Status.Availability != DeveloperProviderAvailability.Available || _backend is null)
        { Feedback = "Preset I/O and session-loading adapter unavailable."; return; }
        if (intent == DeveloperPresetIntent.RestartSameSeed && Preset is not null && PresetCompatibility != DeveloperFailureReason.None)
        { Feedback = "Select a compatible preset first."; return; }
        var correlation = Guid.NewGuid(); _presetInFlight = correlation;
        try
        {
            bool submitted = _backend.TrySubmitPreset(correlation, intent, Preset, out var feedback);
            Feedback = submitted ? "Submitted: " + feedback : feedback;
            if (!submitted) _presetInFlight = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { _presetInFlight = null; Feedback = "Preset submission failed; no completion confirmed."; }
    }
    private DeveloperStateRevision? Revision(DeveloperRevisionDomain domain) => _observation?.Revisions.FirstOrDefault(r => r.Domain == domain) is { IsValid: true } r ? r : null;
    private void Send(DeveloperOperation operation, DeveloperTarget target, DeveloperParameterId parameter,
        ReadOnlySpan<DeveloperValue> values, DeveloperStateRevision? revision)
    {
        if (WriteBlocked || revision is null) { Feedback = "Waiting for current permission, revision, focus or previous result."; return; }
        var request = new DeveloperRequest(_session, Guid.NewGuid(), revision.Value, operation, target, parameter, values);
        var context = new DeveloperExecutionContext(_session, revision.Value,
            p => _participants.Any(participant => participant.Player == p), e => e == _inspector.Entity && _inspector.Session == _session);
        _inFlight = request;
        try
        {
            DeveloperResult result = operation is DeveloperOperation.SetParameter or DeveloperOperation.ResetParameter
                ? _providers.Parameters.Submit(_access!, context, request)
                : operation is DeveloperOperation.SpawnUnit or DeveloperOperation.GrantResource
                    ? _providers.Actions.Submit(_access!, context, request) : _providers.Sessions.Submit(_access!, context, request);
            Feedback = "Submitted; waiting for correlated authoritative result.";
            if (result.Session != request.Session || result.Correlation != request.Correlation)
            { _inFlight = null; Feedback = "Invalid submission response; no application confirmed."; return; }
            if (result.Status is DeveloperRequestStatus.Applied or DeveloperRequestStatus.Rejected) Observe(result);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { _inFlight = null; Feedback = "Submission failed; no application confirmed."; }
    }
    public void Observe(DeveloperResult result)
    {
        if (_inFlight is not { } request || result.Session != _session || result.Correlation != request.Correlation ||
            result.Revision.Domain != request.ExpectedRevision.Domain || result.Status == DeveloperRequestStatus.Accepted) return;
        Feedback = result.Status switch
        {
            DeveloperRequestStatus.Applied => "Applied at tick " + result.AppliedTick!.Value.Value,
            DeveloperRequestStatus.Pending => "Confirmed pending; waiting for a new match.",
            _ => Failure(result.Failure)
        };
        if (request.Parameter != DeveloperParameterId.None && Find(request.Parameter, request.Target) is { } draft)
        {
            if (result.Status == DeveloperRequestStatus.Rejected) draft.NeedsReview = true;
            if (result.Status == DeveloperRequestStatus.Applied)
                foreach (var effect in result.AffectedValues)
                    if (effect.Kind == DeveloperEffectKind.Parameter && effect.Parameter == request.Parameter && effect.Target == request.Target)
                    { draft.Observed = new(request.Parameter, request.Target, effect.Value, result.Revision); draft.Value = effect.Value; draft.Dirty = false; }
        }
        if (_history.Count == 64) _history.Dequeue();
        _history.Enqueue(request.Operation + ": " + Feedback); _inFlight = null;
    }
    public static string Failure(DeveloperFailureReason reason) => reason switch
    {
        DeveloperFailureReason.StaleRevision => "Revision conflict; current state will reload. Draft unverified: review and explicitly apply again.",
        DeveloperFailureReason.StaleSession => "Session changed; request rejected.",
        DeveloperFailureReason.InvalidTarget => "Target disappeared or is not valid for this action.",
        DeveloperFailureReason.ProviderUnavailable => "Provider unavailable; no change applied.",
        DeveloperFailureReason.CapabilityUnavailable or DeveloperFailureReason.SessionNotActivated => "Developer permission unavailable.",
        _ => "Rejected: " + reason
    };
    public static string Format(DeveloperValue value) => value.Kind switch
    {
        DeveloperValueKind.Boolean => value.AsBoolean() ? "ON" : "OFF",
        DeveloperValueKind.SignedNumber => value.AsInt32().ToString(CultureInfo.InvariantCulture),
        DeveloperValueKind.UnsignedNumber => value.AsUInt32().ToString(CultureInfo.InvariantCulture),
        DeveloperValueKind.RealNumber => value.AsDouble().ToString("0.####", CultureInfo.InvariantCulture), _ => "unavailable"
    };
    internal static double Number(DeveloperValue value) => value.Kind switch
    { DeveloperValueKind.Boolean => value.AsBoolean() ? 1 : 0, DeveloperValueKind.SignedNumber => value.AsInt32(), DeveloperValueKind.UnsignedNumber => value.AsUInt32(), _ => value.AsDouble() };
    private static DeveloperValue FromNumber(DeveloperValueKind kind, double number) => kind switch
    { DeveloperValueKind.Boolean => DeveloperValue.FromBoolean(number != 0), DeveloperValueKind.SignedNumber => DeveloperValue.FromInt32(checked((int)number)),
      DeveloperValueKind.UnsignedNumber => DeveloperValue.FromUInt32(checked((uint)number)), _ => DeveloperValue.FromDouble(number) };
    private static DeveloperValue Step(DeveloperParameterDescriptor descriptor, DeveloperValue value, int direction)
    {
        double next = Math.Clamp(Number(value) + direction * Number(descriptor.Step), Number(descriptor.Minimum), Number(descriptor.Maximum));
        return FromNumber(descriptor.Kind, next);
    }
}
