using BenchmarkDotNet.Attributes;
using ForgeLine.Core;

namespace ForgeLine.Ecs.Benchmarks;

[MemoryDiagnoser]
public class EntityComponentBenchmarks
{
    private EntityRegistry _readRegistry = null!;
    private EntityId[] _readEntities = null!;
    private EntityRegistry _destroyRegistry = null!;
    private EntityId[] _destroyEntities = null!;
    private EntityRegistry _addRegistry = null!;
    private EntityId[] _addEntities = null!;
    private EntityRegistry _removeRegistry = null!;
    private EntityId[] _removeEntities = null!;

    [Params(1_000, 10_000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void SetupReadWorkloads()
    {
        _readRegistry = new EntityRegistry(EntityCount);
        _readEntities = new EntityId[EntityCount];

        for (int index = 0; index < EntityCount; index++)
        {
            EntityId entity = _readRegistry.CreateEntity();
            _readEntities[index] = entity;
            _readRegistry.AddComponent(entity, new Position(index, index));
            _readRegistry.AddComponent(entity, new Velocity(1, -1));
        }
    }

    [IterationSetup(Target = nameof(EntityDestruction))]
    public void SetupDestruction()
    {
        _destroyRegistry = new EntityRegistry(EntityCount);
        _destroyEntities = new EntityId[EntityCount];

        for (int index = 0; index < EntityCount; index++)
        {
            _destroyEntities[index] = _destroyRegistry.CreateEntity();
        }
    }

    [IterationSetup(Target = nameof(ComponentAdd))]
    public void SetupComponentAdd()
    {
        _addRegistry = new EntityRegistry(EntityCount);
        _addEntities = new EntityId[EntityCount];

        for (int index = 0; index < EntityCount; index++)
        {
            _addEntities[index] = _addRegistry.CreateEntity();
        }
    }

    [IterationSetup(Target = nameof(ComponentRemove))]
    public void SetupComponentRemove()
    {
        _removeRegistry = new EntityRegistry(EntityCount);
        _removeEntities = new EntityId[EntityCount];

        for (int index = 0; index < EntityCount; index++)
        {
            EntityId entity = _removeRegistry.CreateEntity();
            _removeEntities[index] = entity;
            _removeRegistry.AddComponent(entity, new Position(index, index));
        }
    }

    [Benchmark]
    public int EntityCreation()
    {
        var registry = new EntityRegistry(EntityCount);

        for (int index = 0; index < EntityCount; index++)
        {
            registry.CreateEntity();
        }

        return registry.EntityCount;
    }

    [Benchmark]
    public int EntityDestruction()
    {
        int destroyed = 0;

        for (int index = 0; index < _destroyEntities.Length; index++)
        {
            if (_destroyRegistry.DestroyEntity(_destroyEntities[index]))
            {
                destroyed++;
            }
        }

        return destroyed;
    }

    [Benchmark]
    public int ComponentAdd()
    {
        for (int index = 0; index < _addEntities.Length; index++)
        {
            _addRegistry.AddComponent(_addEntities[index], new Position(index, index));
        }

        return _addRegistry.GetComponentCount<Position>();
    }

    [Benchmark]
    public int ComponentRemove()
    {
        int removed = 0;

        for (int index = 0; index < _removeEntities.Length; index++)
        {
            if (_removeRegistry.RemoveComponent<Position>(_removeEntities[index]))
            {
                removed++;
            }
        }

        return removed;
    }

    [Benchmark]
    public int ComponentLookup()
    {
        int checksum = 0;

        for (int index = 0; index < _readEntities.Length; index++)
        {
            if (_readRegistry.TryGetComponent(_readEntities[index], out Position position))
            {
                checksum += position.X;
            }
        }

        return checksum;
    }

    [Benchmark]
    public int DenseIteration()
    {
        int checksum = 0;

        foreach (EntityId entity in _readRegistry.Query<Position>())
        {
            checksum += _readRegistry.GetComponent<Position>(entity).X;
        }

        return checksum;
    }

    [Benchmark]
    public int MultiComponentQuery()
    {
        int count = 0;

        foreach (EntityId _ in _readRegistry.Query<Position, Velocity>())
        {
            count++;
        }

        return count;
    }

    private readonly record struct Position(int X, int Y);

    private readonly record struct Velocity(int X, int Y);
}
