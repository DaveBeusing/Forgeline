using System.Numerics;
using ForgeLine.Platform;

namespace ForgeLine.Input;

public sealed class InputState
{
    private readonly HashSet<PlatformKey> _keysDown = [];
    private readonly ulong[] _keyPressSequences = new ulong[Enum.GetValues<PlatformKey>().Length];
    private readonly bool[] _keyPressed = new bool[Enum.GetValues<PlatformKey>().Length];
    private readonly HashSet<PlatformMouseButton> _mouseButtonsDown = [];
    private readonly HashSet<PlatformKey> _suppressedKeys = [];
    private readonly HashSet<PlatformMouseButton> _suppressedButtons = [];
    private readonly Dictionary<PlatformMouseButton, Vector2> _mousePressPositions = [];
    private readonly HashSet<PlatformMouseButton> _mouseButtonsReleased = [];

    private bool _hasPointerPosition;
    private Vector2 _pointerPosition;
    private Vector2 _pointerDelta;
    private int _wheelDelta;
    private bool _focusLostThisFrame;

    public bool FocusLostThisFrame =>
        _focusLostThisFrame;

    public bool HasPointerPosition => _hasPointerPosition;

    public Vector2 PointerPosition => _pointerPosition;

    public Vector2 PointerDelta => _pointerDelta;

    public int WheelDelta => _wheelDelta;

    public void BeginFrame()
    {
        Array.Clear(_keyPressed);
        _mousePressPositions.Clear();
        _mouseButtonsReleased.Clear();
        _pointerDelta = Vector2.Zero;
        _wheelDelta = 0;
        _focusLostThisFrame = false;
    }

    public void Apply(PlatformInputEvent inputEvent)
    {
        switch (inputEvent.Kind)
        {
            case PlatformInputEventKind.KeyDown:
                if (inputEvent.Key != PlatformKey.Unknown && !_suppressedKeys.Contains(inputEvent.Key))
                {
                    if (_keysDown.Add(inputEvent.Key))
                    {
                        _keyPressSequences[(int)inputEvent.Key]++;
                        _keyPressed[(int)inputEvent.Key] = true;
                    }
                }

                break;

            case PlatformInputEventKind.KeyUp:
                _suppressedKeys.Remove(inputEvent.Key);
                _keysDown.Remove(inputEvent.Key);
                break;

            case PlatformInputEventKind.MouseButtonDown:
                UpdatePointerPosition(inputEvent.PointerX, inputEvent.PointerY, accumulateDelta: false);
                if (inputEvent.MouseButton != PlatformMouseButton.None && !_suppressedButtons.Contains(inputEvent.MouseButton))
                {
                    _mouseButtonsDown.Add(inputEvent.MouseButton);
                    _mousePressPositions.TryAdd(inputEvent.MouseButton, _pointerPosition);
                }

                break;

            case PlatformInputEventKind.MouseButtonUp:
                _mouseButtonsReleased.Add(inputEvent.MouseButton);
                _suppressedButtons.Remove(inputEvent.MouseButton);
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
                _focusLostThisFrame = true;
                break;

            case PlatformInputEventKind.PointerLeft:
                _mousePressPositions.Clear();
                _mouseButtonsReleased.Clear();
                _hasPointerPosition = false;
                _pointerDelta = Vector2.Zero;
                _mouseButtonsDown.Clear();
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(inputEvent),
                    inputEvent.Kind,
                    "Unsupported platform input event.");
        }
    }

    public bool IsKeyDown(PlatformKey key) => _keysDown.Contains(key);

    // Survives a release between frames; repeats and suppressed keys do not advance it.
    public ulong KeyPressSequence(PlatformKey key) => _keyPressSequences[(int)key];
    public bool WasKeyPressed(PlatformKey key) => _keyPressed[(int)key];

    public bool IsMouseButtonDown(PlatformMouseButton button) =>
        _mouseButtonsDown.Contains(button);

    public bool TryGetMousePressPosition(PlatformMouseButton button, out Vector2 position) =>
        _mousePressPositions.TryGetValue(button, out position);

    public bool WasMouseButtonReleased(PlatformMouseButton button) =>
        _mouseButtonsReleased.Contains(button);

    public void Reset()
    {
        Array.Clear(_keyPressed);
        _mousePressPositions.Clear();
        _mouseButtonsReleased.Clear();
        _suppressedKeys.Clear();
        _suppressedButtons.Clear();
        _keysDown.Clear();
        _mouseButtonsDown.Clear();
        _hasPointerPosition = false;
        _pointerDelta = Vector2.Zero;
        _wheelDelta = 0;
        _focusLostThisFrame = false;
    }

    public void SuppressHeldInput()
    {
        Array.Clear(_keyPressed);
        _mousePressPositions.Clear();
        _mouseButtonsReleased.Clear();
        _suppressedKeys.UnionWith(_keysDown);
        _suppressedButtons.UnionWith(_mouseButtonsDown);
        _keysDown.Clear();
        _mouseButtonsDown.Clear();
        _hasPointerPosition = false;
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
