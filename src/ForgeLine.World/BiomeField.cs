using System.Numerics;

namespace ForgeLine.World;

/// <summary>Static environment fields over an immutable ITerrainQuery; no ECS, renderer or runtime weather.</summary>
public sealed class BiomeField : IEnvironmentQuery
{
    // ITerrainQuery uses floats. Bound physical coordinates before converting; integer climate remains exact.
    public const long MaximumCoordinateMillimeters = 1_000_000_000;
    private const int Unit = ushort.MaxValue;
    private readonly ITerrainQuery _terrain;
    private readonly WorldGenerationRandom _random;
    private readonly long _minimumX, _minimumZ, _maximumX, _maximumZ;

    public BiomeField(WorldGenerationRecipe recipe, ITerrainQuery terrain, BiomeFieldProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(terrain);
        recipe.Validate();
        Profile = profile ?? new();
        Profile.Validate();
        if (!string.Equals(recipe.ClimateProfileId, Profile.Id, StringComparison.Ordinal))
            throw new ArgumentException("Recipe climate profile must match the supplied biome profile.", nameof(recipe));
        Recipe = recipe;
        _terrain = terrain;
        _random = new(recipe);
        _minimumX = (long)recipe.Origin.X * recipe.ChunkSizeMillimeters;
        _minimumZ = (long)recipe.Origin.Z * recipe.ChunkSizeMillimeters;
        _maximumX = ((long)recipe.Origin.X + recipe.WidthInChunks) * recipe.ChunkSizeMillimeters;
        _maximumZ = ((long)recipe.Origin.Z + recipe.HeightInChunks) * recipe.ChunkSizeMillimeters;
        if (_minimumX < -MaximumCoordinateMillimeters || _minimumZ < -MaximumCoordinateMillimeters ||
            _maximumX > MaximumCoordinateMillimeters || _maximumZ > MaximumCoordinateMillimeters)
            throw new ArgumentOutOfRangeException(nameof(recipe), "Biome terrain-query extent exceeds supported physical coordinates.");
    }

    public WorldGenerationRecipe Recipe { get; }
    public BiomeFieldProfile Profile { get; }

