namespace ForgeLine.Platform;

public interface IWindow : IDisposable
{
    NativeWindowHandle NativeHandle { get; }

    WindowSize ClientSize { get; }

    uint Dpi { get; }

    bool IsFocused { get; }

    bool IsMinimized { get; }

    bool IsOpen { get; }

    WindowMode Mode { get; }

    void RequestClose();

    void SetMode(WindowMode mode);

    bool TryDequeueEvent(out WindowEvent windowEvent);
}
