namespace ForgeLine.Presentation;

/// <summary>GPU chunk residency only; authoritative terrain remains fully available.</summary>
public readonly record struct TerrainResidencySettings(bool Enabled, int MaximumResidentChunks = 128)
{
    public static TerrainResidencySettings Default => new(true, 128);
    public static TerrainResidencySettings Eager => new(false, 128);

    public void Validate()
    {
        if (MaximumResidentChunks is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(MaximumResidentChunks));
    }
}
