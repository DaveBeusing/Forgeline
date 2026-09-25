using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ArtilleryFireMissionSystemTests
{
    private static readonly PlayerId BluePlayer = new(1);
    private static readonly FactionId BlueFaction = new(1);
    private static readonly FactionId RedFaction = new(2);

    [Fact]
    public void HiddenCoordinateMissionIsCancelledWithoutAmmunitionUse()
    {
        ArtilleryScenario scenario =
            CreateScenario(
                initialAmmunition: 3.0,
                acquisitionTicks: 0);

        var command =
            new FireMissionCommand(
                BluePlayer,
                [scenario.Artillery],
                new Vector3(150.0f, 0.0f, 50.0f),
                requestedRounds: 1,
                scenario.Simulation.CurrentTick);

        scenario.Simulation.SubmitCommand(
            command,
            scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();

        FireMissionState mission =
            scenario.Simulation.Entities.GetComponent<FireMissionState>(
                scenario.Artillery);

        Assert.Equal(
            FireMissionStatus.Cancelled,
            mission.Status);
        Assert.Equal(
            3.0,
            scenario.Inventories.GetQuantity(
                scenario.ArtilleryInventory,
                ResourceIds.Ammunition),
            precision: 6);
        Assert.Equal(
            0UL,
            scenario.ArtillerySystem.Metrics.TotalShotsFired);
    }

    [Fact]
    public void RadarContactMissionUsesLastKnownCoordinateWithoutLiveTargetReference()
    {
        ArtilleryScenario scenario =
            CreateScenario(
                initialAmmunition: 2.0,
                acquisitionTicks: 0);

        EntityId recon =
            CreateReconSensor(
                scenario.Simulation,
                new Vector3(80.0f, 0.0f, 50.0f),
                radarOnly: true);
        EntityId target =
            CreateDamageableTarget(
                scenario.Simulation,
                new Vector3(150.0f, 0.0f, 50.0f),
                health: 200.0);

        IntelligenceContactKey key =
            IntelligenceContactKey.FromEntity(target);
        var command =
            new FireMissionCommand(
                BluePlayer,
                [scenario.Artillery],
                key,
                requestedRounds: 1,
                scenario.Simulation.CurrentTick);

        scenario.Simulation.SubmitCommand(
            command,
            scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            scenario.Intelligence.TryGetContact(
                BlueFaction,
                key,
                out IntelligenceContact contact));
        Assert.Equal(
            IntelligenceState.Detected,
            contact.State);

        FireMissionState mission =
            scenario.Simulation.Entities.GetComponent<FireMissionState>(
                scenario.Artillery);

        Assert.Equal(
            contact.LastKnownPosition,
            mission.TargetPosition);
        Assert.Equal(
            key,
            mission.ContactKey);
        Assert.Equal(
            FireMissionStatus.Complete,
            mission.Status);
        Assert.Equal(
            1,
            mission.RoundsFired);
        Assert.Equal(
            1UL,
            scenario.ArtillerySystem.Metrics.TotalShotsFired);

        _ = recon;
    }

    [Theory]
    [InlineData(70.0f)]
    [InlineData(470.0f)]
    public void MinimumAndMaximumRangeAreEnforced(float targetX)
    {
        ArtilleryScenario scenario =
            CreateScenario(
                initialAmmunition: 2.0,
                acquisitionTicks: 0,
                minimumRange: 50.0f,
                maximumRange: 300.0f);

        CreateReconSensor(
            scenario.Simulation,
            new Vector3(targetX, 0.0f, 50.0f),
            radarOnly: false,
            visualRange: 40.0f);

        var command =
            new FireMissionCommand(
                BluePlayer,
                [scenario.Artillery],
                new Vector3(targetX, 0.0f, 50.0f),
                requestedRounds: 1,
                scenario.Simulation.CurrentTick);

        scenario.Simulation.SubmitCommand(
            command,
            scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();

        FireMissionState mission =
            scenario.Simulation.Entities.GetComponent<FireMissionState>(
                scenario.Artillery);

        Assert.Equal(
            FireMissionStatus.Cancelled,
            mission.Status);
        Assert.Equal(
            0UL,
            scenario.ArtillerySystem.Metrics.TotalShotsFired);
    }

    [Fact]
    public void ProjectileTravelsForConfiguredTimeAndAppliesAreaDamageExactlyOnce()
    {
        ArtilleryScenario scenario =
            CreateScenario(
                initialAmmunition: 1.0,
                acquisitionTicks: 0,
                projectileSpeed: 100.0f,
                damage: 40.0,
                areaRadius: 20.0f,
                minimumDamageFraction: 0.2);

        CreateReconSensor(
            scenario.Simulation,
            new Vector3(150.0f, 0.0f, 50.0f),
            radarOnly: false,
            visualRange: 40.0f);

        EntityId center =
            CreateDamageableTarget(
                scenario.Simulation,
                new Vector3(150.0f, 0.0f, 50.0f),
                health: 200.0);
        EntityId halfRadius =
            CreateDamageableTarget(
                scenario.Simulation,
                new Vector3(160.0f, 0.0f, 50.0f),
                health: 200.0);
        EntityId outside =
            CreateDamageableTarget(
                scenario.Simulation,
                new Vector3(180.5f, 0.0f, 50.0f),
                health: 200.0);

        var command =
            new FireMissionCommand(
                BluePlayer,
                [scenario.Artillery],
                new Vector3(150.0f, 0.0f, 50.0f),
                requestedRounds: 1,
                scenario.Simulation.CurrentTick);

        scenario.Simulation.SubmitCommand(
            command,
            scenario.Simulation.CurrentTick.Next());
        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            1,
            scenario.Simulation.Entities.GetComponentCount<
                IndirectFireProjectileState>());

        scenario.Simulation.RunTicks(
            19,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            200.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                center).Current,
            precision: 6);
        Assert.Equal(
            0UL,
            scenario.ArtillerySystem.Metrics.TotalImpacts);

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            160.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                center).Current,
            precision: 6);
        Assert.Equal(
            176.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                halfRadius).Current,
            precision: 6);
        Assert.Equal(
            200.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                outside).Current,
            precision: 6);
        Assert.Equal(
            1UL,
            scenario.ArtillerySystem.Metrics.TotalImpacts);
        Assert.Equal(
            2UL,
            scenario.ArtillerySystem.Metrics.TotalAreaDamageTargets);

        scenario.Simulation.RunTicks(
            5,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            160.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                center).Current,
            precision: 6);
        Assert.Equal(
            176.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                halfRadius).Current,
            precision: 6);
        Assert.Equal(
            1UL,
            scenario.ArtillerySystem.Metrics.TotalImpacts);
    }

    [Fact]
    public void MissionStopsOnEmptyAmmunitionAndResumesAfterBattlefieldSupply()
    {
        ArtilleryScenario scenario =
            CreateScenario(
                initialAmmunition: 1.0,
                acquisitionTicks: 0,
                fireIntervalTicks: 1,
                artilleryCapacity: 3.0,
                registerSupplySystem: true);

        EntityId recon =
            CreateReconSensor(
                scenario.Simulation,
                new Vector3(150.0f, 0.0f, 50.0f),
                radarOnly: true);
        EntityId target =
            CreateDamageableTarget(
                scenario.Simulation,
                new Vector3(150.0f, 0.0f, 50.0f),
                health: 500.0);

        InventoryId providerInventory =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(
                    10.0,
                    [ResourceIds.Ammunition]));
        Assert.True(
            scenario.Inventories.Add(
                providerInventory,
                ResourceIds.Ammunition,
                2.0).Succeeded);

        EntityId provider =
            scenario.Simulation.Entities.CreateEntity();
        scenario.Simulation.Entities.AddComponent(
            provider,
            new WorldTransform(
                new Vector3(55.0f, 0.0f, 50.0f),
                Quaternion.Identity,
                Vector3.One));
        scenario.Simulation.Entities.AddComponent(
            provider,
            new SupplyProvider(
                providerInventory,
                BluePlayer,
                resupplyRangeMeters: 20.0f,
                enabled: false));

        var command =
            new FireMissionCommand(
                BluePlayer,
                [scenario.Artillery],
                IntelligenceContactKey.FromEntity(target),
                requestedRounds: 3,
                scenario.Simulation.CurrentTick);

        scenario.Simulation.SubmitCommand(
            command,
            scenario.Simulation.CurrentTick.Next());

        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();

        FireMissionState noAmmo =
            scenario.Simulation.Entities.GetComponent<FireMissionState>(
                scenario.Artillery);

        Assert.Equal(
            FireMissionStatus.NoAmmo,
            noAmmo.Status);
        Assert.Equal(
            1,
            noAmmo.RoundsFired);
        Assert.Equal(
            0.0,
            scenario.Inventories.GetQuantity(
                scenario.ArtilleryInventory,
                ResourceIds.Ammunition),
            precision: 6);

        scenario.Simulation.Entities.SetComponent(
            provider,
            new SupplyProvider(
                providerInventory,
                BluePlayer,
                resupplyRangeMeters: 20.0f,
                enabled: true));

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(
            2.0,
            scenario.Inventories.GetQuantity(
                scenario.ArtilleryInventory,
                ResourceIds.Ammunition),
            precision: 6);

        scenario.Simulation.RunTicks(
            3,
            TestContext.Current.CancellationToken);

        FireMissionState completed =
            scenario.Simulation.Entities.GetComponent<FireMissionState>(
                scenario.Artillery);

        Assert.Equal(
            FireMissionStatus.Complete,
            completed.Status);
        Assert.Equal(
            3,
            completed.RoundsFired);
        Assert.Equal(
            3UL,
            scenario.ArtillerySystem.Metrics.TotalShotsFired);
        Assert.Equal(
            3.0,
            scenario.ArtillerySystem.Metrics.TotalAmmunitionConsumed,
            precision: 6);
        Assert.True(
            scenario.BattlefieldSupply!.Metrics.TotalAmmunitionTransferred >=
            2.0);

        _ = recon;
    }

    private static ArtilleryScenario CreateScenario(
        double initialAmmunition,
        int acquisitionTicks,
        float minimumRange = 40.0f,
        float maximumRange = 400.0f,
        int fireIntervalTicks = 5,
        float projectileSpeed = 100.0f,
        double damage = 40.0,
        float areaRadius = 20.0f,
        double minimumDamageFraction = 0.2,
        double artilleryCapacity = 10.0,
        bool registerSupplySystem = false)
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                seed: 1234);
        var inventories =
            new InventoryStore();
        var runtime =
            new CombatRuntime();
        var intelligence =
            new FactionIntelligenceStore(
                new IntelligenceGridSettings
                {
                    CellSizeMeters = 16.0f
                });
        var artilleryWeapons =
            new ArtilleryWeaponCatalog();
        ArtilleryWeaponDefinition definition =
            new(
                new WeaponId(100),
                minimumRange,
                maximumRange,
                fireIntervalTicks,
                acquisitionTicks,
                ammunitionPerShot: 1.0,
                new DamagePayload(damage),
                areaRadius,
                minimumDamageFraction,
                projectileSpeed,
                apexHeightMeters: 50.0f,
                dispersionRadiusMeters: 0.0f,
                effectiveness:
                    new WeaponEffectiveness(
                        TargetClassMask.All,
                        penetration: 50.0));
        artilleryWeapons.Add(definition);

        TerrainWorld terrain =
            CreateFlatTerrain();

        var intelligenceSystem =
            new BattlefieldIntelligenceSystem(
                intelligence);
        var artillerySystem =
            new ArtilleryFireMissionSystem(
                artilleryWeapons,
                inventories,
                runtime,
                intelligence,
                terrain);
        BattlefieldSupplySystem? supply =
            registerSupplySystem
                ? new BattlefieldSupplySystem(inventories)
                : null;

        simulation.RegisterSystem(intelligenceSystem);
        simulation.RegisterSystem(artillerySystem);
        simulation.RegisterSystem(
            new CombatDamageResolutionSystem(
                runtime,
                armor: null,
                artilleryWeapons: artilleryWeapons));

        if (supply is not null)
        {
            simulation.RegisterSystem(supply);
        }

        simulation.RegisterSystem(
            new CombatEntityLifecycleSystem(
                runtime));

        InventoryId artilleryInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    artilleryCapacity,
                    [ResourceIds.Ammunition]));

        if (initialAmmunition > 0.0)
        {
            Assert.True(
                inventories.Add(
                    artilleryInventory,
                    ResourceIds.Ammunition,
                    initialAmmunition).Succeeded);
        }

        EntityId artillery =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            artillery,
            new WorldTransform(
                new Vector3(50.0f, 0.0f, 50.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            artillery,
            new ControllableEntity(
                BluePlayer,
                ControllableEntityCategory.Unit));
        simulation.Entities.AddComponent(
            artillery,
            new Combatant(BlueFaction));
        simulation.Entities.AddComponent(
            artillery,
            new IntelligenceSignature(
                BlueFaction,
                identityKey: 1));
        simulation.Entities.AddComponent(
            artillery,
            new ArtilleryCapability(
                definition.Id));
        simulation.Entities.AddComponent(
            artillery,
            new AmmunitionState(
                artilleryInventory,
                artilleryCapacity));

        return new ArtilleryScenario(
            simulation,
            inventories,
            intelligence,
            artillerySystem,
            supply,
            artillery,
            artilleryInventory);
    }

    private static EntityId CreateReconSensor(
        SimulationCoordinator simulation,
        Vector3 position,
        bool radarOnly,
        float visualRange = 200.0f)
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
                BlueFaction,
                identityKey:
                    entity.Index + 10));

        if (radarOnly)
        {
            simulation.Entities.AddComponent(
                entity,
                new RadarSensorState(
                    BlueFaction,
                    detectionRangeMeters: 250.0f,
                    identificationRangeMeters: 0.0f,
                    updateIntervalTicks: 1));
        }
        else
        {
            simulation.Entities.AddComponent(
                entity,
                new VisualSensorState(
                    BlueFaction,
                    visualRange,
                    updateIntervalTicks: 1));
        }

        return entity;
    }

    private static EntityId CreateDamageableTarget(
        SimulationCoordinator simulation,
        Vector3 position,
        double health)
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
            new Combatant(RedFaction));
        simulation.Entities.AddComponent(
            entity,
            new IntelligenceSignature(
                RedFaction,
                identityKey:
                    entity.Index + 100));
        simulation.Entities.AddComponent(
            entity,
            new Targetable(
                TargetClass.ArmoredVehicle));
        simulation.Entities.AddComponent(
            entity,
            HealthState.Full(health));

        return entity;
    }

    private static TerrainWorld CreateFlatTerrain()
    {
        var settings =
            new WorldGridSettings
            {
                ChunkSizeMeters = 512.0f,
                HeightSamplesPerSide = 2
            };
        var heightfield =
            new TerrainHeightfield(
                2,
                512.0f,
                [0.0f, 0.0f, 0.0f, 0.0f]);

        return new TerrainWorld(
            settings,
            [
                new TerrainChunk(
                    new ChunkCoordinate(0, 0),
                    heightfield)
            ]);
    }

    private readonly record struct ArtilleryScenario(
        SimulationCoordinator Simulation,
        InventoryStore Inventories,
        FactionIntelligenceStore Intelligence,
        ArtilleryFireMissionSystem ArtillerySystem,
        BattlefieldSupplySystem? BattlefieldSupply,
        EntityId Artillery,
        InventoryId ArtilleryInventory);
}
