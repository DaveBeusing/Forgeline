using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public sealed class TerrainLineOfFireBenchmarks
{
    private TerrainLineOfFirePolicy _policy = null!;
    private EntityId _source;
    private EntityId _target;

    [Params(100, 1_000)]
    public int CheckCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _policy =
            new TerrainLineOfFirePolicy(
                new FlatTerrain());
        _source =
            new EntityId(1, 1);
        _target =
            new EntityId(2, 1);
    }

    [Benchmark]
    public int EvaluateTerrainLineOfFire()
    {
        int visible = 0;

        for (int index = 0;
             index < CheckCount;
             index++)
        {
            float z =
                index % 32;

            if (_policy.HasLineOfFire(
                    _source,
                    _target,
                    new Vector3(0.0f, 4.0f, z),
                    new Vector3(240.0f, 4.0f, z)))
            {
                visible++;
            }
        }

        return visible;
    }

    private sealed class FlatTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds =>
            new(
                new Vector3(
                    -1_000.0f,
                    -100.0f,
                    -1_000.0f),
                new Vector3(
                    1_000.0f,
                    100.0f,
                    1_000.0f));

        public bool TrySampleHeight(
            float worldX,
            float worldZ,
            out float height)
        {
            _ = worldX;
            _ = worldZ;
            height = 0.0f;
            return true;
        }

        public bool TrySampleNormal(
            float worldX,
            float worldZ,
            out Vector3 normal)
        {
            _ = worldX;
            _ = worldZ;
            normal = Vector3.UnitY;
            return true;
        }
    }
}

[MemoryDiagnoser]
public sealed class SuppressionBenchmarks
{
    private SimulationCoordinator _simulation = null!;

    [Params(100, 1_000)]
    public int InfantryCount { get; set; }

    [IterationSetup]
    public void SetupIteration()
    {
        _simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20,
                initialEntityCapacity:
                    Math.Max(
                        256,
                        InfantryCount + 32));
        _simulation.RegisterSystem(
            new SuppressionSystem());

        for (int index = 0;
             index < InfantryCount;
             index++)
        {
            EntityId entity =
                _simulation.Entities.CreateEntity();
            _simulation.Entities.AddComponent(
                entity,
                SuppressionProfile.InfantryDefault);
            _simulation.Entities.AddComponent(
                entity,
                new SuppressionState(
                    0.5,
                    SuppressionLevel.Suppressed,
                    SimulationTick.Zero,
                    SimulationTick.Zero));
        }
    }

    [Benchmark]
    public ulong UpdateSuppressionTick()
    {
        _simulation.AdvanceOneTick();
        return _simulation.CurrentTick.Value;
    }
}
