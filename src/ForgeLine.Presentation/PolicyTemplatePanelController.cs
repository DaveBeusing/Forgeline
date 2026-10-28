using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct PolicyTemplatePanelView(bool Open, int Slot, PolicyTemplate Draft, bool Dirty,
    bool Saved, bool Pending, bool Applying, int Field, int Focus, int DetailPage, string Feedback,
    IReadOnlyList<PolicyTemplatePreviewRow> Rows, int Omitted, bool HasSelection, bool PreviewValid);

/// <summary>Bounded session-local drafts and deliberate command fan-out. Never owns economy state.</summary>
internal sealed class PolicyTemplatePanelController
{
    private readonly GameplayBindingRegistry _bindings;
    private readonly PolicyTemplate?[] _slots = new PolicyTemplate?[PolicyTemplate.MaximumSlots];
    private readonly ulong[] _keys = new ulong[Enum.GetValues<PlatformKey>().Length];
    private readonly List<PolicyTemplatePreviewRow> _rows = new(PolicyTemplate.MaximumTargets);
    private IReadOnlyList<PolicyTemplatePreviewRow> _copiedRows = [];
    private SimulationSessionId _session;
    private PolicyTemplateSelectionSnapshot? _data;
    private PolicyTemplate _draft;
    private EntityId[] _selection = [];
    private ResourceId[] _outputs = [];
    private bool _open, _dirty, _pending, _applying, _previewValid;
    private int _slot, _field, _focus = 1, _detail, _sourceIndex = -1, _next, _pendingRow = -1;
    private string _feedback = "EMPTY SLOT / SELECT ONE SOURCE AND COPY";
    private PlayerActionRequest? _request;
    private ulong _hostSequence, _press, _revision, _armedRevision;
    private PlayerCommandCorrelationId _correlation;
    private PlayerCommandResultReadModel? _earlyResult;
    private GameplayHudLayout _layout;
    private int _armed = -1;

