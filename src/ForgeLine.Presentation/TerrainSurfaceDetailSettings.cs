namespace ForgeLine.Presentation;

public readonly record struct TerrainSurfaceDetailSettings(
    bool Enabled,
    float MicroTileMeters,
    float MicroColorStrength,
    float MicroNormalStrength,
    float RockSlopeStart,
    float RockSlopeEnd)
{
    public static TerrainSurfaceDetailSettings Default => new(true, 4, .04f, .06f, .25f, .6f);
    public static TerrainSurfaceDetailSettings Disabled => Default with { Enabled = false };

    public void Validate()
    {
        Require(MicroTileMeters, 1, 32, nameof(MicroTileMeters));
        Require(MicroColorStrength, 0, .08f, nameof(MicroColorStrength));
        Require(MicroNormalStrength, 0, .12f, nameof(MicroNormalStrength));
        Require(RockSlopeStart, .1f, .8f, nameof(RockSlopeStart));
        Require(RockSlopeEnd, RockSlopeStart + .05f, 1, nameof(RockSlopeEnd));
    }

    private static void Require(float value, float minimum, float maximum, string name)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, value, $"Terrain detail must be finite and between {minimum} and {maximum}.");
    }
}
