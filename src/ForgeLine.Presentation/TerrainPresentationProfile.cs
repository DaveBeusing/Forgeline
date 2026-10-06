using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;

namespace ForgeLine.Presentation;

public enum TerrainMaterialSlot : byte
{
    GrassGround = 0,
    Dirt = 1,
    Mud = 2,
    Rock = 3,
    Gravel = 4,
    IndustrialGround = 5,
    Concrete = 6,
    Scorched = 7
}

public readonly record struct TerrainMaterialDefinition(
    TerrainMaterialSlot Slot,
    string AssetId,
    Vector3 BaseColor,
    float Roughness,
    float Metallic,
    string? BaseColorTextureAssetId = null,
    TerrainSurfaceTexture? BaseColorTexture = null,
    float TextureTileMeters = 128.0f);

public readonly record struct TerrainBlendRegion(
    TerrainMaterialSlot Slot,
    Vector2 Center,
    Vector2 HalfExtents,
    float FeatherMeters);

public sealed class TerrainPresentationProfile
{
    public const int MaterialSlotCount = 8;

    private const float TextureInfluence = 0.72f;
    private const float SecondaryTextureInfluence = 0.18f;

    private readonly Dictionary<TerrainMaterialSlot, TerrainMaterialDefinition> _materials;
    private readonly TerrainBlendRegion[] _regions;

    public TerrainPresentationProfile(
        IEnumerable<TerrainMaterialDefinition> materials,
        IEnumerable<TerrainBlendRegion>? regions = null)
    {
        ArgumentNullException.ThrowIfNull(materials);

        _materials =
            materials.ToDictionary(
                static material => material.Slot);
        _regions =
            regions?.ToArray() ??
            [];

        FallbackMaterial =
            _materials.TryGetValue(
                TerrainMaterialSlot.Dirt,
                out TerrainMaterialDefinition dirt)
                ? dirt
                : new TerrainMaterialDefinition(
                    TerrainMaterialSlot.Dirt,
                    "material.world.terrain.dirt",
                    new Vector3(0.34f, 0.28f, 0.20f),
                    0.88f,
                    0.0f);
    }

    public TerrainMaterialDefinition FallbackMaterial { get; }

    public TerrainMaterialDefinition GetMaterial(
        TerrainMaterialSlot slot) =>
        _materials.TryGetValue(
            slot,
            out TerrainMaterialDefinition material)
            ? material
            : FallbackMaterial;

    public void CalculateMaterialWeights(
        Vector3 position,
        Vector3 normal,
        Span<float> destination)
    {
        if (destination.Length < MaterialSlotCount)
        {
            throw new ArgumentException(
                $"Terrain material weights require at least {MaterialSlotCount} values.",
                nameof(destination));
        }

        Span<float> weights =
            destination[..MaterialSlotCount];
        weights.Clear();

        Vector3 normalized =
            normal.LengthSquared() > 0.0001f
                ? Vector3.Normalize(normal)
                : Vector3.UnitY;
        float slope =
            Math.Clamp(
                1.0f - normalized.Y,
                0.0f,
                1.0f);
        float elevation =
            Math.Clamp(
                (position.Y + 20.0f) / 80.0f,
                0.0f,
                1.0f);

        float dirtBlend =
            Math.Clamp(
                elevation * 0.45f +
                slope * 0.45f,
                0.0f,
                1.0f);
        weights[(int)TerrainMaterialSlot.GrassGround] =
            1.0f - dirtBlend;
        weights[(int)TerrainMaterialSlot.Dirt] =
            dirtBlend;

        float mudBlend =
            Math.Clamp(
                (0.28f - elevation) *
                1.4f,
                0.0f,
                0.55f);
        ScaleWeights(
            weights,
            1.0f - mudBlend);
        weights[(int)TerrainMaterialSlot.Mud] +=
            mudBlend;

        float rockBlend =
            Math.Clamp(
                slope *
                2.1f,
                0.0f,
                1.0f);
        ScaleWeights(
            weights,
            1.0f - rockBlend);
        weights[(int)TerrainMaterialSlot.Rock] +=
            rockBlend;

        Vector2 point =
            new(
                position.X,
                position.Z);

        for (int index = 0;
             index < _regions.Length;
             index++)
        {
            TerrainBlendRegion region =
                _regions[index];
            float weight =
                CalculateRegionWeight(
                    point,
                    region);

            if (weight <= 0.0f)
            {
                continue;
            }

            ScaleWeights(
                weights,
                1.0f - weight);
            weights[(int)region.Slot] +=
                weight;
        }

        NormalizeWeights(
            weights);
    }

