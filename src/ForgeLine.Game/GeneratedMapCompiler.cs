using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed record GeneratedMapBlob(int Bytes, string Sha256);
public sealed record GeneratedMapManifest(int FormatVersion, GeneratedMapRecipe Recipe,
    string AssetSha256, string ContentSha256, string GameplaySha256, string VisualSha256,
    string BiomeSha256, string DecorationSha256, long DecorationInstances,
    GeneratedMapBlob Geography, GeneratedMapBlob Terrain);

/// <summary>Offline compilation. No registration, renderer or filesystem is required.</summary>
public static class GeneratedMapCompiler
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true, MaxDepth = 24,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GeneratedMapPackage Compile(GeneratedMapRecipe recipe, string assetSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        recipe.Validate();
        RequireHash(assetSha256, "asset identity");
        cancellationToken.ThrowIfCancellationRequested();
        var output = ProceduralTerrainFactory.Create(recipe.Terrain);
        var terrain = output.Terrain;
        var grid = recipe.Terrain.Generation;
        var biomes = new BiomeField(grid, terrain);
        using var biomeHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var chunk in terrain.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            biomeHash.AppendData(Convert.FromHexString(BiomeFieldRaster.Create(biomes, chunk.Coordinate).ComputeFingerprint().Sha256));
        }
        string biomeSha = Convert.ToHexStringLower(biomeHash.GetHashAndReset());
        var result = ProceduralStrategicLayoutGenerator.Generate(terrain, grid, cancellationToken: cancellationToken);
        if (result.Layout is not { } layout)
            throw new InvalidDataException("Generated map qualification failed: " + string.Join("; ", result.Attempts.Select(a => a.Code + ": " + a.Message)));
        var map = WithTacticalAreas(layout.Definition);
        // The complete gameplay geography passes the same gates after tactical composition.
        BattlefieldValidator.ValidateDefinition(map);
        var geography = BattlefieldOperationalGeographyValidator.Validate(map, terrain);
        var audit = StrategicMapAudit.Analyze(map, terrain, cancellationToken: cancellationToken);
        if (audit.ErrorCount != 0 || audit.WarningCount != 0)
            throw new InvalidDataException("Generated map failed the complete strategic audit.");

        byte[] geographyBytes = Serialize(new
        {
            FormatVersion = 1, map.Metadata, map.Starts, map.Resources, map.Sites,
            map.RoadNodes, map.RoadEdges, map.Crossings, map.Objectives,
            map.StaticNavigationObstacles, TacticalAreas = map.TacticalAreas.Areas
        });
        if (geographyBytes.Length > BattlefieldMapArtifact.MaximumInputBytes)
            throw new InvalidDataException("Generated geography exceeds its budget.");
        byte[] terrainBytes = EncodeTerrain(terrain);
        var content = CentralDivideScenario.CreateContent();
        string contentSha = ScenarioContentIdentity.ComputeSha256(content);
        var geographyBlob = Blob(geographyBytes);
        var terrainBlob = Blob(terrainBytes);
        string gameplay = Hash(Serialize(new
        {
            recipe.GeneratorVersion, TerrainRecipe = recipe.Terrain, recipe.TacticalProfileId,
            ContentSha256 = contentSha, Geography = geographyBlob, Terrain = terrainBlob,
            LayoutSha256 = layout.Fingerprint.Sha256
        }));
        var clearance = new BattlefieldScatterClearance(map, layout.Reservations);
        var scatter = new EnvironmentScatterGenerator(grid,
            EnvironmentScatterProfile.Temperate(recipe.ScatterDensityPermille, recipe.MaximumScatterInstances), biomes, clearance);
        using var decorationHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long instances = 0;
        foreach (var chunk in terrain.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decoration = scatter.GenerateChunk(chunk.Coordinate);
            instances += decoration.Instances.Length;
            decorationHash.AppendData(Convert.FromHexString(decoration.Sha256));
        }
        string decorationSha = Convert.ToHexStringLower(decorationHash.GetHashAndReset());
        string visual = Hash(Serialize(new
        {
            GameplaySha256 = gameplay, recipe.MaterialProfileId, recipe.ScatterProfileId,
            recipe.ScatterDensityPermille, recipe.MaximumScatterInstances,
            AssetSha256 = assetSha256, BiomeSha256 = biomeSha, DecorationSha256 = decorationSha
        }));
        var manifest = new GeneratedMapManifest(1, recipe, assetSha256, contentSha, gameplay, visual,
            biomeSha, decorationSha, instances, geographyBlob, terrainBlob);
        return new(manifest, map, terrain, biomes, clearance, geographyBytes, terrainBytes, geography, audit);
    }

    private static BattlefieldDefinition WithTacticalAreas(BattlefieldDefinition map)
    {
        // Cover belongs to explicit site geography; decoration has no gameplay meaning.
        var areas = map.Sites.Select(site => new TacticalAreaDefinition("cover." + site.Key,
            site.DisplayName + " cover", new AxisAlignedBounds(
                new Vector3(site.BuildArea.Minimum.X, -128, site.BuildArea.Minimum.Z - 16),
                new Vector3(site.BuildArea.Maximum.X, 256, site.BuildArea.Minimum.Z - 4)), .75, false));
        return Copy(map, new TacticalAreaSet(areas));
    }

    internal static BattlefieldDefinition Copy(BattlefieldDefinition map, TacticalAreaSet? areas = null) => new(
        map.Metadata, map.Starts.ToArray(), map.Resources.ToArray(), [], map.Sites.ToArray(),
        map.RoadNodes.ToArray(), map.RoadEdges.ToArray(), map.Crossings.ToArray(), map.Objectives.ToArray(),
        map.StaticNavigationObstacles.ToArray(), map.TerrainVisual with
        { ProfileId = "terrain.biome.v1", MaterialAssetIds = CentralDivideBattlefield.Create().TerrainVisual.MaterialAssetIds.ToArray() })
        { MapRevision = map.MapRevision, TacticalAreas = areas ?? map.TacticalAreas };

    private static byte[] EncodeTerrain(TerrainWorld terrain)
    {
        // Fixed little-endian IEEE samples retain the exact ITerrainQuery heights, including shared edges.
        var bytes = new byte[checked(8 + terrain.Chunks.Count * (8 + 65 * 65 * 4))];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x31544746); // FGT1
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), terrain.Chunks.Count);
        int offset = 8;
        foreach (var chunk in terrain.Chunks)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), chunk.Coordinate.X);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 4), chunk.Coordinate.Z);
            offset += 8;
            for (int z = 0; z < 65; z++) for (int x = 0; x < 65; x++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), chunk.Heightfield.GetHeight(x, z));
                offset += 4;
            }
        }
        return bytes;
    }

    internal static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static GeneratedMapBlob Blob(byte[] bytes) => new(bytes.Length, Hash(bytes));
    internal static void RequireHash(string? value, string name)
    {
        if (value is not { Length: 64 } || value.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid SHA-256 " + name + ".");
    }
}
