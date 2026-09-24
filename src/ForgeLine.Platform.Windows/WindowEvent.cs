namespace ForgeLine.Platform;

public readonly record struct WindowEvent(
    WindowEventKind Kind,
    WindowSize ClientSize,
    uint Dpi,
    bool IsFocused,
    bool IsMinimized,
    WindowMode Mode);
