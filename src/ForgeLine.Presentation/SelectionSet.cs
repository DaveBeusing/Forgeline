using ForgeLine.Core;

namespace ForgeLine.Presentation;

public sealed class SelectionSet
{
    private readonly List<EntityId> _entities = [];

    public int Count => _entities.Count;

    public IReadOnlyCollection<EntityId> Entities => _entities;

    public bool Contains(EntityId entity) =>
        entity.IsValid && _entities.BinarySearch(entity) >= 0;

    public void Clear() => _entities.Clear();

    internal void Add(EntityId entity)
    {
        if (!entity.IsValid) return;
        int index = _entities.BinarySearch(entity);
        if (index < 0) _entities.Insert(~index, entity);
    }

    public void SetSingle(EntityId entity)
    {
        _entities.Clear();

        if (entity.IsValid)
        {
            _entities.Add(entity);
        }
    }

    public void Toggle(EntityId entity)
    {
        if (!entity.IsValid)
        {
            return;
        }

        int index = _entities.BinarySearch(entity);
        if (index < 0)
        {
            _entities.Insert(~index, entity);
        }
        else _entities.RemoveAt(index);
    }

    public void Replace(ReadOnlySpan<EntityId> entities)
    {
        _entities.Clear();
        AddRange(entities);
    }

    public void ToggleRange(ReadOnlySpan<EntityId> entities)
    {
        for (int index = 0; index < entities.Length; index++)
        {
            Toggle(entities[index]);
        }
    }

    public void Remove(EntityId entity)
    {
        int index = _entities.BinarySearch(entity);
        if (index >= 0) _entities.RemoveAt(index);
    }

    public EntityId[] ToArray()
    {
        var result = new EntityId[_entities.Count];
        _entities.CopyTo(result);
        return result;
    }

    private void AddRange(ReadOnlySpan<EntityId> entities)
    {
        for (int index = 0; index < entities.Length; index++)
        {
            if (entities[index].IsValid)
            {
                Add(entities[index]);
            }
        }
    }
}
