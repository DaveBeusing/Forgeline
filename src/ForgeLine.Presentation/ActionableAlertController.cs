using System.Globalization;
using System.Text;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct AlertNotification(PlayerAlertState Kind, bool Feedback = false, int Hidden = 0, bool Local = false);
public readonly record struct AlertActivation(bool Captured, EntityId Target = default, bool OpenOperations = false,
    OperationsCategory Category = OperationsCategory.Blocked, bool NotificationDetails = false);

/// <summary>Presentation-only identity, page and reversible expansion progress. Never grants target authority.</summary>
public readonly record struct NotificationExpansion(AlertNotification Item, AlertIdentity Identity, ulong Revision,
    bool Open = false, float Progress = 0, int Page = 0, string Content = "")
{
    public bool Matches(in NotificationCardLayout card) =>
        (Open || Progress > 0) && card.Item == Item && card.Identity == Identity &&
        card.Revision == Revision && card.Item.Hidden == 0;
}

/// <summary>Navigation only; retains short clicks, rejects lifecycle edges and never emits simulation commands.</summary>
public sealed class ActionableAlertController
{
    private ulong _press;
    private SimulationSessionId _session;
    private PlayerId _player;
    private SimulationTick _tick;
    private GameplayHudLayout _layout;
    private bool _initialized;
    private double _feedbackSeconds;
    private bool _pending;
    private int _row;
    private AlertNotification _item;
    private AlertIdentity _identity;
    private NotificationStackLayout _pressedStack;
    private bool _details;
    private EntityId _target;
    private AlertIdentity _focusCursor;
    private int _focusRevision;
    private bool _hasFocusCursor;
    private bool _pressFocus;
    private bool _pressPaging;
    private float _expansionPosition;
    private EntityId _expandedTarget;
    private int _expandedStackRevision;
    private NotificationExpansion _expansion;
    public NotificationExpansion Expansion => _expansion with
    {
        Progress = UiMotionTimeline.SmoothStep(TimeSpan.FromMilliseconds(_expansionPosition * 200),
            TimeSpan.FromMilliseconds(200))
    };
    public string Feedback { get; private set; } = string.Empty;
    public void Unavailable() { Feedback = "TARGET UNAVAILABLE - OPEN OPERATIONS"; _feedbackSeconds = 3; }
    public void CancelInput(InputState input) { _press = input.MousePressSequence(PlatformMouseButton.Left); _pending = false; }
    public AlertActivation Update(InputState input, PresentationSnapshot? snapshot, in GameplayHudLayout layout, bool blocked = false, double seconds = 0, bool dockExpanded = false, string localFeedback = "",
        bool animateDetails = true)
    {
        if (_session.IsSpecified && snapshot?.SessionId is { IsSpecified: true } incoming &&
            (incoming.Value < _session.Value || incoming == _session && snapshot.Tick.Value < _tick.Value))
        { CancelInput(input); ResetTransient(); return default; }
        _feedbackSeconds = Math.Max(0, _feedbackSeconds - Math.Max(0, seconds));
        if (_feedbackSeconds == 0) Feedback = string.Empty;
        ulong press = input.MousePressSequence(PlatformMouseButton.Left); bool fresh = press != _press; _press = press;
        PlayerId currentPlayer = snapshot?.PlayerExperience?.Player ?? default;
        bool changed = _initialized && (_layout != layout || _session != snapshot?.SessionId ||
            _player != currentPlayer);
        _initialized = true; _layout = layout;
        if (_session != snapshot?.SessionId || _player != currentPlayer)
        { Feedback = string.Empty; _feedbackSeconds = 0; ResetTransient(); }
        _session = snapshot?.SessionId ?? default;
        _player = currentPlayer;
        _tick = snapshot?.Tick ?? default;
        if (blocked || input.FocusLostThisFrame || snapshot?.PlayerExperience is not { IsMatchComplete: false } experience)
        { _pending = false; _expansionPosition = _expansion.Open ? 1 : 0; return default; }
        var plain = ActionableAlertLayout.Resolve(snapshot, layout, dockExpanded, localFeedback);
        var alerts = ActionableAlertSnapshot.Resolve(snapshot);
        if ((_expansion.Open || _expansionPosition > 0) &&
            (!HasExpandedCard(plain) || _expandedStackRevision != plain.Revision ||
                !ExpandedSourceCurrent(alerts)))
        { _expansion = default; _expansionPosition = 0; }
        float requested = _expansion.Open ? 1 : 0;
        bool settleDetails = !animateDetails || changed || !double.IsFinite(seconds) ||
            seconds > .25;
        _expansionPosition = settleDetails ? requested : Math.Clamp(_expansionPosition +
            (float)Math.Max(0, seconds) / .2f * (requested > _expansionPosition ? 1 : -1), 0, 1);
        if (!_expansion.Open && _expansionPosition == 0) _expansion = default;
        var stack = _expansion.Open || _expansionPosition > 0
            ? ActionableAlertLayout.Resolve(snapshot, layout, dockExpanded, localFeedback, Expansion)
            : plain;
        bool originValid = input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin);
        bool captured = _pending || input.HasPointerPosition && stack.Contains(input.PointerPosition) || fresh && originValid && stack.Contains(origin);
        if (changed) { _pending = false; return new(captured); }
        if (fresh)
        {
            _pending = false; _identity = default;
            if (!originValid || !stack.Contains(origin)) return new(captured);
            _row = stack.Hit(origin);
            if (_row < 0) return new(captured);
            var card = stack.Card(_row);
            _item = card.Item;
            _pressedStack = stack;
            _details = card.Item.Hidden > 0 || card.Truncated && !card.Details.IsEmpty && card.Details.Contains(origin);
            _pressFocus = !_details && !card.Action.IsEmpty && card.Action.Contains(origin);
            _pressPaging = !card.ExpansionBounds.IsEmpty && card.ExpansionBounds.Contains(origin);
            _pending = true;
            _target = default;
            if (alerts is not null)
                for (int i = 0; i < alerts.Alerts.Count; i++)
                    if (alerts.Alerts[i].Identity.Kind == _item.Kind)
                    { _identity = alerts.Alerts[i].Identity; _target = alerts.Alerts[i].Target; }
        }
        if (!_pending || input.IsMouseButtonDown(PlatformMouseButton.Left)) return new(captured);
        _pending = false;
        if (!input.HasPointerPosition || _row < 0 || _row >= stack.Count ||
            !stack.Card(_row).InteractiveBounds.Contains(input.PointerPosition) || stack != _pressedStack) return new(true);
        var item = stack.Card(_row).Item;
        if (item.Kind != _item.Kind || (item.Hidden > 0) != (_item.Hidden > 0)) return new(true);
        var releasedCard = stack.Card(_row);
        bool releasedDetails = item.Hidden > 0 || releasedCard.Truncated &&
            !releasedCard.Details.IsEmpty && releasedCard.Details.Contains(input.PointerPosition);
        bool releasedFocus = !_details && !releasedCard.Action.IsEmpty &&
            releasedCard.Action.Contains(input.PointerPosition);
        bool releasedPaging = !releasedCard.ExpansionBounds.IsEmpty &&
            releasedCard.ExpansionBounds.Contains(input.PointerPosition);
        if (_details != releasedDetails || _pressFocus != releasedFocus || _pressPaging != releasedPaging) return new(true);
        if (_details) return new(true, OpenOperations: true, NotificationDetails: true);
        if (_pressPaging)
        {
            _expansion = _expansion with { Page = _expansion.Page + 1 };
            return new(true);
        }
        if (!_pressFocus)
        {
            if (_expansion.Open && _expansion.Matches(releasedCard))
                _expansion = _expansion with { Open = false };
            else
            {
                _expansion = new(item, releasedCard.Identity, releasedCard.Revision, true,
                    _expansionPosition, 0, BuildDetail(experience, alerts, item, localFeedback));
                _expandedTarget = _target;
                _expandedStackRevision = plain.Revision;
                _expansionPosition = 0;
            }
            return new(true);
        }
        if (item.Kind == PlayerAlertState.None) return new(true, OpenOperations: true, NotificationDetails: true);
        if (alerts is null) { Unavailable(); return new(true, OpenOperations: true, Category: AlertLifecycleTracker.Category(item.Kind)); }
        bool stillCurrent = false;
        for (int i = 0; i < alerts.Alerts.Count; i++)
            if (alerts.Alerts[i].Identity == _identity && alerts.Alerts[i].Target == _target &&
                alerts.Alerts[i].Identity.Kind == item.Kind) { stillCurrent = true; break; }
        if (!stillCurrent) return new(true);
        if (!_target.IsValid)
        {
            Unavailable();
            return new(true, OpenOperations: true, Category: AlertLifecycleTracker.Category(item.Kind));
        }
        // Cycle only through currently published, locally authorized, focusable conditions.
        // Count/order/target revisions reset the cursor instead of reusing a stale index.
        bool advance = _hasFocusCursor && _focusRevision == plain.Revision;
        int count = ResourcePowerHudModel.ResolveNotificationCount(experience);
        int start = 0;
        for (int i = 0; i < count; i++)
        {
            var candidate = ActionableAlertLayout.Ordered(experience, i);
            if ((advance && candidate.Kind == _focusCursor.Kind) ||
                (!advance && candidate.Kind == item.Kind))
            {
                start = advance ? (i + 1) % count : i;
                break;
            }
        }
        for (int offset = 0; offset < count; offset++)
        {
            var candidate = ActionableAlertLayout.Ordered(experience, (start + offset) % count);
            if (candidate.Feedback || candidate.Kind == PlayerAlertState.None) continue;
            for (int a = 0; a < alerts.Alerts.Count; a++)
                if (alerts.Alerts[a].Identity.Kind == candidate.Kind &&
                    alerts.Alerts[a].Target.IsValid)
                {
                    _focusCursor = alerts.Alerts[a].Identity;
                    _hasFocusCursor = true;
                    _focusRevision = plain.Revision;
                    return new(true, alerts.Alerts[a].Target, Category: alerts.Alerts[a].Operations);
                }
        }
        _hasFocusCursor = false;
        Unavailable();
        return new(true, OpenOperations: true, Category: AlertLifecycleTracker.Category(item.Kind));
    }

    private bool HasExpandedCard(in NotificationStackLayout stack)
    {
        for (int i = 0; i < stack.Count; i++)
            if ((_expansion with { Progress = _expansionPosition }).Matches(stack.Card(i))) return true;
        return false;
    }

    private bool ExpandedSourceCurrent(ActionableAlertSnapshot? alerts)
    {
        if (_expansion.Item.Feedback || _expansion.Item.Local) return true;
        if (alerts is null) return false;
        for (int i = 0; i < alerts.Alerts.Count; i++)
            if (alerts.Alerts[i].Identity == _expansion.Identity &&
                alerts.Alerts[i].Target == _expandedTarget) return true;
        return false;
    }

    private static string BuildDetail(in PlayerExperienceSnapshot experience,
        ActionableAlertSnapshot? alerts, in AlertNotification item, string localFeedback)
    {
        Span<char> summary = stackalloc char[2048];
        var text = new StringBuilder(320);
        text.AppendLine(NotificationText.Format(experience, alerts, item, summary, localFeedback).ToString());
        text.Append("SEVERITY: ").AppendLine(NotificationText.Severity(experience, item).ToString().ToUpperInvariant());
        if (item.Local || item.Feedback)
        {
            text.Append("REASON: ").AppendLine(item.Local ? localFeedback : ResourcePowerHudRenderer.ResolveFailureLabel(experience.Feedback));
            text.Append("TICK: ").AppendLine((item.Feedback ? experience.Feedback.ResolvedAtTick : experience.Tick).Value.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("FOCUS: N/A");
            text.AppendLine("NEXT ACTION: OPEN OPERATIONS NOTIFICATIONS");
            return text.ToString();
        }
        ActionableAlert? source = null;
        if (alerts is not null)
            for (int i = 0; i < alerts.Alerts.Count; i++)
                if (alerts.Alerts[i].Identity.Kind == item.Kind) { source = alerts.Alerts[i]; break; }
        text.Append("REASON: ").AppendLine(source?.Label ?? AlertLifecycleTracker.Label(item.Kind));
        if (item.Kind == PlayerAlertState.CargoFleetShortage && source?.Fleet is { } fleet)
        {
            text.Append("BACKLOG: ").AppendLine(fleet.BacklogRequests.ToString(CultureInfo.InvariantCulture));
            text.Append("ACTIVE TRUCKS: ").AppendLine(fleet.ActiveTrucks.ToString(CultureInfo.InvariantCulture));
            text.Append("IDLE TRUCKS: ").AppendLine(fleet.IdleTrucks.ToString(CultureInfo.InvariantCulture));
            text.Append("EFFECTIVE TRUCKS: ").AppendLine(fleet.EffectiveTrucks.ToString(CultureInfo.InvariantCulture));
            text.Append("AFFECTED FACILITY: ").AppendLine(fleet.AffectedFacility.ToString());
            text.AppendLine("NEXT ACTION: REVIEW LOGISTICS ORDERS AND OWNED CARGO FLEET; ADD CAPACITY ONLY IF THIS CONDITION PERSISTS");
            return text.ToString();
        }
        int affected = item.Kind == PlayerAlertState.SupplyCritical ? experience.CriticalSupplyUnits :
            item.Kind == PlayerAlertState.ProductionBlocked ? experience.BlockedProductionFacilities : 0;
        text.Append("AFFECTED: ").AppendLine(affected > 0 ? affected.ToString(CultureInfo.InvariantCulture) : "N/A");
        text.Append("ONSET TICK: ").AppendLine(source?.Identity.Started.Value.ToString(CultureInfo.InvariantCulture) ?? "N/A");
        text.Append("FOCUS: ").AppendLine(source is { Target.IsValid: true } ? source.Value.Target.ToString() : "N/A");
        text.Append("NEXT ACTION: ").AppendLine(source is { Target.IsValid: true } ?
            "FOCUS AFFECTED ENTITY" : "OPEN OPERATIONS DETAILS");
        return text.ToString();
    }

    private void ResetTransient()
    {
        _pending = false;
        _hasFocusCursor = false;
        _focusCursor = default;
        _focusRevision = 0;
        _expansion = default;
        _expansionPosition = 0;
        _expandedTarget = default;
        _expandedStackRevision = 0;
    }
}
