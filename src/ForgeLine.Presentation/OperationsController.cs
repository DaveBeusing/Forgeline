using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct OperationsView(bool Open, OperationsCategory Filter = OperationsCategory.All,
    int Page = 0, EntityId Selected = default, SimulationSessionId Session = default, bool Suppressed = false);
public readonly record struct OperationsInteraction(bool Captured, EntityId Navigate = default,
    PlayerActionPanelMode Controls = PlayerActionPanelMode.Closed);

public static class OperationsLayout
{
    public const int PageSize = 6;
    public static HudRect Entry(in GameplayHudLayout layout) => new(layout.SecondaryView.X,
        layout.SelectionInspector.Y - 20 * layout.Scale, MathF.Min(126 * layout.Scale, layout.SelectionInspector.Width), 16 * layout.Scale);
    public static HudRect Panel(in GameplayHudLayout layout) => new(layout.SafeArea.X, layout.TopStatusBar.Bottom + 8 * layout.Scale,
        MathF.Min(450 * layout.Scale, MathF.Max(0, layout.ActionDock.X - layout.SafeArea.X - 8 * layout.Scale)),
        MathF.Max(0, layout.SelectionInspector.Y - layout.TopStatusBar.Bottom - 36 * layout.Scale));
    public static float Scale(in GameplayHudLayout layout) { var p = Panel(layout); return MathF.Min(layout.Scale, MathF.Min(p.Width / 450, p.Height / 346)); }
    public static HudRect Control(in GameplayHudLayout layout, int index)
    {
        var p = Panel(layout); float s = Scale(layout);
        if (s <= 0) return default;
        return index switch
        {
            0 => new(p.X + 366 * s, p.Y + 6 * s, 78 * s, 16 * s),
            1 => new(p.X + 6 * s, p.Y + 104 * s, 210 * s, 16 * s),
            2 => new(p.X + 222 * s, p.Y + 104 * s, 222 * s, 16 * s),
            >= 3 and <= 8 => new(p.X + 6 * s, p.Y + (126 + (index - 3) * 22) * s, 438 * s, 20 * s),
            9 => new(p.X + 6 * s, p.Y + 322 * s, 210 * s, 18 * s),
            11 => new(p.X + 6 * s, p.Y + 292 * s, 438 * s, 18 * s),
            _ => new(p.X + 222 * s, p.Y + 322 * s, 222 * s, 18 * s)
        };
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
    private SimulationSessionId _session;
    private GameplayHudLayout _layout;
    private bool _initialized;
    private EntityId _lastRouteFocus;
    public OperationsView View { get; private set; }
    public void CancelInput(InputState input) => _press = input.MousePressSequence(PlatformMouseButton.Left);
    public OperationsInteraction Update(InputState input, PresentationSnapshot? snapshot, in GameplayHudLayout layout,
        PresentationInteractionState interaction, bool blocked = false)
    {
        ulong press = input.MousePressSequence(PlatformMouseButton.Left);
        bool fresh = press != _press; _press = press;
        bool transition = _initialized && (_layout != layout || _session != snapshot?.SessionId);
        bool newSession = _session != snapshot?.SessionId;
        _initialized = true; _layout = layout; _session = snapshot?.SessionId ?? default;
        if (newSession || snapshot?.PlayerExperience?.IsMatchComplete == true)
        { View = default; _lastRouteFocus = default; interaction.SetOperationsOpen(false); }
        View = View with { Suppressed = blocked || input.FocusLostThisFrame };
        if (View.Suppressed)
        {
            interaction.SetOperationsOpen(false);
            var tab = OperationsLayout.Entry(layout);
            return new(input.HasPointerPosition && tab.Contains(input.PointerPosition));
        }
        interaction.SetOperationsOpen(View.Open);
        var entry = OperationsLayout.Entry(layout);
        var panel = OperationsLayout.Panel(layout);
        bool captured = input.HasPointerPosition && (entry.Contains(input.PointerPosition) || View.Open && panel.Contains(input.PointerPosition));
        bool originValid = input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin);
        captured |= fresh && originValid && (entry.Contains(origin) || View.Open && panel.Contains(origin));
        if (transition || !fresh || !input.HasPointerPosition || !originValid || snapshot?.PlayerExperience is null ||
            !snapshot.SessionId.IsSpecified || snapshot.PlayerExperience.Value.IsMatchComplete) return new(captured);
        if (entry.Contains(origin))
        {
            View = new(!View.Open, Session: _session); interaction.SetOperationsOpen(View.Open); return new(true);
        }
        if (!View.Open && snapshot?.PlayerExperience is { } experience && !experience.IsMatchComplete &&
            (layout.TopStatusBar.Contains(origin) || layout.AlertStack.Contains(origin) ||
                layout.SelectionInspector.Contains(origin) && experience.Selection.HasSingleEntityDetails &&
                (experience.Selection.HasInventory || experience.Selection.HasSupply || experience.Selection.Work.Kind != PlayerWorkKind.None)))
        {
            var filter = layout.AlertStack.Contains(origin) ? OperationsCategory.Blocked :
                layout.SelectionInspector.Contains(origin) ? experience.Selection.HasSupply ? OperationsCategory.Supply :
                    experience.Selection.Work.Kind != PlayerWorkKind.None ? OperationsCategory.Production : OperationsCategory.Logistics : OperationsCategory.All;
            View = new(true, filter, Session: _session); interaction.SetOperationsOpen(true); return new(true);
        }
        if (!View.Open || !panel.Contains(origin)) return new(captured);
        if (OperationsLayout.Control(layout, 0).Contains(origin))
        { View = default; interaction.SetOperationsOpen(false); return new(true); }
        if (OperationsSnapshot.Resolve(snapshot) is not { } data) return new(true);
        for (int index = 1; index <= 11; index++)
        {
            if (!OperationsLayout.Control(layout, index).Contains(origin)) continue;
            if (index == 1) View = View with { Filter = (OperationsCategory)(((int)View.Filter + 1) % 6), Page = 0, Selected = default };
            else if (index == 2) View = View with { Page = OperationsLayout.Row(data, View with { Page = View.Page + 1 }, 0) is null ? 0 : View.Page + 1, Selected = default };
            else if (index is >= 3 and <= 8 && OperationsLayout.Row(data, View, index - 3) is { } row)
                View = View with { Selected = row.Entity };
            else if (index == 11)
            {
                EntityId first = default, next = default;
                foreach (var route in data.Routes)
                {
                    EntityId target = route.Source == View.Selected ? route.Destination : route.Destination == View.Selected ? route.Source : default;
                    if (!target.IsValid) continue;
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
                        return new(true, facility.Entity, index == 10 ? facility.Controls : PlayerActionPanelMode.Closed);
            }
            break;
        }
        return new(true);
    }
}
