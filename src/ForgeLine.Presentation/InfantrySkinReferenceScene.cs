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
    internal static bool IsLoad(VisualReferenceSceneKind kind) =>
        kind is >= VisualReferenceSceneKind.InfantryRifleSingle and <= VisualReferenceSceneKind.InfantryEngineerThousand;
    internal static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, double phase)
    {
        bool mixed = kind == VisualReferenceSceneKind.InfantrySkinningRepeated;
        bool load = IsLoad(kind);
        int count = kind switch
        {
            VisualReferenceSceneKind.InfantryRifleSingle or VisualReferenceSceneKind.InfantryEngineerSingle => 1,
            VisualReferenceSceneKind.InfantryRifleHundred or VisualReferenceSceneKind.InfantryEngineerHundred => 100,
            VisualReferenceSceneKind.InfantryRifleThousand or VisualReferenceSceneKind.InfantryEngineerThousand => 1000,
            _ => mixed ? 1000 : 9
        };
        bool repeated = mixed || count > 9;
        bool engineer = kind is VisualReferenceSceneKind.InfantrySkinningEngineer or
            VisualReferenceSceneKind.InfantryEngineerSingle or VisualReferenceSceneKind.InfantryEngineerHundred or VisualReferenceSceneKind.InfantryEngineerThousand;
        int columns = count == 100 ? 10 : 40;
        Vector3 target = new(650, 0, 520);
        if (!terrain.TrySampleHeight(target.X, target.Z, out float ground)) throw new InvalidOperationException("Infantry target lies outside terrain.");
        target.Y = ground + .9f;
        var instances = new RenderInstance[count];
        for (int i = 0; i < instances.Length; i++)
        {
            var family = mixed ? (i % 2 == 0 ? UnitIds.RifleSquad : UnitIds.CombatEngineer) : engineer ? UnitIds.CombatEngineer : UnitIds.RifleSquad;
            Vector3 position = target + (count == 1 ? Vector3.Zero : repeated ? new Vector3((i % columns - (columns - 1) * .5f) * 3.2f, 0, (i / columns - (count / columns - 1) * .5f) * 3.2f)
                : new Vector3((i % 3 - 1) * 3.2f, 0, (i / 3 - 1) * 4));
            if (!terrain.TrySampleHeight(position.X, position.Z, out float height)) throw new InvalidOperationException("Infantry reference lies outside terrain.");
            position.Y = height + .9f;
            InfantryAction action = repeated || load ? InfantryAction.Walk : (InfantryAction)i;
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
