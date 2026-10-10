using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct AlertNotification(PlayerAlertState Kind, bool Feedback = false, int Hidden = 0);
public static class ActionableAlertLayout
{
    public static HudRect Row(in GameplayHudLayout layout, int row) => new(layout.AlertStack.X,
        layout.AlertStack.Y + row * 21 * layout.Scale, layout.AlertStack.Width, 21 * layout.Scale);
    public static AlertNotification Item(in PlayerExperienceSnapshot experience, in GameplayHudLayout layout, int row)
    {
        int maximum = (int)(layout.AlertStack.Height / (21 * layout.Scale));
        int total = ResourcePowerHudModel.ResolveNotificationCount(experience);
        if (row < 0 || row >= maximum || row >= total) return default;
        int visible = total > maximum && maximum > 1 ? maximum - 1 : maximum;
        if (row == visible && total > visible) return new(default, Hidden: total - visible);
        int index = 0;
        for (int slot = 0; slot < 6; slot++)
        {
            bool feedback = slot == 2;
            var kind = feedback ? default : AlertLifecycleTracker.Kind(slot < 2 ? slot : slot - 1);
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
    public string Feedback { get; private set; } = string.Empty;
    public void Unavailable() { Feedback = "TARGET UNAVAILABLE - OPEN OPERATIONS"; _feedbackSeconds = 3; }
    public void CancelInput(InputState input) => _press = input.MousePressSequence(PlatformMouseButton.Left);
    public AlertActivation Update(InputState input, PresentationSnapshot? snapshot, in GameplayHudLayout layout, bool blocked = false, double seconds = 0)
    {
        _feedbackSeconds = Math.Max(0, _feedbackSeconds - Math.Max(0, seconds));
        if (_feedbackSeconds == 0) Feedback = string.Empty;
        ulong press = input.MousePressSequence(PlatformMouseButton.Left); bool fresh = press != _press; _press = press;
        bool changed = _initialized && (_layout != layout || _session != snapshot?.SessionId);
        _initialized = true; _layout = layout;
        if (_session != snapshot?.SessionId) { Feedback = string.Empty; _feedbackSeconds = 0; }
        _session = snapshot?.SessionId ?? default;
        if (blocked || input.FocusLostThisFrame || snapshot?.PlayerExperience is not { IsMatchComplete: false } experience) return default;
        bool originValid = input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin);
        bool captured = input.HasPointerPosition && layout.AlertStack.Contains(input.PointerPosition) || fresh && originValid && layout.AlertStack.Contains(origin);
        if (!fresh || changed || !originValid || !layout.AlertStack.Contains(origin) || !input.HasPointerPosition || !layout.AlertStack.Contains(input.PointerPosition)) return new(captured);
        int row = (int)((origin.Y - layout.AlertStack.Y) / (21 * layout.Scale));
        if (!ActionableAlertLayout.Row(layout, row).Contains(input.PointerPosition)) return new(true);
        var item = ActionableAlertLayout.Item(experience, layout, row);
        if (item.Hidden > 0) return new(true, OpenOperations: true);
        if (item.Kind == PlayerAlertState.None) return new(true);
        if (ActionableAlertSnapshot.Resolve(snapshot) is { } alerts)
            for (int i = 0; i < alerts.Alerts.Count; i++)
                if (alerts.Alerts[i].Identity.Kind == item.Kind)
                {
                    var alert = alerts.Alerts[i];
                    if (alert.Target.IsValid) return new(true, alert.Target, Category: alert.Operations);
                    Unavailable(); return new(true, OpenOperations: true, Category: alert.Operations);
                }
        Feedback = "ALERT DATA UNAVAILABLE - OPEN OPERATIONS"; _feedbackSeconds = 3;
        return new(true, OpenOperations: true, Category: AlertLifecycleTracker.Category(item.Kind));
    }
}
