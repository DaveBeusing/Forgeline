namespace ForgeLine.Platform;

public enum PlatformInputEventKind
{
    KeyDown = 0,
    KeyUp = 1,
    MouseButtonDown = 2,
    MouseButtonUp = 3,
    PointerMoved = 4,
    MouseWheel = 5,
    FocusLost = 6
}

public readonly record struct PlatformInputEvent(
    PlatformInputEventKind Kind,
    PlatformKey Key,
    PlatformMouseButton MouseButton,
    int PointerX,
    int PointerY,
    int WheelDelta)
{
    public static PlatformInputEvent KeyChanged(PlatformInputEventKind kind, PlatformKey key) =>
        new(kind, key, PlatformMouseButton.None, 0, 0, 0);

    public static PlatformInputEvent MouseButtonChanged(
        PlatformInputEventKind kind,
        PlatformMouseButton button,
        int pointerX,
        int pointerY) =>
        new(kind, PlatformKey.Unknown, button, pointerX, pointerY, 0);

    public static PlatformInputEvent PointerMoved(int pointerX, int pointerY) =>
        new(
            PlatformInputEventKind.PointerMoved,
            PlatformKey.Unknown,
            PlatformMouseButton.None,
            pointerX,
            pointerY,
            0);

    public static PlatformInputEvent MouseWheel(int pointerX, int pointerY, int wheelDelta) =>
        new(
            PlatformInputEventKind.MouseWheel,
            PlatformKey.Unknown,
            PlatformMouseButton.None,
            pointerX,
            pointerY,
            wheelDelta);

    public static PlatformInputEvent FocusLost() =>
        new(
            PlatformInputEventKind.FocusLost,
            PlatformKey.Unknown,
            PlatformMouseButton.None,
            0,
            0,
            0);
}
