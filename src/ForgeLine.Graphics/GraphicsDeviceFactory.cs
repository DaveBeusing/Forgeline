using ForgeLine.Platform;

namespace ForgeLine.Graphics;

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

        return new D3D12GraphicsDevice(window, configuration ?? GraphicsConfiguration.Default);
    }
}
