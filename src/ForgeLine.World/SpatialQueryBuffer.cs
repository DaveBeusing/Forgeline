using System.Runtime.InteropServices;
using ForgeLine.Core;

namespace ForgeLine.World;

public sealed class SpatialQueryBuffer
{
    private readonly List<EntityId> _entities;
    private readonly HashSet<EntityId> _seen;

    public SpatialQueryBuffer(int initialCapacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);

        _entities = new List<EntityId>(initialCapacity);
        _seen = new HashSet<EntityId>();
    }

    public int Count => _entities.Count;

    public ReadOnlySpan<EntityId> Results =>
        CollectionsMarshal.AsSpan(_entities);

    public void Clear()
    {
        _entities.Clear();
        _seen.Clear();
    }

    internal bool Add(EntityId entity)
    {
        if (!_seen.Add(entity))
        {
            return false;
        }

        _entities.Add(entity);
        return true;
    }

    internal void SortStable()
    {
        _entities.Sort();
    }
}
