namespace ForgeLine.World;

public enum ScatterClassification : byte { Tree = 1, Shrub = 2, Grass = 3, Rock = 4, IndustrialProp = 5 }

/// <summary>Physical sizes and conservative spacing are independent of presentation LOD.</summary>
public sealed record ScatterSpecies(string AssetId, ScatterClassification Classification,
    int WidthMillimeters, int HeightMillimeters, int DepthMillimeters, int RadiusMillimeters,
    ushort MinimumScalePermille, ushort MaximumScalePermille, ushort PivotHeightPermille,
    BiomeWeights Affinity, ushort MaximumSlope = 6000, ushort MinimumSoil = 0,
    ushort MinimumMoisture = 0, ushort MaximumMoisture = ushort.MaxValue,
    int MinimumTemperatureMilliCelsius = -50_000, int MaximumTemperatureMilliCelsius = 50_000);

/// <summary>Copied, bounded, versioned scatter rules. No mutable asset registry or render settings.</summary>
public sealed class EnvironmentScatterProfile
{
    private readonly ScatterSpecies[] _species;

    public EnvironmentScatterProfile(string id, IEnumerable<ScatterSpecies> species,
        int densityPermille = 650, int clusterPeriodMillimeters = 192_000,
        int maximumInstancesPerChunk = 4096, int maximumTotalInstances = 1_000_000, uint version = 1)
    {
        if (version != 1 || !WorldGenerationRecipe.IsProfileId(id) || densityPermille is < 0 or > 1000 ||
            clusterPeriodMillimeters is < 64_000 or > 1_024_000 ||
            maximumInstancesPerChunk is < 1 or > 4096 || maximumTotalInstances is < 1 or > 4_000_000)
            throw new ArgumentException("Unsupported scatter profile or budgets.");
        ArgumentNullException.ThrowIfNull(species);
        var entries = new List<ScatterSpecies>();
        foreach (var item in species)
        {
            if (item is null || entries.Count == 64) throw new ArgumentException("Scatter palette exceeds 64 entries.", nameof(species));
            if (!WorldGenerationRecipe.IsProfileId(item.AssetId) || !Enum.IsDefined(item.Classification) ||
                item.WidthMillimeters is < 1 or > 32_000 || item.HeightMillimeters is < 1 or > 64_000 ||
                item.DepthMillimeters is < 1 or > 32_000 || item.RadiusMillimeters is < 250 or > 16_000 ||
                item.MinimumScalePermille is < 250 or > 2000 || item.MaximumScalePermille < item.MinimumScalePermille ||
                item.MaximumScalePermille > 2000 || item.PivotHeightPermille > 1000 ||
                item.MinimumMoisture > item.MaximumMoisture || item.Affinity.Sum == 0 ||
                item.MinimumTemperatureMilliCelsius < -50_000 || item.MaximumTemperatureMilliCelsius > 50_000 ||
                item.MinimumTemperatureMilliCelsius > item.MaximumTemperatureMilliCelsius)
                throw new ArgumentException("Invalid scatter species.", nameof(species));
            // Radius covers the maximum-scale rotated horizontal mesh bounding circle.
            long radius = (long)item.RadiusMillimeters * 2000;
            long sizeSquared = (long)item.WidthMillimeters * item.WidthMillimeters + (long)item.DepthMillimeters * item.DepthMillimeters;
            if (radius * radius < sizeSquared * item.MaximumScalePermille * item.MaximumScalePermille)
                throw new ArgumentException("Spacing radius does not contain the maximum scaled footprint.", nameof(species));
            entries.Add(item);
        }
        _species = entries.OrderBy(s => s.AssetId, StringComparer.Ordinal).ToArray();
        if (_species.Length == 0 || _species.Select(s => s.AssetId).Distinct(StringComparer.Ordinal).Count() != _species.Length)
            throw new ArgumentException("A nonempty unique asset palette is required.", nameof(species));
        Id = id; Version = version; DensityPermille = densityPermille;
        ClusterPeriodMillimeters = clusterPeriodMillimeters;
        MaximumInstancesPerChunk = maximumInstancesPerChunk; MaximumTotalInstances = maximumTotalInstances;
        Species = Array.AsReadOnly(_species);
    }

