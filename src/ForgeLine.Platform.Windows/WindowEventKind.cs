namespace ForgeLine.Platform;

public enum WindowEventKind
{
    Created = 0,
    CloseRequested = 1,
    Closed = 2,
    Resized = 3,
    Minimized = 4,
    Restored = 5,
    FocusGained = 6,
    FocusLost = 7,
    DpiChanged = 8,
    ModeChanged = 9
}
