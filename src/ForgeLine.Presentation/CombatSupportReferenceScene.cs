using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class CombatSupportReferenceScene
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };
    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain)
    {
        UnitId[] families = [UnitIds.ScoutVehicle, UnitIds.MobileArtillery, UnitIds.CargoTruck, UnitIds.SupplyTruck];
        UnitId family = kind switch
        {
            VisualReferenceSceneKind.ScoutVehicleProduction => UnitIds.ScoutVehicle,
            VisualReferenceSceneKind.ArtilleryProduction => UnitIds.MobileArtillery,
            VisualReferenceSceneKind.CargoTruckProduction or VisualReferenceSceneKind.CargoTruckSingle or
                VisualReferenceSceneKind.CargoTruckHundred or VisualReferenceSceneKind.CargoTruckRepeated => UnitIds.CargoTruck,
            VisualReferenceSceneKind.SupplyTruckProduction or VisualReferenceSceneKind.SupplyTruckSingle or
                VisualReferenceSceneKind.SupplyTruckHundred or VisualReferenceSceneKind.SupplyTruckRepeated => UnitIds.SupplyTruck,
            _ => default
        };
        bool single = kind is VisualReferenceSceneKind.CargoTruckSingle or VisualReferenceSceneKind.SupplyTruckSingle;
        bool hundred = kind is VisualReferenceSceneKind.CargoTruckHundred or VisualReferenceSceneKind.SupplyTruckHundred;
        bool repeated = hundred || kind is VisualReferenceSceneKind.CombatSupportRepeated or
            VisualReferenceSceneKind.CargoTruckRepeated or VisualReferenceSceneKind.SupplyTruckRepeated;
        int count = single ? 1 : hundred ? 100 : repeated ? 1000 : 8;
        if (!repeated && !family.IsSpecified) throw new ArgumentOutOfRangeException(nameof(kind));
        Vector3 target = new(650, 0, 520);
        target.Y = Height(target.X, target.Z);
        var simulation = new SimulationCoordinator(seed: VisualReferenceScene.Seed);
        var intelligence = new FactionIntelligenceStore(new() { CellSizeMeters = 32 });
        var snapshots = new PresentationSnapshotBuffer();
        simulation.RegisterSystem(new BattlefieldIntelligenceSystem(intelligence));
        simulation.RegisterTickObserver(new PresentationExtractor(snapshots, intelligence, new(1), terrain.WorldBounds));
        EntityId sensor = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(sensor, new WorldTransform(target, Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(sensor, new VisualSensorState(new(1), rangeMeters: repeated ? 800 : 120));
        var definitions = DirectorateContent.CreateUnitCatalog();
        var review = new Dictionary<EntityId, int>();
        for (int i = 0; i < count; i++)
        {
            UnitId id = family.IsSpecified ? family : families[i % families.Length];
            var definition = definitions[id];
            Vector3 position = target + (single ? Vector3.Zero : hundred ? new Vector3((i % 10 - 4.5f) * 9, 0, (i / 10 - 4.5f) * 10)
                : repeated ? new Vector3((i % 40 - 19.5f) * 9, 0, (i / 40 - 12) * 10)
                : new Vector3((i % 4 - 1.5f) * 12, 0, (i / 4 - .5f) * 18));
            position.Y = Height(position.X, position.Z) + definition.Movement.HeightOffset;
            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, new WorldTransform(position, Quaternion.Identity, definition.VisualScale));
            simulation.Entities.AddComponent(entity, new VisualIdentity(definition.VisualId));
            if (!repeated && i == 7)
                simulation.Entities.AddComponent(entity, new UnitWreckPresentationIdentity(id, new(1)));
            else
            {
                simulation.Entities.AddComponent(entity, new UnitIdentity(id, new(1)));
                simulation.Entities.AddComponent(entity, new ControllableEntity(new(1), ControllableEntityCategory.Unit));
                if (!repeated && i is 5 or 6) simulation.Entities.AddComponent(entity, new HealthState(i == 5 ? 50 : 20, 100));
            }
            review.Add(entity, i);
        }
        simulation.RunTicks(VisualReferenceScene.FixedTick);
        if (!snapshots.TryReadLatest(out var extracted)) throw new InvalidOperationException("Unit reference extraction failed.");
        RenderInstance[] visible = extracted.Instances.ToArray();
        for (int i = 0; i < visible.Length; i++)
        {
            RenderInstance item = visible[i];
            if (!review.TryGetValue(item.Entity, out int pose) || item.UnitFeature.IsWreck) continue;
            bool armed = item.UnitFeature.Unit == UnitIds.ScoutVehicle || item.UnitFeature.Unit == UnitIds.MobileArtillery;
            // Fixed cosmetic read-model inputs qualify rigid transforms, not a live combat workload.
            visible[i] = item with
            {
                MechanicalPose = new(true, repeated || pose == 2 ? 1.7 : 0, 1.6, 1.6,
                    armed && pose == 3 && !repeated ? 1.55 : -1,
                    armed && pose is 1 or 3 && !repeated ? .2f : 0, 0),
                UnitFeature = item.UnitFeature with { AimYawRadians = armed && pose is 1 or 3 && !repeated ? .6f : 0 }
            };
        }
        var snapshot = new PresentationSnapshot(extracted.Tick, extracted.TickDuration, extracted.SimulationEntityCount,
            visible, intelligence: extracted.Intelligence);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            kind, VisualReferenceScene.Seed, tick = snapshot.Tick.Value,
            instances = visible.Select(x => new { x.Entity, x.Transform, x.UnitFeature, x.MechanicalPose })
        }, FingerprintOptions)));
        return new(kind, target, snapshot, default, fingerprint);

        float Height(float x, float z) => terrain.TrySampleHeight(x, z, out float height) ? height : 0;
    }
}
