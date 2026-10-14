using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Frozen authored workload; real production/event extraction is qualified separately.</summary>
internal static class EffectReferenceScene
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };
    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, ForgeLine.World.TerrainWorld terrain,
        bool includeHiddenEnemy, RuntimeAssetCatalog? assets)
    {
        var original = VisualReferenceScene.Create(VisualReferenceSceneKind.IndustrialBase, terrain, includeHiddenEnemy, assets);
        var instances = original.Snapshot.Instances.ToArray().ToList();
        uint identity = 0xC000_0000;
        if (kind == VisualReferenceSceneKind.IndustrialEffects)
        {
            foreach (var instance in original.Snapshot.Instances)
                if (IndustrialEffectPreset.TryResolve(instance.BuildingFeature, true, out var preset))
                    Add(preset.Effect, preset.ResolveAnchor(instance.BuildingFeature, instance.Transform, assets), instance.Entity, true);
        }
        else
        {
            VfxEffectKind[] kinds = [VfxEffectKind.ExplosionBuilding, VfxEffectKind.DestructionSmokePlume,
                VfxEffectKind.DestructionDustCloud, VfxEffectKind.DestructionSparkEmission];
            for (int i = 0; i < 1024; i++)
            {
                Vector3 point = original.Target + new Vector3((i % 32 - 15.5f) * 4, 1, (i / 32 - 15.5f) * 4);
                if (terrain.TrySampleHeight(point.X, point.Z, out float height)) point.Y = height + 1;
                Add(kinds[i % kinds.Length], point, new EntityId(identity++, 1), i % kinds.Length != 0);
            }
        }
        var pool = new DecalPool();
        var scratch = new RenderInstance[pool.MaximumPublished];
        for (int i = 0; i < 64; i++)
        {
            Vector3 point = original.Target + new Vector3((i % 8 - 3.5f) * 12, 0, (i / 8 - 3.5f) * 12);
            if (terrain.TrySampleHeight(point.X, point.Z, out float height)) point.Y = height + .06f;
            DecalKind mark = (i % 4) switch { 0 => DecalKind.Crater, 1 => DecalKind.Scorch, 2 => DecalKind.TireTrack, _ => DecalKind.TrackedVehicle };
            pool.TrySpawn(new(mark, DecalSurface.Ground, new(point, Quaternion.Identity, new(6, 1, 6))), original.Snapshot.Tick, out _);
        }
        int published = pool.CopyVisibleTo(scratch, original.Snapshot.Tick, new VisibleFootprints());
        for (int i = 0; i < published; i++) instances.Add(scratch[i]);
        var source = original.Snapshot;
        var snapshot = new PresentationSnapshot(source.Tick, source.TickDuration, source.SimulationEntityCount, instances.ToArray(),
            source.Intelligence, source.SessionId, source.PlayerExperience, playerActions: source.PlayerActions,
            vfxMetrics: source.VfxMetrics, strategicOverlay: source.StrategicOverlay) { DecalMetrics = pool.Metrics };
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { version = 1, kind, original.Target, instances }, FingerprintOptions)));
        return original with { Kind = kind, Snapshot = snapshot, Fingerprint = fingerprint };

        void Add(VfxEffectKind effect, Vector3 position, EntityId owner, bool continuous)
        {
            var definition = VfxPresentationCatalog.Get(effect);
            instances.Add(new(new EntityId(identity++, 1), new(position, Quaternion.Identity, definition.BaseScale), new(1),
                RenderMaterialHandle.Default, RenderVisibilityMask.World, (uint)effect,
                VfxFeature: new(effect, ((ulong)owner.Generation << 32) | owner.Index, owner.Generation, continuous, SpawnedAtTick: original.Snapshot.Tick.Value)));
        }
    }

    private sealed class VisibleFootprints : IDecalVisibility
    {
        public bool IsGroundVisible(Vector3 position) => true;
        public bool TryGetReceiver(EntityId receiver, out RenderTransform transform) { transform = default; return false; }
    }
}
