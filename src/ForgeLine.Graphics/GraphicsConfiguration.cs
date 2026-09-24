namespace ForgeLine.Graphics;

public sealed record GraphicsConfiguration
{
    public const int MinimumBufferCount = 2;
    public const int MaximumBufferCount = 4;

    public static GraphicsConfiguration Default { get; } = new();

    public int BufferCount { get; init; } = 3;

    public bool EnableDebugLayer { get; init; } =
#if DEBUG
        true;
#else
        false;
#endif

    public bool AllowSoftwareAdapterFallback { get; init; } = true;

    public bool EnableVSync { get; init; } = true;

    internal void Validate()
    {
        if (BufferCount is < MinimumBufferCount or > MaximumBufferCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BufferCount),
                BufferCount,
                $"Graphics buffer count must be between {MinimumBufferCount} and {MaximumBufferCount}.");
        }
    }
}
