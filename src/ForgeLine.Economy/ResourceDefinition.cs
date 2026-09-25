using ForgeLine.Core;

namespace ForgeLine.Economy;

public sealed record ResourceDefinition
{
    public required ResourceId Id { get; init; }

    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public double DefaultExtractionRatePerSecond { get; init; }

    public double DefaultRichness { get; init; } = 1.0;

    public void Validate()
    {
        if (!Id.IsSpecified)
        {
            throw new ArgumentException("Resource definitions require a stable resource ID.", nameof(Id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);

        if (!double.IsFinite(DefaultExtractionRatePerSecond) ||
            DefaultExtractionRatePerSecond <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultExtractionRatePerSecond));
        }

        if (!double.IsFinite(DefaultRichness) || DefaultRichness <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultRichness));
        }
    }
}
