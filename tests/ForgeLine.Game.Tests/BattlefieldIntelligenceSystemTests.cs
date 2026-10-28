using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class BattlefieldIntelligenceSystemTests
{
    private static readonly System.Text.Json.JsonSerializerOptions SnapshotJsonOptions = new() { IncludeFields = true };
    private static readonly FactionId BlueFaction = new(1);
    private static readonly FactionId RedFaction = new(2);

    [Fact]
    public void ExplorationPersistsAfterVisualVisibilityIsLost()
    {
        var simulation = new SimulationCoordinator();
        var store = new FactionIntelligenceStore();
        var intelligence =
            new BattlefieldIntelligenceSystem(store);
        simulation.RegisterSystem(intelligence);

        EntityId sensor =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            sensor,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            sensor,
            new VisualSensorState(
                BlueFaction,
                rangeMeters: 48.0f));

        simulation.AdvanceOneTick();

        VisibilityCellCoordinate originCell =
            store.WorldToCell(Vector3.Zero);

        Assert.Equal(
            IntelligenceState.Visible,
            store.GetTerrainState(
                BlueFaction,
                originCell));
        Assert.True(
            store.GetExploredCellCount(
                BlueFaction) > 0);

        Assert.True(
            simulation.Entities.DestroyEntity(
                sensor));

        simulation.AdvanceOneTick();

        Assert.Equal(
            IntelligenceState.Explored,
            store.GetTerrainState(
                BlueFaction,
                originCell));
        Assert.Equal(
            0,
            store.GetVisibleCellCount(
                BlueFaction));
    }

    [Fact]
    public void RadarTransitionsFromDetectedToIdentifiedAndKeepsLastKnownContact()
    {
        var simulation = new SimulationCoordinator();
        var store = new FactionIntelligenceStore();
        simulation.RegisterSystem(
            new BattlefieldIntelligenceSystem(store));

        EntityId sensor =
            CreateSensorEntity(
                simulation,
                BlueFaction,
                Vector3.Zero);
        simulation.Entities.AddComponent(
            sensor,
            new RadarSensorState(
                BlueFaction,
                detectionRangeMeters: 100.0f,
                identificationRangeMeters: 20.0f,
                updateIntervalTicks: 1));

        EntityId target =
            CreateSignatureEntity(
                simulation,
                RedFaction,
                new Vector3(50.0f, 0.0f, 0.0f),
                identityKey: 77);

        simulation.AdvanceOneTick();

        IntelligenceContactKey contactKey =
            IntelligenceContactKey.FromEntity(target);
        Assert.True(
            store.TryGetContact(
                BlueFaction,
                contactKey,
                out IntelligenceContact detected));
        Assert.Equal(
            IntelligenceState.Detected,
            detected.State);
        Assert.Equal(
            0U,
            detected.IdentityKey);
        Assert.True(detected.IsCurrent);

        simulation.Entities.SetComponent(
            target,
            new WorldTransform(
                new Vector3(10.0f, 0.0f, 0.0f),
                Quaternion.Identity,
                Vector3.One));

        simulation.AdvanceOneTick();

        Assert.True(
            store.TryGetContact(
                BlueFaction,
                contactKey,
                out IntelligenceContact identified));
        Assert.Equal(
            IntelligenceState.Identified,
            identified.State);
        Assert.Equal(
            77U,
            identified.IdentityKey);
        Assert.Equal(
            new Vector3(10.0f, 0.0f, 0.0f),
            identified.LastKnownPosition);
        Assert.True(identified.IsCurrent);

        simulation.Entities.SetComponent(
            target,
            new WorldTransform(
                new Vector3(150.0f, 0.0f, 0.0f),
                Quaternion.Identity,
                Vector3.One));

        simulation.AdvanceOneTick();

        Assert.True(
            store.TryGetContact(
                BlueFaction,
                contactKey,
                out IntelligenceContact stale));
        Assert.False(stale.IsCurrent);
        Assert.Equal(
            new Vector3(10.0f, 0.0f, 0.0f),
            stale.LastKnownPosition);
    }

    [Fact]
    public void IntelligenceStateIsIsolatedPerFaction()
    {
        var simulation = new SimulationCoordinator();
        var store = new FactionIntelligenceStore();
        simulation.RegisterSystem(
            new BattlefieldIntelligenceSystem(store));

        EntityId sensor =
            CreateSensorEntity(
                simulation,
                BlueFaction,
                Vector3.Zero);
        simulation.Entities.AddComponent(
            sensor,
            new VisualSensorState(
                BlueFaction,
                rangeMeters: 100.0f));

        EntityId target =
            CreateSignatureEntity(
                simulation,
                RedFaction,
                new Vector3(20.0f, 0.0f, 0.0f),
                identityKey: 99);

        simulation.AdvanceOneTick();

        Assert.True(
            store.IsEntityCurrentlyIdentified(
                BlueFaction,
                target));
        Assert.False(
            store.IsEntityCurrentlyDetected(
                RedFaction,
                target));
        Assert.Equal(
            0,
            store.GetExploredCellCount(
                RedFaction));
    }

    [Fact]
    public void HiddenEnemyCannotBeAcquiredUntilVisuallyIdentified()
    {
        var simulation = new SimulationCoordinator();
        var store = new FactionIntelligenceStore();
        var intelligence =
            new BattlefieldIntelligenceSystem(store);
        var availability =
            new IntelligenceTargetAvailabilityPolicy(
                simulation.Entities,
                store);
        var weapons = new WeaponCatalog();
        WeaponDefinition weapon =
            CreateTargetingWeapon();
        weapons.Add(weapon);

        simulation.RegisterSystem(intelligence);
        simulation.RegisterSystem(
            new TargetAcquisitionSystem(
                weapons,
                targetAvailability:
                    availability));

        EntityId source =
            CreateCombatEntity(
                simulation,
                BlueFaction,
                Vector3.Zero,
                TargetClass.LightVehicle,
                identityKey: 10);
        simulation.Entities.AddComponent(
            source,
            new VisualSensorState(
                BlueFaction,
                rangeMeters: 20.0f));
        simulation.Entities.AddComponent(
            source,
            new WeaponState(
                weapon.Id,
                EntityId.Invalid));

        EntityId target =
            CreateCombatEntity(
                simulation,
                RedFaction,
                new Vector3(50.0f, 0.0f, 0.0f),
                TargetClass.ArmoredVehicle,
                identityKey: 20);

        simulation.AdvanceOneTick();

        Assert.False(
            simulation.Entities.GetComponent<WeaponState>(
                source).Target.IsValid);

        simulation.Entities.SetComponent(
            target,
            new WorldTransform(
                new Vector3(10.0f, 0.0f, 0.0f),
                Quaternion.Identity,
                Vector3.One));

        simulation.AdvanceOneTick();

        Assert.Equal(
            target,
            simulation.Entities.GetComponent<WeaponState>(
                source).Target);
    }

    [Fact]
    public void RadarOnlyDetectionDoesNotLeakEntityTargeting()
    {
        var simulation = new SimulationCoordinator();
        var store = new FactionIntelligenceStore();
        var intelligence =
            new BattlefieldIntelligenceSystem(store);
        var availability =
            new IntelligenceTargetAvailabilityPolicy(
                simulation.Entities,
                store);
        var weapons = new WeaponCatalog();
        WeaponDefinition weapon =
            CreateTargetingWeapon();
        weapons.Add(weapon);

        simulation.RegisterSystem(intelligence);
        simulation.RegisterSystem(
            new TargetAcquisitionSystem(
                weapons,
                targetAvailability:
                    availability));

        EntityId source =
            CreateCombatEntity(
                simulation,
                BlueFaction,
                Vector3.Zero,
                TargetClass.LightVehicle,
                identityKey: 10);
        simulation.Entities.AddComponent(
            source,
            new RadarSensorState(
                BlueFaction,
                detectionRangeMeters: 100.0f,
                identificationRangeMeters: 0.0f,
                updateIntervalTicks: 1));
        simulation.Entities.AddComponent(
            source,
            new WeaponState(
                weapon.Id,
                EntityId.Invalid));

        EntityId target =
            CreateCombatEntity(
                simulation,
                RedFaction,
                new Vector3(30.0f, 0.0f, 0.0f),
                TargetClass.ArmoredVehicle,
                identityKey: 20);

        simulation.AdvanceOneTick();

        Assert.True(
            store.IsEntityCurrentlyDetected(
                BlueFaction,
                target));
        Assert.False(
            store.IsEntityCurrentlyIdentified(
                BlueFaction,
                target));
        Assert.False(
            simulation.Entities.GetComponent<WeaponState>(
                source).Target.IsValid);
    }

    [Fact]
    public void RadarUpdateIntervalBoundsScanFrequency()
    {
        var simulation = new SimulationCoordinator();
        var store = new FactionIntelligenceStore();
        var intelligence =
            new BattlefieldIntelligenceSystem(store);
        simulation.RegisterSystem(intelligence);

        EntityId sensor =
            CreateSensorEntity(
                simulation,
                BlueFaction,
                Vector3.Zero);
        simulation.Entities.AddComponent(
            sensor,
            new RadarSensorState(
                BlueFaction,
                detectionRangeMeters: 100.0f,
                updateIntervalTicks: 4));

        simulation.RunTicks(
            8,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            2UL,
            intelligence.Metrics.TotalSensorScans);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexFreeOcclusionBudgetEventuallyIdentifiesEveryCandidate(bool radar)
    {
        string expected = Run(false);
        Assert.Equal(expected, Run(false));
        Assert.Equal(expected, Run(true));

        string Run(bool indexed)
        {
            var simulation = new SimulationCoordinator();
            var spatial = indexed ? new ForgeLine.World.SpatialGridIndex() : null;
            var timeline = new List<string>();
            var store = new FactionIntelligenceStore();
            simulation.RegisterSystem(new BattlefieldIntelligenceSystem(store, spatial,
                visualOcclusion: new VisualOcclusionPolicy(new FlatTerrain(), ForgeLine.World.TacticalAreaSet.Empty)));
            var sensor = CreateSensorEntity(simulation, BlueFaction, new Vector3(10, 0, 10));
            if (radar) simulation.Entities.AddComponent(sensor,
                new RadarSensorState(BlueFaction, 100, 100, updateIntervalTicks: 1));
            else simulation.Entities.AddComponent(sensor, new VisualSensorState(BlueFaction, 100, updateIntervalTicks: 1));
            var targets = Enumerable.Range(0, BattlefieldIntelligenceSystem.MaximumVisualTracesPerSensor + 1)
                .Select(i => CreateSignatureEntity(simulation, RedFaction, new Vector3(20, 0, 10), (uint)i + 1)).ToArray();
            foreach (var entity in targets.Prepend(sensor)) Index(entity);
            var identified = new HashSet<EntityId>();
            for (int tick = 0; tick < 3; tick++)
            {
                simulation.AdvanceOneTick();
                foreach (var target in targets)
                    if (store.IsEntityCurrentlyIdentified(BlueFaction, target)) identified.Add(target);
                if (tick == 0) Assert.Equal(BattlefieldIntelligenceSystem.MaximumVisualTracesPerSensor, identified.Count);
                timeline.Add(Snapshot());
            }
            Assert.Equal(targets.Length, identified.Count);
            var removed = targets[^1]; spatial?.Remove(removed); Assert.True(simulation.Entities.DestroyEntity(removed));
            var replacement = CreateSignatureEntity(simulation, RedFaction, new Vector3(20, 0, 10), 999);
            Index(replacement);
            Assert.Equal(removed.Index, replacement.Index); Assert.NotEqual(removed, replacement);
            simulation.RunTicks(3, TestContext.Current.CancellationToken);
            Assert.True(store.TryGetContact(BlueFaction, IntelligenceContactKey.FromEntity(replacement), out var contact));
            Assert.Equal(999u, contact.IdentityKey);
            timeline.Add(Snapshot());
            return string.Join("|", timeline);
    
            string Snapshot() => System.Text.Json.JsonSerializer.Serialize(store.Capture(BlueFaction),
                SnapshotJsonOptions);
            void Index(EntityId entity)
            {
                spatial?.Insert(new SpatialPresence(Vector3.One,
                    new ForgeLine.World.SpatialEntryMetadata(1, 1, ForgeLine.World.SpatialMobility.Static))
                    .CreateEntry(entity, simulation.Entities.GetComponent<WorldTransform>(entity)));
            }
            }
    }

    private sealed class FlatTerrain : ForgeLine.World.ITerrainQuery
    {
        public ForgeLine.World.AxisAlignedBounds WorldBounds => new(Vector3.Zero, new Vector3(256, 20, 256));
        public bool TrySampleHeight(float x, float z, out float height) { height = 0; return true; }
        public bool TrySampleNormal(float x, float z, out Vector3 normal) { normal = Vector3.UnitY; return true; }
    }

    private static EntityId CreateSensorEntity(
        SimulationCoordinator simulation,
        FactionId faction,
        Vector3 position)
    {
        EntityId entity =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            entity,
            new IntelligenceSignature(
                faction,
                identityKey: entity.Index + 1));
        return entity;
    }

    private static EntityId CreateSignatureEntity(
        SimulationCoordinator simulation,
        FactionId faction,
        Vector3 position,
        uint identityKey)
    {
        EntityId entity =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            entity,
            new IntelligenceSignature(
                faction,
                identityKey));
        return entity;
    }

    private static EntityId CreateCombatEntity(
        SimulationCoordinator simulation,
        FactionId faction,
        Vector3 position,
        TargetClass targetClass,
        uint identityKey)
    {
        EntityId entity =
            CreateSignatureEntity(
                simulation,
                faction,
                position,
                identityKey);
        simulation.Entities.AddComponent(
            entity,
            new Combatant(faction));
        simulation.Entities.AddComponent(
            entity,
            new Targetable(targetClass));
        simulation.Entities.AddComponent(
            entity,
            HealthState.Full(100.0));
        return entity;
    }

    private static WeaponDefinition CreateTargetingWeapon() =>
        new(
            new WeaponId(1),
            rangeMeters: 100.0f,
            fireIntervalTicks: 20,
            ammunitionPerShot: 1.0,
            new DamagePayload(10.0),
            WeaponDeliveryModel.Hitscan,
            effectiveness:
                new WeaponEffectiveness(
                    TargetClassMask.All,
                    penetration: 100.0));
}
