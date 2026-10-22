using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class DecalReferenceScene
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    internal static bool Contains(VisualReferenceSceneKind kind) =>
        kind is >= VisualReferenceSceneKind.DecalAsphaltGallery and <= VisualReferenceSceneKind.DecalSlopeGallery;
    private static TerrainMaterialSlot Surface(VisualReferenceSceneKind kind) => kind switch
    {
        VisualReferenceSceneKind.DecalConcreteGallery => TerrainMaterialSlot.Concrete,
        VisualReferenceSceneKind.DecalDirtGallery => TerrainMaterialSlot.Dirt,
        VisualReferenceSceneKind.DecalGravelGallery => TerrainMaterialSlot.Gravel,
        _ => TerrainMaterialSlot.IndustrialGround
    };

    internal static TerrainPresentationProfile TerrainProfile(VisualReferenceSceneKind kind, Vector3 target, TerrainPresentationProfile original) =>
        new(original.Materials, [new(Surface(kind), new(target.X, target.Z), new(1000), 16, Strength: 1)]);

    internal static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain)
    {
        int count = kind switch
        {
            VisualReferenceSceneKind.DecalSingle => 1,
            VisualReferenceSceneKind.Decals100 => 100,
            VisualReferenceSceneKind.Decals1000 => 1000,
            VisualReferenceSceneKind.DecalsOverlap => 128,
            _ => 8
        };
        WorldVisualId[] families = [WorldVisualId.DecalTireTracks, WorldVisualId.DecalTrackedVehicleMarks,
            WorldVisualId.DecalRoadWear, WorldVisualId.DecalOilStain, WorldVisualId.DecalBlastMark,
            WorldVisualId.DecalShellImpact, WorldVisualId.DecalScorchMark, WorldVisualId.DecalConcreteCrack];
        Vector3 target = new(650, Height(650, 520), 520);
        if (kind == VisualReferenceSceneKind.DecalSlopeGallery)
        {
            float highestSlope = 1;
            for (int z = 128; z <= 2944; z += 128)
            for (int x = 128; x <= 2944; x += 128)
                if (terrain.TrySampleNormal(x, z, out Vector3 sample) && sample.Y is > .65f && sample.Y < highestSlope)
                { highestSlope = sample.Y; target = new(x, Height(x, z), z); }
        }
        var instances = new RenderInstance[count];
        bool overlap = kind == VisualReferenceSceneKind.DecalsOverlap;
        int columns = count == 100 ? 10 : count == 1000 ? 32 : 4;
        int positions = overlap ? 16 : count;
        int rows = (positions + columns - 1) / columns;
        float spacing = count >= 100 && !overlap ? 5 : overlap ? 10 : 8;
        for (int i = 0; i < count; i++)
        {
            int cell = overlap ? i / 8 : i;
            Vector3 position = target + (count == 1 ? Vector3.Zero : new Vector3(
                (cell % columns - (columns - 1) * .5f) * spacing, 0,
                (cell / columns - (rows - 1) * .5f) * spacing));
            if (!terrain.TrySampleNormal(position.X, position.Z, out Vector3 normal))
                throw new InvalidOperationException("Decal normal lies outside terrain.");
            Vector3 right = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitZ));
            Vector3 forward = Vector3.Cross(right, normal);
            var basis = new Matrix4x4(right.X, right.Y, right.Z, 0, normal.X, normal.Y, normal.Z, 0,
                forward.X, forward.Y, forward.Z, 0, 0, 0, 0, 1);
            Quaternion rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationY((i % 4) * MathF.PI * .5f) * basis);
            position.Y = Height(position.X, position.Z) + .06f + (overlap ? i % 8 * .002f : 0);
            float opacity = overlap ? .08f + (i % 8) * .07f : .65f;
            // Authored diagnostic copies exercise the real quad/material path. Live pool caps are unchanged.
            instances[i] = new(new EntityId((uint)i + 1, 1), new(position, rotation, new(6, 1, 6)),
                new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
                WorldFeature: new(families[i % families.Length], WorldPresentationKind.Decal, default, false),
                DecalFeature: new(DecalKind.Hit, opacity));
        }
        var snapshot = new PresentationSnapshot(new SimulationTick(VisualReferenceScene.FixedTick), TimeSpan.FromMilliseconds(50), 0, instances);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { kind, version = 1, surface = Surface(kind), target, instances }, Options)));
        return new(kind, target, snapshot, default, fingerprint);

        float Height(float x, float z) => terrain.TrySampleHeight(x, z, out float height)
            ? height : throw new InvalidOperationException("Decal reference lies outside terrain.");
    }
}
