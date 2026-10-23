using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class ResourceDepositVariantReferenceScene
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    internal static bool Contains(VisualReferenceSceneKind kind) => kind is
        VisualReferenceSceneKind.ResourceDepositVariantsGrass or VisualReferenceSceneKind.ResourceDepositVariantsRock or
        VisualReferenceSceneKind.ResourceDepositVariantsMud or VisualReferenceSceneKind.ResourceDepositVariantLoad1000;

    internal static TerrainPresentationProfile TerrainProfile(VisualReferenceSceneKind kind, Vector3 target, TerrainPresentationProfile original)
    {
        TerrainMaterialSlot slot = kind switch
        {
            VisualReferenceSceneKind.ResourceDepositVariantsRock => TerrainMaterialSlot.Rock,
            VisualReferenceSceneKind.ResourceDepositVariantsMud => TerrainMaterialSlot.Mud,
            _ => TerrainMaterialSlot.GrassGround
        };
        return new(original.Materials, [new(slot, new(target.X, target.Z), new(1000), 16, Strength: 1)]);
    }

    internal static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, RuntimeAssetCatalog? assets)
    {
        var basis = ResourceDepositReferenceScene.Create(VisualReferenceSceneKind.ResourceDeposits1000, terrain, assets);
        int count = kind == VisualReferenceSceneKind.ResourceDepositVariantLoad1000 ? 1000 : 576;
        int columns = count == 1000 ? 40 : 24;
        int rows = (count + columns - 1) / columns;
        float spacing = count == 1000 ? 32 : 40;
        var instances = basis.Snapshot.Instances[..count].ToArray();
        for (int i = 0; i < count; i++)
        {
            var instance = instances[i];
            Vector3 position = basis.Target + new Vector3((i % columns - (columns - 1) * .5f) * spacing, 0,
                (i / columns - (rows - 1) * .5f) * spacing);
            if (!terrain.TrySampleHeight(position.X, position.Z, out float height))
                throw new InvalidOperationException("Resource variant reference lies outside terrain.");
            position.Y = height + instance.Transform.Scale.Y * .5f;
            instances[i] = instance with
            {
                ResourceField = null,
                Transform = instance.Transform with { Position = position, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (i % 7) * MathF.Tau / 7) },
                WorldFeature = instance.WorldFeature with
                {
                    ResourceState = (ResourceDepositPresentationState)(1 + i / 192 % 3),
                    ResourceVariant = ResourceDepositVariantCatalog.SelectAuthored(instance.WorldFeature.Visual, i / 4 % 12, i / 48 % 4)
                }
            };
        }
        var source = basis.Snapshot;
        var snapshot = new PresentationSnapshot(source.Tick, source.TickDuration, source.SimulationEntityCount,
            instances, sessionId: source.SessionId, playerExperience: source.PlayerExperience);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { kind, version = 1, basis.Target, instances }, Options)));
        return new(kind, basis.Target, snapshot, default, fingerprint);
    }
}
