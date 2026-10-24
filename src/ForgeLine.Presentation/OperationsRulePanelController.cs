using System.Globalization;
using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct OperationsRulePanelView(
    bool Open, OperationsRuleSelectionSnapshot? Data, OperationsRuleDefinition Draft,
    uint ExpectedRevision, bool NewDraft, bool DraftEnabled, bool Dirty, bool Pending,
    int Field, int Focus, int DetailPage, string Feedback, OperationsRuleReadModel? Observed, bool InputGuarded = false)
{
    public GameplayBindingRegistry? Bindings { get; init; }
}

public static class OperationsRulePanelLayout
{
    public const int ControlCount = 12;
    public static HudRect Control(in GameplayHudLayout layout, int index)
    {
        HudRect panel = OperationsLayout.Panel(layout);
        float s = OperationsLayout.Scale(layout), x = MathF.Min(layout.Scale, panel.Width / 450);
        if (s <= 0 || panel.IsEmpty) return default;
        if (index == 0)
        {
            float width = MathF.Min(90 * MathF.Min(layout.Scale, 1.25f), panel.Width * .4f);
            return new(panel.Right - 6 * x - width, panel.Y + 6 * s, width, 22 * s);
        }
        if (index is >= 1 and <= 4) return new(panel.X + 6 * x, panel.Y + (56 + (index - 1) * 26) * s, 438 * x, 22 * s);
        if (index is >= 5 and <= 10)
        {
            int pair = (index - 5) / 2;
            bool right = index % 2 == 0;
            return new(panel.X + (right ? 222 : 6) * x, panel.Y + (160 + pair * 26) * s,
                (right ? 222 : 210) * x, 22 * s);
        }
        return new(panel.X + 6 * x, panel.Y + 346 * s, 438 * x, 22 * s);
    }
    public static HudRect Details(in GameplayHudLayout layout)
    {
        var panel = OperationsLayout.Panel(layout);
        float s = OperationsLayout.Scale(layout);
        return new(panel.X + 6 * s, panel.Y + 240 * s, MathF.Max(0, panel.Width - 12 * s), 100 * s);
    }
}

/// <summary>Local drafts and input gestures only; every mutation leaves as a normal player request.</summary>
internal sealed class OperationsRulePanelController
{
    private readonly GameplayBindingRegistry _bindings;
    private readonly ulong[] _keys = new ulong[Enum.GetValues<PlatformKey>().Length];
    private readonly Func<ulong> _allocateId;
    private readonly TimeProvider _clock;
    private long _lastSubmissionTimestamp;
    private bool _hasSubmitted, _reviewRejectedDraft;
    private SimulationSessionId _session;
    private EntityId _facility;
    private OperationsRuleSelectionSnapshot? _data;
    private OperationsRuleDefinition _draft;
    private ulong _selectedId;
    private uint _revision;
    private bool _enabled = true, _dirty, _open, _pending, _boundaryPending;
    private int _field, _focus = 1, _detailPage;
    private string _feedback = string.Empty;
    private ulong _press, _hostSequence;
    private PlayerCommandCorrelationId _correlation;
    private PlayerCommandResultReadModel? _earlyResult;
    private int _armed = -1;
    private EntityId _armedFacility;
    private ulong _armedRule;
    private OperationsRuleDefinition _armedDraft;
    private bool _armedEnabled;
    private GameplayHudLayout _layout;
    private PlayerActionRequest? _request;
    private OperationsRuleOperation _pendingOperation;
    private ulong _pendingRule;
    private EntityId _pendingFacility;
    private SimulationTick? _reloadAfter;