    public string Id { get; }
    public uint Version { get; }
    public int DensityPermille { get; }
    public int ClusterPeriodMillimeters { get; }
    public int MaximumInstancesPerChunk { get; }
    public int MaximumTotalInstances { get; }
    public IReadOnlyList<ScatterSpecies> Species { get; }

    public static EnvironmentScatterProfile Temperate(int densityPermille = 650, int maximumTotalInstances = 1_000_000) => new(
        "scatter.temperate.v1",
        [
            new("mesh.world.vegetation.conifer", ScatterClassification.Tree, 6000, 18000, 6000, 5500, 800, 1200, 0, new(6000,100,800,0,400), MinimumSoil: 8000),
            new("mesh.world.environment.conifer_swept", ScatterClassification.Tree, 7000,18000,7000,6500,800,1200,0,new(3000,50,500,0,200),MinimumSoil:8000),
            new("mesh.world.vegetation.broadleaf", ScatterClassification.Tree, 10000,14000,10000,9000,800,1200,0,new(5000,400,100,0,700),MinimumSoil:12000,MinimumMoisture:18000),
            new("mesh.world.environment.broadleaf_asymmetric", ScatterClassification.Tree, 10000,14000,10000,9000,800,1200,0,new(2500,200,50,0,350),MinimumSoil:12000,MinimumMoisture:18000),
            new("mesh.world.vegetation.scrub", ScatterClassification.Shrub,3000,2200,3000,2600,800,1200,0,new(1200,900,2800,200,700),MinimumSoil:3000),
            new("mesh.world.vegetation.grass_clump", ScatterClassification.Grass,1000,800,1000,900,800,1200,0,new(1000,6000,1800,100,3000),MinimumSoil:1000),
            new("mesh.world.prop.rock", ScatterClassification.Rock,4000,2500,3500,3300,800,1200,500,new(200,300,1800,6000,100),MaximumSlope:30000),
            new("mesh.world.environment.rock_leaning",ScatterClassification.Rock,4500,2500,4000,3700,800,1200,500,new(100,150,900,3000,50),MaximumSlope:30000),
            new("mesh.world.prop.crate",ScatterClassification.IndustrialProp,2000,1800,2000,1800,850,1150,500,new(1000,1000,1000,1000,1000),MaximumSlope:1200),
            new("mesh.world.prop.drum",ScatterClassification.IndustrialProp,1200,1800,1200,1100,850,1150,500,new(1000,1000,1000,1000,1000),MaximumSlope:1200),
            new("mesh.world.prop.pallet",ScatterClassification.IndustrialProp,2500,400,2000,1900,850,1150,500,new(1000,1000,1000,1000,1000),MaximumSlope:1200)
        ], densityPermille, maximumTotalInstances: maximumTotalInstances);

    internal void WriteTo(WorldCanonicalWriter writer)
    {
        writer.WriteUInt32(Version); writer.WriteIdentifier(Id); writer.WriteInt32(DensityPermille);
        writer.WriteInt32(ClusterPeriodMillimeters); writer.WriteInt32(MaximumInstancesPerChunk); writer.WriteInt32(MaximumTotalInstances);
        writer.WriteInt32(_species.Length);
        foreach (var s in _species)
        {
            writer.WriteIdentifier(s.AssetId); writer.WriteUInt32((uint)s.Classification);
            foreach (int value in new[] { s.WidthMillimeters, s.HeightMillimeters, s.DepthMillimeters, s.RadiusMillimeters,
                s.MinimumScalePermille, s.MaximumScalePermille, s.PivotHeightPermille,
                s.Affinity.Forest, s.Affinity.Meadow, s.Affinity.DrySlope, s.Affinity.Rock, s.Affinity.WetDepression,
                s.MaximumSlope, s.MinimumSoil, s.MinimumMoisture, s.MaximumMoisture,
                s.MinimumTemperatureMilliCelsius, s.MaximumTemperatureMilliCelsius }) writer.WriteInt32(value);
        }
    }
}
