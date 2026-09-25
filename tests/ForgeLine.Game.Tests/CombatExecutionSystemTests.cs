using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class CombatExecutionSystemTests
{
    private static readonly FactionId BlueFaction = new(1);
    private static readonly FactionId RedFaction = new(2);

    [Fact]
    public void HitscanCadenceConsumesAuthoritativeAmmunitionAndAppliesDamage()
    {
        CombatScenario scenario =
            CreateScenario(
                CreateHitscanWeapon(
                    fireIntervalTicks: 2,
                    damage: 10.0),
                shooterAmmunition: 10.0,
                targetPosition: new Vector3(10.0f, 0.0f, 0.0f));

        scenario.Simulation.RunTicks(5);

        Assert.Equal(
            7.0,
            GetAmmunitionQuantity(scenario),
            precision: 6);
        Assert.Equal(
            70.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                scenario.Target).Current,
            precision: 6);
        Assert.Equal(3UL, scenario.Runtime.Metrics.TotalShotsFired);
        Assert.Equal(3UL, scenario.Runtime.Metrics.TotalHits);
        Assert.Equal(30.0, scenario.Runtime.Metrics.TotalDamageApplied, precision: 6);
    }

    [Fact]
    public void ReloadWindowDelaysSubsequentMagazine()
    {
        var weapon =
            new WeaponDefinition(
                new WeaponId(1),
                rangeMeters: 100.0f,
                fireIntervalTicks: 1,
                ammunitionPerShot: 1.0,
                new DamagePayload(10.0),
                WeaponDeliveryModel.Hitscan,
                magazineSize: 2,
                reloadTicks: 3);

        CombatScenario scenario =
            CreateScenario(
                weapon,
                shooterAmmunition: 10.0,
                targetPosition: new Vector3(10.0f, 0.0f, 0.0f));

        scenario.Simulation.RunTicks(5);

        Assert.Equal(3UL, scenario.Runtime.Metrics.TotalShotsFired);
        Assert.Equal(
            7.0,
            GetAmmunitionQuantity(scenario),
            precision: 6);
        Assert.Equal(
            70.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                scenario.Target).Current,
            precision: 6);
    }

    [Fact]
    public void EmptyAmmunitionPreventsFireAndDamage()
    {
        CombatScenario scenario =
            CreateScenario(
                CreateHitscanWeapon(),
                shooterAmmunition: 0.0,
                targetPosition: new Vector3(10.0f, 0.0f, 0.0f));

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(0UL, scenario.Runtime.Metrics.TotalShotsFired);
        Assert.Equal(
            100.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                scenario.Target).Current,
            precision: 6);
        Assert.Equal(
            0.0,
            GetAmmunitionQuantity(scenario),
            precision: 6);
    }

    [Fact]
    public void OutOfRangeTargetDoesNotConsumeAmmunition()
    {
        CombatScenario scenario =
            CreateScenario(
                CreateHitscanWeapon(rangeMeters: 25.0f),
                shooterAmmunition: 5.0,
                targetPosition: new Vector3(30.0f, 0.0f, 0.0f));

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(0UL, scenario.Runtime.Metrics.TotalShotsFired);
        Assert.Equal(
            5.0,
            GetAmmunitionQuantity(scenario),
            precision: 6);
        Assert.Equal(
            100.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                scenario.Target).Current,
            precision: 6);
    }

    [Fact]
    public void PhysicalProjectileUsesSpatialIndexAndImpactsExactlyOnce()
    {
        var spatialIndex = new SpatialGridIndex();
        WeaponDefinition weapon =
            CreateProjectileWeapon(
                projectileSpeedMetersPerSecond: 20.0f,
                projectileLifetimeTicks: 20,
                damage: 25.0);

        CombatScenario scenario =
            CreateScenario(
                weapon,
                shooterAmmunition: 5.0,
                targetPosition: new Vector3(2.0f, 0.0f, 0.0f),
                spatialIndex);

        scenario.Simulation.Entities.AddComponent(
            scenario.Target,
            new SpatialPresence(
                new Vector3(0.5f),
                new SpatialEntryMetadata(
                    RedFaction.Value,
                    categoryMask: 1,
                    SpatialMobility.Mobile)));

        scenario.Simulation.RunTicks(3);

        Assert.Equal(
            75.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                scenario.Target).Current,
            precision: 6);
        Assert.Equal(1UL, scenario.Runtime.Metrics.TotalProjectilesSpawned);
        Assert.Equal(1UL, scenario.Runtime.Metrics.TotalImpacts);
        Assert.Equal(1UL, scenario.Runtime.Metrics.TotalHits);
        Assert.Equal(0, scenario.Runtime.Metrics.ActiveProjectiles);

        scenario.Simulation.RunTicks(5);

        Assert.Equal(
            75.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                scenario.Target).Current,
            precision: 6);
        Assert.Equal(1UL, scenario.Runtime.Metrics.TotalImpacts);
        Assert.Equal(1UL, scenario.Runtime.Metrics.TotalHits);
    }

    [Fact]
    public void ProjectileCanResolveAfterItsSourceWasDestroyed()
    {
        CombatScenario scenario =
            CreateScenario(
                CreateProjectileWeapon(
                    projectileSpeedMetersPerSecond: 20.0f,
                    projectileLifetimeTicks: 20,
                    damage: 20.0),
                shooterAmmunition: 1.0,
                targetPosition: new Vector3(2.0f, 0.0f, 0.0f));

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(
                scenario.Shooter));

        scenario.Simulation.RunTicks(2);

        Assert.Equal(
            80.0,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                scenario.Target).Current,
            precision: 6);
        Assert.Equal(1UL, scenario.Runtime.Metrics.TotalImpacts);
        Assert.Equal(1UL, scenario.Runtime.Metrics.TotalHits);
    }

    [Fact]
    public void ZeroHealthTargetIsDestroyedAtLifecycleSynchronizationPoint()
    {
        CombatScenario scenario =
            CreateScenario(
                CreateHitscanWeapon(damage: 100.0),
                shooterAmmunition: 1.0,
                targetPosition: new Vector3(5.0f, 0.0f, 0.0f),
                targetHealth: 50.0);

        scenario.Simulation.AdvanceOneTick();

        Assert.False(
            scenario.Simulation.Entities.IsAlive(
                scenario.Target));
        Assert.Equal(1UL, scenario.Runtime.Metrics.TotalDestructions);
        Assert.Contains(
            scenario.Runtime.Events,
            combatEvent =>
                combatEvent.Type ==
                CombatEventType.EntityDestroyed &&
                combatEvent.Target == scenario.Target);
    }

    [Fact]
    public void StaleTargetIsClearedWithoutFiring()
    {
        CombatScenario scenario =
            CreateScenario(
                CreateHitscanWeapon(),
                shooterAmmunition: 5.0,
                targetPosition: new Vector3(5.0f, 0.0f, 0.0f));

        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(
                scenario.Target));

        scenario.Simulation.AdvanceOneTick();

        WeaponState state =
            scenario.Simulation.Entities.GetComponent<WeaponState>(
                scenario.Shooter);

        Assert.False(state.Target.IsValid);
        Assert.Equal(0UL, scenario.Runtime.Metrics.TotalShotsFired);
        Assert.Equal(
            5.0,
            GetAmmunitionQuantity(scenario),
            precision: 6);
    }

    [Fact]
    public void HeadlessCombatOutcomeIsRepeatable()
    {
        CombatOutcome first =
            RunRepeatableHeadlessScenario();
        CombatOutcome second =
            RunRepeatableHeadlessScenario();

        Assert.Equal(first, second);
        Assert.Equal(4UL, first.Shots);
        Assert.Equal(60.0, first.TargetHealth, precision: 6);
        Assert.Equal(6.0, first.AmmunitionRemaining, precision: 6);
    }

    private static CombatOutcome RunRepeatableHeadlessScenario()
    {
        CombatScenario scenario =
            CreateScenario(
                CreateHitscanWeapon(
                    fireIntervalTicks: 2,
                    damage: 10.0),
                shooterAmmunition: 10.0,
                targetPosition: new Vector3(12.0f, 0.0f, 0.0f),
                simulationSeed: 12345);

        scenario.Simulation.RunTicks(7);

        return new CombatOutcome(
            scenario.Runtime.Metrics.TotalShotsFired,
            scenario.Simulation.Entities.GetComponent<HealthState>(
                scenario.Target).Current,
            GetAmmunitionQuantity(scenario));
    }

    private static WeaponDefinition CreateHitscanWeapon(
        float rangeMeters = 100.0f,
        int fireIntervalTicks = 1,
        double damage = 10.0) =>
        new(
            new WeaponId(1),
            rangeMeters,
            fireIntervalTicks,
            ammunitionPerShot: 1.0,
            new DamagePayload(damage),
            WeaponDeliveryModel.Hitscan);

    private static WeaponDefinition CreateProjectileWeapon(
        float projectileSpeedMetersPerSecond,
        int projectileLifetimeTicks,
        double damage) =>
        new(
            new WeaponId(1),
            rangeMeters: 100.0f,
            fireIntervalTicks: 20,
            ammunitionPerShot: 1.0,
            new DamagePayload(damage),
            WeaponDeliveryModel.PhysicalProjectile,
            projectileSpeedMetersPerSecond: projectileSpeedMetersPerSecond,
            projectileRadiusMeters: 0.1f,
            projectileLifetimeTicks: projectileLifetimeTicks);

    private static CombatScenario CreateScenario(
        WeaponDefinition weapon,
        double shooterAmmunition,
        Vector3 targetPosition,
        SpatialGridIndex? spatialIndex = null,
        double targetHealth = 100.0,
        ulong simulationSeed = 1)
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                seed: simulationSeed);
        var inventories = new InventoryStore();
        var catalog = new WeaponCatalog();
        var runtime = new CombatRuntime();

        catalog.Add(weapon);

        if (spatialIndex is not null)
        {
            var synchronizer =
                new SpatialIndexSynchronizer(
                    spatialIndex);
            simulation.RegisterSystem(
                new SpatialIndexSystem(
                    synchronizer));
        }

        simulation.RegisterSystem(
            new CombatExecutionSystem(
                catalog,
                inventories,
                runtime,
                spatialIndex));
        simulation.RegisterSystem(
            new CombatDamageResolutionSystem(
                runtime));
        simulation.RegisterSystem(
            new CombatEntityLifecycleSystem(
                runtime,
                spatialIndex));

        EntityId shooter =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            shooter,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            shooter,
            new Combatant(BlueFaction));
        simulation.Entities.AddComponent(
            shooter,
            HealthState.Full(100.0));

        InventoryId shooterInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    totalCapacity: 100.0,
                    acceptedResources: [ResourceIds.Ammunition]));

        if (shooterAmmunition > 0.0)
        {
            Assert.True(
                inventories.Add(
                    shooterInventory,
                    ResourceIds.Ammunition,
                    shooterAmmunition).Succeeded);
        }

        simulation.Entities.AddComponent(
            shooter,
            new AmmunitionState(
                shooterInventory,
                capacity: 100.0));

        EntityId target =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            target,
            new WorldTransform(
                targetPosition,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            target,
            new Combatant(RedFaction));
        simulation.Entities.AddComponent(
            target,
            HealthState.Full(targetHealth));
        simulation.Entities.AddComponent(
            target,
            CombatHitbox.Default);

        simulation.Entities.AddComponent(
            shooter,
            new WeaponState(
                weapon.Id,
                target));

        return new CombatScenario(
            simulation,
            inventories,
            runtime,
            shooter,
            target,
            shooterInventory);
    }

    private static double GetAmmunitionQuantity(
        in CombatScenario scenario) =>
        scenario.Inventories.GetQuantity(
            scenario.ShooterInventory,
            ResourceIds.Ammunition);

    private readonly record struct CombatScenario(
        SimulationCoordinator Simulation,
        InventoryStore Inventories,
        CombatRuntime Runtime,
        EntityId Shooter,
        EntityId Target,
        InventoryId ShooterInventory);

    private readonly record struct CombatOutcome(
        ulong Shots,
        double TargetHealth,
        double AmmunitionRemaining);
}
