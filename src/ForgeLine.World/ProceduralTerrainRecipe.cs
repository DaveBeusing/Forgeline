namespace ForgeLine.World;

/// <summary>Version 1 landform parameters, in integer millimeters and permille.</summary>
public sealed record ProceduralTerrainRecipe
{
    public const string ProfileId = "terrain.landforms.v1";
    public WorldGenerationRecipe Generation { get; init; } = new() { TerrainProfileId = ProfileId };
    public uint Version { get; init; } = 1;
    public int BaseHeightMillimeters { get; init; } = 20_000;
    public int HillHeightMillimeters { get; init; } = 18_000;
    public int RidgeHeightMillimeters { get; init; } = 24_000;
    public int DetailHeightMillimeters { get; init; } = 500;
    public int DrainageDepthMillimeters { get; init; } = 4_000;
    public int FlatStrengthPermille { get; init; } = 600;

    // Conservative continuous gradient bound plus fixed-point/height rounding allowance.
    public double MaximumGradient => Math.Sqrt(2) * (
        3.0 * HillHeightMillimeters / 2_048_000 +
        3.0 * RidgeHeightMillimeters / 1_024_000 +
        3.0 * DetailHeightMillimeters / 128_000 +
        12.0 * DrainageDepthMillimeters / 2_048_000 +
        (HillHeightMillimeters + RidgeHeightMillimeters + DetailHeightMillimeters) *
        (FlatStrengthPermille / 1000.0) * 1.5 / 2_048_000) + 0.02;

    public void Validate()
    {
        if (Generation is null) throw new InvalidOperationException("A generation recipe is required.");
        Generation.Validate();
        if (Version != 1 || Generation.TerrainProfileId != ProfileId)
            throw new InvalidOperationException("Unsupported landform version or terrain profile.");
        // Keep the established 4 m grid and float-based ITerrainQuery coordinates representable.
        if (Generation.ChunkSizeMillimeters != 256_000 || Generation.SamplesPerSide != 65 ||
            Generation.Origin.X < -4096 || Generation.Origin.Z < -4096 ||
            (long)Generation.Origin.X + Generation.WidthInChunks > 4096 ||
            (long)Generation.Origin.Z + Generation.HeightInChunks > 4096)
            throw new InvalidOperationException("Landforms require 256 m chunks, 65 samples and bounds within +/-1048576 m.");
        if (BaseHeightMillimeters is < -100_000 or > 100_000 ||
            HillHeightMillimeters is < 0 or > 40_000 || RidgeHeightMillimeters is < 0 or > 40_000 ||
            DetailHeightMillimeters is < 0 or > 1_000 || DrainageDepthMillimeters is < 0 or > 8_000 ||
            FlatStrengthPermille is < 0 or > 1000 || MaximumGradient > 0.35)
            throw new InvalidOperationException("Landform parameters exceed height or gradient limits.");
    }
}