    internal OperationsRulePanelController(GameplayBindingRegistry bindings, Func<ulong>? allocateId = null, TimeProvider? timeProvider = null)
    {
        _bindings = bindings;
        _clock = timeProvider ?? TimeProvider.System;
        // This is an external player-request identity. The submitted numeric value is recorded in replay.
        _allocateId = allocateId ?? (() => BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()));
    }

    internal OperationsRulePanelView View => new(_open, _data, _draft, _revision, _selectedId == 0,
        _enabled, _dirty, _pending || _boundaryPending, _field, _focus, _detailPage, _feedback, Observed(),
        _hasSubmitted && _clock.GetElapsedTime(_lastSubmissionTimestamp) < TimeSpan.FromMilliseconds(500))
        { Bindings = _bindings };

    internal void Open() { _open = true; _facility = default; _selectedId = 0; _draft = default;
        _feedback = "SELECT ONE OWNED PROCESSOR"; _focus = 1; _armed = -1; }
    internal void Close() { _open = false; _armed = -1; _request = null; }
    internal void CancelInput(InputState input) { _armed = -1; _press = input.MousePressSequence(PlatformMouseButton.Left); SynchronizeKeys(input); }

    internal void Update(InputState input, PresentationSnapshot? snapshot, in GameplayHudLayout layout,
        bool blocked, SelectionSet? selection)
    {
        if (_session != snapshot?.SessionId)
        {
            _session = snapshot?.SessionId ?? default;
            _facility = default; _pending = false; _hostSequence = 0; _correlation = default;
            _earlyResult = null; _reloadAfter = null; _request = null; _armed = -1; _hasSubmitted = false; _reviewRejectedDraft = false;
        }
        PlayerActionSnapshot? actions = PlayerActionDockHudModel.ResolveActions(snapshot);
        _data = PlayerActionDockHudModel.MatchesSelection(actions, selection) &&
            actions?.OperationsRules is { } permitted && permitted.Facility == actions.Production?.Entity &&
            permitted.Player == snapshot?.PlayerExperience?.Player ? permitted : null;
        _boundaryPending = actions?.PendingCommandCount > 0;
        EntityId selected = _data?.Facility ?? default;
        bool waitingForRequestedData = _data is null && _facility.IsValid &&
            actions?.Production?.Entity == _facility && PlayerActionDockHudModel.MatchesSelection(actions, selection);
        if (!waitingForRequestedData && _facility != selected)
        {
            _facility = selected;
            _armed = -1;
            NewDraft();
        }
        if (_reloadAfter is { } resolved && snapshot is not null && snapshot.Tick >= resolved)
        {
            _reloadAfter = null;
            if (_pendingFacility == _facility)
            {
                _selectedId = _pendingOperation == OperationsRuleOperation.Remove ? 0 : _pendingRule;
                if (_reviewRejectedDraft && Observed() is { } current)
                { _revision = current.Revision; _dirty = true; }
                else Reload();
                _reviewRejectedDraft = false;
            }
        }
        bool transition = _layout != layout;
        _layout = layout;
        bool fresh = input.MousePressSequence(PlatformMouseButton.Left) != _press;
        _press = input.MousePressSequence(PlatformMouseButton.Left);
        if (blocked || transition || input.FocusLostThisFrame || OperationsLayout.Panel(layout).IsEmpty)
        {
            _armed = -1;
            SynchronizeKeys(input);
            return;
        }
        if (Pressed(input, GameplayAction.NextItem)) { _focus = (_focus + 1) % OperationsRulePanelLayout.ControlCount; _armed = -1; }
        if (Pressed(input, GameplayAction.PrimarySetting)) Activate(4);
        if (Pressed(input, GameplayAction.SecondarySetting)) Activate(2);
        if (Pressed(input, GameplayAction.Decrease)) Activate(5);
        if (Pressed(input, GameplayAction.Increase)) Activate(6);
        if (Pressed(input, GameplayAction.Activate)) Activate(_focus);
        if (Pressed(input, GameplayAction.CancelJob)) Activate(9);
        if (fresh && input.TryGetMousePressPosition(PlatformMouseButton.Left, out Vector2 origin))
        {
            _armed = -1;
            for (int i = 0; i < OperationsRulePanelLayout.ControlCount; i++)
                if (OperationsRulePanelLayout.Control(layout, i).Contains(origin)) { _armed = i; break; }
            _armedFacility = _facility;
            _armedRule = _selectedId;
            _armedDraft = _draft;
            _armedEnabled = _enabled;
        }
        if (input.WasMouseButtonReleased(PlatformMouseButton.Left))
        {
            int control = _armed;
            _armed = -1;
            if (control >= 0 && _armedFacility == _facility && _armedRule == _selectedId &&
                _armedDraft == _draft && _armedEnabled == _enabled && input.HasPointerPosition &&
                OperationsRulePanelLayout.Control(layout, control).Contains(input.PointerPosition))
            { _focus = control; Activate(control); }
        }
        if (actions?.PendingCommandCount > 0 && !_pending) _armed = -1;
    }

    private bool Pressed(InputState input, GameplayAction action)
    {
        PlatformKey key = _bindings.Key(action);
        ulong sequence = input.KeyPressSequence(key);
        bool changed = sequence != _keys[(int)key];
        _keys[(int)key] = sequence;
        return changed && input.IsKeyDown(key);
    }
    private void SynchronizeKeys(InputState input)
    { for (int i = 0; i < _keys.Length; i++) _keys[i] = input.KeyPressSequence((PlatformKey)i); }

    private OperationsRuleReadModel? Observed()
    {
        if (_selectedId != 0 && _data is not null)
            foreach (var row in _data.Rules) if (row.Definition.Id == _selectedId) return row;
        return null;
    }

    private void NewDraft()
    {
        _selectedId = 0; _revision = 0; _enabled = true; _dirty = false;
        _field = 0; _detailPage = 0;
        if (_data is null) { _draft = default; _feedback = "SELECT ONE OWNED PROCESSOR"; return; }
        ulong id = _allocateId();
        if (id == 0 || _data.UsedRuleIds.Contains(id)) { _draft = default; _feedback = "NEW ID UNAVAILABLE / RETRY NEW"; return; }
        var amounts = LogisticsQuantityEditor.NewManualDefaults(_data.InputCapacity);
        ResourceId resource = _data.Inputs.Count > 0 ? _data.Inputs[0].ResourceId : ResourceId.None;
        _draft = new(id, _data.Player, _facility, OperationsRuleTemplate.EnsureInputStockPolicy,
            resource, 0, amounts.Target, amounts.Maximum, LogisticsStockPriority.Normal);
        _feedback = "LOCAL DRAFT / APPLY TO CREATE";
    }
    private void Reload()
    {
        if (Observed() is { } row)
        { _draft = row.Definition; _revision = row.Revision; _enabled = row.Enabled; _dirty = false; }
        else NewDraft();
    }
    private void CycleRule()
    {
        if (_data is null) return;
        if (_selectedId == 0)
        {
            if (_data.Rules.Count == 0) { NewDraft(); return; }
            _selectedId = _data.Rules[0].Definition.Id;
        }
        else
        {
            int index = -1;
            for (int i = 0; i < _data.Rules.Count; i++) if (_data.Rules[i].Definition.Id == _selectedId) index = i;
            if (index < 0 || index + 1 >= _data.Rules.Count) { NewDraft(); return; }
            _selectedId = _data.Rules[index + 1].Definition.Id;
        }
        Reload(); _feedback = "COPIED RULE / LOCAL EDITS ONLY"; _detailPage = 0;
    }

    internal static string ApplyReason(in OperationsRulePanelView view)
    {
        if (view.Pending) return "COMMAND PENDING";
        if (view.InputGuarded) return "WAIT BEFORE NEXT CHANGE";
        if (!view.NewDraft && !view.Dirty) return "NO UNAPPLIED CHANGES";
        if (view.Data is null) return "SELECT ONE OWNED PROCESSOR";
        if (!view.NewDraft && !view.Observed.HasValue) return "RULE REMOVED / RELOAD";
        if (view.NewDraft && (view.Data.UsedRuleIds.Count >= OperationsRuleSystem.MaximumRulesPerPlayer ||
            view.Data.Rules.Count >= OperationsRuleSystem.MaximumRulesPerFacility)) return "RULE LIMIT REACHED";
        if (view.NewDraft && view.Draft.Template == OperationsRuleTemplate.EnsureInputStockPolicy &&
            !LogisticsQuantityEditor.FitsCapacity(LogisticsQuantityEditor.Minimum, view.Data.InputCapacity)) return "INPUT CAPACITY BELOW EDITOR STEP";
        if (!view.Draft.Owner.IsSpecified || view.Draft.Id == 0 || !OperationsRuleSystem.ValidDefinition(view.Draft)) return "INVALID RULE SETTINGS";
        if (view.Draft.Template == OperationsRuleTemplate.EnsureInputStockPolicy)
        {
            bool found = false;
            foreach (var input in view.Data.Inputs) found |= input.ResourceId == view.Draft.Resource;
            if (!found) return "QUEUE MATCHING INPUT RECIPE";
            if (!LogisticsQuantityEditor.FitsCapacity(view.Draft.Maximum, view.Data.InputCapacity)) return "INPUT CAPACITY EXCEEDED";
        }
        return string.Empty;
    }
    internal static bool CanUse(int control, in OperationsRulePanelView view) => control switch
    {
        0 or 11 => true,
        7 => ApplyReason(view).Length == 0,
        8 => !view.Pending && !view.InputGuarded && view.Data is not null && (view.NewDraft || !view.Dirty && view.Observed.HasValue),
        9 => !view.Pending && !view.InputGuarded && !view.NewDraft && view.Observed.HasValue,
        3 => !view.Pending && view.Data is { Inputs.Count: > 0 } && view.Draft.Template == OperationsRuleTemplate.EnsureInputStockPolicy,
        _ => !view.Pending && view.Data is not null
    };

    private void Activate(int control)
    {
        if (control == 0) { Close(); return; }
        if (control == 11) { _detailPage = _detailPage == int.MaxValue ? 0 : _detailPage + 1; return; }
        if (!CanUse(control, View)) { _feedback = control == 7 ? ApplyReason(View) : "ACTION UNAVAILABLE / REVIEW CURRENT STATE"; return; }
        switch (control)
        {
            case 1: CycleRule(); return;
            case 2:
                if (_draft.Template == OperationsRuleTemplate.EnsureInputStockPolicy)
                    _draft = _draft with { Template = OperationsRuleTemplate.PauseUntilOutputAvailable, Resource = ResourceId.None };
                else
                {
                    var defaults = LogisticsQuantityEditor.NewManualDefaults(_data!.InputCapacity);
                    _draft = _draft with { Template = OperationsRuleTemplate.EnsureInputStockPolicy,
                        Resource = _data.Inputs.Count > 0 ? _data.Inputs[0].ResourceId : ResourceId.None,
                        Minimum = 0, Target = defaults.Target, Maximum = defaults.Maximum };
                }
                break;
            case 3:
                int selected = -1;
                for (int i = 0; i < _data!.Inputs.Count; i++) if (_data.Inputs[i].ResourceId == _draft.Resource) selected = i;
                _draft = _draft with { Resource = _data.Inputs[(selected + 1) % _data.Inputs.Count].ResourceId };
                break;
            case 4: _field = (_field + 1) % 5; return;
            case 5: Adjust(-1); return;
            case 6: Adjust(1); return;
            case 7: Submit(OperationsRuleOperation.Upsert, _draft, _revision, _enabled); return;
            case 8:
                if (_selectedId == 0) { _enabled = !_enabled; break; }
                var observed = Observed()!.Value;
                Submit(OperationsRuleOperation.SetEnabled, observed.Definition, observed.Revision, !observed.Enabled);
                return;
            case 9:
                var removed = Observed()!.Value;
                Submit(OperationsRuleOperation.Remove, removed.Definition, removed.Revision, false);
                return;
            case 10: Reload(); _feedback = "CURRENT SETTINGS RELOADED / REVIEW BEFORE APPLY"; return;
        }
        _dirty = true;
        _feedback = "LOCAL DRAFT / APPLY TO CHANGE RULE";
    }

    private void Adjust(int direction)
    {
        if (_field < 3)
        {
            if (_draft.Template != OperationsRuleTemplate.EnsureInputStockPolicy) { _feedback = "NO INPUT QUANTITIES FOR OUTPUT TEMPLATE"; return; }
            var decision = LogisticsQuantityEditor.DecideField((PlayerStockThresholdField)_field,
                _draft.Minimum, _draft.Target, _draft.Maximum, _data!.InputCapacity, direction, allowZeroMinimum: true);
            if (!decision.Allowed) { _feedback = decision.Reason; return; }
            _draft = _field switch
            {
                0 => _draft with { Minimum = decision.Next },
                1 => _draft with { Target = decision.Next },
                _ => _draft with { Maximum = decision.Next }
            };
        }
        else if (_field == 3)
        {
            var priorities = Enum.GetValues<LogisticsStockPriority>();
            int index = Array.IndexOf(priorities, _draft.Priority);
            _draft = _draft with { Priority = priorities[(index + direction + priorities.Length) % priorities.Length] };
        }
        else _draft = _draft with { CooldownTicks = (uint)Math.Clamp((long)_draft.CooldownTicks + direction * 5L,
            OperationsRuleSystem.MinimumCooldownTicks, OperationsRuleSystem.MaximumCooldownTicks) };
        _dirty = true; _feedback = "LOCAL DRAFT / APPLY TO CHANGE RULE";
    }
    private void Submit(OperationsRuleOperation operation, OperationsRuleDefinition definition, uint revision, bool enabled)
    {
        if (_pending || _request.HasValue) return;
        _request = PlayerActionRequest.OperationsRule(operation, definition, revision, enabled);
        _hasSubmitted = true; _lastSubmissionTimestamp = _clock.GetTimestamp();
        _pending = true; _pendingOperation = operation; _pendingRule = definition.Id; _pendingFacility = definition.Facility;
        _correlation = default; _earlyResult = null; _feedback = "PENDING / WAIT FOR AUTHORITATIVE RESULT"; _armed = -1;
    }
    internal bool TryTakeRequest(out PlayerActionRequest request)
    { request = _request ?? default; bool available = _request.HasValue; _request = null; return available; }
    internal void ExpectSubmission(ulong sequence) => _hostSequence = sequence;
    internal void SubmissionFailed()
    { _pending = false; _hostSequence = 0; _feedback = "SUBMISSION FAILED / APPLY AGAIN WHEN READY"; }
    internal void ObserveSubmission(ulong sequence, PlayerCommandSubmissionReceipt? receipt)
    {
        if (!_pending || sequence != _hostSequence) return;
        _hostSequence = 0;
        if (receipt is not { Accepted: true } accepted || accepted.SessionId != _session)
        { SubmissionFailed(); return; }
        _correlation = accepted.CorrelationId;
        if (_earlyResult is { } early) { _earlyResult = null; ObserveResult(early); }
    }
    internal void ObserveResult(in PlayerCommandResultReadModel result)
    {
        if (!_pending || result.Kind != PlayerCommandKind.OperationsRule || result.SessionId != _session) return;
        if (!_correlation.IsSpecified) { _earlyResult = result; return; }
        if (result.CorrelationId != _correlation) return;
        _pending = false; _correlation = default;
        _reviewRejectedDraft = result.OperationsResult == OperationsRuleResult.RevisionMismatch &&
            _pendingOperation == OperationsRuleOperation.Upsert;
        _feedback = result.OperationsResult == OperationsRuleResult.RevisionMismatch
            ? "REVISION CHANGED / REVIEW CURRENT / APPLY AGAIN"
            : result.State == PlayerCommandFeedbackState.Accepted ? "RULE CHANGE CONFIRMED" : "REJECTED / " + result.OperationsResult;
        if (result.State == PlayerCommandFeedbackState.Accepted || result.OperationsResult == OperationsRuleResult.RevisionMismatch)
            _reloadAfter = result.ResolvedAtTick;
    }

    internal static string SelectorLabel(in OperationsRulePanelView view)
    {
        if (view.NewDraft) return "NEW DRAFT / SELECT RULE";
        if (view.Data is not null)
            for (int i = 0; i < view.Data.Rules.Count; i++)
                if (view.Data.Rules[i].Definition.Id == view.Draft.Id)
                    return FormattableString.Invariant($"RULE {i + 1}/{view.Data.Rules.Count} / R{view.ExpectedRevision}");
        return "RULE UNAVAILABLE / RELOAD";
    }

    internal static string FieldLabel(in OperationsRulePanelView view)
    {
        var d = view.Draft;
        return view.Field switch
        {
            0 => "MIN " + d.Minimum.ToString("G", CultureInfo.InvariantCulture),
            1 => "TARGET " + d.Target.ToString("G", CultureInfo.InvariantCulture),
            2 => "MAX " + d.Maximum.ToString("G", CultureInfo.InvariantCulture),
            3 => "PRIORITY " + d.Priority,
            _ => FormattableString.Invariant($"COOLDOWN {d.CooldownTicks} TICKS / {d.CooldownTicks * (view.Data?.TickDuration.TotalSeconds ?? 0):0.###}s")
        };
    }
}
