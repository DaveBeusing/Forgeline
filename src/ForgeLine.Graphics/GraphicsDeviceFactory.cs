using ForgeLine.Platform;

namespace ForgeLine.Graphics;

public readonly record struct GraphicsWindowTarget(
    nint NativeHandle,
    int Width,
    int Height,
    bool Suspended)
{
    public void Validate()
    {
        if (NativeHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(NativeHandle));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(Width);
        ArgumentOutOfRangeException.ThrowIfNegative(Height);
    }
}

public static class GraphicsDeviceFactory
{
    public static IGraphicsDevice CreateForWindow(
        IWindow window,
        GraphicsConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!window.NativeHandle.IsValid)
        {
            throw new ArgumentException(
                "The graphics device requires a valid native window handle.",
                nameof(window));
        }

        return CreateForWindowTarget(
            new GraphicsWindowTarget(
                window.NativeHandle.Value,
                window.ClientSize.Width,
                window.ClientSize.Height,
                window.IsMinimized || window.ClientSize.IsEmpty),
            configuration);
    }

    public static IGraphicsDevice CreateForWindowTarget(
        in GraphicsWindowTarget target,
        GraphicsConfiguration? configuration = null)
    {
        target.Validate();
        return new D3D12GraphicsDevice(
            target,
            configuration ?? GraphicsConfiguration.Default);
    }
}
