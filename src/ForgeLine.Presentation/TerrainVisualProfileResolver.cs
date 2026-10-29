using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public static class TerrainVisualProfileResolver
{
    public const string BiomeProfileId = "terrain.biome.v1";

    public static BattlefieldTerrainVisualDefinition CreateBiomeDefinition() => new(
        BiomeProfileId, BattlefieldTerrainControlEncoding.RgbaFourLayer, 4, 33,
        TerrainPresentationProfile.CreateCentralDivide().Materials.Select(m => m.AssetId).ToArray());

    public static TerrainPresentationProfile Resolve(BattlefieldTerrainVisualDefinition visual, TerrainWorld world,
        IEnvironmentQuery? environment = null, TerrainInfrastructureMask? infrastructure = null,
        RuntimeAssetCatalog? runtimeAssets = null)
    {
        ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(world);
        visual.Validate();
        var materials = TerrainPresentationProfile.CreateCentralDivide(runtimeAssets).Materials;
        if (visual.ControlSamplesPerSide != 33 || !visual.MaterialAssetIds.SequenceEqual(materials.Select(m => m.AssetId), StringComparer.Ordinal))
            throw new InvalidOperationException("Terrain visual profile requires the unchanged eight-material, four-layer contract.");
        if (visual.ProfileId is "central_divide.production" or "split_basin.production")
        {
            if (environment is not null || infrastructure is not null)
                throw new ArgumentException("Authored profile IDs cannot silently opt into procedural inputs.", nameof(visual));
            return TerrainPresentationProfile.CreateCentralDivide(runtimeAssets);
        }
        if (visual.ProfileId != BiomeProfileId)
            throw new InvalidOperationException("Unsupported terrain visual profile ID.");
        ArgumentNullException.ThrowIfNull(environment);
        return new TerrainPresentationProfile(materials, [], [],
            new BiomeTerrainMaterialSource(world, environment, infrastructure ?? TerrainInfrastructureMask.Empty));
    }

    /// <summary>Reads existing outer heightfield samples; does not alter the world's exclusive query ownership.</summary>
    public static IEnvironmentQuery CreateEnvironment(TerrainWorld world, WorldGenerationRecipe recipe,
        BiomeFieldProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(recipe);
        recipe.Validate();
        var coordinates = world.Chunks.Select(c => c.Coordinate).ToArray();
        if (coordinates.Length != (long)recipe.WidthInChunks * recipe.HeightInChunks ||
            coordinates.Min(c => c.X) != recipe.Origin.X || coordinates.Min(c => c.Z) != recipe.Origin.Z ||
            coordinates.Max(c => c.X) != (long)recipe.Origin.X + recipe.WidthInChunks - 1 ||
            coordinates.Max(c => c.Z) != (long)recipe.Origin.Z + recipe.HeightInChunks - 1 ||
            world.Settings.ChunkSizeMeters * 1000.0 != recipe.ChunkSizeMillimeters ||
            world.Chunks.Any(c => c.Heightfield.SamplesPerSide != recipe.SamplesPerSide))
            throw new ArgumentException("Environment recipe must match the complete supplied terrain rectangle.", nameof(recipe));
        return new BiomeField(recipe, new ClosedTerrainQuery(world), profile);
    }

    private sealed class ClosedTerrainQuery(TerrainWorld world) : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => world.WorldBounds;

        public bool TrySampleHeight(float x, float z, out float height)
        {
            if (!TryLocate(x, z, out var chunk, out float localX, out float localZ)) { height = 0; return false; }
            height = chunk.Heightfield.SampleHeight(localX, localZ);
            return true;
        }
        public bool TrySampleNormal(float x, float z, out System.Numerics.Vector3 normal)
        {
            if (!TryLocate(x, z, out var chunk, out float localX, out float localZ)) { normal = default; return false; }
            normal = chunk.Heightfield.SampleNormal(localX, localZ);
            return true;
        }
        private bool TryLocate(float x, float z, out TerrainChunk chunk, out float localX, out float localZ)
        {
            chunk = null!; localX = 0; localZ = 0;
            if (!float.IsFinite(x) || !float.IsFinite(z) || x < WorldBounds.Minimum.X || x > WorldBounds.Maximum.X ||
                z < WorldBounds.Minimum.Z || z > WorldBounds.Maximum.Z) return false;
            float size = world.Settings.ChunkSizeMeters;
            int cx = checked((int)MathF.Floor(x / size));
            int cz = checked((int)MathF.Floor(z / size));
            if (x == WorldBounds.Maximum.X) cx = checked(cx - 1);
            if (z == WorldBounds.Maximum.Z) cz = checked(cz - 1);
            if (!world.TryGetChunk(new(cx, cz), out chunk)) return false;
            localX = Math.Clamp(x - chunk.Bounds.Minimum.X, 0, size);
            localZ = Math.Clamp(z - chunk.Bounds.Minimum.Z, 0, size);
            return true;
        }
    }
}
