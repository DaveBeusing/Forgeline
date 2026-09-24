using System.Numerics;
using ForgeLine.Platform;

namespace ForgeLine.Input;

public sealed class InputState
{
    private readonly HashSet<PlatformKey> _keysDown = [];
    private readonly HashSet<PlatformMouseButton> _mouseButtonsDown = [];

    private bool _hasPointerPosition;
    private Vector2 _pointerPosition;
    private Vector2 _pointerDelta;
    private int _wheelDelta;

    public bool HasPointerPosition => _hasPointerPosition;

    public Vector2 PointerPosition => _pointerPosition;

    public Vector2 PointerDelta => _pointerDelta;

    public int WheelDelta => _wheelDelta;

    public void BeginFrame()
    {
        _hasPointerPosition = false;
        _pointerDelta = Vector2.Zero;
        _wheelDelta = 0;
    }

    public void Apply(PlatformInputEvent inputEvent)
    {
        switch (inputEvent.Kind)
        {
            case PlatformInputEventKind.KeyDown:
                if (inputEvent.Key != PlatformKey.Unknown)
                {
                    _keysDown.Add(inputEvent.Key);
                }

                break;

            case PlatformInputEventKind.KeyUp:
                _keysDown.Remove(inputEvent.Key);
                break;

            case PlatformInputEventKind.MouseButtonDown:
                UpdatePointerPosition(inputEvent.PointerX, inputEvent.PointerY, accumulateDelta: false);
                if (inputEvent.MouseButton != PlatformMouseButton.None)
                {
                    _mouseButtonsDown.Add(inputEvent.MouseButton);
                }

                break;

            case PlatformInputEventKind.MouseButtonUp:
                UpdatePointerPosition(inputEvent.PointerX, inputEvent.PointerY, accumulateDelta: false);
                _mouseButtonsDown.Remove(inputEvent.MouseButton);
                break;

            case PlatformInputEventKind.PointerMoved:
                UpdatePointerPosition(inputEvent.PointerX, inputEvent.PointerY, accumulateDelta: true);
                break;

            case PlatformInputEventKind.MouseWheel:
                UpdatePointerPosition(inputEvent.PointerX, inputEvent.PointerY, accumulateDelta: false);
                _wheelDelta += inputEvent.WheelDelta;
                break;

            case PlatformInputEventKind.FocusLost:
                Reset();
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(inputEvent),
                    inputEvent.Kind,
                    "Unsupported platform input event.");
        }
    }

    public bool IsKeyDown(PlatformKey key) => _keysDown.Contains(key);

    public bool IsMouseButtonDown(PlatformMouseButton button) =>
        _mouseButtonsDown.Contains(button);

    public void Reset()
    {
        _keysDown.Clear();
        _mouseButtonsDown.Clear();
        _pointerDelta = Vector2.Zero;
        _wheelDelta = 0;
    }

    private void UpdatePointerPosition(int x, int y, bool accumulateDelta)
    {
        var next = new Vector2(x, y);

        if (_hasPointerPosition && accumulateDelta)
        {
            _pointerDelta += next - _pointerPosition;
        }

        _pointerPosition = next;
        _hasPointerPosition = true;
    }
}
