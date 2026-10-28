using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct GlobalProductionView(GlobalProductionFilter Filter = GlobalProductionFilter.All,
    int Page = 0, EntityId Factory = default, EntityId Request = default, int DetailPage = 0,
    bool Collapsed = false, int Focus = 1, string Feedback = "");
public readonly record struct GlobalProductionCommand(EntityId Factory, EntityId Request,
    bool Cancel, ProductionPriority Priority);
internal readonly record struct GlobalProductionInteraction(bool Captured = false, bool Close = false,
    bool Back = false, EntityId Navigate = default, bool Controls = false, bool Rally = false);

public static class GlobalProductionLayout
{
    public const int PageSize = 6;
    public static HudRect Panel(in GameplayHudLayout layout, bool collapsed) => OperationsLayout.Panel(layout) with
    { Height = collapsed ? MathF.Min(OperationsLayout.Panel(layout).Height, 58 * OperationsLayout.Scale(layout)) : OperationsLayout.Panel(layout).Height };
    public static HudRect Control(in GameplayHudLayout layout, int index)
    {
        var p = OperationsLayout.Panel(layout); float s = OperationsLayout.Scale(layout), x = MathF.Min(layout.Scale, p.Width / 450);
        if (s <= 0 || x <= 0) return default;
        return index switch
        {
            0 => new(p.X + 366*x, p.Y + 6*s, 78*x, 22*s),
            26 => new(p.X + 6*x, p.Y + 6*s, 108*x, 22*s),
            3 => new(p.X + 264*x, p.Y + 6*s, 96*x, 22*s),
            1 => new(p.X + 6*x, p.Y + 58*s, 210*x, 22*s),
            2 => new(p.X + 222*x, p.Y + 58*s, 108*x, 22*s),
            4 => new(p.X + 336*x, p.Y + 58*s, 108*x, 22*s),
            >= 10 and < 16 => new(p.X + 6*x, p.Y + (86 + (index-10)*24)*s, 438*x, 22*s),
            >= 20 and <= 25 => new(p.X + (6 + (index-20)%3*148)*x, p.Y + (302 + (index-20)/3*28)*s, 142*x, 22*s),
            _ => default
        };
    }
    public static GlobalProductionFactory? Row(GlobalProductionSnapshot? data, in GlobalProductionView view,
        int row, IReadOnlyCollection<EntityId>? selection)
    {
        if (data is null) return null;
        long skip = (long)Math.Max(0, view.Page) * PageSize + row;
        for (int i = 0; i < data.Factories.Count; i++)
            if (GlobalProductionSnapshot.Matches(data.Factories[i], view.Filter, selection) && skip-- == 0) return data.Factories[i];
        return null;
    }
    public static GlobalProductionOrder? Order(GlobalProductionFactory? factory, EntityId request)
    {
        if (factory is null) return null;
        for (int i = 0; i < factory.Orders.Count; i++) if (factory.Orders[i].Request == request) return factory.Orders[i];
        return null;
    }
}

internal sealed class GlobalProductionController
{
    private static readonly int[] FocusOrder = [1,2,4,10,11,12,13,14,15,20,21,22,23,24,25,3,26,0];
    private readonly GameplayBindingRegistry _bindings;
    private GlobalProductionCommand? _pending;
    private ulong _submission;
    private PlayerCommandCorrelationId _correlation;
    private SimulationSessionId _resultSession;
    private bool _armed, _initialized;
    private int _control;
    private EntityId _factory, _request, _row;
    private GlobalProductionFilter _filter;
    private GameplayHudLayout _layout;
    private SimulationSessionId _session;
    private ulong _press;
    internal GlobalProductionController(GameplayBindingRegistry bindings) => _bindings = bindings;
    internal GlobalProductionView View { get; private set; } = new(Focus: 1, Feedback: "");
    internal void Open() { View = new(Focus: 1, Feedback: ""); _armed = false; _pending = null; _initialized = false; _submission = 0; _correlation = default; }
    internal void Close() { _armed = false; _pending = null; _submission = 0; _correlation = default; }
    internal void CancelInput() => _armed = false;
    internal bool TryTakeCommand(out GlobalProductionCommand command)
    { command = _pending ?? default; bool found = _pending.HasValue; _pending = null; return found; }
    internal void Submitted(bool success, ulong sequence)
    {
        _submission = success ? sequence : 0; _correlation = default;
        View = View with { Feedback = success ? "SUBMITTED / WAIT FOR AUTHORITATIVE QUEUE" : "NOT SUBMITTED / RETRY" };
    }
    internal void ObserveSubmission(ulong sequence, PlayerCommandSubmissionReceipt? receipt)
    {
        if (_submission == 0 || sequence != _submission) return;
        _submission = 0;
        if (receipt is { Accepted: true } value) { _correlation = value.CorrelationId; _resultSession = value.SessionId; }
        else View = View with { Feedback = "NOT SUBMITTED / RETRY" };
    }
    internal void ObserveResult(in PlayerCommandResultReadModel result)
    {
        if (!_correlation.IsSpecified || result.SessionId != _resultSession || result.CorrelationId != _correlation) return;
        _correlation = default;
        View = View with { Feedback = result.State == PlayerCommandFeedbackState.Accepted ? "ACCEPTED / CURRENT QUEUE ABOVE" : "REJECTED / REVIEW CURRENT QUEUE" };
    }

