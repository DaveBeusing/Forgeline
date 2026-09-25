using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class BuildingCommandProcessingSystem : ISimulationSystem
{
    private readonly BuildingDefinitionCatalog _definitions;
    private readonly BuildingPlacementService _placement;
    private readonly InventoryStore _inventories;
    private readonly SpatialGridIndex _spatialIndex;
    private long _acceptedCommands;
    private long _rejectedCommands;
    private BuildCommandRejectionReason _lastRejection;
    private BuildingPlacementFailureReason _lastPlacementFailure;
    private EntityId _lastCreatedSite;

    public BuildingCommandProcessingSystem(
        BuildingDefinitionCatalog definitions,
        BuildingPlacementService placement,
        InventoryStore inventories,
        SpatialGridIndex spatialIndex)
    {
        _definitions = definitions ??
            throw new ArgumentNullException(nameof(definitions));
        _placement = placement ??
            throw new ArgumentNullException(nameof(placement));
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
        _spatialIndex = spatialIndex ??
            throw new ArgumentNullException(nameof(spatialIndex));
    }

    public SimulationPhase Phase => SimulationPhase.OrderProcessing;

    public BuildCommandMetrics Metrics =>
        new(
            _acceptedCommands,
            _rejectedCommands,
            _lastRejection,
            _lastPlacementFailure,
            _lastCreatedSite);

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var pending = new List<EntityId>();
        foreach (EntityId entity in
                 context.Entities.Query<BuildingBuildRequest>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            pending.Add(entity);
        }

        for (int index = 0; index < pending.Count; index++)
        {
            EntityId requestEntity = pending[index];
            if (!context.Entities.IsAlive(requestEntity) ||
                !context.Entities.TryGetComponent(
                    requestEntity,
                    out BuildingBuildRequest request))
            {
                continue;
            }

            ProcessRequest(context, requestEntity, request);
        }
    }

    private void ProcessRequest(
        SimulationContext context,
        EntityId requestEntity,
        in BuildingBuildRequest request)
    {
        if (!_definitions.TryGet(
                request.BuildingId,
                out BuildingDefinition? definition))
        {
            Reject(
                context,
                requestEntity,
                BuildCommandRejectionReason.UnknownBuilding,
                BuildingPlacementFailureReason.UnknownBuilding);
            return;
        }

        BuildingPlacementResult placement = _placement.Evaluate(
            context.Entities,
            request.Issuer,
            request.BuildingId,
            request.Position,
            request.Orientation);

        if (!placement.IsValid)
        {
            Reject(
                context,
                requestEntity,
                BuildCommandRejectionReason.PlacementInvalid,
                placement.Failure);
            return;
        }

        if (!TryResolveSourceInventory(
                context.Entities,
                request,
                out InventoryId inventoryId))
        {
            Reject(
                context,
                requestEntity,
                BuildCommandRejectionReason.InvalidSourceInventory,
                BuildingPlacementFailureReason.None);
            return;
        }

        if (context.Entities.TryGetComponent(
                request.SourceInventory,
                out StorageDepot sourceDepot) &&
            sourceDepot.Owner.IsSpecified &&
            sourceDepot.Owner.Value != request.Issuer.Value)
        {
            Reject(
                context,
                requestEntity,
                BuildCommandRejectionReason.SourceInventoryOwnershipMismatch,
                BuildingPlacementFailureReason.None);
            return;
        }

        if (!TryReserveCosts(inventoryId, definition))
        {
            Reject(
                context,
                requestEntity,
                BuildCommandRejectionReason.InsufficientResources,
                BuildingPlacementFailureReason.None);
            return;
        }

        ResourceId extractedResourceId = ResourceId.None;
        if (placement.ResourceDeposit.IsValid &&
            context.Entities.TryGetComponent(
                placement.ResourceDeposit,
                out ResourceDeposit deposit))
        {
            extractedResourceId = deposit.ResourceId;
        }

        EntityId site = context.Entities.CreateEntity();
        Vector3 visualScale = new(
            definition.Footprint.Width,
            definition.Footprint.Height,
            definition.Footprint.Depth);
        var transform = new WorldTransform(
            placement.Bounds.Center,
            definition.Footprint.GetRotation(request.Orientation),
            visualScale);
        Vector3 halfExtents =
            (placement.Bounds.Maximum - placement.Bounds.Minimum) * 0.5f;
        var presence = new SpatialPresence(
            halfExtents,
            new SpatialEntryMetadata(
                request.Issuer.Value,
                (ulong)ControllableEntityCategory.Building,
                SpatialMobility.Static));

        context.Entities.AddComponent(site, transform);
        context.Entities.AddComponent(site, new VisualIdentity(definition.VisualId));
        context.Entities.AddComponent(
            site,
            new ControllableEntity(
                request.Issuer,
                ControllableEntityCategory.Building));
        context.Entities.AddComponent(site, presence);
        context.Entities.AddComponent(
            site,
            new ConstructionSite(
                request.BuildingId,
                request.Issuer,
                inventoryId,
                context.Tick,
                definition.ConstructionTicks,
                progressTicks: 0,
                extractedResourceId,
                placement.ResourceDeposit));

        _spatialIndex.Upsert(presence.CreateEntry(site, transform));

        _acceptedCommands++;
        _lastRejection = BuildCommandRejectionReason.None;
        _lastPlacementFailure = BuildingPlacementFailureReason.None;
        _lastCreatedSite = site;

        context.Entities.DestroyEntity(requestEntity);
    }

    private bool TryResolveSourceInventory(
        EntityRegistry entities,
        in BuildingBuildRequest request,
        out InventoryId inventoryId)
    {
        if (!entities.IsAlive(request.SourceInventory) ||
            !entities.TryGetComponent(
                request.SourceInventory,
                out InventoryStorage storage) ||
            !_inventories.Contains(storage.InventoryId))
        {
            inventoryId = InventoryId.None;
            return false;
        }

        inventoryId = storage.InventoryId;
        return true;
    }

    private bool TryReserveCosts(
        InventoryId inventoryId,
        BuildingDefinition definition)
    {
        int reservedCount = 0;

        for (int index = 0; index < definition.Costs.Count; index++)
        {
            BuildingResourceCost cost = definition.Costs[index];
            InventoryOperationResult result =
                _inventories.Reserve(
                    inventoryId,
                    cost.ResourceId,
                    cost.Quantity);

            if (result.Succeeded)
            {
                reservedCount++;
                continue;
            }

            for (int rollbackIndex = reservedCount - 1;
                 rollbackIndex >= 0;
                 rollbackIndex--)
            {
                BuildingResourceCost reserved =
                    definition.Costs[rollbackIndex];
                InventoryOperationResult rollback =
                    _inventories.ReleaseReservation(
                        inventoryId,
                        reserved.ResourceId,
                        reserved.Quantity);

                EngineInvariant.Require(
                    rollback.Succeeded,
                    DiagnosticCategory.Simulation,
                    "CONSTRUCTION_RESERVATION_ROLLBACK_FAILED",
                    $"Failed to roll back construction reservation for inventory {inventoryId}.");
            }

            return false;
        }

        return true;
    }

    private void Reject(
        SimulationContext context,
        EntityId requestEntity,
        BuildCommandRejectionReason rejection,
        BuildingPlacementFailureReason placementFailure)
    {
        _rejectedCommands++;
        _lastRejection = rejection;
        _lastPlacementFailure = placementFailure;
        _lastCreatedSite = EntityId.Invalid;

        context.Entities.DestroyEntity(requestEntity);
    }
}

