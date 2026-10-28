namespace ForgeLine.Assets;

public readonly record struct AssetVector3(float X, float Y, float Z)
{
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
}

public readonly record struct AssetVariationRange(float Min, float Max)
{
    public bool IsValid => float.IsFinite(Min) && float.IsFinite(Max) && Min <= Max;
}

// Authoring recommendations only. Collision proxies never grant gameplay authority.
public enum ScatterInteraction
{
    PresentationOnly = 1,
}

public sealed record ScatterAssetMetadata
{
    public int GeneratorVersion { get; init; } = 1;
    public required string FamilyId { get; init; }
    public required AssetVector3 GroundAnchor { get; init; }
    public required AssetVector3 NaturalSizeMeters { get; init; }
    public required AssetVariationRange UniformScale { get; init; }
    public required AssetVariationRange TintMultiplier { get; init; }
    public required AssetVariationRange YawDegrees { get; init; }
    public required AssetVariationRange SlopeDegrees { get; init; }
    public required float MinimumSpacingMeters { get; init; }
    public IReadOnlyList<string> BiomeAffinities { get; init; } = [];
    public ScatterInteraction Interaction { get; init; } = ScatterInteraction.PresentationOnly;
}

public static class ScatterAssetValidation
{
    public static void Validate(ScatterAssetMetadata? metadata, RuntimeAssetType type, AssetBounds? bounds = null)
    {
        if (metadata is null) return;
        if (type != RuntimeAssetType.Mesh || metadata.GeneratorVersion != 1 ||
            !AssetId.TryParse(metadata.FamilyId, out _) ||
            !metadata.GroundAnchor.IsFinite || !metadata.NaturalSizeMeters.IsFinite ||
            metadata.NaturalSizeMeters.X is <= 0 or > 1024 ||
            metadata.NaturalSizeMeters.Y is <= 0 or > 1024 ||
            metadata.NaturalSizeMeters.Z is <= 0 or > 1024 ||
            !Within(metadata.UniformScale, .25f, 4) ||
            !Within(metadata.TintMultiplier, .5f, 1.5f) ||
            !Within(metadata.YawDegrees, 0, 360) ||
            !Within(metadata.SlopeDegrees, 0, 89) ||
            !float.IsFinite(metadata.MinimumSpacingMeters) || metadata.MinimumSpacingMeters is <= 0 or > 4096 ||
            metadata.Interaction != ScatterInteraction.PresentationOnly ||
            metadata.BiomeAffinities is null || metadata.BiomeAffinities.Count is < 1 or > 16)
            throw new InvalidDataException("Invalid version-1 scatter metadata or asset owner type.");

        string? previous = null;
        foreach (string biome in metadata.BiomeAffinities)
        {
            if (!AssetId.TryParse(biome, out _) || previous is not null && StringComparer.Ordinal.Compare(previous, biome) >= 0)
                throw new InvalidDataException("Biome affinities must be valid, unique, ordinally sorted profile IDs.");
            previous = biome;
        }
        if (bounds is { } box && (!box.IsValid ||
            metadata.GroundAnchor.X < box.MinX || metadata.GroundAnchor.X > box.MaxX ||
            metadata.GroundAnchor.Y < box.MinY || metadata.GroundAnchor.Y > box.MaxY ||
            metadata.GroundAnchor.Z < box.MinZ || metadata.GroundAnchor.Z > box.MaxZ))
            throw new InvalidDataException("Scatter ground anchor lies outside mesh bounds.");
    }

    private static bool Within(AssetVariationRange range, float min, float max) =>
        range.IsValid && range.Min >= min && range.Max <= max;
}