    internal GlobalProductionInteraction Update(InputState input, PresentationSnapshot? snapshot, in GameplayHudLayout layout,
        IReadOnlyCollection<EntityId>? selection, bool blocked)
    {
        var panel = GlobalProductionLayout.Panel(layout, View.Collapsed);
        ulong press = input.MousePressSequence(PlatformMouseButton.Left);
        bool fresh = press != _press; _press = press;
        bool captured = input.HasPointerPosition && panel.Contains(input.PointerPosition);
        bool transition = _initialized && (_layout != layout || _session != snapshot?.SessionId);
        _initialized = true; _layout = layout; _session = snapshot?.SessionId ?? default;
        if (blocked || transition || input.FocusLostThisFrame || panel.IsEmpty)
        { _armed = false; return new(captured); }
        var data = GlobalProductionSnapshot.Resolve(snapshot);
        var selected = data?.Find(View.Factory);
        if (selected is not null && !GlobalProductionSnapshot.Matches(selected, View.Filter, selection)) selected = null;
        if (selected is null && View.Factory.IsValid)
            View = View with { Factory = default, Request = default, Feedback = "FACTORY UNAVAILABLE IN THIS VIEW" };
        if (selected is not null && GlobalProductionLayout.Order(selected, View.Request) is null)
            View = View with { Request = selected.Orders.Count > 0 ? selected.Orders[0].Request : default, DetailPage = 0 };
        if (GlobalProductionLayout.Row(data, View, 0, selection) is null && View.Page > 0) View = View with { Page = 0 };

        if (!View.Collapsed && input.WasKeyPressed(_bindings.Key(GameplayAction.NextItem)))
        {
            int index = Array.IndexOf(FocusOrder, View.Focus);
            do { index = (index + 1) % FocusOrder.Length; }
            while (View.Collapsed && FocusOrder[index] is not (0 or 3 or 26));
            View = View with { Focus = FocusOrder[index] };
        }
        if (!View.Collapsed && input.WasKeyPressed(_bindings.Key(GameplayAction.Activate)))
        {
            var result = Activate(View.Focus, snapshot!, data, selection);
            input.SuppressHeldInput();
            return result with { Captured = true };
        }
        if (fresh && input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin))
        {
            _armed = false;
            foreach (int control in FocusOrder)
                if ((!View.Collapsed || control is 0 or 3 or 26) && GlobalProductionLayout.Control(layout, control).Contains(origin))
                {
                    _armed = true; _control = control; _factory = View.Factory; _request = View.Request; _filter = View.Filter;
                    _row = control is >= 10 and < 16 ? GlobalProductionLayout.Row(data, View, control-10, selection)?.Entity ?? default : default;
                    View = View with { Focus = control };
                    break;
                }
        }
        captured |= _armed;
        if (!input.WasMouseButtonReleased(PlatformMouseButton.Left)) return new(captured);
        bool activate = _armed && input.HasPointerPosition && GlobalProductionLayout.Control(layout, _control).Contains(input.PointerPosition);
        _armed = false;
        if (!activate || _factory != View.Factory || _request != View.Request || _filter != View.Filter) return new(captured);
        if (_control is >= 10 and < 16 && _row != GlobalProductionLayout.Row(data, View, _control-10, selection)?.Entity) return new(true);
        return Activate(_control, snapshot!, data, selection) with { Captured = true };
    }

    private GlobalProductionInteraction Activate(int control, PresentationSnapshot snapshot, GlobalProductionSnapshot? data,
        IReadOnlyCollection<EntityId>? selection)
    {
        if (View.Collapsed && control is not (0 or 3 or 26)) return default;
        if (control == 0) return new(Close:true);
        if (control == 26) return new(Back:true);
        if (control == 3) { View = View with { Collapsed = !View.Collapsed, Focus = 3 }; return default; }
        if (data is null) return default;
        if (control == 1)
        { View = View with { Filter = (GlobalProductionFilter)(((int)View.Filter+1)%6), Page=0, Factory=default, Request=default, Feedback="" }; return default; }
        if (control == 2)
        {
            int next = View.Page == int.MaxValue ? 0 : View.Page + 1;
            View = View with { Page = GlobalProductionLayout.Row(data, View with { Page = next }, 0, selection) is null ? 0 : next, Factory=default, Request=default, Feedback="" };
            return default;
        }
        if (control is >= 10 and < 16 && GlobalProductionLayout.Row(data, View, control-10, selection) is { } row)
        {
            View = View with { Factory=row.Entity, Request=row.Orders.Count>0?row.Orders[0].Request:default, DetailPage=0, Feedback="" };
            return default;
        }
        var factory = data.Find(View.Factory);
        if (factory is null) return default;
        var order = GlobalProductionLayout.Order(factory, View.Request);
        if (control == 4)
        { View = View with { DetailPage = (View.DetailPage+1) % (2 + (order?.Materials.Count ?? 0)), Feedback="" }; return default; }
        if (control == 22)
        {
            for (int i=0;i<factory.Orders.Count;i++)
                if (factory.Orders[i].Request == View.Request)
                { View=View with { Request=factory.Orders[(i+1)%factory.Orders.Count].Request, DetailPage=0, Feedback="" }; break; }
            return default;
        }
        if (control is 20 or 21 or 25)
        {
            if (!OperationsWorldOverlay.CanFocus(snapshot, factory.Entity))
            { View=View with { Feedback="FACTORY NOT CURRENTLY VISIBLE / CANNOT TARGET" }; return default; }
            return new(Navigate:factory.Entity, Controls:control==21, Rally:control==25);
        }
        if (control is 23 or 24 && order is not null)
        {
            _pending = new(factory.Entity, order.Request, control==23,
                (ProductionPriority)(((int)order.Priority + 1) % Enum.GetValues<ProductionPriority>().Length));
            View=View with { Feedback="LOCAL INTENT / AWAITING SUBMISSION" };
        }
        return default;
    }
}