    public IReadOnlyList<TerrainMaterialDefinition> Materials =>
        _materials
            .OrderBy(static pair => pair.Key)
            .Select(static pair => pair.Value)
            .ToArray();

    public Vector4 SampleBaseColor(
        Vector3 position,
        Vector3 normal)
    {
        Vector3 normalized =
            normal.LengthSquared() > 0.0001f
                ? Vector3.Normalize(normal)
                : Vector3.UnitY;
        float slope =
            Math.Clamp(
                1.0f - normalized.Y,
                0.0f,
                1.0f);
        float elevation =
            Math.Clamp(
                (position.Y + 20.0f) / 80.0f,
                0.0f,
                1.0f);

        Vector3 grass =
            SampleMaterialColor(
                GetMaterial(
                    TerrainMaterialSlot.GrassGround),
                position);
        Vector3 dirt =
            SampleMaterialColor(
                GetMaterial(
                    TerrainMaterialSlot.Dirt),
                position);
        Vector3 mud =
            SampleMaterialColor(
                GetMaterial(
                    TerrainMaterialSlot.Mud),
                position);
        Vector3 rock =
            SampleMaterialColor(
                GetMaterial(
                    TerrainMaterialSlot.Rock),
                position);

        Vector3 color =
            Vector3.Lerp(
                grass,
                dirt,
                Math.Clamp(
                    elevation * 0.45f +
                    slope * 0.45f,
                    0.0f,
                    1.0f));
        color =
            Vector3.Lerp(
                color,
                mud,
                Math.Clamp(
                    (0.28f - elevation) *
                    1.4f,
                    0.0f,
                    0.55f));
        color =
            Vector3.Lerp(
                color,
                rock,
                Math.Clamp(
                    slope *
                    2.1f,
                    0.0f,
                    1.0f));

        Vector2 point =
            new(
                position.X,
                position.Z);

        for (int index = 0;
             index < _regions.Length;
             index++)
        {
            TerrainBlendRegion region =
                _regions[index];
            float weight =
                CalculateRegionWeight(
                    point,
                    region);

            if (weight <= 0.0f)
            {
                continue;
            }

            color =
                Vector3.Lerp(
                    color,
                    SampleMaterialColor(
                        GetMaterial(
                            region.Slot),
                        position),
                    weight);
        }

        return new Vector4(
            Vector3.Clamp(
                color,
                Vector3.Zero,
                Vector3.One),
            1.0f);
    }

