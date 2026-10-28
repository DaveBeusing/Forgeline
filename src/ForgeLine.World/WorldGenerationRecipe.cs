namespace ForgeLine.World;

/// <summary>Versioned integer inputs; profile IDs identify immutable, externally resolved rules.</summary>
public sealed record WorldGenerationRecipe
{
    public const uint CurrentGeneratorVersion = 1;
    public const long MaximumSamples = 4_000_000;

    public uint GeneratorVersion { get; init; } = CurrentGeneratorVersion;
    public ulong Seed { get; init; }
    public ChunkCoordinate Origin { get; init; }
    public int WidthInChunks { get; init; } = 1;
    public int HeightInChunks { get; init; } = 1;
    public int ChunkSizeMillimeters { get; init; } = 256_000;
    public int SamplesPerSide { get; init; } = 65;
    public int RegionSizeInChunks { get; init; } = 8;
    public string TerrainProfileId { get; init; } = "terrain.default.v1";
    public string ClimateProfileId { get; init; } = "climate.default.v1";
    public string GeographyProfileId { get; init; } = "geography.default.v1";
    public string RulesProfileId { get; init; } = "rules.default.v1";
    // No optional rules are defined in version 1. Reserved bits must be zero.
    public uint Options { get; init; }

    public WorldGenerationDiagnostic? GetValidationError()
    {
        if (GeneratorVersion != CurrentGeneratorVersion)
            return new(WorldGenerationError.UnknownGeneratorVersion, "Unsupported generator version.");
        if (WidthInChunks is < 1 or > 256 || HeightInChunks is < 1 or > 256 ||
            SamplesPerSide is < 2 or > 129 || RegionSizeInChunks is < 1 or > 32 ||
            ChunkSizeMillimeters is < 1 or > 16_000_000 ||
            ChunkSizeMillimeters % (SamplesPerSide - 1) != 0 ||
            (long)WidthInChunks * HeightInChunks * SamplesPerSide * SamplesPerSide > MaximumSamples ||
            (long)Origin.X + WidthInChunks - 1 > int.MaxValue ||
            (long)Origin.Z + HeightInChunks - 1 > int.MaxValue)
            return new(WorldGenerationError.InvalidLimits, "Grid exceeds version 1 integer or allocation limits.");
        if (!IsProfileId(TerrainProfileId) || !IsProfileId(ClimateProfileId) ||
            !IsProfileId(GeographyProfileId) || !IsProfileId(RulesProfileId))
            return new(WorldGenerationError.InvalidProfileId, "Profile IDs must be 1..96 lowercase ASCII identifier characters.");
        if (Options != 0)
            return new(WorldGenerationError.UnknownOptions, "Version 1 defines no optional rule bits.");
        return null;
    }

    public void Validate()
    {
        if (GetValidationError() is { } error)
            throw new InvalidOperationException($"{error.Code}: {error.Message}");
    }

    internal static bool IsProfileId(string? value) =>
        value is { Length: > 0 and <= 96 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-');
}

/// <summary>Decoration inputs are separate from authoritative recipe inputs.</summary>
public sealed record WorldDecorationRecipe
{
    public uint Version { get; init; } = 1;
    public string VegetationProfileId { get; init; } = "vegetation.default.v1";
    public string PropsProfileId { get; init; } = "props.default.v1";
    public string RulesProfileId { get; init; } = "decoration.default.v1";

    public void Validate()
    {
        if (Version != 1 || !WorldGenerationRecipe.IsProfileId(VegetationProfileId) ||
            !WorldGenerationRecipe.IsProfileId(PropsProfileId) || !WorldGenerationRecipe.IsProfileId(RulesProfileId))
            throw new InvalidOperationException("Unsupported decoration recipe version or profile ID.");
    }
}
