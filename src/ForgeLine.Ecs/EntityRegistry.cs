using ForgeLine.Core;

namespace ForgeLine.Ecs;

public sealed class EntityRegistry
{
    private readonly EntityAllocator _entities;
    private readonly Dictionary<Type, IComponentStore> _componentStores = new();
    private ulong _structuralVersion;

    public EntityRegistry(int initialEntityCapacity = 256)
    {
        _entities = new EntityAllocator(initialEntityCapacity);
    }

    public int EntityCount => _entities.Count;

    public int Capacity => _entities.Capacity;

    public int ComponentTypeCount => _componentStores.Count;

    public EntityRegistryDiagnostics Diagnostics =>
        new(EntityCount, Capacity, ComponentTypeCount);

    internal int SlotCount => _entities.SlotCount;

    internal ulong StructuralVersion => _structuralVersion;

    public EntityId CreateEntity()
    {
        EntityId entity = _entities.Create();
        _structuralVersion++;
        return entity;
    }

    public bool DestroyEntity(EntityId entity)
    {
        if (!_entities.IsAlive(entity))
        {
            return false;
        }

        foreach (IComponentStore store in _componentStores.Values)
        {
            store.Remove(entity);
        }

        bool destroyed = _entities.Destroy(entity);
        _structuralVersion++;
        return destroyed;
    }

    public bool IsAlive(EntityId entity)
    {
        return _entities.IsAlive(entity);
    }

    public void AddComponent<T>(EntityId entity, in T component)
        where T : struct
    {
        EnsureAlive(entity);

        ComponentStore<T> store = GetOrCreateStore<T>();
        store.Add(entity, component);
        _structuralVersion++;
    }

    public bool RemoveComponent<T>(EntityId entity)
        where T : struct
    {
        EnsureAlive(entity);

        if (!TryGetStore<T>(out ComponentStore<T>? store) || !store.Remove(entity))
        {
            return false;
        }

        _structuralVersion++;
        return true;
    }

    public bool HasComponent<T>(EntityId entity)
        where T : struct
    {
        return _entities.IsAlive(entity)
            && TryGetStore<T>(out ComponentStore<T>? store)
            && store.Contains(entity);
    }

    public bool TryGetComponent<T>(EntityId entity, out T component)
        where T : struct
    {
        if (!_entities.IsAlive(entity)
            || !TryGetStore<T>(out ComponentStore<T>? store)
            || !store.TryGet(entity, out component))
        {
            component = default;
            return false;
        }

        return true;
    }

    public ref T GetComponent<T>(EntityId entity)
        where T : struct
    {
        EnsureAlive(entity);

        if (!TryGetStore<T>(out ComponentStore<T>? store))
        {
            throw new KeyNotFoundException($"No component store is registered for {typeof(T).Name}.");
        }

        return ref store.GetRef(entity);
    }

    public void SetComponent<T>(EntityId entity, in T component)
        where T : struct
    {
        EnsureAlive(entity);

        if (!TryGetStore<T>(out ComponentStore<T>? store))
        {
            throw new KeyNotFoundException($"No component store is registered for {typeof(T).Name}.");
        }

        store.Set(entity, component);
    }

    public int GetComponentCount<T>()
        where T : struct
    {
        return TryGetStore<T>(out ComponentStore<T>? store) ? store.Count : 0;
    }

    public EntityQuery<T> Query<T>(QueryIterationOrder order = QueryIterationOrder.Dense)
        where T : struct
    {
        TryGetStore<T>(out ComponentStore<T>? store);
        return new EntityQuery<T>(this, store, order);
    }

    public EntityQuery<TFirst, TSecond> Query<TFirst, TSecond>(
        QueryIterationOrder order = QueryIterationOrder.Dense)
        where TFirst : struct
        where TSecond : struct
    {
        TryGetStore<TFirst>(out ComponentStore<TFirst>? first);
        TryGetStore<TSecond>(out ComponentStore<TSecond>? second);
        return new EntityQuery<TFirst, TSecond>(this, first, second, order);
    }

    internal bool TryGetEntityAtIndex(int index, out EntityId entity)
    {
        return _entities.TryGetAliveEntity(index, out entity);
    }

    private ComponentStore<T> GetOrCreateStore<T>()
        where T : struct
    {
        if (TryGetStore<T>(out ComponentStore<T>? existing))
        {
            return existing;
        }

        var store = new ComponentStore<T>(Capacity);
        _componentStores.Add(typeof(T), store);
        return store;
    }

    private bool TryGetStore<T>(out ComponentStore<T>? store)
        where T : struct
    {
        if (_componentStores.TryGetValue(typeof(T), out IComponentStore? untyped))
        {
            store = (ComponentStore<T>)untyped;
            return true;
        }

        store = null;
        return false;
    }

    private void EnsureAlive(EntityId entity)
    {
        if (!_entities.IsAlive(entity))
        {
            throw new InvalidOperationException($"Entity {entity} is not alive.");
        }
    }
}
