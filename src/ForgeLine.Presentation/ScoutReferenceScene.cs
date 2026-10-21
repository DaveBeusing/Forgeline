using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Frozen scout review inputs; no navigation, sensor authority or animation controller.
internal static class ScoutReferenceScene
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };

    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, RuntimeAssetCatalog? assets)
    {
        int count = kind switch
        {
            VisualReferenceSceneKind.ScoutSingle or VisualReferenceSceneKind.ScoutActive or VisualReferenceSceneKind.ScoutSelected => 1,
            VisualReferenceSceneKind.ScoutRepeated100 => 100,
            VisualReferenceSceneKind.ScoutRepeated1000 => 1000,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var basis = VisualReferenceScene.Create(VisualReferenceSceneKind.ScoutVehicleProduction, terrain, false, assets);
        var context = VisualReferenceScene.Create(VisualReferenceSceneKind.DaylightTerrain, terrain, false, assets).Snapshot;
        RenderInstance template = basis.Snapshot.Instances.ToArray().First(x => x.UnitFeature.Unit == UnitIds.ScoutVehicle &&
            x.UnitFeature.DamageState == UnitPresentationDamageState.Intact);
        var definition = DirectorateContent.CreateUnitCatalog()[UnitIds.ScoutVehicle];
        bool selected = kind == VisualReferenceSceneKind.ScoutSelected;
        var instances = new RenderInstance[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 position = basis.Target + (count == 1 ? Vector3.Zero : count == 100
                ? new Vector3((i % 10 - 4.5f) * 9, 0, (i / 10 - 4.5f) * 10)
                : new Vector3((i % 40 - 19.5f) * 9, 0, (i / 40 - 12) * 10));
            position.Y = (terrain.TrySampleHeight(position.X, position.Z, out float height) ? height : 0) + definition.Movement.HeightOffset;
            bool active = kind is VisualReferenceSceneKind.ScoutActive or VisualReferenceSceneKind.ScoutSelected || count > 1 && i % 2 == 1;
            instances[i] = template with
            {
                Entity = new EntityId(0xE2000000u + (uint)i, 1),
                Transform = new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, i % 4 * MathF.PI / 2), definition.VisualScale),
                Selectable = selected ? template.Selectable : default,
                MechanicalPose = new(true, active ? 1.7 : 0, active ? 2 : 0, 2, -1, 0, 0)
            };
        }
        PlayerExperienceSnapshot? experience = context.PlayerExperience is { } sourceExperience
            ? sourceExperience with { Selection = PlayerSelectionSummary.Empty with
                { Count = selected ? 1 : 0, Kind = selected ? PlayerSelectionKind.Unit : PlayerSelectionKind.None,
                  DisplayName = selected ? "Scout Vehicle" : string.Empty } } : null;
        var snapshot = new PresentationSnapshot(basis.Snapshot.Tick, basis.Snapshot.TickDuration, count, instances,
            intelligence: basis.Snapshot.Intelligence, sessionId: context.SessionId, playerExperience: experience);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { kind, version = 1, basis.Target, instances, snapshot.SessionId, snapshot.PlayerExperience }, FingerprintOptions)));
        return new(kind, basis.Target, snapshot, default, fingerprint);
    }
}
