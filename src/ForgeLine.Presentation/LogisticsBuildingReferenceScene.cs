using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class LogisticsBuildingReferenceScene
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };

    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, RuntimeAssetCatalog? assets)
    {
        var family = kind switch
        {
            VisualReferenceSceneKind.StorageDepotRepeated or VisualReferenceSceneKind.StorageDepotStress => BuildingIds.StorageDepot,
            VisualReferenceSceneKind.SupplyDepotRepeated or VisualReferenceSceneKind.SupplyDepotStress => BuildingIds.SupplyDepot,
            _ => BuildingIds.LogisticsHub
        };
        var gallery = family == BuildingIds.StorageDepot ? VisualReferenceSceneKind.StorageDepotProduction :
            family == BuildingIds.SupplyDepot ? VisualReferenceSceneKind.SupplyDepotProduction : VisualReferenceSceneKind.LogisticsHubProduction;
        var basis = VisualReferenceScene.Create(gallery, terrain, false, assets);
        RenderInstance template = basis.Snapshot.Instances.ToArray().First(instance =>
            instance.BuildingFeature.Building == family &&
            instance.BuildingFeature.State == BuildingPresentationState.Operational);
        int count = kind is VisualReferenceSceneKind.StorageDepotStress or VisualReferenceSceneKind.SupplyDepotStress or VisualReferenceSceneKind.LogisticsHubStress ? 1000 : 100;
        int columns = count == 1000 ? 32 : 10;
        Vector3 target = new(512, 0, 512);
        var instances = new RenderInstance[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 position = target + new Vector3((i % columns - (columns-1)*.5f) * 24, 0, (i / columns - (columns-1)*.5f) * 24);
            position.Y = (terrain.TrySampleHeight(position.X, position.Z, out float height) ? height : 0) + template.Transform.Scale.Y * .5f;
            instances[i] = template with
            {
                Entity = new EntityId(0xE0000000u + (uint)i, 1),
                Transform = new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, i % 4 * MathF.PI / 2), template.Transform.Scale)
            };
        }
        var snapshot = new PresentationSnapshot(basis.Snapshot.Tick, basis.Snapshot.TickDuration, count, instances);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { kind, version = 1, target, instances }, FingerprintOptions)));
        return new(kind, target, snapshot, default, fingerprint);
    }
}
