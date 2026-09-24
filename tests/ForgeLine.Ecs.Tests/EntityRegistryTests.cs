using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Ecs.Tests;

public sealed class EntityRegistryTests
{
    [Fact]
    public void CreateAndDestroyUpdatesLivenessAndDiagnostics()
    {
        var registry = new EntityRegistry(initialEntityCapacity: 2);

        EntityId entity = registry.CreateEntity();

        Assert.True(entity.IsValid);
        Assert.True(registry.IsAlive(entity));
        Assert.Equal(1, registry.EntityCount);
        Assert.Equal(1, registry.Diagnostics.LiveEntityCount);

        Assert.True(registry.DestroyEntity(entity));
        Assert.False(registry.IsAlive(entity));
        Assert.Equal(0, registry.EntityCount);
        Assert.False(registry.DestroyEntity(entity));
    }

    [Fact]
    public void ReusedSlotReceivesNewGeneration()
    {
        var registry = new EntityRegistry(initialEntityCapacity: 1);
        EntityId original = registry.CreateEntity();

        Assert.True(registry.DestroyEntity(original));

        EntityId replacement = registry.CreateEntity();

        Assert.Equal(original.Index, replacement.Index);
        Assert.NotEqual(original.Generation, replacement.Generation);
        Assert.False(registry.IsAlive(original));
        Assert.True(registry.IsAlive(replacement));
    }

    [Fact]
    public void StaleHandleCannotMutateReplacementEntity()
    {
        var registry = new EntityRegistry(initialEntityCapacity: 1);
        EntityId original = registry.CreateEntity();
        registry.AddComponent(original, new Position(1, 2));
        registry.DestroyEntity(original);

        EntityId replacement = registry.CreateEntity();
        registry.AddComponent(replacement, new Position(7, 8));

        Assert.Throws<InvalidOperationException>(
            () => registry.SetComponent(original, new Position(99, 99)));

        Assert.True(registry.TryGetComponent(replacement, out Position position));
        Assert.Equal(new Position(7, 8), position);
    }

    [Fact]
    public void ComponentsSupportAddGetUpdateAndRemove()
    {
        var registry = new EntityRegistry();
        EntityId entity = registry.CreateEntity();

        registry.AddComponent(entity, new Position(4, 5));

        Assert.True(registry.HasComponent<Position>(entity));
        Assert.True(registry.TryGetComponent(entity, out Position original));
        Assert.Equal(new Position(4, 5), original);

        registry.SetComponent(entity, new Position(9, 10));
        ref Position component = ref registry.GetComponent<Position>(entity);
        Assert.Equal(new Position(9, 10), component);

        Assert.True(registry.RemoveComponent<Position>(entity));
        Assert.False(registry.HasComponent<Position>(entity));
        Assert.False(registry.RemoveComponent<Position>(entity));
    }

    [Fact]
    public void DuplicateAddFailsFast()
    {
        var registry = new EntityRegistry();
        EntityId entity = registry.CreateEntity();
        registry.AddComponent(entity, new Position(1, 1));

        Assert.Throws<InvalidOperationException>(
            () => registry.AddComponent(entity, new Position(2, 2)));
    }

    [Fact]
    public void MissingComponentHasExplicitBehavior()
    {
        var registry = new EntityRegistry();
        EntityId entity = registry.CreateEntity();

        Assert.False(registry.TryGetComponent(entity, out Position _));
        Assert.Throws<KeyNotFoundException>(() => registry.GetComponent<Position>(entity));
    }

    [Fact]
    public void DestroyingEntityCleansAllAttachedComponents()
    {
        var registry = new EntityRegistry(initialEntityCapacity: 1);
        EntityId entity = registry.CreateEntity();
        registry.AddComponent(entity, new Position(1, 2));
        registry.AddComponent(entity, new Velocity(3, 4));

        Assert.True(registry.DestroyEntity(entity));
        Assert.Equal(0, registry.GetComponentCount<Position>());
        Assert.Equal(0, registry.GetComponentCount<Velocity>());

        EntityId replacement = registry.CreateEntity();
        Assert.False(registry.HasComponent<Position>(replacement));
        Assert.False(registry.HasComponent<Velocity>(replacement));
    }

