using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct AlertNotification(PlayerAlertState Kind, bool Feedback = false, int Hidden = 0);
public static class ActionableAlertLayout
{
    public static float Scale(in GameplayHudLayout layout) => HudInformationLayout.AlertScale(layout);
    public static HudRect Row(in GameplayHudLayout layout, int row)
    {
        var region = HudInformationLayout.Alerts(layout);
        float scale = Scale(layout);
        return new(region.X, region.Y + row * 21 * scale, region.Width, 18 * scale);
    }
    public static bool Contains(in PlayerExperienceSnapshot experience, in GameplayHudLayout layout, System.Numerics.Vector2 point)
    {
        int maximum = Scale(layout) > 0 ? (int)(layout.AlertStack.Height / (21 * Scale(layout))) : 0;
        for (int row = 0; row < maximum; row++)
        {
            var item = Item(experience, layout, row);
            if ((item.Kind != PlayerAlertState.None || item.Feedback || item.Hidden > 0) && Row(layout, row).Contains(point)) return true;
        }
        return false;
    }
    public static AlertNotification Item(in PlayerExperienceSnapshot experience, in GameplayHudLayout layout, int row)
    {
        int maximum = Scale(layout) > 0 ? (int)(layout.AlertStack.Height / (21 * Scale(layout))) : 0;
        int total = ResourcePowerHudModel.ResolveNotificationCount(experience);
        if (row < 0 || row >= maximum || row >= total) return default;
        int visible = total > maximum && maximum > 1 ? maximum - 1 : maximum;
        if (row == visible && total > visible) return new(default, Hidden: total - visible);
        int index = 0;
        for (int slot = 0; slot < 6; slot++)
        {
            bool feedback = slot == 3;
            var kind = feedback ? default : AlertLifecycleTracker.Kind(slot < 3 ? slot : slot - 1);
            if (feedback ? !ResourcePowerHudModel.IsCommandFeedbackVisible(experience) : (experience.Alerts & kind) == 0) continue;
            if (index++ == row) return new(kind, feedback);
        }
        return default;
    }
}
public readonly record struct AlertActivation(bool Captured, EntityId Target = default, bool OpenOperations = false,
    OperationsCategory Category = OperationsCategory.Blocked);

/// <summary>Navigation only; retains short clicks, rejects lifecycle edges and never emits simulation commands.</summary>
public sealed class ActionableAlertController
{
    private ulong _press;
    private SimulationSessionId _session;
    private GameplayHudLayout _layout;
    private bool _initialized;
    private double _feedbackSeconds;
    private bool _pending;
    private int _row;
    private AlertNotification _item;
    private AlertIdentity _identity;
    public string Feedback { get; private set; } = string.Empty;
    public void Unavailable() { Feedback = "TARGET UNAVAILABLE - OPEN OPERATIONS"; _feedbackSeconds = 3; }
    public void CancelInput(InputState input) { _press = input.MousePressSequence(PlatformMouseButton.Left); _pending = false; }
    public AlertActivation Update(InputState input, PresentationSnapshot? snapshot, in GameplayHudLayout layout, bool blocked = false, double seconds = 0)
    {
        _feedbackSeconds = Math.Max(0, _feedbackSeconds - Math.Max(0, seconds));
        if (_feedbackSeconds == 0) Feedback = string.Empty;
        ulong press = input.MousePressSequence(PlatformMouseButton.Left); bool fresh = press != _press; _press = press;
        bool changed = _initialized && (_layout != layout || _session != snapshot?.SessionId);
        _initialized = true; _layout = layout;
        if (_session != snapshot?.SessionId) { Feedback = string.Empty; _feedbackSeconds = 0; }
        _session = snapshot?.SessionId ?? default;
        if (blocked || input.FocusLostThisFrame || snapshot?.PlayerExperience is not { IsMatchComplete: false } experience)
        { _pending = false; return default; }
        bool originValid = input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin);
        bool captured = _pending || input.HasPointerPosition && ActionableAlertLayout.Contains(experience, layout, input.PointerPosition) || fresh && originValid && ActionableAlertLayout.Contains(experience, layout, origin);
        if (changed) { _pending = false; return new(captured); }
        var alerts = ActionableAlertSnapshot.Resolve(snapshot);
        if (fresh)
        {
            _pending = false; _identity = default;
            if (!originValid || !ActionableAlertLayout.Contains(experience, layout, origin)) return new(captured);
            _row = (int)((origin.Y - layout.AlertStack.Y) / (21 * ActionableAlertLayout.Scale(layout)));
            if (!ActionableAlertLayout.Row(layout, _row).Contains(origin)) return new(true);
            _item = ActionableAlertLayout.Item(experience, layout, _row);
            _pending = _item.Kind != PlayerAlertState.None || _item.Hidden > 0;
            if (alerts is not null)
                for (int i = 0; i < alerts.Alerts.Count; i++)
                    if (alerts.Alerts[i].Identity.Kind == _item.Kind) _identity = alerts.Alerts[i].Identity;
        }
        if (!_pending || input.IsMouseButtonDown(PlatformMouseButton.Left)) return new(captured);
        _pending = false;
        if (!input.HasPointerPosition || !ActionableAlertLayout.Row(layout, _row).Contains(input.PointerPosition)) return new(true);
        var item = ActionableAlertLayout.Item(experience, layout, _row);
        if (item.Kind != _item.Kind || (item.Hidden > 0) != (_item.Hidden > 0)) return new(true);
        if (item.Hidden > 0) return new(true, OpenOperations: true);
        if (item.Kind == PlayerAlertState.None) return new(true);
        if (alerts is not null)
            for (int i = 0; i < alerts.Alerts.Count; i++)
                if (alerts.Alerts[i].Identity.Kind == item.Kind)
                {
                    var alert = alerts.Alerts[i];
                    if (_identity != alert.Identity) return new(true);
                    if (alert.Target.IsValid) return new(true, alert.Target, Category: alert.Operations);
                    Unavailable(); return new(true, OpenOperations: true, Category: alert.Operations);
                }
        Feedback = "ALERT DATA UNAVAILABLE - OPEN OPERATIONS"; _feedbackSeconds = 3;
        return new(true, OpenOperations: true, Category: AlertLifecycleTracker.Category(item.Kind));
    }
}
