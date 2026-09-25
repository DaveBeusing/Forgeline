using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct BuildingLogisticsRegistrationMetrics(
    int ManagedNodeCount,
    long AddedNodeCount,
    long UpdatedNodeCount,
    long RemovedNodeCount);

public sealed class BuildingLogisticsRegistrationSystem
    : ISimulationSystem
{
    private readonly LogisticsNetwork _network;
    private readonly HashSet<EntityId> _managedEntities = new();
    private readonly HashSet<EntityId> _seenEntities = new();
    private readonly List<EntityId> _staleEntities = new();
    private long _addedNodeCount;
    private long _updatedNodeCount;
    private long _removedNodeCount;

    public BuildingLogisticsRegistrationSystem(
        LogisticsNetwork network)
    {
        _network = network ??
            throw new ArgumentNullException(nameof(network));
    }

    public SimulationPhase Phase =>
        SimulationPhase.EntityLifecycle;

    public BuildingLogisticsRegistrationMetrics Metrics =>
        new(
            _managedEntities.Count,
            _addedNodeCount,
            _updatedNodeCount,
            _removedNodeCount);

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _seenEntities.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<CompletedBuilding>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (!TryDescribeNode(
                    context.Entities,
                    entity,
                    out LogisticsNodeKind kind,
                    out LogisticsNodeCapabilities capabilities,
                    out bool enabled,
                    out System.Numerics.Vector3 position))
            {
                continue;
            }

            _seenEntities.Add(entity);

            if (_network.TryGetNodeForEntity(
                    entity,
                    out LogisticsNodeId nodeId))
            {
                if (_network.UpdateNode(
                        nodeId,
                        position,
                        kind,
                        capabilities,
                        enabled))
                {
                    _updatedNodeCount++;
                }
            }
            else
            {
                _network.AddNode(
                    entity,
                    position,
                    kind,
                    capabilities,
                    enabled);
                _addedNodeCount++;
            }

            _managedEntities.Add(entity);
        }

        _staleEntities.Clear();
        foreach (EntityId entity in _managedEntities)
        {
            if (!_seenEntities.Contains(entity))
            {
                _staleEntities.Add(entity);
            }
        }

        for (int index = 0;
             index < _staleEntities.Count;
             index++)
        {
            EntityId entity = _staleEntities[index];

            if (_network.TryGetNodeForEntity(
                    entity,
                    out LogisticsNodeId nodeId) &&
                _network.RemoveNode(nodeId))
            {
                _removedNodeCount++;
            }

            _managedEntities.Remove(entity);
        }
    }

    private static bool TryDescribeNode(
        EntityRegistry entities,
        EntityId entity,
        out LogisticsNodeKind kind,
        out LogisticsNodeCapabilities capabilities,
        out bool enabled,
        out System.Numerics.Vector3 position)
    {
        if (!entities.TryGetComponent(
                entity,
                out WorldTransform transform))
        {
            kind = default;
            capabilities = LogisticsNodeCapabilities.None;
            enabled = false;
            position = default;
            return false;
        }

        bool hasExtractor =
            entities.TryGetComponent(
                entity,
                out ResourceExtractor extractor);
        bool hasStorageDepot =
            entities.TryGetComponent(
                entity,
                out StorageDepot storageDepot);
        bool hasLogisticsHub =
            entities.TryGetComponent(
                entity,
                out LogisticsHub logisticsHub);
        bool hasSupplyDepot =
            entities.TryGetComponent(
                entity,
                out SupplyDepot supplyDepot);
        bool hasProduction =
            entities.HasComponent<ProductionFacility>(entity);

        if (!hasExtractor &&
            !hasStorageDepot &&
            !hasLogisticsHub &&
            !hasSupplyDepot &&
            !hasProduction)
        {
            kind = default;
            capabilities = LogisticsNodeCapabilities.None;
            enabled = false;
            position = default;
            return false;
        }

        capabilities = LogisticsNodeCapabilities.None;
        enabled = true;

        if (hasExtractor)
        {
            capabilities |=
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.Storage;
            enabled &= extractor.Enabled;
        }

        if (hasStorageDepot)
        {
            capabilities |=
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution;
            enabled &=
                storageDepot.State ==
                StorageDepotState.Operational;
        }

        if (hasLogisticsHub)
        {
            capabilities |=
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution;
            enabled &=
                logisticsHub.State ==
                LogisticsHubState.Operational;
        }

        if (hasSupplyDepot)
        {
            capabilities |=
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Storage |
                LogisticsNodeCapabilities.Distribution |
                LogisticsNodeCapabilities.Supply;
            enabled &=
                supplyDepot.State ==
                SupplyDepotState.Operational;
        }

        if (hasProduction)
        {
            capabilities |=
                LogisticsNodeCapabilities.CargoSource |
                LogisticsNodeCapabilities.CargoDestination |
                LogisticsNodeCapabilities.Processing;
        }

        kind = hasSupplyDepot
            ? LogisticsNodeKind.SupplyDepot
            : hasLogisticsHub
                ? LogisticsNodeKind.LogisticsHub
                : hasExtractor
                    ? LogisticsNodeKind.ExtractorOutput
                    : hasStorageDepot
                        ? LogisticsNodeKind.StorageDepot
                        : LogisticsNodeKind.ProcessingFacility;

        position = transform.Position;
        return true;
    }
}