    [Fact]
    public void ComponentRemovalUpdatesSingleAndMultiComponentQueries()
    {
        var registry = new EntityRegistry();
        EntityId first = registry.CreateEntity();
        EntityId second = registry.CreateEntity();

        registry.AddComponent(first, new Position(1, 0));
        registry.AddComponent(first, new Velocity(1, 0));
        registry.AddComponent(second, new Position(2, 0));
        registry.AddComponent(second, new Velocity(2, 0));

        Assert.Equal(2, Count(registry.Query<Position>()));
        Assert.Equal(2, Count(registry.Query<Position, Velocity>()));

        Assert.True(registry.RemoveComponent<Velocity>(second));

        Assert.Equal(2, Count(registry.Query<Position>()));
        Assert.Equal(1, Count(registry.Query<Position, Velocity>()));
        Assert.True(registry.HasComponent<Velocity>(first));
    }

    [Fact]
    public void StableQueryOrdersByEntityIndex()
    {
        var registry = new EntityRegistry();
        EntityId first = registry.CreateEntity();
        EntityId second = registry.CreateEntity();
        EntityId third = registry.CreateEntity();

        registry.AddComponent(third, new Position(3, 0));
        registry.AddComponent(first, new Position(1, 0));
        registry.DestroyEntity(second);

        EntityId[] dense = Collect(registry.Query<Position>());
        EntityId[] stable = Collect(
            registry.Query<Position>(QueryIterationOrder.StableByEntityIndex));

        Assert.Equal(new[] { third, first }, dense);
        Assert.Equal(new[] { first, third }, stable);
    }

    [Fact]
    public void CapacityGrowthPreservesEntitiesAndComponents()
    {
        var registry = new EntityRegistry(initialEntityCapacity: 1);
        EntityId first = default;
        EntityId last = default;

        for (int index = 0; index < 512; index++)
        {
            EntityId entity = registry.CreateEntity();
            registry.AddComponent(entity, new Position(index, -index));

            if (index == 0)
            {
                first = entity;
            }

            last = entity;
        }

        Assert.True(registry.Capacity >= 512);
        Assert.True(registry.TryGetComponent(first, out Position firstPosition));
        Assert.True(registry.TryGetComponent(last, out Position lastPosition));
        Assert.Equal(new Position(0, 0), firstPosition);
        Assert.Equal(new Position(511, -511), lastPosition);
    }

    [Fact]
    public void IterationNeverReturnsDestroyedEntities()
    {
        var registry = new EntityRegistry();
        EntityId alive = registry.CreateEntity();
        EntityId destroyed = registry.CreateEntity();

        registry.AddComponent(alive, new Position(1, 0));
        registry.AddComponent(destroyed, new Position(2, 0));
        registry.DestroyEntity(destroyed);

        EntityId[] entities = Collect(registry.Query<Position>());

        Assert.Equal(new[] { alive }, entities);
    }

    [Fact]
    public void StructuralMutationDuringIterationFailsFast()
    {
        var registry = new EntityRegistry();
        EntityId entity = registry.CreateEntity();
        registry.AddComponent(entity, new Position(1, 0));

        EntityQuery<Position>.Enumerator enumerator = registry.Query<Position>().GetEnumerator();
        Assert.True(enumerator.MoveNext());

        registry.CreateEntity();

        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void WarmHotPathDoesNotAllocatePerEntity()
    {
        const int entityCount = 256;
        var registry = new EntityRegistry(initialEntityCapacity: entityCount);

        for (int index = 0; index < entityCount; index++)
        {
            EntityId entity = registry.CreateEntity();
            registry.AddComponent(entity, new Position(index, index));
        }

        RunHotPath(registry, passes: 2);

        long before = GC.GetAllocatedBytesForCurrentThread();
        RunHotPath(registry, passes: 32);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocatedBytes);
    }

    private static void RunHotPath(EntityRegistry registry, int passes)
    {
        for (int pass = 0; pass < passes; pass++)
        {
            foreach (EntityId entity in registry.Query<Position>())
            {
                if (!registry.TryGetComponent(entity, out Position position))
                {
                    throw new InvalidOperationException("Query returned an entity without the queried component.");
                }

                registry.SetComponent(entity, new Position(position.X + 1, position.Y));
            }
        }
    }

    private static int Count<T>(EntityQuery<T> query)
        where T : struct
    {
        int count = 0;
        foreach (EntityId _ in query)
        {
            count++;
        }

        return count;
    }

    private static int Count<TFirst, TSecond>(EntityQuery<TFirst, TSecond> query)
        where TFirst : struct
        where TSecond : struct
    {
        int count = 0;
        foreach (EntityId _ in query)
        {
            count++;
        }

        return count;
    }

    private static EntityId[] Collect<T>(EntityQuery<T> query)
        where T : struct
    {
        var entities = new List<EntityId>();
        foreach (EntityId entity in query)
        {
            entities.Add(entity);
        }

        return entities.ToArray();
    }

    private readonly record struct Position(int X, int Y);

    private readonly record struct Velocity(int X, int Y);
}
