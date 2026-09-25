using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public sealed class IntelligenceSensorBenchmarks
{
    private SimulationCoordinator _simulation = null!;

    [Params(100, 1_000)]
    public int UnitCount { get; set; }

    [IterationSetup]
    public void SetupIteration()
    {
        _simulation =
            CreateScenario(
                UnitCount);
    }

    [Benchmark]
    public ulong ExecuteSensorTick()
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
                initialEntityCapacity:
                    Math.Max(256, unitCount + 16));
        var spatial =
            new SpatialGridIndex();
        var synchronizer =
            new SpatialIndexSynchronizer(
                spatial);
        var store =
            new FactionIntelligenceStore(
                new IntelligenceGridSettings
                {
                    CellSizeMeters = 32.0f
                });

        simulation.RegisterSystem(
            new SpatialIndexSystem(
                synchronizer));
        simulation.RegisterSystem(
            new BattlefieldIntelligenceSystem(
                store,
                spatial));

        var blue =
            new FactionId(1);
        var red =
            new FactionId(2);

        for (int index = 0;
             index < unitCount;
             index++)
        {
            FactionId faction =
                index % 2 == 0
                    ? blue
                    : red;
            float x =
                (index % 32) * 12.0f;
            float z =
                (index / 32) * 12.0f;

            EntityId entity =
                simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(
                entity,
                new WorldTransform(
                    new Vector3(x, 0.0f, z),
                    Quaternion.Identity,
                    Vector3.One));
            simulation.Entities.AddComponent(
                entity,
                new IntelligenceSignature(
                    faction,
                    identityKey:
                        checked((uint)index + 1)));
            simulation.Entities.AddComponent(
                entity,
                new SpatialPresence(
                    new Vector3(0.5f),
                    new SpatialEntryMetadata(
                        faction.Value,
                        CategoryMask: 1)));

            if (index % 4 == 0)
            {
                simulation.Entities.AddComponent(
                    entity,
                    new VisualSensorState(
                        faction,
                        rangeMeters: 96.0f,
                        updateIntervalTicks: 1));
            }
            else if (index % 4 == 1)
            {
                simulation.Entities.AddComponent(
                    entity,
                    new RadarSensorState(
                        faction,
                        detectionRangeMeters: 180.0f,
                        identificationRangeMeters: 60.0f,
                        updateIntervalTicks: 4));
            }
        }

        return simulation;
    }
}
