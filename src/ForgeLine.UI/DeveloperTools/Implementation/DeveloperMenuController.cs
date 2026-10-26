using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;

namespace ForgeLine.UI;

/// <summary>Owns menu input before all world controllers. Press edges submit once; lifetime changes suppress held inputs.</summary>
public sealed class DeveloperMenuController : IDisposable
{
    private readonly FrontendInputLifetime _lifetime = new();
    private IReadOnlyList<DeveloperMenuRow> _rows = Array.Empty<DeveloperMenuRow>();
    private int _focus, _scroll;
    private long _stamp = long.MinValue;
    public DeveloperMenuController(DeveloperMenuModel model) => Model = model;
    public DeveloperMenuModel Model { get; }
    public bool Visible => Model.Visible;
    public bool Open(InputState input)
    {
        bool opened = Model.Open(); _focus = 0; _scroll = 0; _stamp = long.MinValue;
        if (opened) input.SuppressHeldInput();
        return opened;
    }
    public void Update(InputState input, FrontendLayout layout, bool focused)
    {
        _lifetime.Update(input, layout.Geometry, Visible ? 1 : 0, focused);
        if (!Visible || !focused || layout.Geometry.IsEmpty) return;
        Rebuild();
        bool click = input.TryGetMousePressPosition(PlatformMouseButton.Left, out var pointer);
        if (input.WasKeyPressed(PlatformKey.Escape) || click && FrontendHitTesting.SecondaryAction(pointer.X, pointer.Y, layout))
        { Model.Close(); input.SuppressHeldInput(); return; }
        if (input.WasKeyPressed(PlatformKey.Up) || input.WheelDelta > 0) _focus = Math.Max(0, _focus - 1);
        if (input.WasKeyPressed(PlatformKey.Down) || input.WheelDelta < 0) _focus = Math.Min(_rows.Count - 1, _focus + 1);
        if (click && FrontendHitTesting.DetailRow(pointer.X, pointer.Y, layout, Math.Min(_rows.Count - _scroll, layout.Geometry.VisibleDetailRows)) is int row)
        {
            _focus = row + _scroll;
            if (FrontendHitTesting.DetailAdjust(pointer.X, pointer.Y, layout, layout.Geometry.VisibleDetailRows) is int direction)
                _rows[_focus].Adjust?.Invoke(direction);
            else _rows[_focus].Activate?.Invoke();
            _stamp = long.MinValue;
        }
        else if (_rows.Count != 0)
        {
            if (input.WasKeyPressed(PlatformKey.Left)) { _rows[_focus].Adjust?.Invoke(-1); _stamp = long.MinValue; }
            if (input.WasKeyPressed(PlatformKey.Right)) { _rows[_focus].Adjust?.Invoke(1); _stamp = long.MinValue; }
            if (input.WasKeyPressed(PlatformKey.Enter)) { _rows[_focus].Activate?.Invoke(); _stamp = long.MinValue; }
        }
        Rebuild();
        int capacity = layout.Geometry.VisibleDetailRows;
        if (_focus < _scroll) _scroll = _focus;
        if (_focus >= _scroll + capacity) _scroll = Math.Max(0, _focus - capacity + 1);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _rows.Count - capacity));
        // World input remains blocked even if the session adapter has resumed simulation.
        input.SuppressHeldInput();
    }
    private void Rebuild()
    {
        var hash = new HashCode(); hash.Add(Model.Section); hash.Add(Model.Feedback); hash.Add(Model.HostPaused);
        hash.Add(Model.Inspector); hash.Add(Model.Observation?.Tick); hash.Add(Model.Observation?.Loading); hash.Add(Model.WriteBlocked);
        hash.Add(Model.Debug.View); hash.Add(Model.Providers.Menu.Status); hash.Add(Model.Providers.Sessions.Status);
        hash.Add(Model.Providers.Actions.Status); hash.Add(Model.Providers.Parameters.Status); hash.Add(Model.Providers.Presets.Status);
        foreach (var draft in Model.Drafts) { hash.Add(draft.Observed); hash.Add(draft.Value); hash.Add(draft.NeedsReview); }
        long stamp = hash.ToHashCode();
        if (_stamp == stamp) return;
        _rows = DeveloperMenuView.Build(Model); _stamp = stamp;
        _focus = Math.Clamp(_focus, 0, Math.Max(0, _rows.Count - 1));
    }
    public void Dispose() => Model.Close();
    public FrontendSurfaceView Surface(FrontendLayout layout)
    { Rebuild(); return DeveloperMenuView.Surface(_rows, _focus, _scroll, layout.Geometry.VisibleDetailRows, Model.Feedback); }
}
