namespace ForgeLine.Graphics;

public sealed record GraphicsConfiguration
{
    public const int MinimumBufferCount = 2;
    public const int MaximumBufferCount = 4;

    public static GraphicsConfiguration Default { get; } = new();

    public int BufferCount { get; init; } = 3;

#if DEBUG
    public bool EnableDebugLayer { get; init; } = true;
#else
    public bool EnableDebugLayer { get; init; }
#endif

    public bool AllowSoftwareAdapterFallback { get; init; } = true;

    public bool ForceSoftwareAdapter { get; init; }

    public bool EnableVSync { get; init; } = true;

    public bool EnableMemoryDiagnostics { get; init; }

    internal void Validate()
    {
        if (ForceSoftwareAdapter && !AllowSoftwareAdapterFallback)
            throw new ArgumentException("Forced WARP requires software adapters to be allowed.");
        if (BufferCount is < MinimumBufferCount or > MaximumBufferCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BufferCount),
                BufferCount,
                $"Graphics buffer count must be between {MinimumBufferCount} and {MaximumBufferCount}.");
        }
    }
}
