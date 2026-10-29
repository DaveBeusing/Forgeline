using ForgeLine.World;

namespace ForgeLine.Game;

/// <summary>Integer inputs to the complete, application-owned map pipeline.</summary>
public sealed record GeneratedMapRecipe
{
    public const int CurrentVersion = 1;
    public int GeneratorVersion { get; init; } = CurrentVersion;
    public ProceduralTerrainRecipe Terrain { get; init; } = new()
    {
        Generation = new()
        {
            Seed = 17, WidthInChunks = 12, HeightInChunks = 12,
            TerrainProfileId = ProceduralTerrainRecipe.ProfileId,
            ClimateProfileId = "climate.temperate.v1",
            GeographyProfileId = ProceduralStrategicLayoutGenerator.GeographyProfile,
            RulesProfileId = ProceduralStrategicLayoutGenerator.RulesProfile
        }
    };
    public string TacticalProfileId { get; init; } = "tactical.site-cover.v1";
    public string ScatterProfileId { get; init; } = "scatter.temperate.v1";
    public string MaterialProfileId { get; init; } = "terrain.biome.v1";
    public int ScatterDensityPermille { get; init; } = 650;
    public int MaximumScatterInstances { get; init; } = 1_000_000;

    public void Validate()
    {
        if (GeneratorVersion != CurrentVersion || Terrain is null)
            throw new InvalidDataException("Unsupported generated map generator version or missing terrain recipe.");
        Terrain.Validate();
        var grid = Terrain.Generation;
        if (grid.Origin != new ChunkCoordinate(0, 0) || grid.WidthInChunks is < 8 or > 16 ||
            grid.HeightInChunks is < 8 or > 16 || grid.RegionSizeInChunks != 8 ||
            grid.ClimateProfileId != "climate.temperate.v1" ||
            grid.GeographyProfileId != ProceduralStrategicLayoutGenerator.GeographyProfile ||
            grid.RulesProfileId != ProceduralStrategicLayoutGenerator.RulesProfile ||
            TacticalProfileId != "tactical.site-cover.v1" || ScatterProfileId != "scatter.temperate.v1" ||
            MaterialProfileId != "terrain.biome.v1")
            throw new InvalidDataException("Unsupported generated map size or profile; version 1 requires 8..16 zero-origin chunks per axis.");
        _ = EnvironmentScatterProfile.Temperate(ScatterDensityPermille, MaximumScatterInstances);
    }
}
