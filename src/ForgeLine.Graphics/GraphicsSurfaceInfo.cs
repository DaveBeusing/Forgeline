namespace ForgeLine.Graphics;

public readonly record struct GraphicsSurfaceInfo(
    int Width,
    int Height,
    int BufferCount,
    int FrameIndex,
    bool IsSuspended,
    string PresentMode);
