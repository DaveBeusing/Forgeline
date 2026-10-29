using System.Text.Json;
using ForgeLine.World;

namespace ForgeLine.Game;

/// <summary>A verified map plus immutable, reproducible content; callers receive owned definitions.</summary>
public sealed class GeneratedMapPackage
{
    public const string ManifestFileName = "map.flgmap.json";
    public const string GeographyFileName = "geography.json";
    public const string TerrainFileName = "terrain.bin";
    public const int MaximumManifestBytes = 64 * 1024;
    public const int MaximumTerrainBytes = 8 + 256 * (8 + 65 * 65 * 4);
    private readonly BattlefieldDefinition _map;
    private readonly TerrainWorld _terrain;
    private readonly BiomeField _biomes;
    private readonly BattlefieldScatterClearance _clearance;
    private readonly byte[] _geographyBytes, _terrainBytes, _manifestBytes;

    internal GeneratedMapPackage(GeneratedMapManifest manifest, BattlefieldDefinition map, TerrainWorld terrain,
        BiomeField biomes, BattlefieldScatterClearance clearance, byte[] geographyBytes, byte[] terrainBytes,
        BattlefieldOperationalGeographyReport geography, StrategicMapAuditReport audit)
    {
        Manifest = manifest; _map = map; _terrain = terrain; _biomes = biomes; _clearance = clearance;
        _geographyBytes = geographyBytes; _terrainBytes = terrainBytes;
        _manifestBytes = GeneratedMapCompiler.Serialize(manifest);
        ManifestSha256 = GeneratedMapCompiler.Hash(_manifestBytes);
        Geography = geography; Audit = audit;
    }

    public GeneratedMapManifest Manifest { get; }
    public string ManifestSha256 { get; }
    public string CompositionKey => "generated.skirmish." + Manifest.GameplaySha256;
    public BattlefieldDefinition Definition => GeneratedMapCompiler.Copy(_map);
    public BattlefieldOperationalGeographyReport Geography { get; }
    public StrategicMapAuditReport Audit { get; }
    public byte[] GetManifestBytes() => _manifestBytes.ToArray();
    public TerrainWorld CreateTerrain() => new(_terrain.Settings, _terrain.Chunks);
    public IEnvironmentQuery Environment => _biomes;
    public EnvironmentScatterChunk GenerateDecoration(ChunkCoordinate coordinate) => new EnvironmentScatterGenerator(
        Manifest.Recipe.Terrain.Generation, EnvironmentScatterProfile.Temperate(
            Manifest.Recipe.ScatterDensityPermille, Manifest.Recipe.MaximumScatterInstances), _biomes, _clearance).GenerateChunk(coordinate);

    public MatchComposition CreateComposition() => new(CompositionKey, Definition, CentralDivideScenario.CreateContent(),
        _ => CreateTerrain(),
        static (entities, inventories, factory, world, start, catalogs, stock) =>
            CentralDivideStartingBaseFactory.Create(entities, inventories, factory, world, start, stock, catalogs.Units, catalogs.Buildings),
        static systems => systems)
    {
        Identity = new(_map.Metadata.Key, _map.MapRevision, Manifest.GameplaySha256, 1,
            ScenarioContentIdentity.DirectorateKey, Manifest.ContentSha256,
            ScenarioContentIdentity.StartRule, ScenarioContentIdentity.ObjectiveRule)
        {
            Generated = new(Manifest.Recipe.GeneratorVersion, Manifest.Recipe.Terrain.Generation.Seed,
                Manifest.GameplaySha256, Manifest.VisualSha256, ManifestSha256, Manifest.AssetSha256)
        }
    };

    /// <summary>Create-only output. The manifest is written last; incomplete exports cannot load.</summary>
    public void Write(string directory)
    {
        string root = Path.GetFullPath(directory);
        CheckPath(root);
        Directory.CreateDirectory(root);
        foreach (string name in new[] { GeographyFileName, TerrainFileName, ManifestFileName })
            if (File.Exists(Path.Combine(root, name)) || Directory.Exists(Path.Combine(root, name)))
                throw new IOException("Generated map output already exists: " + name);
        WriteNew(Path.Combine(root, GeographyFileName), _geographyBytes);
        WriteNew(Path.Combine(root, TerrainFileName), _terrainBytes);
        WriteNew(Path.Combine(root, ManifestFileName), _manifestBytes);
    }

