using System.Buffers.Binary;
using System.Text.Json;

namespace ForgeLine.World;

public readonly record struct BiomeDistribution(double Forest, double Meadow, double DrySlope, double Rock, double WetDepression);
public readonly record struct BiomeCounts(int Forest, int Meadow, int DrySlope, int Rock, int WetDepression);

public sealed record BiomeFieldStatistics(
    int SampleCount, int AvailableCount, int EcologicalCount, int WaterCount, int BuiltCount,
    double MeanTemperatureMilliCelsius, double MeanMoisture, double MeanSoilFertility,
    BiomeDistribution MeanWeights, BiomeCounts DominantCounts);

public sealed record BiomeFieldDiagnosticReport(
    uint FormatVersion, WorldGenerationRecipe Recipe, BiomeFieldProfile Profile,
    ChunkCoordinate Coordinate, EnvironmentSurface Surface, int SamplesPerSide,
    string Sha256, BiomeFieldStatistics Statistics)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, NewLine = "\n" };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

/// <summary>Immutable per-chunk diagnostic/scatter samples, packed into 26 bytes per lattice point.</summary>
public sealed class BiomeFieldRaster
{
    public const int BytesPerSample = 26;
    private readonly byte[] _data;
    private readonly WorldGenerationRecipe _recipe;
    private readonly BiomeFieldProfile _profile;

    private BiomeFieldRaster(BiomeField field, ChunkCoordinate coordinate, EnvironmentSurface surface)
    {
        _recipe = field.Recipe;
        _profile = field.Profile;
        Coordinate = coordinate;
        Surface = surface;
        SamplesPerSide = _recipe.SamplesPerSide;
        _data = new byte[checked(SamplesPerSide * SamplesPerSide * BytesPerSample)];
        int spacing = _recipe.ChunkSizeMillimeters / (SamplesPerSide - 1);
        for (int z = 0; z < SamplesPerSide; z++)
        for (int x = 0; x < SamplesPerSide; x++)
        {
            long worldX = (long)coordinate.X * _recipe.ChunkSizeMillimeters + x * spacing;
            long worldZ = (long)coordinate.Z * _recipe.ChunkSizeMillimeters + z * spacing;
            if (field.TrySample(worldX, worldZ, out var sample, surface))
                WriteSample(_data.AsSpan((z * SamplesPerSide + x) * BytesPerSample, BytesPerSample), sample);
        }
    }

    public ChunkCoordinate Coordinate { get; }
    public EnvironmentSurface Surface { get; }
    public int SamplesPerSide { get; }
    public int PackedByteCount => _data.Length;

