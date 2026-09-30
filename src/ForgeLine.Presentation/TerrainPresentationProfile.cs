using System.Numerics;

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
    float Metallic);

public readonly record struct TerrainBlendRegion(
    TerrainMaterialSlot Slot,
    Vector2 Center,
    Vector2 HalfExtents,
    float FeatherMeters);

public sealed class TerrainPresentationProfile
{
    private readonly IReadOnlyDictionary<TerrainMaterialSlot, TerrainMaterialDefinition> _materials;
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
            GetMaterial(
                TerrainMaterialSlot.GrassGround).BaseColor;
        Vector3 dirt =
            GetMaterial(
                TerrainMaterialSlot.Dirt).BaseColor;
        Vector3 mud =
            GetMaterial(
                TerrainMaterialSlot.Mud).BaseColor;
        Vector3 rock =
            GetMaterial(
                TerrainMaterialSlot.Rock).BaseColor;

        Vector3 color =
            Vector3.Lerp(
                grass,
                dirt,
                Math.Clamp(
                    elevation * 0.45f + slope * 0.45f,
                    0.0f,
                    1.0f));
        color =
            Vector3.Lerp(
                color,
                mud,
                Math.Clamp(
                    (0.28f - elevation) * 1.4f,
                    0.0f,
                    0.55f));
        color =
            Vector3.Lerp(
                color,
                rock,
                Math.Clamp(
                    slope * 2.1f,
                    0.0f,
                    1.0f));

        Vector2 point =
            new(position.X, position.Z);

        for (int index = 0; index < _regions.Length; index++)
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
                    GetMaterial(region.Slot).BaseColor,
                    weight);
        }

        return new Vector4(
            Vector3.Clamp(
                color,
                Vector3.Zero,
                Vector3.One),
            1.0f);
    }

    public static TerrainPresentationProfile CreateCentralDivide() =>
        new(
            [
                new(TerrainMaterialSlot.GrassGround, "material.world.terrain.grass_ground", new Vector3(0.20f, 0.30f, 0.15f), 0.90f, 0.0f),
                new(TerrainMaterialSlot.Dirt, "material.world.terrain.dirt", new Vector3(0.34f, 0.27f, 0.18f), 0.92f, 0.0f),
                new(TerrainMaterialSlot.Mud, "material.world.terrain.mud", new Vector3(0.20f, 0.18f, 0.13f), 0.98f, 0.0f),
                new(TerrainMaterialSlot.Rock, "material.world.terrain.rock", new Vector3(0.38f, 0.39f, 0.37f), 0.82f, 0.0f),
                new(TerrainMaterialSlot.Gravel, "material.world.terrain.gravel", new Vector3(0.42f, 0.40f, 0.35f), 0.88f, 0.0f),
                new(TerrainMaterialSlot.IndustrialGround, "material.world.terrain.industrial_ground", new Vector3(0.30f, 0.31f, 0.29f), 0.78f, 0.05f),
                new(TerrainMaterialSlot.Concrete, "material.world.terrain.concrete", new Vector3(0.48f, 0.49f, 0.47f), 0.84f, 0.0f),
                new(TerrainMaterialSlot.Scorched, "material.world.terrain.scorched", new Vector3(0.15f, 0.13f, 0.11f), 0.96f, 0.0f)
            ],
            [
                new(TerrainMaterialSlot.Gravel, new Vector2(1_536.0f, 1_536.0f), new Vector2(1_120.0f, 32.0f), 24.0f),
                new(TerrainMaterialSlot.Concrete, new Vector2(1_536.0f, 920.0f), new Vector2(78.0f, 58.0f), 12.0f),
                new(TerrainMaterialSlot.IndustrialGround, new Vector2(650.0f, 520.0f), new Vector2(150.0f, 120.0f), 28.0f),
                new(TerrainMaterialSlot.IndustrialGround, new Vector2(2_420.0f, 2_560.0f), new Vector2(150.0f, 120.0f), 28.0f),
                new(TerrainMaterialSlot.Scorched, new Vector2(1_610.0f, 1_690.0f), new Vector2(90.0f, 80.0f), 35.0f)
            ]);

    private static float CalculateRegionWeight(
        Vector2 point,
        in TerrainBlendRegion region)
    {
        Vector2 distance =
            Vector2.Abs(
                point - region.Center) -
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
                dx * dx +
                dz * dz);

        if (outside <= 0.0f)
        {
            return 0.92f;
        }

        if (region.FeatherMeters <= 0.0f ||
            outside >= region.FeatherMeters)
        {
            return 0.0f;
        }

        return 0.92f *
            (1.0f - outside / region.FeatherMeters);
    }
}
