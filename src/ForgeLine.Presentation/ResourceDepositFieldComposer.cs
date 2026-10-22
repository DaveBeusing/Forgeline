using System.Globalization;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Simulation-owner presentation cache; never retained by or mutated through a snapshot.
internal sealed class ResourceDepositFieldComposer(ITerrainQuery terrain, RuntimeAssetCatalog? assets = null)
{
    private readonly Dictionary<EntityId, ResourceDepositFieldIdentity> _authored = [];
    private readonly Dictionary<EntityId, Entry> _cache = [];
    private readonly HashSet<EntityId> _seen = [];
    private readonly List<EntityId> _expired = [];
    private EntityRegistry? _registry;
    internal int CachedDepositCount => _cache.Count;

    internal static ResourceDepositFieldComposer Create(MatchRuntime scenario, RuntimeAssetCatalog? assets)
    {
        var result = new ResourceDepositFieldComposer(scenario.Terrain, assets);
        var ids = scenario.BattlefieldRuntime.ResourceEntities;
        var definitions = scenario.Battlefield.Resources;
        for (int index = 0; index < ids.Count; index++)
            result._authored.Add(ids[index], new(scenario.Battlefield.Metadata.Key, definitions[index].Key));
        return result;
    }

    internal void Apply(Span<RenderInstance> instances, EntityRegistry entities)
    {
        if (!ReferenceEquals(_registry, entities)) { _cache.Clear(); _registry = entities; }
        _seen.Clear();
        int deposits = 0;
        foreach (ref readonly var instance in instances)
        {
            if (!Eligible(instance, entities, out _)) continue;
            deposits++;
            if (deposits <= ResourceDepositFieldComposition.MaximumCachedDeposits) _seen.Add(instance.Entity);
        }
        // Every admitted field receives the same quota. No dictionary ordering,
        // first-family preference, camera distance or render-pass budget influences layout.
        int quota = deposits == 0 || deposits > ResourceDepositFieldComposition.MaximumCachedDeposits ? 0 : Math.Min(ResourceDepositFieldComposition.MaximumClustersPerDeposit,
            ResourceDepositFieldComposition.MaximumSnapshotClusters / deposits);
        // Retire before admitting new parents, including complete viewer/session changes.
        _expired.Clear();
        foreach (var entity in _cache.Keys)
            if (quota == 0 || !_seen.Contains(entity)) _expired.Add(entity);
        foreach (var entity in _expired) _cache.Remove(entity);
        foreach (ref var instance in instances)
        {
            instance = instance with { ResourceField = null };
            if (quota == 0 || !Eligible(instance, entities, out var deposit)) continue;
            if (!_cache.TryGetValue(instance.Entity, out var entry) || entry.Bounds.Minimum != deposit.Bounds.Minimum ||
                entry.Bounds.Maximum != deposit.Bounds.Maximum ||
                entry.Resource != deposit.ResourceId || entry.Quota != quota)
            {
                var identity = _authored.TryGetValue(instance.Entity, out var known) ? known :
                    new ResourceDepositFieldIdentity("entity", string.Create(CultureInfo.InvariantCulture,
                        $"entity.{instance.Entity.Index}.{instance.Entity.Generation}"));
                entry = new(deposit.Bounds, deposit.ResourceId, quota,
                    ResourceDepositFieldComposition.Create(identity, deposit.ResourceId, deposit.Bounds, terrain, quota, assets));
                _cache[instance.Entity] = entry;
            }
            if (entry.Remaining != deposit.RemainingQuantity || entry.Total != deposit.TotalQuantity)
            {
                entry.Field = ResourceDepositFieldComposition.WithRemaining(entry.Layout, deposit.RemainingFraction);
                entry.Remaining = deposit.RemainingQuantity;
                entry.Total = deposit.TotalQuantity;
            }
            instance = instance with { ResourceField = entry.Field };
        }
    }

    private static bool Eligible(in RenderInstance instance, EntityRegistry entities, out ResourceDeposit deposit)
    {
        deposit = default;
        return instance.WorldFeature.Kind == WorldPresentationKind.ResourceDeposit &&
            (instance.Visibility & RenderVisibilityMask.World) != 0 &&
            entities.TryGetComponent(instance.Entity, out deposit) && ResourceDepositFieldComposition.Supports(deposit.ResourceId);
    }

    private sealed class Entry(AxisAlignedBounds bounds, ResourceId resource, int quota, ResourceDepositFieldCluster[] layout)
    {
        internal AxisAlignedBounds Bounds { get; } = bounds;
        internal ResourceId Resource { get; } = resource;
        internal int Quota { get; } = quota;
        internal ResourceDepositFieldCluster[] Layout { get; } = layout;
        internal double Remaining { get; set; } = double.NaN;
        internal double Total { get; set; }
        internal ResourceDepositField? Field { get; set; }
    }
}