    public static BiomeFieldRaster Create(BiomeField field, ChunkCoordinate coordinate,
        EnvironmentSurface surface = EnvironmentSurface.Natural)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (!Enum.IsDefined(surface)) throw new ArgumentOutOfRangeException(nameof(surface));
        var recipe = field.Recipe;
        if (coordinate.X < recipe.Origin.X || coordinate.Z < recipe.Origin.Z ||
            coordinate.X >= (long)recipe.Origin.X + recipe.WidthInChunks ||
            coordinate.Z >= (long)recipe.Origin.Z + recipe.HeightInChunks)
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        return new(field, coordinate, surface);
    }

    public bool TryGetSample(int x, int z, out EnvironmentSample sample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, SamplesPerSide);
        ArgumentOutOfRangeException.ThrowIfNegative(z);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(z, SamplesPerSide);
        ReadOnlySpan<byte> bytes = _data.AsSpan((z * SamplesPerSide + x) * BytesPerSample, BytesPerSample);
        sample = default;
        if (bytes[0] == 0) return false;
        sample = new(BinaryPrimitives.ReadInt32LittleEndian(bytes[2..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[6..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]),
            (EnvironmentSurface)bytes[1],
            new(BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[22..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[24..])));
        return true;
    }

    public WorldGenerationFingerprint ComputeFingerprint() =>
        WorldGenerationFingerprint.Gameplay(_recipe, CanonicalContent());

    public BiomeFieldDiagnosticReport CaptureDiagnostics() =>
        new(1, _recipe, _profile, Coordinate, Surface, SamplesPerSide, ComputeFingerprint().Sha256, GetStatistics());

    public BiomeFieldStatistics GetStatistics()
    {
        int available = 0, ecological = 0, water = 0, built = 0;
        long temperature = 0, moisture = 0, soil = 0;
        var totals = new long[5];
        var dominant = new int[5];
        for (int z = 0; z < SamplesPerSide; z++)
        for (int x = 0; x < SamplesPerSide; x++)
        {
            if (!TryGetSample(x, z, out var sample)) continue;
            available++;
            temperature += sample.TemperatureMilliCelsius;
            moisture += sample.Moisture;
            soil += sample.SoilFertility;
            if (sample.Surface == EnvironmentSurface.Water) water++;
            if (sample.Surface == EnvironmentSurface.Built) built++;
            if (sample.Weights.DominantBiome is not { } biome) continue;
            ecological++;
            dominant[(int)biome]++;
            for (int index = 0; index < 5; index++) totals[index] += sample.Weights[(BiomeKind)index];
        }
        double Div(long value) => ecological == 0 ? 0 : value / ((double)ecological * ushort.MaxValue);
        return new(SamplesPerSide * SamplesPerSide, available, ecological, water, built,
            available == 0 ? 0 : temperature / (double)available,
            available == 0 ? 0 : moisture / (double)available / ushort.MaxValue,
            available == 0 ? 0 : soil / (double)available / ushort.MaxValue,
            new(Div(totals[0]), Div(totals[1]), Div(totals[2]), Div(totals[3]), Div(totals[4])),
            new(dominant[0], dominant[1], dominant[2], dominant[3], dominant[4]));
    }

    private IEnumerable<WorldGenerationContent> CanonicalContent()
    {
        var header = new WorldCanonicalWriter();
        _profile.WriteTo(header);
        header.WriteInt32(Coordinate.X);
        header.WriteInt32(Coordinate.Z);
        header.WriteInt32(SamplesPerSide);
        header.WriteUInt32((uint)Surface);
        yield return new(WorldSampleCoordinate.FromChunk(Coordinate, 0, 0, SamplesPerSide),
            0x42494F50, 1, 0, header.ToArray());
        for (int z = 0; z < SamplesPerSide; z++)
            yield return new(WorldSampleCoordinate.FromChunk(Coordinate, 0, z, SamplesPerSide),
                0x42494F52, 1, 0, _data.AsSpan(z * SamplesPerSide * BytesPerSample, SamplesPerSide * BytesPerSample));
    }

    private static void WriteSample(Span<byte> bytes, EnvironmentSample sample)
    {
        bytes[0] = 1;
        bytes[1] = (byte)sample.Surface;
        BinaryPrimitives.WriteInt32LittleEndian(bytes[2..], sample.HeightMillimeters);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[6..], sample.TemperatureMilliCelsius);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[10..], sample.Slope);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[12..], sample.Moisture);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[14..], sample.SoilFertility);
        for (int index = 0; index < 5; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[(16 + index * 2)..], sample.Weights[(BiomeKind)index]);
    }
}

/// <summary>Bounded FIFO cache. Eviction changes work only; immutable raster values remain identical.</summary>
public sealed class BiomeFieldCache
{
    private readonly object _gate = new();
    private readonly BiomeField _field;
    private readonly int _capacity;
    private readonly Dictionary<(ChunkCoordinate, EnvironmentSurface), BiomeFieldRaster> _rasters = [];
    private readonly Queue<(ChunkCoordinate, EnvironmentSurface)> _order = [];

    public BiomeFieldCache(BiomeField field, int capacity = 8)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 32);
        _field = field;
        _capacity = capacity;
    }

    public int Count { get { lock (_gate) return _rasters.Count; } }

    public BiomeFieldRaster GetRaster(ChunkCoordinate coordinate, EnvironmentSurface surface = EnvironmentSurface.Natural)
    {
        var key = (coordinate, surface);
        lock (_gate)
        {
            if (_rasters.TryGetValue(key, out var existing)) return existing;
            // Serialize cold builds too, bounding transient memory by one additional raster per cache.
            var raster = BiomeFieldRaster.Create(_field, coordinate, surface);
            if (_rasters.Count == _capacity) _rasters.Remove(_order.Dequeue());
            _rasters.Add(key, raster);
            _order.Enqueue(key);
            return raster;
        }
    }
}
