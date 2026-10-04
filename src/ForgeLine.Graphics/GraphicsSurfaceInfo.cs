namespace ForgeLine.Graphics;

public readonly record struct GraphicsSurfaceInfo(
    int Width,
    int Height,
    int BufferCount,
    int FrameIndex,
    bool IsSuspended,
    string PresentMode,
    bool IsOccluded = false,
    bool ResizePending = false,
    ulong ResizeGeneration = 0,
    ulong AppliedResizeGeneration = 0);
