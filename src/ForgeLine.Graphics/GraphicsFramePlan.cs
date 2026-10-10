namespace ForgeLine.Graphics;

public enum GraphicsFramePass
{
    World,
    Overlay
}

public enum GraphicsFrameTargetFormat
{
    Rgba8Unorm,
    Rgba16Float,
    Depth32Float
}

public enum GraphicsFrameTargetLifetime
{
    // Reuse requires the current back-buffer submission fence.
    BackBuffer,
    // Resize/shutdown requires all submitted frames to retire.
    Surface
}

public readonly record struct GraphicsFrameTargetDescriptor(
    int Width,
    int Height,
    GraphicsFrameTargetFormat Format,
    GraphicsFrameTargetLifetime Lifetime,
    int ResourceCount)
{
    // Logical texel payload, excluding allocation alignment and driver overhead.
    public long PayloadBytes => checked((long)Width * Height *
        (Format == GraphicsFrameTargetFormat.Rgba16Float ? 8 : 4) * ResourceCount);
}

/// <summary>The direct-output frame contract. Contains no transient copies or allocations.</summary>
public readonly record struct GraphicsFramePlan
{
    public GraphicsFramePlan(int width, int height, int bufferCount, bool linearScene = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (bufferCount is < GraphicsConfiguration.MinimumBufferCount or > GraphicsConfiguration.MaximumBufferCount)
            throw new ArgumentOutOfRangeException(nameof(bufferCount));

        Output = new(width, height, GraphicsFrameTargetFormat.Rgba8Unorm,
            GraphicsFrameTargetLifetime.BackBuffer, bufferCount);
        Depth = new(width, height, GraphicsFrameTargetFormat.Depth32Float,
            GraphicsFrameTargetLifetime.Surface, 1);
        Scene = linearScene ? new(width, height, GraphicsFrameTargetFormat.Rgba16Float,
            GraphicsFrameTargetLifetime.BackBuffer, bufferCount) : null;
    }

    public GraphicsFrameTargetDescriptor Output { get; }
    public GraphicsFrameTargetDescriptor Depth { get; }
    public GraphicsFrameTargetDescriptor? Scene { get; }
    public long TransientPayloadBytes => Scene?.PayloadBytes ?? 0;
}

internal struct GraphicsFramePassState
{
    internal GraphicsFramePass Current { get; private set; }

    internal bool Enter(GraphicsFramePass pass)
    {
        if (!Enum.IsDefined(pass))
            throw new ArgumentOutOfRangeException(nameof(pass));
        if (pass < Current)
            throw new InvalidOperationException("World submission cannot follow the output-space overlay pass.");
        if (pass == Current)
            return false;
        Current = pass;
        return true;
    }
}