    public static TerrainPresentationProfile CreateCentralDivide(
        RuntimeAssetCatalog? runtimeAssets = null)
    {
        TerrainMaterialDefinition[] defaults =
        [
            new(
                TerrainMaterialSlot.GrassGround,
                "material.world.terrain.grass_ground",
                new Vector3(0.29f, 0.39f, 0.22f),
                0.90f,
                0.0f,
                TextureTileMeters: 160.0f),
            new(
                TerrainMaterialSlot.Dirt,
                "material.world.terrain.dirt",
                new Vector3(0.42f, 0.32f, 0.21f),
                0.92f,
                0.0f,
                TextureTileMeters: 128.0f),
            new(
                TerrainMaterialSlot.Mud,
                "material.world.terrain.mud",
                new Vector3(0.29f, 0.25f, 0.18f),
                0.98f,
                0.0f,
                TextureTileMeters: 112.0f),
            new(
                TerrainMaterialSlot.Rock,
                "material.world.terrain.rock",
                new Vector3(0.46f, 0.47f, 0.44f),
                0.82f,
                0.0f,
                TextureTileMeters: 96.0f),
            new(
                TerrainMaterialSlot.Gravel,
                "material.world.terrain.gravel",
                new Vector3(0.50f, 0.47f, 0.40f),
                0.88f,
                0.0f,
                TextureTileMeters: 80.0f),
            new(
                TerrainMaterialSlot.IndustrialGround,
                "material.world.terrain.industrial_ground",
                new Vector3(0.37f, 0.38f, 0.35f),
                0.78f,
                0.05f,
                TextureTileMeters: 112.0f),
            new(
                TerrainMaterialSlot.Concrete,
                "material.world.terrain.concrete",
                new Vector3(0.56f, 0.56f, 0.52f),
                0.84f,
                0.0f,
                TextureTileMeters: 96.0f),
            new(
                TerrainMaterialSlot.Scorched,
                "material.world.terrain.scorched",
                new Vector3(0.20f, 0.17f, 0.14f),
                0.96f,
                0.0f,
                TextureTileMeters: 112.0f)
        ];

        TerrainMaterialDefinition[] materials =
            defaults
                .Select(
                    material =>
                        ResolveRuntimeMaterial(
                            runtimeAssets,
                            material))
                .ToArray();

        return new TerrainPresentationProfile(
            materials,
            [
                new(
                    TerrainMaterialSlot.Gravel,
                    new Vector2(
                        1_536.0f,
                        1_536.0f),
                    new Vector2(
                        1_120.0f,
                        32.0f),
                    24.0f),
                new(
                    TerrainMaterialSlot.Concrete,
                    new Vector2(
                        1_536.0f,
                        920.0f),
                    new Vector2(
                        78.0f,
                        58.0f),
                    12.0f),
                new(
                    TerrainMaterialSlot.IndustrialGround,
                    new Vector2(
                        650.0f,
                        520.0f),
                    new Vector2(
                        150.0f,
                        120.0f),
                    28.0f),
                new(
                    TerrainMaterialSlot.IndustrialGround,
                    new Vector2(
                        2_420.0f,
                        2_560.0f),
                    new Vector2(
                        150.0f,
                        120.0f),
                    28.0f),
                new(
                    TerrainMaterialSlot.Scorched,
                    new Vector2(
                        1_610.0f,
                        1_690.0f),
                    new Vector2(
                        90.0f,
                        80.0f),
                    35.0f)
            ]);
    }

