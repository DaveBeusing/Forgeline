using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Immutable review snapshots copied from the existing extracted operational states.
internal static class OperationalBuildingReferenceScene
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };

    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, RuntimeAssetCatalog? assets)
    {
        var (gallery, building, count) = kind switch
        {
            VisualReferenceSceneKind.VehicleFactorySingle => (VisualReferenceSceneKind.VehicleFactoryProduction, BuildingIds.VehicleFactory, 1),
            VisualReferenceSceneKind.VehicleFactoryRepeated => (VisualReferenceSceneKind.VehicleFactoryProduction, BuildingIds.VehicleFactory, 100),
            VisualReferenceSceneKind.PowerPlantSingle => (VisualReferenceSceneKind.PowerPlantProduction, BuildingIds.PowerPlant, 1),
            VisualReferenceSceneKind.PowerPlantRepeated => (VisualReferenceSceneKind.PowerPlantProduction, BuildingIds.PowerPlant, 100),
            VisualReferenceSceneKind.BarracksSingle => (VisualReferenceSceneKind.BarracksProduction, BuildingIds.Barracks, 1),
            VisualReferenceSceneKind.BarracksRepeated => (VisualReferenceSceneKind.BarracksProduction, BuildingIds.Barracks, 100),
            VisualReferenceSceneKind.RadarSingle => (VisualReferenceSceneKind.RadarProduction, BuildingIds.Radar, 1),
            VisualReferenceSceneKind.RadarRepeated => (VisualReferenceSceneKind.RadarProduction, BuildingIds.Radar, 100),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var basis = VisualReferenceScene.Create(gallery, terrain, false, assets);
        RenderInstance template = basis.Snapshot.Instances.ToArray().First(instance =>
            instance.BuildingFeature.Building == building &&
            instance.BuildingFeature.State == BuildingPresentationState.Operational);
        var instances = new RenderInstance[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 position = basis.Target + (count == 1 ? Vector3.Zero :
                new Vector3((i % 10 - 4.5f) * 28, 0, (i / 10 - 4.5f) * 28));
            position.Y = (terrain.TrySampleHeight(position.X, position.Z, out float height) ? height : 0) + template.Transform.Scale.Y * .5f;
            instances[i] = template with
            {
                Entity = new EntityId(0xE1000000u + (uint)i, 1),
                Transform = new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, i % 4 * MathF.PI / 2), template.Transform.Scale)
            };
        }
        PlayerExperienceSnapshot? experience = basis.Snapshot.PlayerExperience is { } sourceExperience
            ? sourceExperience with { Selection = PlayerSelectionSummary.Empty } : null;
        var snapshot = new PresentationSnapshot(basis.Snapshot.Tick, basis.Snapshot.TickDuration, count, instances,
            sessionId: basis.Snapshot.SessionId, playerExperience: experience);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { kind, version = 2, basis.Target, instances, snapshot.SessionId, snapshot.PlayerExperience }, FingerprintOptions)));
        return new(kind, basis.Target, snapshot, default, fingerprint);
    }
}