public sealed class BuildingConstructionSystem : ISimulationSystem
{
    private static readonly PowerNetworkId DefaultPowerNetwork = new(1);

    private readonly BuildingDefinitionCatalog _definitions;
    private readonly InventoryStore _inventories;
    private readonly SpatialGridIndex _spatialIndex;
    private long _cancelledSites;
    private long _completedBuildings;

    public BuildingConstructionSystem(
        BuildingDefinitionCatalog definitions,
        InventoryStore inventories,
        SpatialGridIndex spatialIndex)
    {
        _definitions = definitions ??
            throw new ArgumentNullException(nameof(definitions));
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
        _spatialIndex = spatialIndex ??
            throw new ArgumentNullException(nameof(spatialIndex));
    }

    public SimulationPhase Phase => SimulationPhase.Economy;

    public BuildingConstructionMetrics Metrics { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sites = new List<EntityId>();
        foreach (EntityId entity in
                 context.Entities.Query<ConstructionSite>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            sites.Add(entity);
        }

        for (int index = 0; index < sites.Count; index++)
        {
            EntityId entity = sites[index];
            if (!context.Entities.IsAlive(entity) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out ConstructionSite site))
            {
                continue;
            }

            if (context.Entities.HasComponent<ConstructionCancellationRequest>(entity))
            {
                Cancel(context, entity, site);
                continue;
            }

            ConstructionSite advanced = site.Advance();
            if (!advanced.IsComplete)
            {
                context.Entities.SetComponent(entity, advanced);
                continue;
            }

            Complete(context, entity, advanced);
        }

