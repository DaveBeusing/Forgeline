using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct AlertNotification(PlayerAlertState Kind, bool Feedback = false, int Hidden = 0, bool Local = false);
public readonly record struct AlertActivation(bool Captured, EntityId Target = default, bool OpenOperations = false,
    OperationsCategory Category = OperationsCategory.Blocked, bool NotificationDetails = false);

/// <summary>Navigation only; retains short clicks, rejects lifecycle edges and never emits simulation commands.</summary>
public sealed class ActionableAlertController
{
    private ulong _press;
    private SimulationSessionId _session;
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
    public string Feedback { get; private set; } = string.Empty;
    public void Unavailable() { Feedback = "TARGET UNAVAILABLE - OPEN OPERATIONS"; _feedbackSeconds = 3; }
    public void CancelInput(InputState input) { _press = input.MousePressSequence(PlatformMouseButton.Left); _pending = false; }
    public AlertActivation Update(InputState input, PresentationSnapshot? snapshot, in GameplayHudLayout layout, bool blocked = false, double seconds = 0, bool dockExpanded = false, string localFeedback = "")
    {
        if (_session.IsSpecified && snapshot?.SessionId is { IsSpecified: true } incoming &&
            (incoming.Value < _session.Value || incoming == _session && snapshot.Tick.Value < _tick.Value))
        { CancelInput(input); return default; }
        _feedbackSeconds = Math.Max(0, _feedbackSeconds - Math.Max(0, seconds));
        if (_feedbackSeconds == 0) Feedback = string.Empty;
        ulong press = input.MousePressSequence(PlatformMouseButton.Left); bool fresh = press != _press; _press = press;
        bool changed = _initialized && (_layout != layout || _session != snapshot?.SessionId);
        _initialized = true; _layout = layout;
        if (_session != snapshot?.SessionId) { Feedback = string.Empty; _feedbackSeconds = 0; }
        _session = snapshot?.SessionId ?? default;
        _tick = snapshot?.Tick ?? default;
        if (blocked || input.FocusLostThisFrame || snapshot?.PlayerExperience is not { IsMatchComplete: false } experience)
        { _pending = false; return default; }
        var stack = ActionableAlertLayout.Resolve(snapshot, layout, dockExpanded, localFeedback);
        bool originValid = input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin);
        bool captured = _pending || input.HasPointerPosition && stack.Contains(input.PointerPosition) || fresh && originValid && stack.Contains(origin);
        if (changed) { _pending = false; return new(captured); }
        var alerts = ActionableAlertSnapshot.Resolve(snapshot);
        if (fresh)
        {
            _pending = false; _identity = default;
            if (!originValid || !stack.Contains(origin)) return new(captured);
            _row = stack.Hit(origin);
            if (_row < 0) return new(captured);
            var card = stack.Card(_row);
            _item = card.Item;
            _pressedStack = stack;
            _details = card.Item.Hidden > 0 || card.Truncated && (card.Item.Feedback || card.Item.Local || card.Details.Contains(origin));
            _pending = card.Item.Kind != PlayerAlertState.None || _details;
            _target = default;
            if (alerts is not null)
                for (int i = 0; i < alerts.Alerts.Count; i++)
                    if (alerts.Alerts[i].Identity.Kind == _item.Kind)
                    { _identity = alerts.Alerts[i].Identity; _target = alerts.Alerts[i].Target; }
        }
        if (!_pending || input.IsMouseButtonDown(PlatformMouseButton.Left)) return new(captured);
        _pending = false;
        if (!input.HasPointerPosition || !stack.Card(_row).Bounds.Contains(input.PointerPosition) || stack != _pressedStack) return new(true);
        var item = stack.Card(_row).Item;
        if (item.Kind != _item.Kind || (item.Hidden > 0) != (_item.Hidden > 0)) return new(true);
        var releasedCard = stack.Card(_row);
        bool releasedDetails = item.Hidden > 0 || releasedCard.Truncated &&
            (item.Feedback || item.Local || releasedCard.Details.Contains(input.PointerPosition));
        if (_details != releasedDetails) return new(true);
        if (_details) return new(true, OpenOperations: true, NotificationDetails: true);
        if (item.Kind == PlayerAlertState.None) return new(true);
        if (alerts is not null)
            for (int i = 0; i < alerts.Alerts.Count; i++)
                if (alerts.Alerts[i].Identity.Kind == item.Kind)
                {
                    var alert = alerts.Alerts[i];
                    if (_identity != alert.Identity || _target != alert.Target) return new(true);
                    if (alert.Target.IsValid) return new(true, alert.Target, Category: alert.Operations);
                    Unavailable(); return new(true, OpenOperations: true, Category: alert.Operations);
                }
        Feedback = "ALERT DATA UNAVAILABLE - OPEN OPERATIONS"; _feedbackSeconds = 3;
        return new(true, OpenOperations: true, Category: AlertLifecycleTracker.Category(item.Kind));
    }
}
