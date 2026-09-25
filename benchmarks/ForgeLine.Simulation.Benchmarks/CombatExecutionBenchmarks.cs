using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public sealed class CombatWeaponBenchmarks
{
    private SimulationCoordinator _simulation = null!;

    [Params(100, 1_000)]
    public int ArmedEntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _simulation =
            CreateHitscanSimulation(
                ArmedEntityCount);
    }

    [Benchmark]
    public ulong ExecuteDirectFireTick()
    {
        _simulation.AdvanceOneTick();
        return _simulation.CurrentTick.Value;
    }

    private static SimulationCoordinator CreateHitscanSimulation(
        int pairCount)
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                initialEntityCapacity:
                    Math.Max(256, pairCount * 2 + 16));
        var inventories =
            new InventoryStore();
        var catalog =
            new WeaponCatalog();
        var runtime =
            new CombatRuntime();

        WeaponDefinition weapon =
            new(
                new WeaponId(1),
                rangeMeters: 100.0f,
                fireIntervalTicks: 1,
                ammunitionPerShot: 1.0,
                new DamagePayload(1.0),
                WeaponDeliveryModel.Hitscan);

        catalog.Add(weapon);
        simulation.RegisterSystem(
            new CombatExecutionSystem(
                catalog,
                inventories,
                runtime));
        simulation.RegisterSystem(
            new CombatDamageResolutionSystem(
                runtime));
        simulation.RegisterSystem(
            new CombatEntityLifecycleSystem(
                runtime));

        var blue = new FactionId(1);
        var red = new FactionId(2);

        for (int index = 0;
             index < pairCount;
             index++)
        {
            float row =
                index * 2.0f;

            EntityId target =
                simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(
                target,
                new WorldTransform(
                    new Vector3(
                        10.0f,
                        0.0f,
                        row),
                    Quaternion.Identity,
                    Vector3.One));
            simulation.Entities.AddComponent(
                target,
                new Combatant(red));
            simulation.Entities.AddComponent(
                target,
                HealthState.Full(
                    1_000_000_000.0));

            EntityId shooter =
                simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(
                shooter,
                new WorldTransform(
                    new Vector3(
                        0.0f,
                        0.0f,
                        row),
                    Quaternion.Identity,
                    Vector3.One));
            simulation.Entities.AddComponent(
                shooter,
                new Combatant(blue));

            InventoryId inventory =
                inventories.CreateInventory(
                    new InventorySpecification(
                        1_000_000.0,
                        [ResourceIds.Ammunition]));
            _ =
                inventories.Add(
                    inventory,
                    ResourceIds.Ammunition,
                    1_000_000.0);

            simulation.Entities.AddComponent(
                shooter,
                new AmmunitionState(
                    inventory,
                    1_000_000.0));
            simulation.Entities.AddComponent(
                shooter,
                new WeaponState(
                    weapon.Id,
                    target));
        }

        return simulation;
    }
}

[MemoryDiagnoser]
public sealed class ProjectileSimulationBenchmarks
{
    private SimulationCoordinator _simulation = null!;

    [Params(100, 1_000)]
    public int ProjectileCount { get; set; }

    [IterationSetup]
    public void SetupIteration()
    {
        _simulation =
            CreateProjectileSimulation(
                ProjectileCount);
    }

    [Benchmark]
    public ulong ExecuteProjectileTick()
    {
        _simulation.AdvanceOneTick();
        return _simulation.CurrentTick.Value;
    }

