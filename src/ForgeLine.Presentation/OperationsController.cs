using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct OperationsView(bool Open, OperationsCategory Filter = OperationsCategory.All,
    int Page = 0, EntityId Selected = default, SimulationSessionId Session = default, bool Suppressed = false, bool Notifications = false);
public readonly record struct OperationsInteraction(bool Captured, EntityId Navigate = default,
    PlayerActionPanelMode Controls = PlayerActionPanelMode.Closed);

public static class OperationsLayout
{
    public const int PageSize = 6;
    public static HudRect Panel(in GameplayHudLayout layout)
    {
        float gap = 8 * MathF.Min(layout.Scale, 1.25f);
        float y = layout.TopStatusBar.Bottom + gap;
        float height = MathF.Max(0, layout.SafeArea.Bottom - y - gap);
        float width = MathF.Min(450 * layout.Scale,
            MathF.Max(0, layout.TopStatusBar.X - layout.SafeArea.X - gap));
        float panelHeight = MathF.Min(height, 374 * layout.Scale);
        return new(layout.SafeArea.X, y, width, panelHeight);
    }
    // Narrow columns fit horizontally without shrinking text and row height.
    public static float Scale(in GameplayHudLayout layout) { var p = Panel(layout); return p.IsEmpty ? 0 : MathF.Min(layout.Scale, p.Height / 374); }
    public static HudRect Control(in GameplayHudLayout layout, int index)
    {
        var p = Panel(layout); float s = Scale(layout), x = MathF.Min(layout.Scale, p.Width / 450);
        if (s <= 0) return default;
        return index switch
        {
            12 => new(p.X + 222 * x, p.Y + 6 * s, 138 * x, 22 * s),
            0 => new(p.X + 366 * x, p.Y + 6 * s, 78 * x, 22 * s),
            1 => new(p.X + 6 * x, p.Y + 120 * s, 210 * x, 22 * s),
            2 => new(p.X + 222 * x, p.Y + 120 * s, 222 * x, 22 * s),
            >= 3 and <= 8 => new(p.X + 6 * x, p.Y + (146 + (index - 3) * 22) * s, 438 * x, 22 * s),
            9 => new(p.X + 6 * x, p.Y + 346 * s, 210 * x, 22 * s),
            11 => new(p.X + 6 * x, p.Y + 316 * s, 438 * x, 22 * s),
            _ => new(p.X + 222 * x, p.Y + 346 * s, 222 * x, 22 * s)
        };
    }
    public static float NotificationTextSize(in GameplayHudLayout layout) => 14 * ActionableAlertLayout.Scale(layout);
    public static HudRect NotificationControl(in GameplayHudLayout layout, int index)
    {
        var panel = Panel(layout); float size = NotificationTextSize(layout);
        float line = UiFontMetrics.Default.Description.LineHeight / UiFontMetrics.Default.Description.PixelSize * size;
        float height = line + 8, half = MathF.Max(0, (panel.Width - 18) / 2);
        return index switch
        {
            0 => new(panel.Right - MathF.Min(90 * ActionableAlertLayout.Scale(layout), panel.Width * .3f) - 6, panel.Y + 4,
                MathF.Min(90 * ActionableAlertLayout.Scale(layout), panel.Width * .3f), height),
            1 => new(panel.X + 6, panel.Bottom - height - 6, half, height),
            2 => new(panel.X + 12 + half, panel.Bottom - height - 6, half, height),
            _ => new(panel.X + 8, panel.Y + height + 12 + (index - 3) * (line + 4),
                MathF.Max(0, panel.Width - 16), line + 4)
        };
    }
    public static int NotificationRows(in GameplayHudLayout layout)
    {
        var first = NotificationControl(layout, 3); var footer = NotificationControl(layout, 1);
        return Math.Clamp((int)((footer.Y - 4 - first.Y) / MathF.Max(1, first.Height)), 0, PageSize);
    }

    public static OperationsFacility? Row(OperationsSnapshot snapshot, in OperationsView view, int index)
    {
        int skip = Math.Max(0, view.Page) * PageSize + index;
        for (int rowIndex = 0; rowIndex < snapshot.Facilities.Count; rowIndex++)
        {
            var row = snapshot.Facilities[rowIndex];
            if (view.Filter == OperationsCategory.All || row.Category == view.Filter ||
                view.Filter == OperationsCategory.Power && (row.PowerDemand.HasValue || row.GenerationCapacity.HasValue) ||
                view.Filter == OperationsCategory.Blocked && row.Cause.Length > 0)
            { if (skip-- == 0) return row; }
        }
        return null;
    }
}