    private static TerrainMaterialDefinition ResolveRuntimeMaterial(
        RuntimeAssetCatalog? runtimeAssets,
        TerrainMaterialDefinition fallback)
    {
        if (runtimeAssets is null)
        {
            return fallback;
        }

        AssetId id =
            AssetId.Parse(
                fallback.AssetId);

        if (!runtimeAssets.TryGet(
                id,
                out RuntimeAssetRecord? record) ||
            record is null ||
            record.Type != RuntimeAssetType.Material)
        {
            return fallback;
        }

        try
        {
            RuntimeAssetContent content =
                runtimeAssets.Read(
                    id);
            using JsonDocument document =
                JsonDocument.Parse(
                    content.Payload);
            JsonElement root =
                document.RootElement;

            if (!TryReadBaseColor(
                    root,
                    out Vector3 baseColor) ||
                !TryReadUnitFactor(
                    root,
                    "roughnessFactor",
                    out float roughness) ||
                !TryReadUnitFactor(
                    root,
                    "metallicFactor",
                    out float metallic))
            {
                return fallback;
            }

            string? textureAssetId =
                TryReadTextureAssetId(
                    root,
                    "baseColorTexture");
            TerrainSurfaceTexture? texture =
                TryResolveTexture(
                    runtimeAssets,
                    textureAssetId);

            return fallback with
            {
                BaseColor = baseColor,
                Roughness = roughness,
                Metallic = metallic,
                BaseColorTextureAssetId =
                    texture is null
                        ? null
                        : textureAssetId,
                BaseColorTexture =
                    texture
            };
        }
        catch (IOException)
        {
            return fallback;
        }
        catch (InvalidDataException)
        {
            return fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (ArgumentException)
        {
            return fallback;
        }
        catch (OverflowException)
        {
            return fallback;
        }
    }

    private static TerrainSurfaceTexture? TryResolveTexture(
        RuntimeAssetCatalog runtimeAssets,
        string? textureAssetId)
    {
        if (string.IsNullOrWhiteSpace(
                textureAssetId) ||
            !AssetId.TryParse(
                textureAssetId,
                out AssetId id) ||
            !runtimeAssets.TryGet(
                id,
                out RuntimeAssetRecord? record) ||
            record is null ||
            record.Type != RuntimeAssetType.Texture)
        {
            return null;
        }

        RuntimeAssetContent content =
            runtimeAssets.Read(
                id);

        return TerrainSurfaceTexture.FromRuntimeAsset(
            content);
    }

    private static Vector3 SampleMaterialColor(
        in TerrainMaterialDefinition material,
        Vector3 position)
    {
        TerrainSurfaceTexture? texture =
            material.BaseColorTexture;

        if (texture is null)
        {
            return material.BaseColor;
        }

        Vector2 point =
            new(
                position.X,
                position.Z);
        Vector3 primary =
            texture.SampleWorld(
                point,
                material.TextureTileMeters);

        Vector2 secondaryPoint =
            new(
                point.X *
                    0.43f +
                material.TextureTileMeters *
                    0.37f,
                point.Y *
                    0.43f -
                material.TextureTileMeters *
                    0.29f);
        Vector3 secondary =
            texture.SampleWorld(
                secondaryPoint,
                material.TextureTileMeters *
                2.35f);
        Vector3 sampled =
            Vector3.Lerp(
                primary,
                secondary,
                SecondaryTextureInfluence);

        return Vector3.Lerp(
            material.BaseColor,
            sampled,
            TextureInfluence);
    }

    private static bool TryReadBaseColor(
        JsonElement root,
        out Vector3 baseColor)
    {
        baseColor = default;

        if (!root.TryGetProperty(
                "baseColorFactor",
                out JsonElement factor) ||
            factor.ValueKind !=
            JsonValueKind.Array ||
            factor.GetArrayLength() != 4)
        {
            return false;
        }

        JsonElement.ArrayEnumerator values =
            factor.EnumerateArray();
        Span<float> channels =
            stackalloc float[4];
        int index = 0;

        foreach (JsonElement value in values)
        {
            if (!value.TryGetSingle(
                    out float channel) ||
                !float.IsFinite(
                    channel) ||
                channel < 0.0f ||
                channel > 1.0f)
            {
                return false;
            }

            channels[index++] =
                channel;
        }

        baseColor =
            new Vector3(
                channels[0],
                channels[1],
                channels[2]);
        return true;
    }

    private static bool TryReadUnitFactor(
        JsonElement root,
        string propertyName,
        out float value)
    {
        value = default;

        return root.TryGetProperty(
                propertyName,
                out JsonElement element) &&
            element.TryGetSingle(
                out value) &&
            float.IsFinite(
                value) &&
            value >= 0.0f &&
            value <= 1.0f;
    }

    private static string? TryReadTextureAssetId(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(
                propertyName,
                out JsonElement element) ||
            element.ValueKind !=
            JsonValueKind.String)
        {
            return null;
        }

        string? value =
            element.GetString();

        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value;
    }

    private static void ScaleWeights(
        Span<float> weights,
        float factor)
    {
        for (int index = 0;
             index < weights.Length;
             index++)
        {
            weights[index] *=
                factor;
        }
    }

    private static void NormalizeWeights(
        Span<float> weights)
    {
        float total = 0.0f;

        for (int index = 0;
             index < weights.Length;
             index++)
        {
            float value =
                float.IsFinite(weights[index])
                    ? MathF.Max(weights[index], 0.0f)
                    : 0.0f;
            weights[index] =
                value;
            total +=
                value;
        }

        if (total <= 1e-6f)
        {
            weights.Clear();
            weights[(int)TerrainMaterialSlot.Dirt] =
                1.0f;
            return;
        }

        float reciprocal =
            1.0f /
            total;

        for (int index = 0;
             index < weights.Length;
             index++)
        {
            weights[index] *=
                reciprocal;
        }
    }

    private static float CalculateRegionWeight(
        Vector2 point,
        in TerrainBlendRegion region)
    {
        Vector2 distance =
            Vector2.Abs(
                point -
                region.Center) -
            region.HalfExtents;
        float dx =
            MathF.Max(
                distance.X,
                0.0f);
        float dz =
            MathF.Max(
                distance.Y,
                0.0f);
        float outside =
            MathF.Sqrt(
                dx *
                dx +
                dz *
                dz);

        if (outside <= 0.0f)
        {
            return 0.92f;
        }

        if (region.FeatherMeters <=
                0.0f ||
            outside >=
                region.FeatherMeters)
        {
            return 0.0f;
        }

        return 0.92f *
            (1.0f -
             outside /
             region.FeatherMeters);
    }
}