    internal PolicyTemplatePanelController(GameplayBindingRegistry bindings) => _bindings = bindings;
    internal PolicyTemplatePanelView View
    {
        get
        {
            if (!_copiedRows.SequenceEqual(_rows)) _copiedRows = Array.AsReadOnly(_rows.ToArray());
            return new(_open, _slot, _draft, _dirty, _slots[_slot].HasValue, _pending, _applying,
                _field, _focus, _detail, _feedback, _copiedRows, _data?.Omitted ?? 0,
                _data?.Targets.Count > 0, _previewValid);
        }
    }
    internal void Reset(SimulationSessionId session)
    {
        _session = session; Array.Clear(_slots); _rows.Clear(); _draft = default;
        _selection = []; _outputs = []; _data = null; _request = null; _pending = _applying = _open = _dirty = _previewValid = false;
        _correlation = default; _earlyResult = null; _hostSequence = 0; _pendingRow = -1;
        _slot = _detail = _field = 0; _sourceIndex = -1; _armed = -1;
        _feedback = "NEW SESSION / LOCAL SLOTS EMPTY";
    }
    internal void Open() { _open = true; _armed = -1; }
    internal void Close() { Interrupt("CLOSED / UNSENT TARGETS CANCELLED"); _open = false; }
    internal void CancelInput(InputState input)
    { Interrupt("INPUT INTERRUPTED / PREVIEW AGAIN"); SynchronizeKeys(input); _press = input.MousePressSequence(PlatformMouseButton.Left); }
    private void Interrupt(string reason)
    {
        _armed = -1;
        if (!_applying && !_request.HasValue) return;
        _applying = false; _previewValid = false;
        if (_request.HasValue)
        {
            _request = null; _pending = false;
            if (_pendingRow >= 0) _rows[_pendingRow] = _rows[_pendingRow] with
            { State = PolicyTemplateRowState.Cancelled, Reason = "NOT SUBMITTED" };
            _pendingRow = -1;
        }
        for (int i = _next; i < _rows.Count; i++)
            if (_rows[i].State == PolicyTemplateRowState.Ready)
                _rows[i] = _rows[i] with { State = PolicyTemplateRowState.Cancelled, Reason = "NOT SUBMITTED" };
        _feedback = reason;
    }
    internal void Update(InputState input, PresentationSnapshot? snapshot, in GameplayHudLayout layout,
        bool blocked, SelectionSet? selection)
    {
        if (_session != snapshot?.SessionId)
        {
            Reset(snapshot?.SessionId ?? default);
            SynchronizeKeys(input);
            _press = input.MousePressSequence(PlatformMouseButton.Left);
        }
        var actions = PlayerActionDockHudModel.ResolveActions(snapshot);
        _data = actions?.PolicyTemplates is { } data && data.Session == _session && data.Tick == snapshot?.Tick &&
            (selection is null || selection.Count == data.Targets.Count + data.Omitted &&
             data.Targets.All(target => selection.Contains(target.Entity))) ? data : null;
        EntityId[] current = _data?.Targets.Select(target => target.Entity).ToArray() ?? [];
        if (_selection.Length != current.Length || !_selection.SequenceEqual(current))
        {
            Interrupt("SELECTION CHANGED / PREVIEW AGAIN"); _previewValid = false; _armed = -1;
            _selection = current; _revision++;
        }
        bool transition = _layout != layout; _layout = layout;
        bool fresh = input.MousePressSequence(PlatformMouseButton.Left) != _press;
        _press = input.MousePressSequence(PlatformMouseButton.Left);
        if (blocked || transition || input.FocusLostThisFrame || OperationsLayout.Panel(layout).IsEmpty)
        { CancelInput(input); return; }
        if (Pressed(input, GameplayAction.NextItem)) { _focus = (_focus + 1) % OperationsRulePanelLayout.ControlCount; _armed = -1; }
        if (Pressed(input, GameplayAction.PrimarySetting)) Activate(4);
        if (Pressed(input, GameplayAction.SecondarySetting)) Activate(2);
        if (Pressed(input, GameplayAction.Decrease)) Activate(5);
        if (Pressed(input, GameplayAction.Increase)) Activate(6);
        if (Pressed(input, GameplayAction.Activate)) Activate(_focus);
        if (fresh && input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin))
        {
            _armed = -1;
            for (int i = 0; i < OperationsRulePanelLayout.ControlCount; i++)
                if (OperationsRulePanelLayout.Control(layout, i).Contains(origin)) { _armed = i; break; }
            _armedRevision = _revision;
        }
        if (input.WasMouseButtonReleased(PlatformMouseButton.Left))
        {
            int control = _armed; _armed = -1;
            if (control >= 0 && _armedRevision == _revision && input.HasPointerPosition &&
                OperationsRulePanelLayout.Control(layout, control).Contains(input.PointerPosition))
            { _focus = control; Activate(control); }
        }
        if (_applying && !_pending && !_request.HasValue && actions?.PendingCommandCount == 0) Pump();
    }
    private bool Pressed(InputState input, GameplayAction action)
    {
        var key = _bindings.Key(action); ulong sequence = input.KeyPressSequence(key);
        bool edge = sequence != _keys[(int)key] && input.WasKeyPressed(key); _keys[(int)key] = sequence; return edge;
    }
    private void SynchronizeKeys(InputState input)
    { for (int i = 0; i < _keys.Length; i++) _keys[i] = input.KeyPressSequence((PlatformKey)i); }

    internal static bool CanUse(int control, PolicyTemplatePanelView view) => control switch
    {
        0 or 11 => true,
        9 => !view.Pending && !view.Applying && view.PreviewValid && !view.Dirty &&
            view.Rows.Any(row => row.State == PolicyTemplateRowState.Ready),
        7 => !view.Pending && !view.Applying && view.Draft.Valid,
        8 => !view.Pending && !view.Applying && view.Saved && !view.Dirty && view.HasSelection,
        3 => !view.Pending && !view.Applying && view.HasSelection,
        _ => !view.Pending && !view.Applying
    };
    private void Activate(int control)
    {
        if (control == 0) { Close(); return; }
        if (control == 11) { _detail = (_detail + 1) % 4096; return; }
        if (!CanUse(control, View)) { _feedback = "REVIEW DRAFT, SAVE, PREVIEW, THEN APPLY"; return; }
        _armed = -1; _revision++;
        switch (control)
        {
            case 1:
                _slot = (_slot + 1) % _slots.Length; _draft = _slots[_slot] ?? default;
                _dirty = false; _field = 0; _sourceIndex = -1; _outputs = []; Invalidate();
                _feedback = _slots[_slot].HasValue ? "SAVED LOCAL TEMPLATE / PREVIEW TO APPLY" : "EMPTY SLOT / COPY SOURCE"; return;
            case 2:
                _draft = new(_draft.Kind == PolicyTemplateKind.Stock ? PolicyTemplateKind.Production : PolicyTemplateKind.Stock,
                    default, default, 50, 100, 150, LogisticsStockPriority.Normal, true,
                    ProductionPriority.Normal, ProductionRequestMode.OneShot, default, 100, "");
                _sourceIndex = -1; _field = 0; Changed(); return;
            case 3: CopySource(); return;
            case 4: _field = (_field + 1) % (_draft.Kind == PolicyTemplateKind.Stock ? 5 : 4); return;
            case 5: Adjust(-1); return;
            case 6: Adjust(1); return;
            case 7: _slots[_slot] = _draft; _dirty = false; Invalidate(); _feedback = "SAVED LOCALLY / NO COMMAND SENT"; return;
            case 8:
                _rows.Clear(); _detail = 0;
                foreach (var target in _data!.Targets) _rows.Add(PolicyTemplatePreview.Create(_draft, target));
                _previewValid = true; _feedback = "PREVIEW ONLY / APPLY SENDS ONE COMMAND PER READY TARGET"; return;
            case 9: _next = 0; _applying = true; _previewValid = false; _feedback = "APPLYING / RESULTS ARE PER FACILITY"; return;
            case 10: _slots[_slot] = null; _draft = default; _dirty = false; Invalidate(); _feedback = "LOCAL SLOT CLEARED"; return;
        }
    }
    private void Invalidate() { _rows.Clear(); _previewValid = false; _detail = 0; }
    private void Changed() { _dirty = true; Invalidate(); _feedback = "LOCAL DRAFT / SAVE DOES NOT APPLY"; }
    private void CopySource()
    {
        if (_data?.Targets.Count != 1 || _data.Targets[0].Unavailable.Length > 0)
        { _feedback = "SELECT ONE ELIGIBLE OWNED SOURCE TO COPY"; return; }
        var source = _data.Targets[0];
        if (_draft.Kind == PolicyTemplateKind.Stock)
        {
            if (source.Stocks.Count == 0) { _feedback = "SOURCE HAS NO STOCK CONTROLS"; return; }
            _sourceIndex = (_sourceIndex + 1) % source.Stocks.Count;
            var fact = source.Stocks[_sourceIndex];
            var defaults = LogisticsQuantityEditor.NewManualDefaults(Math.Min(source.InputCapacity, fact.Capacity));
            _draft = new(PolicyTemplateKind.Stock, fact.Resource, default,
                fact.Policy.IsValid ? fact.Minimum : defaults.Minimum, fact.Policy.IsValid ? fact.Target : defaults.Target,
                fact.Policy.IsValid ? fact.Maximum : defaults.Maximum, fact.Policy.IsValid ? fact.Priority : LogisticsStockPriority.Normal,
                fact.Enabled, ProductionPriority.Normal, ProductionRequestMode.OneShot, default, 100, fact.Name);
        }
        else
        {
            if (source.Production is not { Recipes.Count: > 0 } production)
            { _feedback = "SOURCE HAS NO PRODUCTION RECIPE"; return; }
            _sourceIndex = (_sourceIndex + 1) % production.Recipes.Count;
            var recipe = production.Recipes[_sourceIndex];
            var request = production.Requests.FirstOrDefault(item => item.RecipeId == recipe.RecipeId);
            _outputs = recipe.Outputs.Select(output => output.ResourceId).Take(8).ToArray();
            _draft = new(PolicyTemplateKind.Production, default, recipe.RecipeId, 0, 0, 0, LogisticsStockPriority.Normal,
                true, request.RequestEntity.IsValid ? request.Priority : ProductionPriority.Normal,
                request.RequestEntity.IsValid ? request.Mode : ProductionRequestMode.OneShot,
                request.DesiredStockResource.IsSpecified ? request.DesiredStockResource : _outputs.FirstOrDefault(),
                request.DesiredStockQuantity > 0 ? request.DesiredStockQuantity : 100, recipe.DisplayName);
        }
        Changed();
    }
    private void Adjust(int direction)
    {
        if (_draft.Kind == PolicyTemplateKind.Stock)
        {
            if (_field < 3)
            {
                var decision = LogisticsQuantityEditor.DecideField((PlayerStockThresholdField)_field,
                    _draft.Minimum, _draft.Target, _draft.Maximum, PolicyTemplate.MaximumQuantity, direction, true);
                if (!decision.Allowed) { _feedback = decision.Reason; return; }
                _draft = _field switch { 0 => _draft with { Minimum = decision.Next },
                    1 => _draft with { Target = decision.Next }, _ => _draft with { Maximum = decision.Next } };
            }
            else if (_field == 3) _draft = _draft with { StockPriority = Cycle(_draft.StockPriority, direction) };
            else _draft = _draft with { Enabled = !_draft.Enabled };
        }
        else
        {
            if (_field == 0) _draft = _draft with { Mode = Cycle(_draft.Mode, direction) };
            else if (_field == 1) _draft = _draft with { ProductionPriority = Cycle(_draft.ProductionPriority, direction) };
            else if (_field == 2)
            {
                var value = LogisticsQuantityEditor.Decide(_draft.OutputTarget, direction, 50, PolicyTemplate.MaximumQuantity,
                    "MINIMUM IS 50", "TEMPLATE QUANTITY LIMIT");
                if (!value.Allowed) { _feedback = value.Reason; return; }
                _draft = _draft with { OutputTarget = value.Next };
            }
            else if (_outputs.Length > 0)
            {
                int index = Array.IndexOf(_outputs, _draft.Output);
                _draft = _draft with { Output = _outputs[(index + direction + _outputs.Length) % _outputs.Length] };
            }
            else { _feedback = "COPY A SOURCE TO CHOOSE OUTPUT"; return; }
        }
        Changed();
    }
    private static T Cycle<T>(T value, int direction) where T : struct, Enum
    {
        T[] values = Enum.GetValues<T>();
        return values[(Array.IndexOf(values, value) + direction + values.Length) % values.Length];
    }

    private void Pump()
    {
        while (_next < _rows.Count)
        {
            int index = _next++;
            if (_rows[index].State != PolicyTemplateRowState.Ready) continue;
            var row = _rows[index];
            var target = _data?.Targets.FirstOrDefault(item => item.Entity == row.Entity);
            var current = target is null ? null : PolicyTemplatePreview.Create(_draft, target);
            if (current is null || current.Reason.Length > 0 || current.Basis != row.Basis)
            {
                _rows[index] = row with { State = PolicyTemplateRowState.Skipped,
                    Reason = current is { Reason.Length: > 0 } ? current.Reason : "STATE CHANGED / PREVIEW AGAIN" };
                continue;
            }
            _pendingRow = index; _pending = true; _correlation = default; _hostSequence = 0; _earlyResult = null;
            _request = current.Request;
            _rows[index] = row with { State = PolicyTemplateRowState.Pending };
            return;
        }
        _applying = false;
        _feedback = $"FINISHED: {_rows.Count(row => row.State == PolicyTemplateRowState.Confirmed)} CONFIRMED, " +
            $"{_rows.Count(row => row.State == PolicyTemplateRowState.Rejected)} REJECTED, " +
            $"{_rows.Count(row => row.State == PolicyTemplateRowState.Skipped)} SKIPPED / NO AUTOMATIC RETRY";
    }
    internal bool TryTakeRequest(out PlayerActionRequest request)
    { request = _request ?? default; bool found = _request.HasValue; _request = null; return found; }
    internal void ExpectSubmission(ulong sequence) => _hostSequence = sequence;
    internal void SubmissionFailed()
    {
        if (!_pending) return;
        _rows[_pendingRow] = _rows[_pendingRow] with { State = PolicyTemplateRowState.Rejected, Reason = "SUBMISSION FAILED" };
        _pending = false; _pendingRow = -1; _correlation = default; _hostSequence = 0;
        _feedback = "SUBMISSION FAILED / TARGET WILL NOT RETRY";
    }
    internal void ObserveSubmission(ulong sequence, PlayerCommandSubmissionReceipt? receipt)
    {
        if (!_pending || sequence != _hostSequence) return;
        if (receipt is not { Accepted: true } accepted || accepted.SessionId != _session)
        { SubmissionFailed(); return; }
        _correlation = accepted.CorrelationId; _hostSequence = 0;
        if (_earlyResult is { } early) { _earlyResult = null; ObserveResult(early); }
    }
    internal void ObserveResult(in PlayerCommandResultReadModel result)
    {
        PlayerCommandKind expected = _draft.Kind == PolicyTemplateKind.Stock ? PlayerCommandKind.Logistics : PlayerCommandKind.Production;
        if (!_pending || result.SessionId != _session || result.Kind != expected) return;
        if (!_correlation.IsSpecified) { _earlyResult = result; return; }
        if (result.CorrelationId != _correlation) return;
        _rows[_pendingRow] = _rows[_pendingRow] with
        {
            State = result.State == PlayerCommandFeedbackState.Accepted ? PolicyTemplateRowState.Confirmed : PolicyTemplateRowState.Rejected,
            Reason = result.State == PlayerCommandFeedbackState.Accepted
                ? "COMMAND CONFIRMED / STOCK AND PRODUCTION REMAIN PHYSICAL"
                : result.ActionFailure != PlayerLogisticsActionFailureReason.None ? result.ActionFailure.ToString() : "COMMAND REJECTED"
        };
        _pending = false; _pendingRow = -1; _correlation = default;
    }

    internal static string FieldLabel(PolicyTemplatePanelView view) => view.Draft.Kind == PolicyTemplateKind.Stock
        ? view.Field switch
        {
            0 => FormattableString.Invariant($"MIN {view.Draft.Minimum}"),
            1 => FormattableString.Invariant($"TARGET {view.Draft.Target}"),
            2 => FormattableString.Invariant($"MAX {view.Draft.Maximum}"),
            3 => "PRIORITY " + view.Draft.StockPriority,
            _ => view.Draft.Enabled ? "POLICY ENABLED" : "POLICY DISABLED"
        }
        : view.Field switch
        {
            0 => "MODE " + view.Draft.Mode, 1 => "PRIORITY " + view.Draft.ProductionPriority,
            2 => FormattableString.Invariant($"OUTPUT TARGET {view.Draft.OutputTarget}"),
            _ => "OUTPUT " + view.Draft.Output
        };
}