/// <summary>Nonmodal navigation and existing action-dock entry; never submits economic commands.</summary>
public sealed class OperationsController
{
    private ulong _press;
    private bool _armed;
    private ulong _armedPress;
    private System.Numerics.Vector2 _armedOrigin;
    private OperationsView _armedView;
    private int _armedRowIndex = -1;
    private EntityId _armedRowEntity;
    private SimulationSessionId _session;
    private GameplayHudLayout _layout;
    private bool _initialized;
    private EntityId _lastRouteFocus;
    private bool _consumeLeftGesture;
    public OperationsView View { get; private set; }
    public void OpenCategory(OperationsCategory category, SimulationSessionId session, PresentationInteractionState interaction)
    { _armed = false; View = new(true, category, Session: session); interaction.SetOperationsOpen(true); }
    public bool ContainsSurface(System.Numerics.Vector2 point, in GameplayHudLayout layout) =>
        View.Open && !View.Suppressed && OperationsLayout.Panel(layout).Contains(point);
    public void Close(InputState input, PresentationInteractionState interaction)
    {
        View = default; _lastRouteFocus = default;
        CancelInput(input); interaction.SetOperationsOpen(false);
    }
    public void OpenNotifications(SimulationSessionId session, PresentationInteractionState interaction)
    { _armed = false; View = new(true, Session: session, Notifications: true); interaction.SetOperationsOpen(true); }
    public void CancelInput(InputState input)
    {
        _consumeLeftGesture |= _armed && input.IsMouseButtonDown(PlatformMouseButton.Left);
        _press = input.MousePressSequence(PlatformMouseButton.Left); _armed = false;
    }
    public OperationsInteraction Update(InputState input, PresentationSnapshot? snapshot, in GameplayHudLayout layout,
        PresentationInteractionState interaction, bool blocked = false, bool inputCaptured = false)
    {
        ulong press = input.MousePressSequence(PlatformMouseButton.Left);
        bool fresh = press != _press; _press = press;
        bool consumedGesture = _consumeLeftGesture;
        if (fresh && input.TryGetMousePressPosition(PlatformMouseButton.Left, out var capturedOrigin))
        {
            _consumeLeftGesture = View.Open
                ? OperationsLayout.Panel(layout).Contains(capturedOrigin) ||
                    _initialized && OperationsLayout.Panel(_layout).Contains(capturedOrigin)
                : layout.TopStatusBar.Contains(capturedOrigin);
            consumedGesture |= _consumeLeftGesture;
        }
        if (!input.IsMouseButtonDown(PlatformMouseButton.Left)) _consumeLeftGesture = false;
        bool transition = _initialized && (_layout != layout || _session != snapshot?.SessionId);
        bool newSession = _session != snapshot?.SessionId;
        _initialized = true; _layout = layout; _session = snapshot?.SessionId ?? default;
        if (newSession || snapshot?.PlayerExperience?.IsMatchComplete == true)
        {
            View = default; _lastRouteFocus = default; _armed = false;
            interaction.SetOperationsOpen(false);
        }
        View = View with { Suppressed = blocked || input.FocusLostThisFrame };
        if (View.Suppressed)
        {
            _armed = false;
            interaction.SetOperationsOpen(false);
            return new(consumedGesture);
        }
        interaction.SetOperationsOpen(View.Open);
        var panel = OperationsLayout.Panel(layout);
        bool captured = consumedGesture || input.HasPointerPosition && View.Open && panel.Contains(input.PointerPosition);
        if (inputCaptured || transition || snapshot?.PlayerExperience is null ||
            !snapshot.SessionId.IsSpecified || snapshot.PlayerExperience.Value.IsMatchComplete)
        {
            _armed = false;
            return new(captured);
        }

        if (fresh && input.TryGetMousePressPosition(PlatformMouseButton.Left, out var pressOrigin))
        {
            _armed = View.Open ? panel.Contains(pressOrigin) : layout.TopStatusBar.Contains(pressOrigin);
            _armedPress = press;
            _armedOrigin = pressOrigin;
            _armedView = View;
            _armedRowIndex = -1;
            _armedRowEntity = default;
            if (_armed && View.Open && !View.Notifications &&
                OperationsSnapshot.Resolve(snapshot) is { } pressedData)
                for (int index = 3; index <= 8; index++)
                    if (OperationsLayout.Control(layout, index).Contains(pressOrigin))
                    {
                        _armedRowIndex = index;
                        _armedRowEntity = OperationsLayout.Row(pressedData, View, index - 3)?.Entity ?? default;
                        break;
                    }
        }
        captured |= _armed && View.Open && panel.Contains(_armedOrigin);
        if (!input.WasMouseButtonReleased(PlatformMouseButton.Left)) return new(captured);

        bool activate = _armed && _armedPress == press && _armedView == View && input.HasPointerPosition;
        var origin = _armedOrigin;
        _armed = false;
        if (!activate) return new(captured);
        var release = input.PointerPosition;

        if (!View.Open)
        {
            if (layout.TopStatusBar.Contains(origin) && layout.TopStatusBar.Contains(release))
            {
                View = new(true, Session: _session);
                interaction.SetOperationsOpen(true);
                return new(true);
            }
            return new(captured);
        }
        if (!panel.Contains(origin) || !panel.Contains(release)) return new(captured);
        if (View.Notifications)
        {
            if (OperationsLayout.NotificationControl(layout, 0).Contains(origin) &&
                OperationsLayout.NotificationControl(layout, 0).Contains(release))
            { View = default; interaction.SetOperationsOpen(false); }
            else if (OperationsLayout.NotificationControl(layout, 1).Contains(origin) &&
                     OperationsLayout.NotificationControl(layout, 1).Contains(release))
                View = View with { Notifications = false, Page = 0 };
            else if (OperationsLayout.NotificationControl(layout, 2).Contains(origin) &&
                     OperationsLayout.NotificationControl(layout, 2).Contains(release))
                View = View with { Page = View.Page == int.MaxValue ? 0 : View.Page + 1 };
            return new(true);
        }
        if (OperationsLayout.Control(layout, 0).Contains(origin) &&
            OperationsLayout.Control(layout, 0).Contains(release))
        { View = default; interaction.SetOperationsOpen(false); return new(true); }
        if (OperationsLayout.Control(layout, 12).Contains(origin) &&
            OperationsLayout.Control(layout, 12).Contains(release))
        { View = View with { Notifications = true, Page = 0 }; return new(true); }
        if (OperationsSnapshot.Resolve(snapshot) is not { } data) return new(true);
        for (int index = 1; index <= 11; index++)
        {
            var control = OperationsLayout.Control(layout, index);
            if (!control.Contains(origin) || !control.Contains(release)) continue;
            if (index == 1) View = View with { Filter = (OperationsCategory)(((int)View.Filter + 1) % 6), Page = 0, Selected = default };
            else if (index == 2) View = View with { Page = OperationsLayout.Row(data, View with { Page = View.Page + 1 }, 0) is null ? 0 : View.Page + 1, Selected = default };
            else if (index is >= 3 and <= 8 && OperationsLayout.Row(data, View, index - 3) is { } row &&
                     _armedRowIndex == index && _armedRowEntity == row.Entity)
                View = View with { Selected = row.Entity };
            else if (index == 11)
            {
                if (!OperationsWorldOverlay.CanFocus(snapshot, View.Selected)) return new(true);
                EntityId first = default, next = default;
                foreach (var route in data.Routes)
                {
                    EntityId target = route.Source == View.Selected ? route.Destination : route.Destination == View.Selected ? route.Source : default;
                    if (!OperationsWorldOverlay.CanFocus(snapshot, target)) continue;
                    if (!first.IsValid || target.CompareTo(first) < 0) first = target;
                    if (target.CompareTo(_lastRouteFocus) > 0 && (!next.IsValid || target.CompareTo(next) < 0)) next = target;
                }
                _lastRouteFocus = next.IsValid ? next : first;
                return new(true, _lastRouteFocus);
            }
            else if (index is 9 or 10)
            {
                foreach (var facility in data.Facilities)
                    if (facility.Entity == View.Selected)
                    {
                        if (index == 10 && facility.Controls == PlayerActionPanelMode.Closed) return new(true);
                        EntityId target = LogisticsStatusCatalog.IsBlocked(facility.Service) &&
                            facility.Service.VehicleQueue is { WaitReason: not VehicleQueueWaitReason.Unavailable } queue
                            ? queue.Building : facility.Entity;
                        if (!OperationsWorldOverlay.CanFocus(snapshot, target)) return new(true);
                        return new(true, target, index == 10 ? facility.Controls : PlayerActionPanelMode.Closed);
                    }
            }
            break;
        }
        return new(true);
    }
}