    private static SimulationCoordinator CreateProjectileSimulation(
        int projectileCount)
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                initialEntityCapacity:
                    Math.Max(256, projectileCount + 16));
        var inventories =
            new InventoryStore();
        var catalog =
            new WeaponCatalog();
        var runtime =
            new CombatRuntime();

        simulation.RegisterSystem(
            new CombatExecutionSystem(
                catalog,
                inventories,
                runtime));
        simulation.RegisterSystem(
            new CombatDamageResolutionSystem(
                runtime));
        simulation.RegisterSystem(
            new CombatEntityLifecycleSystem(
                runtime));

        EntityId source =
            simulation.Entities.CreateEntity();
        var faction =
            new FactionId(1);

        for (int index = 0;
             index < projectileCount;
             index++)
        {
            EntityId projectile =
                simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(
                projectile,
                new WorldTransform(
                    new Vector3(
                        0.0f,
                        1.0f,
                        index * 2.0f),
                    Quaternion.Identity,
                    Vector3.One));
            simulation.Entities.AddComponent(
                projectile,
                new ProjectileState(
                    source,
                    faction,
                    new WeaponId(1),
                    new Vector3(
                        20.0f,
                        0.0f,
                        0.0f),
                    remainingTicks: 1_000,
                    radiusMeters: 0.1f,
                    new DamagePayload(1.0)));
        }

        return simulation;
    }
}


[MemoryDiagnoser]
public sealed class TargetAcquisitionBenchmarks
{
    private SimulationCoordinator _simulation = null!;

    [Params(100, 1_000)]
    public int CandidateCount { get; set; }

    [IterationSetup]
    public void SetupIteration()
    {
        _simulation =
            CreateAcquisitionSimulation(
                CandidateCount);
    }

    [Benchmark]
    public ulong AcquireDenseFieldTarget()
    {
        _simulation.AdvanceOneTick();
        return _simulation.CurrentTick.Value;
    }

    private static SimulationCoordinator CreateAcquisitionSimulation(
        int candidateCount)
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                initialEntityCapacity:
                    Math.Max(256, candidateCount + 16));
        var spatial =
            new ForgeLine.World.SpatialGridIndex();
        var synchronizer =
            new SpatialIndexSynchronizer(
                spatial);
        var weapons =
            new WeaponCatalog();
        var blue =
            new FactionId(1);
        var red =
            new FactionId(2);

        WeaponDefinition weapon =
            new(
                new WeaponId(1),
                rangeMeters: 250.0f,
                fireIntervalTicks: 20,
                ammunitionPerShot: 1.0,
                new DamagePayload(10.0),
                WeaponDeliveryModel.Hitscan,
                effectiveness:
                    new WeaponEffectiveness(
                        TargetClassMask.All,
                        penetration: 100.0));
        weapons.Add(weapon);

        simulation.RegisterSystem(
            new SpatialIndexSystem(
                synchronizer));
        simulation.RegisterSystem(
            new TargetAcquisitionSystem(
                weapons,
                spatial));

        EntityId source =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            source,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            source,
            new Combatant(
                blue));
        simulation.Entities.AddComponent(
            source,
            new SpatialPresence(
                new Vector3(0.5f),
                new ForgeLine.World.SpatialEntryMetadata(
                    blue.Value,
                    CategoryMask: 1)));
        simulation.Entities.AddComponent(
            source,
            new WeaponState(
                weapon.Id,
                EntityId.Invalid));

        for (int index = 0;
             index < candidateCount;
             index++)
        {
            float angle =
                index *
                (MathF.Tau /
                 Math.Max(1, candidateCount));
            float radius =
                20.0f +
                (index % 200) *
                0.8f;
            Vector3 position =
                new(
                    MathF.Cos(angle) * radius,
                    0.0f,
                    MathF.Sin(angle) * radius);

            EntityId target =
                simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(
                target,
                new WorldTransform(
                    position,
                    Quaternion.Identity,
                    Vector3.One));
            simulation.Entities.AddComponent(
                target,
                new Combatant(
                    red));
            simulation.Entities.AddComponent(
                target,
                new Targetable(
                    index % 2 == 0
                        ? TargetClass.ArmoredVehicle
                        : TargetClass.LightVehicle));
            simulation.Entities.AddComponent(
                target,
                HealthState.Full(100.0));
            simulation.Entities.AddComponent(
                target,
                new TargetPriority(
                    index % 8));
            simulation.Entities.AddComponent(
                target,
                new SpatialPresence(
                    new Vector3(0.5f),
                    new ForgeLine.World.SpatialEntryMetadata(
                        red.Value,
                        CategoryMask: 1)));
        }

        return simulation;
    }
}
