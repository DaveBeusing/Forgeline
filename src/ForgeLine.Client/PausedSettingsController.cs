using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.UI;

namespace ForgeLine.Client;

internal sealed class PausedSettingsController : IDisposable
{
    private readonly IWindow _window;
    private readonly ClientSettingsFrontendAdapter _adapter;
    private readonly ClientDisplayPreview _display;
    private ClientUserSettings _current;
    private ulong _presentedBeforeTrial;
    private string _feedback = "UI, CAMERA AND SOUND APPLY NOW. GRAPHICS AND BINDINGS APPLY NEXT MATCH.";
    internal SettingsModel Model { get; private set; }
    internal SettingsInteractionModel Interaction { get; private set; } = new();
    internal bool Visible { get; private set; }
    internal FrontendSurfaceView Surface => FrontendPresentationAdapter.Settings(Model, Interaction,
        _display.Active ? $"KEEP DISPLAY / {_display.RemainingSeconds}s" : _feedback, _display.Active)
        with { SecondaryAction = "BACK TO PAUSE" };

    internal PausedSettingsController(IWindow window, ClientUserSettings current,
        ClientSettingsFrontendAdapter adapter, Func<double>? seconds = null)
    {
        _window = window;
        _current = current;
        _adapter = adapter;
        _display = new(window, seconds);
        Model = new(ClientSettingsFrontendAdapter.ToSnapshot(current));
    }

    internal void Open(ClientUserSettings current)
    {
        _current = current;
        // Retain saved next-match choices when reopening within the same session.
        try { Model = _adapter.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Model = new(ClientSettingsFrontendAdapter.ToSnapshot(current));
            _feedback = "SETTINGS STORAGE UNAVAILABLE";
        }
        Interaction = new();
        Visible = true;
    }

    internal ClientUserSettings? Update(InputState input, FrontendLayout layout, GraphicsSurfaceInfo surface)
    {
        if (!Visible) return null;
        Interaction.VisibleCapacity = layout.Geometry.VisibleDetailRows;
        if (_display.Expire() || (!_window.IsFocused && _display.Active))
        {
            _display.Cancel();
            RestoreDisplay();
            _feedback = "DISPLAY RESTORED";
        }
        bool click = input.TryGetMousePressPosition(PlatformMouseButton.Left, out var pointer);
        if (input.WasKeyPressed(PlatformKey.Escape) || (click &&
            (FrontendHitTesting.SecondaryAction(pointer.X, pointer.Y, layout) ||
             FrontendHitTesting.Footer(pointer.X, pointer.Y, layout))))
        {
            _display.Cancel();
            Visible = false;
            input.SuppressHeldInput();
            return null;
        }
        if (!_window.IsFocused || _window.IsMinimized) return null;
        if (!_display.Active)
        {
            if (input.WasKeyPressed(PlatformKey.Up) || input.WheelDelta > 0) Interaction.MovePrevious();
            if (input.WasKeyPressed(PlatformKey.Down) || input.WheelDelta < 0) Interaction.MoveNext();
            if (input.WasKeyPressed(PlatformKey.Left)) Interaction.Adjust(Model, -1);
            if (input.WasKeyPressed(PlatformKey.Right)) Interaction.Adjust(Model, 1);
            if (click && FrontendHitTesting.DetailRow(pointer.X, pointer.Y, layout, Interaction.VisibleRowCount) is int row)
            {
                var line = Surface.DetailLines[row];
                Interaction.FocusVisible(row);
                if (FrontendHitTesting.DetailAdjust(pointer.X, pointer.Y, layout, Interaction.VisibleRowCount) is int direction &&
                    (direction < 0 ? line.CanDecrease : line.CanIncrease))
                    Interaction.Adjust(Model, direction);
            }
        }
        bool apply = input.WasKeyPressed(PlatformKey.Enter) ||
            (click && FrontendHitTesting.PrimaryAction(pointer.X, pointer.Y, layout));
        if (!apply || !Interaction.CanApply) return null;
        try
        {
            var candidate = _adapter.CreateCandidate(Model);
            bool changed = candidate.WindowWidth != _current.WindowWidth || candidate.WindowHeight != _current.WindowHeight ||
                candidate.BorderlessFullscreen != _current.BorderlessFullscreen;
            if (changed && !_display.Active)
            {
                _presentedBeforeTrial = surface.PresentedFrameCount;
                _display.Begin(_current.CreateWindowConfiguration(), candidate.CreateWindowConfiguration());
                input.SuppressHeldInput();
                return null;
            }
            if (_display.Active && (surface.PresentedFrameCount <= _presentedBeforeTrial ||
                surface.Width != _window.ClientSize.Width || surface.Height != _window.ClientSize.Height ||
                surface.IsOccluded || surface.ResizePending)) return null;
            var saved = _adapter.Apply(Model);
            _display.Keep();
            _current = LiveSettings(_current, saved);
            _feedback = "SAVED. GRAPHICS AND BINDINGS APPLY NEXT MATCH.";
            input.SuppressHeldInput();
            return saved;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _display.Cancel();
            RestoreDisplay();
            _feedback = "COULD NOT APPLY SETTINGS";
            Console.Error.WriteLine($"[settings:pause-apply-failed] {error.Message}");
            return null;
        }
    }

    private void RestoreDisplay() => Model.SetDisplay(_current.WindowWidth, _current.WindowHeight,
        _current.BorderlessFullscreen, Model.Settings.UiScale);

    internal static ClientUserSettings LiveSettings(ClientUserSettings current, ClientUserSettings saved) => saved with
    {
        RenderQuality = current.RenderQuality, SceneAtmosphere = current.SceneAtmosphere, Particles = current.Particles,
        CameraBindings = current.CameraBindings, GameplayBindings = current.GameplayBindings
    };

    public void Dispose() => _display.Dispose();
}
