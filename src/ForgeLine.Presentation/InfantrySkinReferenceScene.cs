using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class InfantrySkinReferenceScene
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    internal static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, double phase)
    {
        bool repeated = kind == VisualReferenceSceneKind.InfantrySkinningRepeated;
        Vector3 target = new(650, 0, 520);
        var instances = new RenderInstance[repeated ? 1000 : 9];
        for (int i = 0; i < instances.Length; i++)
        {
            var family = repeated ? (i % 2 == 0 ? UnitIds.RifleSquad : UnitIds.CombatEngineer) : kind == VisualReferenceSceneKind.InfantrySkinningEngineer ? UnitIds.CombatEngineer : UnitIds.RifleSquad;
            Vector3 position = target + (repeated ? new Vector3((i % 40 - 19.5f) * 3.2f, 0, (i / 40 - 12) * 3.2f)
                : new Vector3((i % 3 - 1) * 5, 0, (i / 3 - 1) * 6));
            if (!terrain.TrySampleHeight(position.X, position.Z, out float height)) throw new InvalidOperationException("Infantry reference lies outside terrain.");
            position.Y = height + .9f;
            InfantryAction action = repeated ? InfantryAction.Walk : (InfantryAction)i;
            instances[i] = new(new EntityId((uint)i + 1, 1), new(position, Quaternion.Identity, new(1.8f)), new(1),
                RenderMaterialHandle.Default, RenderVisibilityMask.World,
                UnitFeature: new(family, UnitPresentationDamageState.Intact),
                MechanicalPose: new MechanicalPose(true, 0, 0, 1.6, -1, 0, 0)
                { Infantry = new(true, action, phase, action is InfantryAction.Idle or InfantryAction.Walk or InfantryAction.Run or InfantryAction.Aim or InfantryAction.Suppression) });
        }
        var snapshot = new PresentationSnapshot(new SimulationTick(VisualReferenceScene.FixedTick), TimeSpan.FromMilliseconds(50), 0, instances);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { kind, version = 1, phase, instances }, Options)));
        return new(kind, target, snapshot, default, fingerprint);
    }
}

