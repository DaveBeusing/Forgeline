using ForgeLine.Graphics;

namespace ForgeLine.Client;

// Render-owner requests only. Native resize/occlusion/fences remain device-owned.
internal sealed class ClientRenderSurface(int width, int height, bool suspended)
{
    internal int Width { get; private set; } = width;
    internal int Height { get; private set; } = height;
    internal bool Suspended { get; private set; } = suspended || width <= 0 || height <= 0;

    internal bool Update(IGraphicsDevice graphics, int requestedWidth, int requestedHeight, bool suspend, out bool changed)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        suspend |= requestedWidth <= 0 || requestedHeight <= 0;
        changed = suspend != Suspended || !suspend && (requestedWidth != Width || requestedHeight != Height);
        if (suspend)
        {
            if (changed) graphics.Resize(0, 0);
            Suspended = true;
            return false;
        }
        if (changed) graphics.Resize(requestedWidth, requestedHeight);
        Width = requestedWidth; Height = requestedHeight; Suspended = false;
        return true;
    }
}
