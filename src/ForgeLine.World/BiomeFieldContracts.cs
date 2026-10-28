namespace ForgeLine.World;

public enum BiomeKind : byte
{
    TemperateMixedForest = 0,
    OpenMeadow = 1,
    DrySlope = 2,
    RockyGround = 3,
    WetDepression = 4
}

public enum EnvironmentSurface : byte
{
    Natural = 0,
    Water = 1,
    Rock = 2,
    Built = 3
}

/// <summary>UNorm16 weights sum to 65535 on ecological ground, zero on water or built surfaces.</summary>
public readonly record struct BiomeWeights(ushort Forest, ushort Meadow, ushort DrySlope, ushort Rock, ushort WetDepression)
{
    public int Sum => Forest + Meadow + DrySlope + Rock + WetDepression;

    public ushort this[BiomeKind biome] => biome switch
    {
        BiomeKind.TemperateMixedForest => Forest,
        BiomeKind.OpenMeadow => Meadow,
        BiomeKind.DrySlope => DrySlope,
        BiomeKind.RockyGround => Rock,
        BiomeKind.WetDepression => WetDepression,
        _ => throw new ArgumentOutOfRangeException(nameof(biome))
    };

    public BiomeKind? DominantBiome
    {
        get
        {
            if (Sum == 0) return null;
            BiomeKind best = BiomeKind.TemperateMixedForest;
            for (int index = 1; index < 5; index++)
                if (this[(BiomeKind)index] > this[best]) best = (BiomeKind)index;
            return best;
        }
    }
}

public readonly record struct EnvironmentSample(
    int HeightMillimeters, int TemperatureMilliCelsius, ushort Slope,
    ushort Moisture, ushort SoilFertility, EnvironmentSurface Surface, BiomeWeights Weights);

public interface IEnvironmentQuery
{
    /// <summary>Reads an immutable terrain snapshot. Missing or invalid terrain yields false, never invented ground.</summary>
    bool TrySample(long worldXMillimeters, long worldZMillimeters, out EnvironmentSample sample,
        EnvironmentSurface surface = EnvironmentSurface.Natural);
}

/// <summary>Versioned numerical climate rules. Every parameter is included in raster fingerprints.</summary>
public sealed record BiomeFieldProfile
{
    public const string TemperateProfileId = "climate.temperate.v1";

    public uint Version { get; init; } = 1;
    public string Id { get; init; } = TemperateProfileId;
    public int TemperaturePeriodMillimeters { get; init; } = 1_024_000;
    public int MoisturePeriodMillimeters { get; init; } = 768_000;
    public int SoilPeriodMillimeters { get; init; } = 256_000;
    public int BaseTemperatureMilliCelsius { get; init; } = 16_000;
    public int TemperatureAmplitudeMilliCelsius { get; init; } = 12_000;
    public int LapseMilliCelsiusPerKilometer { get; init; } = 6_500;
    public int WaterLevelMillimeters { get; init; }
    public int WetlandHeightMillimeters { get; init; } = 30_000;

    public void Validate()
    {
        if (Version != 1 || !WorldGenerationRecipe.IsProfileId(Id) ||
            TemperaturePeriodMillimeters is < 1_000 or > 16_000_000 ||
            MoisturePeriodMillimeters is < 1_000 or > 16_000_000 ||
            SoilPeriodMillimeters is < 1_000 or > 16_000_000 ||
            BaseTemperatureMilliCelsius is < -50_000 or > 50_000 ||
            TemperatureAmplitudeMilliCelsius is < 0 or > 50_000 ||
            LapseMilliCelsiusPerKilometer is < 0 or > 20_000 ||
            WaterLevelMillimeters is < -100_000_000 or > 100_000_000 ||
            WetlandHeightMillimeters is < 1 or > 1_000_000)
            throw new InvalidOperationException("Unsupported biome profile version, identifier or numerical limits.");
    }

    internal void WriteTo(WorldCanonicalWriter writer)
    {
        writer.WriteUInt32(Version);
        writer.WriteIdentifier(Id);
        writer.WriteInt32(TemperaturePeriodMillimeters);
        writer.WriteInt32(MoisturePeriodMillimeters);
        writer.WriteInt32(SoilPeriodMillimeters);
        writer.WriteInt32(BaseTemperatureMilliCelsius);
        writer.WriteInt32(TemperatureAmplitudeMilliCelsius);
        writer.WriteInt32(LapseMilliCelsiusPerKilometer);
        writer.WriteInt32(WaterLevelMillimeters);
        writer.WriteInt32(WetlandHeightMillimeters);
    }
}
