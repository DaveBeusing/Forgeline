using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Exact-family review loads; all presentation bindings come from the existing gallery.
internal static class TankReferenceScene
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };

    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain,
        RuntimeAssetCatalog? assets, double? mechanicalSeconds)
    {
        int count = kind switch
        {
            VisualReferenceSceneKind.MainBattleTankSingle => 1,
            VisualReferenceSceneKind.MainBattleTankHundred => 100,
            VisualReferenceSceneKind.MainBattleTankThousand => 1000,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var basis = VisualReferenceScene.Create(VisualReferenceSceneKind.MainBattleTankProduction, terrain, false, assets);
        var template = basis.Snapshot.Instances.ToArray().First(x => x.UnitFeature.Unit == UnitIds.MainBattleTank &&
            x.UnitFeature.DamageState == UnitPresentationDamageState.Intact);
        double seconds = mechanicalSeconds ?? 1.6;
        int columns = count == 1000 ? 40 : count == 100 ? 10 : 1;
        var instances = new RenderInstance[count];
        float heightOffset = DirectorateContent.CreateUnitCatalog()[UnitIds.MainBattleTank].Movement.HeightOffset;
        for (int i = 0; i < count; i++)
        {
            Vector3 position = basis.Target + new Vector3((i % columns - (columns - 1) * .5f) * 8,
                0, (i / columns - (count / columns - 1) * .5f) * 11);
            position.Y = (terrain.TrySampleHeight(position.X, position.Z, out float height) ? height : 0) + heightOffset;
            instances[i] = template with
            {
                Entity = new EntityId(0xE2000000u + (uint)i, 1),
                Transform = new(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, i % 4 * MathF.PI / 2), template.Transform.Scale),
                MechanicalPose = new(true, seconds * 2, seconds, seconds, seconds >= 1.55 ? 1.55 : -1, .2f, 0),
                UnitFeature = template.UnitFeature with { AimYawRadians = .6f }
            };
        }
        PlayerExperienceSnapshot? experience = basis.Snapshot.PlayerExperience is { } source
            ? source with { Selection = PlayerSelectionSummary.Empty } : null;
        var snapshot = new PresentationSnapshot(basis.Snapshot.Tick, basis.Snapshot.TickDuration, count, instances,
            sessionId: basis.Snapshot.SessionId, playerExperience: experience);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { kind, version = 1, basis.Target, instances, snapshot.SessionId, snapshot.PlayerExperience }, FingerprintOptions)));
        return new(kind, basis.Target, snapshot, default, fingerprint);
    }
}
