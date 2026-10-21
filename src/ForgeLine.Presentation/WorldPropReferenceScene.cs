using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Presentation copies only. These review loads never allocate scenery ECS entities.
internal static class WorldPropReferenceScene
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };

    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, RuntimeAssetCatalog? assets)
    {
        int count = kind switch
        {
            VisualReferenceSceneKind.WorldPropsGallery => 22,
            VisualReferenceSceneKind.WorldPropSingle => 1,
            VisualReferenceSceneKind.WorldProps100 => 100,
            VisualReferenceSceneKind.WorldProps1000 => 1000,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var basis = VisualReferenceScene.Create(VisualReferenceSceneKind.EnvironmentKitYard, terrain, false, assets);
        var map = CentralDivideBattlefield.Create();
        var instances = new RenderInstance[count];
        for (int i = 0; i < count; i++)
        {
            var visual = (WorldVisualId)((int)WorldVisualId.PropRock + i % 22);
            var placed = map.WorldObjects.FirstOrDefault(item => item.Visual == visual);
            Vector3 scale = string.IsNullOrEmpty(placed.Key) ? new(4, 2, 4) : placed.Scale;
            Vector3 offset = count switch
            {
                1 => Vector3.Zero,
                22 => new((i % 6 - 2.5f) * 18, 0, (i / 6 - 1.5f) * 18),
                100 => new((i % 10 - 4.5f) * 18, 0, (i / 10 - 4.5f) * 18),
                _ => new((i % 40 - 19.5f) * 18, 0, (i / 40 - 12) * 18)
            };
            Vector3 position = basis.Target + offset;
            if (!terrain.TrySampleHeight(position.X, position.Z, out float ground))
                throw new InvalidOperationException("Prop review point lies outside terrain.");
            position.Y = ground + scale.Y * .5f;
            instances[i] = new(new(0xD100_0000U + (uint)i, 1), new(position, Quaternion.Identity, scale),
                new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
                WorldFeature: new(visual, WorldPresentationKind.Prop, default, false));
        }
        PlayerExperienceSnapshot? experience = basis.Snapshot.PlayerExperience is { } source
            ? source with { Selection = PlayerSelectionSummary.Empty } : null;
        var snapshot = new PresentationSnapshot(basis.Snapshot.Tick, basis.Snapshot.TickDuration,
            basis.Snapshot.SimulationEntityCount, instances, playerExperience: experience);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { kind, version = 1, basis.Target, instances }, FingerprintOptions)));
        return new(kind, basis.Target, snapshot, default, fingerprint);
    }
}
