using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public sealed class ArtilleryFireMissionBenchmarks
{
    private SimulationCoordinator _simulation = null!;

    [Params(10, 100)]
    public int ArtilleryCount { get; set; }

    [IterationSetup]
    public void SetupIteration()
    {
        _simulation =
            CreateScenario(ArtilleryCount);
    }

    [Benchmark]
    public ulong ExecuteSimultaneousFireMissions()
    {
        _simulation.AdvanceOneTick();
        return _simulation.CurrentTick.Value;
    }

    private static SimulationCoordinator CreateScenario(
        int artilleryCount)
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                seed: 12345,
                initialEntityCapacity:
                    Math.Max(256, artilleryCount * 2 + 16));
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
        var weapons =
            new ArtilleryWeaponCatalog();
        weapons.Add(
            new ArtilleryWeaponDefinition(
                new WeaponId(100),
                minimumRangeMeters: 20.0f,
                maximumRangeMeters: 500.0f,
                fireIntervalTicks: 20,
                acquisitionTicks: 0,
                ammunitionPerShot: 1.0,
                new DamagePayload(40.0),
                areaRadiusMeters: 18.0f,
                minimumDamageFraction: 0.2,
                projectileSpeedMetersPerSecond: 120.0f,
                apexHeightMeters: 80.0f,
                dispersionRadiusMeters: 3.0f));

        TerrainWorld terrain =
            CreateFlatTerrain();

        simulation.RegisterSystem(
            new BattlefieldIntelligenceSystem(
                intelligence));
        simulation.RegisterSystem(
            new ArtilleryFireMissionSystem(
                weapons,
                inventories,
                runtime,
                intelligence,
                terrain));
        simulation.RegisterSystem(
            new CombatDamageResolutionSystem(
                runtime,
                artilleryWeapons: weapons));
        simulation.RegisterSystem(
            new CombatEntityLifecycleSystem(
                runtime));

        var bluePlayer =
            new PlayerId(1);
        var blueFaction =
            new FactionId(1);

        EntityId observer =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            observer,
            new WorldTransform(
                new Vector3(250.0f, 0.0f, 250.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            observer,
            new IntelligenceSignature(
                blueFaction,
                identityKey: 1));
        simulation.Entities.AddComponent(
            observer,
            new VisualSensorState(
                blueFaction,
                rangeMeters: 220.0f,
                updateIntervalTicks: 1));

        for (int index = 0;
             index < artilleryCount;
             index++)
        {
            InventoryId inventory =
                inventories.CreateInventory(
                    new InventorySpecification(
                        10.0,
                        [ResourceIds.Ammunition]));
            _ =
                inventories.Add(
                    inventory,
                    ResourceIds.Ammunition,
                    1.0);

            float x =
                80.0f +
                (index % 10) * 4.0f;
            float z =
                80.0f +
                (index / 10) * 4.0f;

            EntityId artillery =
                simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(
                artillery,
                new WorldTransform(
                    new Vector3(x, 0.0f, z),
                    Quaternion.Identity,
                    Vector3.One));
            simulation.Entities.AddComponent(
                artillery,
                new ControllableEntity(
                    bluePlayer,
                    ControllableEntityCategory.Unit));
            simulation.Entities.AddComponent(
                artillery,
                new Combatant(blueFaction));
            simulation.Entities.AddComponent(
                artillery,
                new ArtilleryCapability(
                    new WeaponId(100)));
            simulation.Entities.AddComponent(
                artillery,
                new AmmunitionState(
                    inventory,
                    capacity: 10.0));

            simulation.SubmitCommand(
                new FireMissionCommand(
                    bluePlayer,
                    [artillery],
                    new Vector3(250.0f, 0.0f, 250.0f),
                    requestedRounds: 1,
                    simulation.CurrentTick),
                simulation.CurrentTick.Next());
        }

        return simulation;
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
}
