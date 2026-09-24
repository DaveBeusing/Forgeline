using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class SpatialIndexSynchronizer
{
    private readonly HashSet<EntityId> _trackedEntities = new();
    private readonly List<EntityId> _staleEntities = new();

    public SpatialIndexSynchronizer(SpatialGridIndex index)
    {
        Index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public SpatialGridIndex Index { get; }

    public int TrackedEntityCount => _trackedEntities.Count;

    public void SynchronizeMovement(EntityRegistry entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        foreach (EntityId entity in entities.Query<WorldTransform, SpatialPresence>())
        {
            WorldTransform transform =
                entities.GetComponent<WorldTransform>(entity);
            SpatialPresence presence =
                entities.GetComponent<SpatialPresence>(entity);

            SpatialEntry entry = presence.CreateEntry(entity, transform);
            Index.Upsert(entry);
            _trackedEntities.Add(entity);
        }

        RemoveStaleEntries(entities);
    }

    public void CleanupLifecycle(EntityRegistry entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        RemoveStaleEntries(entities);
    }

    private void RemoveStaleEntries(EntityRegistry entities)
    {
        _staleEntities.Clear();

        foreach (EntityId entity in _trackedEntities)
        {
            if (!entities.IsAlive(entity) ||
                !entities.HasComponent<WorldTransform>(entity) ||
                !entities.HasComponent<SpatialPresence>(entity))
            {
                _staleEntities.Add(entity);
            }
        }

        for (int index = 0; index < _staleEntities.Count; index++)
        {
            EntityId entity = _staleEntities[index];
            Index.Remove(entity);
            _trackedEntities.Remove(entity);
        }
    }
}
