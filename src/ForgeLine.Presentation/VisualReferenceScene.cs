using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public enum VisualReferenceSceneKind { DaylightTerrain, IndustrialBase, FormationConvoy, LogisticsCorridor, DamageDestruction, BattlefieldCombat }

// Authored presentation acceptance states, not economic/combat simulations or gameplay presets.
public sealed record VisualReferenceScene(VisualReferenceSceneKind Kind, Vector3 Target,
    PresentationSnapshot Snapshot, EntityId HiddenEnemy, string Fingerprint)
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };
    public const ulong Seed = 2026;
    public const int FixedTick = 32;
    public const int FixtureVersion = 2;

    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, bool includeHiddenEnemy = true)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(terrain);
        var battlefield = CentralDivideBattlefield.Create();
        Vector3 target = kind == VisualReferenceSceneKind.LogisticsCorridor
            ? battlefield.Crossings[0].Position : new(650, 0, 520);
        target.Y = Height(target.X, target.Z);
        var simulation = new SimulationCoordinator(seed: Seed);
        var store = new FactionIntelligenceStore(new() { CellSizeMeters = 32 });
        var snapshots = new PresentationSnapshotBuffer();
        simulation.RegisterSystem(new BattlefieldIntelligenceSystem(store));
        simulation.RegisterTickObserver(new PresentationExtractor(snapshots, store, new(1), terrain.WorldBounds));
        // A real sensor/intelligence/extraction boundary filters the authored foreign units.
        EntityId sensor = Add(new(0, 0, 0), Vector3.One, renderable: false);
        simulation.Entities.AddComponent(sensor, new VisualSensorState(new(1), rangeMeters: 130));
        foreach (var item in battlefield.WorldObjects)
        {
            float groundOffset = item.Kind switch
            {
                WorldPresentationKind.Decal => .04f,
                WorldPresentationKind.Vegetation => 0,
                _ => item.Scale.Y * .5f
            };
            EntityId entity = Add(new(item.Position.X - target.X, groundOffset, item.Position.Z - target.Z), item.Scale, yaw: item.RotationDegrees * MathF.PI / 180);
            simulation.Entities.AddComponent(entity, new WorldPresentationIdentity(item.Visual, item.Kind));
        }
        foreach (var item in battlefield.Resources)
        {
            EntityId deposit = Add(new(item.Center.X - target.X, 0, item.Center.Z - target.Z), new(item.HalfExtents.X * 2, 8, item.HalfExtents.Z * 2));
            simulation.Entities.AddComponent(deposit, WorldPresentationIdentity.ForResource(item.ResourceId));
            simulation.Entities.AddComponent(deposit, new ResourceDeposit(item.ResourceId,
                new(item.Center - item.HalfExtents, item.Center + item.HalfExtents), item.TotalQuantity, item.ExtractionRatePerSecond));
        }
        if (kind != VisualReferenceSceneKind.DaylightTerrain)
        {
            for (int i = 0; i < 18; i++)
            {
                EntityId prop = Add(new((i % 6 - 2.5f) * 16, 0, 70 + (i / 6) * 12), new(3, 5, 3));
                simulation.Entities.AddComponent(prop, new WorldPresentationIdentity(i % 2 == 0 ? WorldVisualId.VegetationConifer : WorldVisualId.PropRock,
                    i % 2 == 0 ? WorldPresentationKind.Vegetation : WorldPresentationKind.Prop));
            }
        }
        if (kind == VisualReferenceSceneKind.IndustrialBase)
        {
            BuildingId[] buildings = [BuildingIds.CommandCore, BuildingIds.PowerPlant, BuildingIds.Smelter,
                BuildingIds.ElectronicsPlant, BuildingIds.Refinery, BuildingIds.VehicleFactory,
                BuildingIds.StorageDepot, BuildingIds.SupplyDepot, BuildingIds.LogisticsHub,
                BuildingIds.AmmunitionPlant, BuildingIds.Barracks, BuildingIds.Radar, BuildingIds.Extractor];
            for (int i = 0; i < buildings.Length; i++)
            {
                EntityId building = Add(new((i % 4 - 1.5f) * 27, 0, (i / 4 - 1.5f) * 27), new(16, 10, 16));
                simulation.Entities.AddComponent(building, new CompletedBuilding(buildings[i], new(1), SimulationTick.Zero));
                simulation.Entities.AddComponent(building, new ControllableEntity(new(1), ControllableEntityCategory.Building));
            }
        }
        if (kind is VisualReferenceSceneKind.FormationConvoy or VisualReferenceSceneKind.LogisticsCorridor)
        {
            for (int i = 0; i < 15; i++)
                Unit(i % 3 == 0 ? UnitIds.CargoTruck : UnitIds.MainBattleTank, new((i % 3 - 1) * 10, 0, (i / 3 - 2) * 14));
            for (int i = -3; i <= 3; i++)
            {
                bool bridge = kind == VisualReferenceSceneKind.LogisticsCorridor && i == 0;
                EntityId road = Add(new(0, .08f, i * 20), bridge ? new(18, 4, 42) : new(12, .2f, 20));
                simulation.Entities.AddComponent(road, new InfrastructurePresentationIdentity(
                    bridge ? InfrastructurePresentationKind.RoadBridge : InfrastructurePresentationKind.RoadSegment, $"reference-road-{i}"));
            }
        }
        if (kind is VisualReferenceSceneKind.DamageDestruction or VisualReferenceSceneKind.BattlefieldCombat)
        {
            for (int i = 0; i < 9; i++)
            {
                EntityId unit = Unit(i % 3 == 0 ? UnitIds.RifleSquad : UnitIds.MainBattleTank, new((i % 3 - 1) * 14, 0, (i / 3 - 1) * 14));
                if (i % 3 == 0) simulation.Entities.AddComponent(unit, new HealthState(25, 100));
                if (i == 8) simulation.Entities.AddComponent(unit, new UnitWreckPresentationIdentity(UnitIds.MainBattleTank, new(1)));
            }
            EntityId building = Add(new(-24, 0, -32), new(16, 10, 16));
            simulation.Entities.AddComponent(building, new CompletedBuilding(BuildingIds.VehicleFactory, new(1), SimulationTick.Zero));
            simulation.Entities.AddComponent(building, new HealthState(20, 100));
        }
        EntityId hidden = default;
        if (kind == VisualReferenceSceneKind.BattlefieldCombat)
        {
            EntityId contact = Unit(UnitIds.MainBattleTank, new(55, 0, 10), foreign: true);
            simulation.Entities.AddComponent(contact, new IntelligenceSignature(new(2), identityKey: 42));
            if (includeHiddenEnemy)
            {
                hidden = Unit(UnitIds.MobileArtillery, new(300, 0, 0), foreign: true);
                simulation.Entities.AddComponent(hidden, new IntelligenceSignature(new(2), identityKey: 43));
            }
        }
        for (int tick = 0; tick < FixedTick; tick++) simulation.AdvanceOneTick();
        if (!snapshots.TryReadLatest(out var extracted)) throw new InvalidOperationException("Reference snapshot was not published.");
        if (hidden.IsValid && extracted.Instances.ToArray().Any(x => x.Entity == hidden))
            throw new InvalidOperationException("Reference extraction exposed a hidden enemy.");
        var experience = new PlayerExperienceSnapshot(new(1), extracted.Tick, PlayerMatchStatus.Active, default,
            new(100, 80, 90, 200, 150, 75, 100), new(default, 500, 300, 300, 0, 0, 0),
            new(extracted.Intelligence!.Cells.Count, extracted.Intelligence.Cells.Count(x => x.State == IntelligenceState.Visible), extracted.Intelligence.Contacts.Count),
            default, PlayerAlertState.None, 0, 0, PlayerCommandFeedback.None, default);
        var visible = extracted.Instances.ToArray().ToList();
        if (kind == VisualReferenceSceneKind.BattlefieldCombat)
        {
            // Authored effects at known visible positions; no events or positions from hidden units.
            VfxEffectKind[] effects = [VfxEffectKind.MuzzleTankCannon, VfxEffectKind.ImpactMetal,
                VfxEffectKind.ImpactDirt, VfxEffectKind.ExplosionBuilding, VfxEffectKind.DestructionSparkEmission];
            for (int i = 0; i < effects.Length; i++)
            {
                var definition = VfxPresentationCatalog.Get(effects[i]);
                Vector3 position = target + new Vector3(i * 10 - 20, 2, 18);
                position.Y = Height(position.X, position.Z) + 2;
                visible.Add(new(new((uint)i + 1_000_000, 1), new(position, Quaternion.Identity, definition.BaseScale),
                    new(1), RenderMaterialHandle.Default, RenderVisibilityMask.Default, VfxFeature: new(effects[i])));
            }
        }
        var snapshot = new PresentationSnapshot(extracted.Tick, extracted.TickDuration, extracted.SimulationEntityCount,
            visible.ToArray(), extracted.Intelligence, new(1), experience with
            {
                Selection = PlayerSelectionSummary.Empty with
                {
                    Count = Math.Min(3, visible.Count(x => x.Selectable.Owner == new PlayerId(1))),
                    Kind = kind == VisualReferenceSceneKind.IndustrialBase ? PlayerSelectionKind.Building : PlayerSelectionKind.Unit,
                    DisplayName = kind.ToString()
                }
            }, playerActions: new(new(1), extracted.Tick,
                [new(BuildingIds.PowerPlant, "Power Plant", [], true), new(BuildingIds.VehicleFactory, "Vehicle Factory", [], true)],
                0, null, null), vfxMetrics: extracted.VfxMetrics);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = FixtureVersion, kind, target, seed = Seed, tick = snapshot.Tick.Value,
            instances = snapshot.Instances.ToArray(), cells = snapshot.Intelligence!.Cells, contacts = snapshot.Intelligence.Contacts,
            snapshot.PlayerExperience, snapshot.PlayerActions
        }, FingerprintOptions)));
        return new(kind, target, snapshot, hidden, fingerprint);

        float Height(float x, float z) => terrain.TrySampleHeight(x, z, out float height) ? height : throw new InvalidOperationException("Reference point lies outside terrain.");
        EntityId Add(Vector3 offset, Vector3 scale, bool renderable = true, float yaw = 0)
        {
            Vector3 position = target + offset;
            position.Y = Height(position.X, position.Z) + offset.Y;
            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, new WorldTransform(position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw), scale));
            if (renderable) simulation.Entities.AddComponent(entity, new VisualIdentity(1));
            return entity;
        }
        EntityId Unit(UnitId id, Vector3 offset, bool foreign = false)
        {
            EntityId unit = Add(offset, id == UnitIds.RifleSquad ? new(2, 2, 2) : new(4, 3, 7));
            simulation.Entities.AddComponent(unit, new UnitIdentity(id, new(foreign ? 2u : 1u)));
            simulation.Entities.AddComponent(unit, new ControllableEntity(new(foreign ? 2UL : 1UL), ControllableEntityCategory.Unit));
            return unit;
        }
    }
}
