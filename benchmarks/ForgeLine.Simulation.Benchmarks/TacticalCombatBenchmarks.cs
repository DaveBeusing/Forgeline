using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public sealed class TacticalCombatBenchmarks
{
    private SimulationCoordinator _simulation = null!;

    [Params(100, 1000)]
    public int UnitCount { get; set; }

    [IterationSetup]
    public void SetupIteration()
    {
        _simulation =
            CreateScenario(UnitCount);
    }

    [Benchmark]
    public ulong AcquireAndCoordinateTacticalTargets()
    {
        _simulation.AdvanceOneTick();
        return _simulation.CurrentTick.Value;
    }

    private static SimulationCoordinator CreateScenario(
        int unitCount)
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                seed: 13579,
                initialEntityCapacity:
                    Math.Max(
                        512,
                        unitCount * 2 + 32));
        var weapons =
            new WeaponCatalog();
        weapons.Add(
            new WeaponDefinition(
                new WeaponId(1),
                rangeMeters: 160.0f,
                fireIntervalTicks: 4,
                ammunitionPerShot: 1.0,
                new DamagePayload(10.0),
                WeaponDeliveryModel.Hitscan));

        var intelligence =
            new FactionIntelligenceStore(
                new IntelligenceGridSettings
                {
                    CellSizeMeters = 16.0f
                });
        intelligence.BeginTick(
            new SimulationTick(1));

        var spatialIndex =
            new SpatialGridIndex();
        var synchronizer =
            new SpatialIndexSynchronizer(
                spatialIndex);
        var availability =
            new IntelligenceTargetAvailabilityPolicy(
                simulation.Entities,
                intelligence);
        var acquisition =
            new TargetAcquisitionSystem(
                weapons,
                spatialIndex,
                availability);
        var tactical =
            new TacticalCombatSystem(
                weapons,
                intelligence);

        simulation.RegisterSystem(
            new TacticalOrderPreparationSystem());
        simulation.RegisterSystem(
            new SpatialIndexSystem(
                synchronizer));
        simulation.RegisterSystem(
            acquisition);
        simulation.RegisterSystem(
            tactical);

        FactionId blue =
            new(1);
        FactionId red =
            new(2);
        PlayerId bluePlayer =
            new(1);

        EntityId group =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            group,
            new CombatGroupIntent(
                CombatOrderKind.AttackMove,
                bluePlayer,
                new Vector3(240.0f, 0.0f, 0.0f),
                hasDestination: true,
                EntityId.Invalid,
                FormationTemplate.Compact,
                unitCount,
                pursuitLeashMeters: 100.0f,
                SimulationTick.Zero));

        int side =
            checked(
                (int)Math.Ceiling(
                    Math.Sqrt(unitCount)));
        const float spacing = 2.0f;

        for (int index = 0;
             index < unitCount;
             index++)
        {
            int xIndex =
                index % side;
            int zIndex =
                index / side;

            Vector3 bluePosition =
                new(
                    xIndex * spacing,
                    0.0f,
                    zIndex * spacing);
            Vector3 redPosition =
                new(
                    80.0f + xIndex * spacing,
                    0.0f,
                    zIndex * spacing);

            EntityId blueEntity =
                CreateCombatEntity(
                    simulation,
                    blue,
                    bluePosition,
                    ownerMetadata: 1,
                    identityKey:
                        checked(
                            (uint)index + 1));
            simulation.Entities.AddComponent(
                blueEntity,
                new WeaponState(
                    new WeaponId(1),
                    EntityId.Invalid));
            simulation.Entities.AddComponent(
                blueEntity,
                FirePolicyState.FireAtWill);
            simulation.Entities.AddComponent(
                blueEntity,
                new CombatOrderState(
                    CombatOrderKind.AttackMove,
                    bluePlayer,
                    EntityId.Invalid,
                    new Vector3(
                        240.0f,
                        0.0f,
                        0.0f),
                    hasDestination: true,
                    bluePosition,
                    pursuitLeashMeters: 100.0f,
                    FormationTemplate.Compact,
                    SimulationTick.Zero,
                    SimulationTick.Zero));
            simulation.Entities.AddComponent(
                blueEntity,
                new CombatGroupMember(
                    group));

            EntityId redEntity =
                CreateCombatEntity(
                    simulation,
                    red,
                    redPosition,
                    ownerMetadata: 2,
                    identityKey:
                        checked(
                            (uint)unitCount +
                            (uint)index +
                            1));

            IntelligenceSignature signature =
                simulation.Entities.GetComponent<IntelligenceSignature>(
                    redEntity);
            intelligence.Observe(
                blue,
                redEntity,
                signature,
                redPosition,
                IntelligenceState.Identified,
                new SimulationTick(1));
        }

        return simulation;
    }

    private static EntityId CreateCombatEntity(
        SimulationCoordinator simulation,
        FactionId faction,
        Vector3 position,
        ulong ownerMetadata,
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
            new Combatant(faction));
        simulation.Entities.AddComponent(
            entity,
            new IntelligenceSignature(
                faction,
                identityKey));
        simulation.Entities.AddComponent(
            entity,
            new Targetable(
                TargetClass.LightVehicle));
        simulation.Entities.AddComponent(
            entity,
            HealthState.Full(100.0));
        simulation.Entities.AddComponent(
            entity,
            new SpatialPresence(
                new Vector3(0.5f),
                new SpatialEntryMetadata(
                    ownerMetadata,
                    CategoryMask: 1,
                    SpatialMobility.Mobile)));

        return entity;
    }
}