        int activeSites = 0;
        foreach (EntityId unused in
                 context.Entities.Query<ConstructionSite>())
        {
            _ = unused;
            activeSites++;
        }

        Metrics = new BuildingConstructionMetrics(
            activeSites,
            _cancelledSites,
            _completedBuildings);
    }

    private void Cancel(
        SimulationContext context,
        EntityId entity,
        in ConstructionSite site)
    {
        BuildingDefinition definition = _definitions[site.BuildingId];
        ReleaseReservations(site.SourceInventory, definition);

        _spatialIndex.Remove(entity);
        context.Entities.DestroyEntity(entity);
        _cancelledSites++;
    }

    private void Complete(
        SimulationContext context,
        EntityId entity,
        in ConstructionSite site)
    {
        BuildingDefinition definition = _definitions[site.BuildingId];

        ValidateReservations(site.SourceInventory, definition);
        ConsumeReservations(site.SourceInventory, definition);
        ActivateCapabilities(context.Entities, entity, site, definition);

        context.Entities.AddComponent(
            entity,
            new CompletedBuilding(
                site.BuildingId,
                site.Owner,
                context.Tick));
        context.Entities.RemoveComponent<ConstructionSite>(entity);

        _completedBuildings++;
    }

    private void ReleaseReservations(
        InventoryId inventoryId,
        BuildingDefinition definition)
    {
        for (int index = 0; index < definition.Costs.Count; index++)
        {
            BuildingResourceCost cost = definition.Costs[index];
            InventoryOperationResult result =
                _inventories.ReleaseReservation(
                    inventoryId,
                    cost.ResourceId,
                    cost.Quantity);

            EngineInvariant.Require(
                result.Succeeded,
                DiagnosticCategory.Simulation,
                "CONSTRUCTION_REFUND_FAILED",
                $"Failed to release construction resources from inventory {inventoryId}.");
        }
    }

    private void ValidateReservations(
        InventoryId inventoryId,
        BuildingDefinition definition)
    {
        EngineInvariant.Require(
            _inventories.Contains(inventoryId),
            DiagnosticCategory.Simulation,
            "CONSTRUCTION_SOURCE_INVENTORY_MISSING",
            $"Construction inventory {inventoryId} disappeared before completion.");

        for (int index = 0; index < definition.Costs.Count; index++)
        {
            BuildingResourceCost cost = definition.Costs[index];
            EngineInvariant.Require(
                _inventories.GetReservedQuantity(
                    inventoryId,
                    cost.ResourceId) >= cost.Quantity,
                DiagnosticCategory.Simulation,
                "CONSTRUCTION_RESERVATION_MISSING",
                $"Construction reservation for resource {cost.ResourceId} is incomplete.");
        }
    }

    private void ConsumeReservations(
        InventoryId inventoryId,
        BuildingDefinition definition)
    {
        for (int index = 0; index < definition.Costs.Count; index++)
        {
            BuildingResourceCost cost = definition.Costs[index];
            InventoryOperationResult result =
                _inventories.ConsumeReserved(
                    inventoryId,
                    cost.ResourceId,
                    cost.Quantity);

            EngineInvariant.Require(
                result.Succeeded,
                DiagnosticCategory.Simulation,
                "CONSTRUCTION_RESOURCE_COMMIT_FAILED",
                $"Failed to consume reserved construction resources from inventory {inventoryId}.");
        }
    }

    private void ActivateCapabilities(
        EntityRegistry entities,
        EntityId entity,
        in ConstructionSite site,
        BuildingDefinition definition)
    {
        FactionId owner = ToFactionId(site.Owner);

        bool usesPower =
            definition.Capabilities.HasFlag(BuildingCapability.PowerGeneration) ||
            definition.Capabilities.HasFlag(BuildingCapability.PowerConsumption);

        if (usesPower)
        {
            entities.AddComponent(
                entity,
                new PowerNetworkMembership(DefaultPowerNetwork));
        }

        if (definition.Capabilities.HasFlag(BuildingCapability.PowerGeneration))
        {
            entities.AddComponent(
                entity,
                new PowerGenerator(definition.PowerGeneration));
        }

        if (definition.Capabilities.HasFlag(BuildingCapability.PowerConsumption))
        {
            entities.AddComponent(
                entity,
                new PowerConsumer(
                    definition.PowerDemand,
                    definition.PowerPriority));
        }

        if (definition.Capabilities.HasFlag(BuildingCapability.Storage))
        {
            IEnumerable<ResourceId>? acceptedResources =
                definition.Capabilities.HasFlag(BuildingCapability.Extraction) &&
                site.ExtractedResourceId.IsSpecified
                    ? new[] { site.ExtractedResourceId }
                    : null;

            InventoryId inventoryId =
                _inventories.CreateInventory(
                    new InventorySpecification(
                        definition.StorageCapacity,
                        acceptedResources));

            entities.AddComponent(
                entity,
                new InventoryStorage(inventoryId));

            if (site.BuildingId == BuildingIds.StorageDepot)
            {
                entities.AddComponent(
                    entity,
                    new StorageDepot(inventoryId, owner));
            }
        }

        if (definition.Capabilities.HasFlag(BuildingCapability.Extraction))
        {
            EngineInvariant.Require(
                site.ResourceDeposit.IsValid &&
                site.ExtractedResourceId.IsSpecified,
                DiagnosticCategory.Simulation,
                "CONSTRUCTION_EXTRACTOR_BINDING_MISSING",
                "Extractor construction completed without a resource deposit binding.");

            entities.AddComponent(
                entity,
                new ResourceExtractor(
                    site.ResourceDeposit,
                    site.ExtractedResourceId,
                    definition.ExtractionRatePerSecond,
                    owner,
                    outputInventory: entity));
        }

        if (definition.Capabilities.HasFlag(BuildingCapability.Command))
        {
            entities.AddComponent(entity, new CommandFacility());
        }

        if (definition.Capabilities.HasFlag(BuildingCapability.Processing))
        {
            entities.AddComponent(entity, new ProcessingFacility());
        }
    }

    private static FactionId ToFactionId(PlayerId player)
    {
        EngineInvariant.Require(
            player.Value <= uint.MaxValue,
            DiagnosticCategory.Simulation,
            "BUILDING_OWNER_FACTION_RANGE",
            $"Player {player} cannot be represented as a faction ID.");

        return new FactionId((uint)player.Value);
    }
}