    /// <summary>Asset identity is supplied by the host, never trusted from the package itself.</summary>
    public static GeneratedMapPackage Load(string directory, string expectedAssetSha256,
        string? expectedManifestSha256 = null, CancellationToken cancellationToken = default)
    {
        GeneratedMapCompiler.RequireHash(expectedAssetSha256, "expected asset identity");
        if (expectedManifestSha256 is not null) GeneratedMapCompiler.RequireHash(expectedManifestSha256, "expected manifest identity");
        string root = Path.GetFullPath(directory);
        byte[] header = ReadBounded(Path.Combine(root, ManifestFileName), MaximumManifestBytes);
        if (expectedManifestSha256 is not null && GeneratedMapCompiler.Hash(header) != expectedManifestSha256)
            throw new InvalidDataException("Generated map manifest integrity differs from the requested identity.");
        GeneratedMapManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<GeneratedMapManifest>(header, GeneratedMapCompiler.JsonOptions)
                ?? throw new InvalidDataException("Generated map manifest is empty.");
        }
        catch (JsonException error) { throw new InvalidDataException("Invalid generated map manifest.", error); }
        if (manifest.FormatVersion != 1 || manifest.Recipe is null)
            throw new InvalidDataException("Unsupported generated map format version or missing recipe.");
        try { manifest.Recipe.Validate(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { throw new InvalidDataException("Invalid generated map recipe.", error); }
        if (!header.AsSpan().SequenceEqual(GeneratedMapCompiler.Serialize(manifest)))
            throw new InvalidDataException("Generated map manifest is not canonical.");
        if (manifest.AssetSha256 != expectedAssetSha256)
            throw new InvalidDataException("Generated map asset identity conflicts with this host.");
        foreach (string? hash in new[] { manifest.ContentSha256, manifest.GameplaySha256, manifest.VisualSha256,
            manifest.BiomeSha256, manifest.DecorationSha256 }) GeneratedMapCompiler.RequireHash(hash, "manifest content identity");
        var grid = manifest.Recipe.Terrain.Generation;
        if (manifest.DecorationInstances < 0 || manifest.DecorationInstances > manifest.Recipe.MaximumScatterInstances ||
            manifest.Terrain is null || manifest.Terrain.Bytes != 8 + grid.WidthInChunks * grid.HeightInChunks * (8 + 65 * 65 * 4))
            throw new InvalidDataException("Generated map counts or terrain length do not match the recipe.");
        VerifyBlob(root, GeographyFileName, manifest.Geography, BattlefieldMapArtifact.MaximumInputBytes);
        VerifyBlob(root, TerrainFileName, manifest.Terrain, MaximumTerrainBytes);
        // Never deserialize untrusted geography/height arrays into a match. Rebuild with compiled rules,
        // including qualification, then require the exact canonical package identity.
        var rebuilt = GeneratedMapCompiler.Compile(manifest.Recipe, expectedAssetSha256, cancellationToken);
        if (!header.AsSpan().SequenceEqual(rebuilt._manifestBytes))
            throw new InvalidDataException("Generated map content does not match its recipe, generator or rules.");
        return rebuilt;
    }

    private static void VerifyBlob(string root, string name, GeneratedMapBlob? blob, int limit)
    {
        if (blob is null || blob.Bytes < 1 || blob.Bytes > limit)
            throw new InvalidDataException("Generated map sidecar exceeds its budget: " + name);
        GeneratedMapCompiler.RequireHash(blob.Sha256, name);
        byte[] bytes = ReadBounded(Path.Combine(root, name), blob.Bytes);
        if (bytes.Length != blob.Bytes || GeneratedMapCompiler.Hash(bytes) != blob.Sha256)
            throw new InvalidDataException("Generated map sidecar integrity failed: " + name);
    }

    private static byte[] ReadBounded(string path, int limit)
    {
        CheckPath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1 || stream.Length > limit) throw new InvalidDataException("Generated map input exceeds its budget: " + Path.GetFileName(path));
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new IOException("Generated map input changed while reading.");
        return bytes;
    }
    private static void CheckPath(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Generated map paths cannot traverse symbolic links or reparse points.");
    }
    private static void WriteNew(string path, byte[] bytes)
    {
        CheckPath(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }
}
