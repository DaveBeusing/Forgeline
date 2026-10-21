using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class CommandCoreReferenceScene
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };

    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, RuntimeAssetCatalog? assets)
    {
        var basis = VisualReferenceScene.Create(VisualReferenceSceneKind.CommandCoreProduction, terrain, false, assets);
        RenderInstance template = basis.Snapshot.Instances.ToArray().First(instance =>
            instance.BuildingFeature.Building == BuildingIds.CommandCore &&
            instance.BuildingFeature.State == BuildingPresentationState.Operational);
        int count = kind == VisualReferenceSceneKind.CommandCoreRepeated ? 100 : 1;
        var instances = new RenderInstance[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 position = basis.Target + (count == 1 ? Vector3.Zero :
                new Vector3((i % 10 - 4.5f) * 26, 0, (i / 10 - 4.5f) * 26));
            position.Y = (terrain.TrySampleHeight(position.X, position.Z, out float height) ? height : 0) + template.Transform.Scale.Y * .5f;
            instances[i] = template with
            {
                Entity = new EntityId(0xE0000000u + (uint)i, 1),
                Transform = new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, i % 4 * MathF.PI / 2), template.Transform.Scale)
            };
        }
        var snapshot = new PresentationSnapshot(basis.Snapshot.Tick, basis.Snapshot.TickDuration, count, instances);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { kind, version = 1, basis.Target, instances }, FingerprintOptions)));
        return new(kind, basis.Target, snapshot, default, fingerprint);
    }
}
