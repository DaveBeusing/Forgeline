using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Copied rooted plants for native review; no navigation or gameplay authority.
internal static class VegetationReferenceScene
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };

    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, RuntimeAssetCatalog? assets)
    {
        int count = kind switch
        {
            VisualReferenceSceneKind.VegetationSingle => 1,
            VisualReferenceSceneKind.VegetationGrove => 40,
            VisualReferenceSceneKind.VegetationRepeated100 => 100,
            VisualReferenceSceneKind.VegetationRepeated1000 => 1000,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var basis = VisualReferenceScene.Create(VisualReferenceSceneKind.DaylightTerrain, terrain, false, assets);
        Vector3 target = basis.Target;
        WorldVisualId[] visuals = [WorldVisualId.VegetationConifer, WorldVisualId.VegetationBroadleaf,
            WorldVisualId.VegetationScrub, WorldVisualId.VegetationGrassClump];
        var map = CentralDivideBattlefield.Create();
        var scales = visuals.Select(v => map.WorldObjects.First(x => x.Visual == v && x.PresentationOnly).Scale).ToArray();
        var instances = new RenderInstance[count];
        int columns = count == 1000 ? 40 : 10;
        float rows = MathF.Ceiling((float)count / columns);
        for (int i = 0; i < count; i++)
        {
            Vector3 position = target + (count == 1 ? Vector3.Zero :
                new Vector3((i % columns - (columns - 1) * .5f) * 12, 0, (i / columns - (rows - 1) * .5f) * 14));
            if (!terrain.TrySampleHeight(position.X, position.Z, out float height))
                throw new InvalidOperationException("Vegetation review point lies outside terrain.");
            position.Y = height;
            instances[i] = new(new EntityId(0xE3000000u + (uint)i, 1),
                new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, i % 4 * MathF.PI / 2), scales[i % 4]),
                new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World, DebugIdentity: (uint)i,
                WorldFeature: new(visuals[i % 4], WorldPresentationKind.Vegetation, default, false));
        }
        PlayerExperienceSnapshot? experience = basis.Snapshot.PlayerExperience is { } sourceExperience
            ? sourceExperience with { Selection = PlayerSelectionSummary.Empty } : null;
        var snapshot = new PresentationSnapshot(basis.Snapshot.Tick, basis.Snapshot.TickDuration, 0, instances,
            sessionId: basis.Snapshot.SessionId, playerExperience: experience);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { kind, version = 1, target, instances }, FingerprintOptions)));
        return new(kind, target, snapshot, default, fingerprint);
    }
}