    public bool TrySample(long worldXMillimeters, long worldZMillimeters, out EnvironmentSample sample,
        EnvironmentSurface surface = EnvironmentSurface.Natural)
    {
        if (!Enum.IsDefined(surface)) throw new ArgumentOutOfRangeException(nameof(surface));
        sample = default;
        if (worldXMillimeters < _minimumX || worldXMillimeters > _maximumX ||
            worldZMillimeters < _minimumZ || worldZMillimeters > _maximumZ)
            return false;
        float x = (float)(worldXMillimeters / 1000.0), z = (float)(worldZMillimeters / 1000.0);
        if (!_terrain.TrySampleHeight(x, z, out float height) || !float.IsFinite(height) ||
            Math.Abs((double)height) > 1_000_000 ||
            !_terrain.TrySampleNormal(x, z, out Vector3 normal) ||
            !float.IsFinite(normal.X) || !float.IsFinite(normal.Y) || !float.IsFinite(normal.Z) || normal.Y < 0)
            return false;
        double length = Math.Sqrt((double)normal.X * normal.X + (double)normal.Y * normal.Y + (double)normal.Z * normal.Z);
        if (!double.IsFinite(length) || length < 0.000001) return false;
        int heightMillimeters = (int)Math.Round((double)height * 1000, MidpointRounding.ToEven);
        int slope = (int)Math.Clamp(Math.Round((1 - normal.Y / length) * Unit, MidpointRounding.ToEven), 0, Unit);
        int temperatureNoise = Noise(worldXMillimeters, worldZMillimeters, Profile.TemperaturePeriodMillimeters, 0x42494F4D0001);
        int moistureNoise = Noise(worldXMillimeters, worldZMillimeters, Profile.MoisturePeriodMillimeters, 0x42494F4D0002);
        int soilNoise = Noise(worldXMillimeters, worldZMillimeters, Profile.SoilPeriodMillimeters, 0x42494F4D0003);
        long relativeHeight = (long)heightMillimeters - Profile.WaterLevelMillimeters;
        int temperature = (int)Math.Clamp(Profile.BaseTemperatureMilliCelsius +
            ((long)temperatureNoise * 2 - Unit) * Profile.TemperatureAmplitudeMilliCelsius / Unit -
            relativeHeight * Profile.LapseMilliCelsiusPerKilometer / 1_000_000, -50_000, 50_000);
        int lowland = Unit - Smooth(relativeHeight, 0, Profile.WetlandHeightMillimeters);
        int rock = Smooth(slope, 5_000, 30_000);
        int moisture = Math.Clamp(moistureNoise * 3 / 4 + lowland / 4 - slope / 3, 0, Unit);
        int soil = Multiply(soilNoise, Unit - rock);
        if (surface == EnvironmentSurface.Natural && relativeHeight <= 0)
            surface = EnvironmentSurface.Water;
        BiomeWeights weights;
        if (surface is EnvironmentSurface.Water or EnvironmentSurface.Built)
        {
            soil = 0;
            if (surface == EnvironmentSurface.Water) moisture = Unit;
            weights = default;
        }
        else if (surface == EnvironmentSurface.Rock)
        {
            soil = 0;
            weights = new(0, 0, 0, Unit, 0);
        }
        else
        {
            int remaining = Unit;
            int rockWeight = rock;
            remaining -= rockWeight;
            int wet = Multiply(remaining, Multiply(Smooth(moisture, 36_000, 60_000), lowland));
            remaining -= wet;
            int dry = Multiply(remaining, Multiply(Smooth(Unit - moisture, 20_000, 50_000), Smooth(slope, 1_000, 18_000)));
            remaining -= dry;
            int comfort = Multiply(Smooth(temperature, -5_000, 10_000), Unit - Smooth(temperature, 24_000, 40_000));
            int forest = Multiply(remaining, Multiply(comfort, Multiply(Smooth(moisture, 20_000, 50_000), soil)));
            remaining -= forest;
            weights = new((ushort)forest, (ushort)remaining, (ushort)dry, (ushort)rockWeight, (ushort)wet);
        }
        sample = new(heightMillimeters, temperature, (ushort)slope, (ushort)moisture, (ushort)soil, surface, weights);
        return true;
    }

    private int Noise(long x, long z, int period, ulong stream)
    {
        static (long Cell, int Fraction) Split(long value, int divisor)
        {
            long cell = value / divisor, remainder = value % divisor;
            if (remainder < 0) { cell--; remainder += divisor; }
            return (cell, (int)((remainder * Unit + divisor / 2) / divisor));
        }
        var a = Split(x, period);
        var b = Split(z, period);
        int tx = Smooth(a.Fraction, 0, Unit), tz = Smooth(b.Fraction, 0, Unit);
        int Corner(long cx, long cz) =>
            (int)(_random.At(WorldGenerationDomain.Climate, new(cx, cz), streamId: stream) >> 48);
        int top = Lerp(Corner(a.Cell, b.Cell), Corner(a.Cell + 1, b.Cell), tx);
        int bottom = Lerp(Corner(a.Cell, b.Cell + 1), Corner(a.Cell + 1, b.Cell + 1), tx);
        return Lerp(top, bottom, tz);
    }

    private static int Lerp(int a, int b, int t) => (int)(((long)a * (Unit - t) + (long)b * t + Unit / 2) / Unit);
    private static int Multiply(int a, int b) => (int)(((long)a * b + Unit / 2) / Unit);

    private static int Smooth(long value, int start, int end)
    {
        if (value <= start) return 0;
        if (value >= end) return Unit;
        long t = (value - start) * Unit / (end - start);
        return (int)(t * t * (3L * Unit - 2 * t) / ((long)Unit * Unit));
    }
}
