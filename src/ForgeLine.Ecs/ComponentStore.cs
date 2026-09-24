using ForgeLine.Core;

namespace ForgeLine.Ecs;

internal sealed class ComponentStore<T> : IComponentStore
    where T : struct
{
    private int[] _sparse;
    private EntityId[] _entities;
    private T[] _components;
    private int _count;

    public ComponentStore(int initialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);

        int capacity = Math.Max(1, initialCapacity);
        _sparse = new int[capacity];
        _entities = new EntityId[capacity];
        _components = new T[capacity];
    }

    public int Count => _count;

    public bool Contains(EntityId entity)
    {
        return TryGetDenseIndex(entity, out _);
    }

    public void Add(EntityId entity, in T component)
    {
        EnsureSparseCapacity(entity.Index);

        if (TryGetDenseIndex(entity, out _))
        {
            throw new InvalidOperationException($"Entity {entity} already has component {typeof(T).Name}.");
        }

        EnsureDenseCapacity(_count + 1);

        int denseIndex = _count;
        _entities[denseIndex] = entity;
        _components[denseIndex] = component;
        _sparse[(int)entity.Index] = denseIndex + 1;
        _count++;
    }

    public bool Remove(EntityId entity)
    {
        if (!TryGetDenseIndex(entity, out int denseIndex))
        {
            return false;
        }

        int lastIndex = _count - 1;
        _sparse[(int)entity.Index] = 0;

        if (denseIndex != lastIndex)
        {
            EntityId movedEntity = _entities[lastIndex];
            _entities[denseIndex] = movedEntity;
            _components[denseIndex] = _components[lastIndex];
            _sparse[(int)movedEntity.Index] = denseIndex + 1;
        }

        _entities[lastIndex] = default;
        _components[lastIndex] = default;
        _count--;

        return true;
    }

    public bool TryGet(EntityId entity, out T component)
    {
        if (!TryGetDenseIndex(entity, out int denseIndex))
        {
            component = default;
            return false;
        }

        component = _components[denseIndex];
        return true;
    }

    public ref T GetRef(EntityId entity)
    {
        if (!TryGetDenseIndex(entity, out int denseIndex))
        {
            throw new KeyNotFoundException($"Entity {entity} does not have component {typeof(T).Name}.");
        }

        return ref _components[denseIndex];
    }

    public void Set(EntityId entity, in T component)
    {
        if (!TryGetDenseIndex(entity, out int denseIndex))
        {
            throw new KeyNotFoundException($"Entity {entity} does not have component {typeof(T).Name}.");
        }

        _components[denseIndex] = component;
    }

    public EntityId GetEntityAt(int denseIndex)
    {
        return _entities[denseIndex];
    }

    private bool TryGetDenseIndex(EntityId entity, out int denseIndex)
    {
        if (entity.Generation == 0 || entity.Index >= (uint)_sparse.Length)
        {
            denseIndex = -1;
            return false;
        }

        int marker = _sparse[(int)entity.Index];
        if (marker == 0)
        {
            denseIndex = -1;
            return false;
        }

        denseIndex = marker - 1;
        return denseIndex < _count && _entities[denseIndex] == entity;
    }

    private void EnsureSparseCapacity(uint entityIndex)
    {
        if (entityIndex < (uint)_sparse.Length)
        {
            return;
        }

        int requiredCapacity = checked((int)entityIndex + 1);
        int newCapacity = _sparse.Length;

        while (newCapacity < requiredCapacity)
        {
            newCapacity = checked(newCapacity * 2);
        }

        Array.Resize(ref _sparse, newCapacity);
    }

    private void EnsureDenseCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= _entities.Length)
        {
            return;
        }

        int newCapacity = _entities.Length;

        while (newCapacity < requiredCapacity)
        {
            newCapacity = checked(newCapacity * 2);
        }

        Array.Resize(ref _entities, newCapacity);
        Array.Resize(ref _components, newCapacity);
    }
}
