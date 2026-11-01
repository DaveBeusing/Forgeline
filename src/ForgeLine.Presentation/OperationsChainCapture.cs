using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Completed-tick observation only. No renderer calls or authority mutations.</summary>
internal static class OperationsChainCapture
{
    internal static OperationsDiagnosisSnapshot? Capture(SimulationContext context,
        PresentationExtractionContext extraction, PlayerActionSnapshot? actions,
        PlayerExperienceSnapshot? experience, RenderInstance[] instances, IReadOnlyList<EntityId> selection)
    {
        var scenario = extraction.Scenario;
        var entities = context.Entities;
        var stock = scenario.Inventories;
        var baseline = OperationsDiagnosisSnapshot.Capture(scenario.Simulation.SessionId, context.Tick, actions, experience);
        if (actions is null || experience is not { IsMatchComplete: false } current ||
            current.Player != extraction.Player || current.Tick != context.Tick ||
            actions.Tick != context.Tick || actions.SessionId != scenario.Simulation.SessionId) return null;
        if (selection.Count != 1) return baseline;
        EntityId root = selection[0];
        if (entities.HasComponent<VisualIdentity>(root))
        {
            bool rootVisible = false;
            foreach (ref readonly var instance in instances.AsSpan())
                if (instance.Entity == root) { rootVisible = OperationsWorldOverlay.IsOwnedVisible(instance, extraction.Player); break; }
            if (!rootVisible) return null;
        }
        if (entities.TryGetComponent(root, out ProductionFacility selectedProduction) &&
            selectedProduction.BlockReason != ProductionBlockReason.NoInput) return baseline;
        if (entities.TryGetComponent(root, out UnitProductionFacility selectedFactory) &&
            selectedFactory.BlockReason != UnitProductionBlockReason.NoInput) return baseline;
        if (!entities.HasComponent<ProductionFacility>(root) && !entities.HasComponent<UnitProductionFacility>(root) &&
            !entities.HasComponent<ConveyorFlowState>(root) && !entities.HasComponent<InventoryStorage>(root) &&
            !entities.HasComponent<SupplyDepot>(root) && !entities.HasComponent<LogisticsHub>(root) &&
            !entities.HasComponent<CargoTransport>(root)) return baseline;
        var visible = new HashSet<EntityId>();
        foreach (ref readonly var instance in instances.AsSpan())
            if (OperationsWorldOverlay.IsOwnedVisible(instance, extraction.Player)) visible.Add(instance.Entity);
        if (!Authorized(root)) return entities.HasComponent<VisualIdentity>(root) ? null : baseline;

        var incoming = new Dictionary<EntityId, List<OperationsDiagnosisLink>>();
        IReadOnlyList<ConveyorBuildingPort>? buildingPorts = null;
        var requests = new Dictionary<EntityId, List<LogisticsTransportRequestReadModel>>();
        bool currentFlow = scenario.Services.ConveyorFlow.ObservationTick == context.Tick;
        if (currentFlow)
            foreach (EntityId belt in entities.Query<ConveyorPiece, ConveyorFlowState>(ForgeLine.Ecs.QueryIterationOrder.StableByEntityIndex))
            {
                if (!Authorized(belt) || !scenario.Services.ConveyorFlow.TryGetObservation(belt, out _)) continue;
                var flow = entities.GetComponent<ConveyorFlowState>(belt);
                AddConveyor(flow.InputA, belt); AddConveyor(flow.InputB, belt);
                if (!entities.HasComponent<ConveyorFlowState>(flow.OutputA)) AddConveyor(belt, flow.OutputA);
                if (!entities.HasComponent<ConveyorFlowState>(flow.OutputB)) AddConveyor(belt, flow.OutputB);
            }
        var distribution = scenario.AutomatedDistribution.LastDebugSnapshot;
        if (distribution.Tick == context.Tick)
            foreach (var request in distribution.Requests)
            {
                if (request.State == LogisticsTransportRequestState.Completed || !request.RequestId.IsSpecified) continue;
                EntityId destinationEntity = scenario.Logistics.TryGetNode(request.Destination, out var destination) && Authorized(destination.Entity)
                    ? destination.Entity : default;
                EntityId target = destinationEntity.IsValid ? destinationEntity : entities.TryGetComponent(request.PolicyEntity, out LogisticsStockPolicy policy) &&
                        policy.ResourceId == request.ResourceId && Authorized(policy.TargetEntity) ? policy.TargetEntity : default;
                if (!target.IsValid) continue;
                if (!requests.TryGetValue(target, out var targetRequests)) requests[target] = targetRequests = [];
                targetRequests.Add(request);
                if (request.State == LogisticsTransportRequestState.Failed ||
                    !scenario.Logistics.TryGetNode(request.Origin, out var origin) ||
                    !Authorized(origin.Entity) || !destinationEntity.IsValid) continue;
                Add(new(request.RequestId, origin.Entity, destinationEntity, request.ResourceId));
            }
        foreach (var list in incoming.Values)
            list.Sort(static (a, b) =>
            {
                int source = a.Source.CompareTo(b.Source);
                if (source != 0) return source;
                int kind = a.Kind.CompareTo(b.Kind);
                return kind != 0 ? kind : a.RequestId.CompareTo(b.RequestId);
            });

        var rows = baseline?.Rows.ToList() ?? [];
        var links = new List<OperationsDiagnosisLink>();
        var visited = new HashSet<(EntityId, ResourceId, OperationsDiagnosisLinkKind)>();
        var pending = new Queue<(EntityId Entity, ResourceId Resource, EntityId[] Path, OperationsDiagnosisLinkKind Kind)>();
        pending.Enqueue((root, default, [root], OperationsDiagnosisLinkKind.ConveyorConnection));
        int steps = 0, omitted = 0, branches = 0, totalRows = baseline?.TotalRows ?? rows.Count, totalLinks = 0;
        bool cycle = false, missing = false;
        while (pending.TryDequeue(out var item))
        {
            if (!visited.Add((item.Entity, item.Resource, item.Kind))) continue;
            if (steps++ > OperationsDiagnosisSnapshot.MaximumTraceDepth) { omitted++; continue; }
            if (!Authorized(item.Entity)) { missing = true; continue; }
            EntityId entity = item.Entity;
            var resources = new List<ResourceId>();
            bool expand = true;
            InventoryId observedInventory = item.Resource.IsSpecified ? Inventory(entity, input: false, item.Kind) : default;
            InventoryId requiredInventory = observedInventory;
            bool observedLeaf = stock.Contains(observedInventory) && !entities.HasComponent<ProductionFacility>(entity) &&
                !entities.HasComponent<UnitProductionFacility>(entity) && !entities.HasComponent<ConveyorFlowState>(entity);
            if (stock.Contains(observedInventory))
            {
                double quantity = stock.GetQuantity(observedInventory, item.Resource);
                double free = stock.GetAvailableQuantity(observedInventory, item.Resource);
                var reason = free > 0 ? OperationsDiagnosisReason.None : quantity > 0
                    ? OperationsDiagnosisReason.ReservedStock : OperationsDiagnosisReason.NoSourceStock;
                Append(Row(OperationsDiagnosisSubject.SourceInventory, entity, item.Resource, "OBSERVED SOURCE STOCK", reason)
                    with { LocalQuantity = quantity, FreeQuantity = free, ReservedQuantity = quantity - free,
                        SourceFreeQuantity = free, Inventory = observedInventory, CauseEntity = reason == OperationsDiagnosisReason.None ? default : entity });
            }
            if (entities.TryGetComponent(entity, out ProductionFacility production))
            {
                bool matchingOutput = !item.Resource.IsSpecified ||
                    scenario.Services.ProductionRecipes.TryGet(production.ActiveRecipe, out var active) &&
                    active.Outputs.Any(output => output.ResourceId == item.Resource);
                if (!matchingOutput) { missing = true; expand = false; }
                if (entity != root)
                    Append(Row(OperationsDiagnosisSubject.Production, entity, default,
                        production.Status.ToString(), matchingOutput ? ProductionReason(production.BlockReason) : OperationsDiagnosisReason.Unknown));
                if (matchingOutput && production.BlockReason == ProductionBlockReason.NoInput) requiredInventory = production.InputInventory;
                if (matchingOutput && production.BlockReason == ProductionBlockReason.NoInput &&
                    scenario.Services.ProductionRecipes.TryGet(production.ActiveRecipe, out var recipe))
                    foreach (var ingredient in recipe.Inputs)
                    {
                        if (!stock.Contains(production.InputInventory)) { missing = true; continue; }
                        double local = stock.GetQuantity(production.InputInventory, ingredient.ResourceId);
                        double free = stock.GetAvailableQuantity(production.InputInventory, ingredient.ResourceId);
                        if (free >= ingredient.Quantity) continue;
                        resources.Add(ingredient.ResourceId);
                        Append(Row(OperationsDiagnosisSubject.ProductionInput, entity, ingredient.ResourceId,
                            "RECIPE INPUT", free < local ? OperationsDiagnosisReason.ReservedStock : OperationsDiagnosisReason.NoInput)
                            with { Demand = ingredient.Quantity - free, LocalQuantity = local,
                                FreeQuantity = free, ReservedQuantity = local - free, Inventory = production.InputInventory });
                    }
            }
            if (entities.TryGetComponent(entity, out UnitProductionFacility unitProduction) &&
                unitProduction.BlockReason == UnitProductionBlockReason.NoInput &&
                scenario.Services.UnitDefinitions.TryGet(unitProduction.ActiveUnit, out var unit))
                foreach (var cost in unit.Costs)
                {
                    requiredInventory = unitProduction.InputInventory;
                    if (!stock.Contains(unitProduction.InputInventory)) { missing = true; continue; }
                    double local = stock.GetQuantity(unitProduction.InputInventory, cost.ResourceId);
                    double free = stock.GetAvailableQuantity(unitProduction.InputInventory, cost.ResourceId);
                    if (free >= cost.Quantity) continue;
                    resources.Add(cost.ResourceId);
                    Append(Row(OperationsDiagnosisSubject.ProductionInput, entity, cost.ResourceId,
                        "UNIT INPUT", free < local ? OperationsDiagnosisReason.ReservedStock : OperationsDiagnosisReason.NoInput)
                        with { Demand = cost.Quantity - free, LocalQuantity = local, FreeQuantity = free,
                            ReservedQuantity = local - free, Inventory = unitProduction.InputInventory });
                }
            if (entity == root && resources.Count == 0 && !entities.HasComponent<ProductionFacility>(entity) &&
                !entities.HasComponent<UnitProductionFacility>(entity) && !entities.HasComponent<ConveyorFlowState>(entity))
            {
                if (baseline is not null)
                    for (int index = 0; index < baseline.Rows.Count; index++)
                    {
                        var row = baseline.Rows[index];
                        bool demand = row.Subject == OperationsDiagnosisSubject.StockPolicy && row.Demand is > 0;
                        bool activeRequest = row.Subject == OperationsDiagnosisSubject.TransportRequest && row.OutstandingRequestQuantity is > 0 &&
                            row.Status is (nameof(LogisticsTransportRequestState.Pending) or nameof(LogisticsTransportRequestState.Assigned) or
                                nameof(LogisticsTransportRequestState.InTransit) or nameof(LogisticsTransportRequestState.RetryPending));
                        if ((demand || activeRequest) && row.Resource.IsSpecified && !resources.Contains(row.Resource)) resources.Add(row.Resource);
                    }
                if (resources.Count == 0) return baseline;
                if (scenario.CargoTransport.TryObserveInventory(entities, entity, input: true, out var targetInventory))
                    requiredInventory = targetInventory;
            }
            if ((entities.HasComponent<ProductionFacility>(entity) || entities.HasComponent<UnitProductionFacility>(entity)) && resources.Count == 0)
            {
                if (entity == root) return baseline;
                expand = false;
            }
            if (entities.TryGetComponent(entity, out ConveyorFlowState conveyor))
            {
                requiredInventory = conveyor.Buffer;
                var primary = OperationsDiagnosisReason.Unknown;
                EntityId cause = default;
                ResourceId observedResource = item.Resource;
                var secondary = OperationsDiagnosisReason.None;
                var tertiary = OperationsDiagnosisReason.None;
                if (currentFlow && scenario.Services.ConveyorFlow.TryGetObservation(entity, out var fact))
                {
                    var first = fact.First;
                    if (item.Resource.IsSpecified && first.Resource.IsSpecified && first.Resource != item.Resource)
                        first = Matches(fact.Second) ? fact.Second : Matches(fact.Third) ? fact.Third : new(ConveyorBlockReason.Unknown, default);
                    primary = ObservedReason(first);
                    secondary = Matches(fact.Second) && fact.Second != first ? ObservedReason(fact.Second) : OperationsDiagnosisReason.None;
                    tertiary = Matches(fact.Third) && fact.Third != first ? ObservedReason(fact.Third) : OperationsDiagnosisReason.None;
                    if (Authorized(first.Cause)) cause = first.Cause;
                    if (!observedResource.IsSpecified && (!first.Cause.IsValid || Authorized(first.Cause))) observedResource = first.Resource;
                    bool Matches(ConveyorBlockObservation candidate) => candidate.Reason != ConveyorBlockReason.None &&
                        (!item.Resource.IsSpecified || !candidate.Resource.IsSpecified || candidate.Resource == item.Resource);
                }
                if (resources.Count == 0 && observedResource.IsSpecified) resources.Add(observedResource);
                Append(Row(OperationsDiagnosisSubject.Conveyor, entity, observedResource, conveyor.Status.ToString(), primary)
                    with { CauseEntity = cause, Inventory = conveyor.Buffer,
                        SecondaryReason = secondary, TertiaryReason = tertiary });
            }
            if (requests.TryGetValue(entity, out var observedRequests))
                foreach (var request in observedRequests)
                {
                    if (rows.Any(row => row.Entity == entity && row.Subject == OperationsDiagnosisSubject.TransportRequest &&
                        row.RequestId == request.RequestId)) continue;
                    var reasons = new OperationsDiagnosisSnapshot.ReasonAccumulator();
                    reasons.Add(OperationsDiagnosisSnapshot.FromFailure(request.FailureReason));
                    reasons.Add(OperationsDiagnosisSnapshot.FromBottleneck(request.BottleneckReason));
                    if (reasons.First == OperationsDiagnosisReason.None &&
                        request.State is LogisticsTransportRequestState.Failed or LogisticsTransportRequestState.RetryPending)
                        reasons.Add(OperationsDiagnosisReason.Unknown);
                    EntityId source = scenario.Logistics.TryGetNode(request.Origin, out var origin) && Authorized(origin.Entity)
                        ? origin.Entity : default;
                    EntityId truck = Authorized(request.AssignedTruck) ? request.AssignedTruck : default;
                    double? loaded = truck.IsValid &&
                        request.State is (LogisticsTransportRequestState.Assigned or LogisticsTransportRequestState.InTransit) &&
                        entities.TryGetComponent(truck, out CargoTransportOrder order) &&
                        order.Origin == request.Origin && order.Destination == request.Destination && order.ResourceId == request.ResourceId &&
                        entities.TryGetComponent(truck, out CargoTransportRuntimeState transport)
                        ? Math.Max(0, transport.LoadedQuantity - transport.DeliveredQuantity) : null;
                    Append(Row(OperationsDiagnosisSubject.TransportRequest, entity, request.ResourceId, request.State.ToString(), reasons.First)
                        with { RequestId = request.RequestId, OutstandingRequestQuantity = request.RequestedQuantity,
                            Source = source, Destination = entity, AssignedTruck = truck, LoadedCargoQuantity = loaded,
                            SecondaryReason = reasons.Second, TertiaryReason = reasons.Third });
                }
            if (!expand) continue;
            if (resources.Count == 0 && item.Resource.IsSpecified) resources.Add(item.Resource);
            if (resources.Count == 0) resources.Add(default);
            foreach (ResourceId resource in resources)
            {
                if (!incoming.TryGetValue(entity, out var sources))
                {
                    if (resource.IsSpecified && !observedLeaf) missing = true;
                    continue;
                }
                int alternatives = 0;
                foreach (var candidate in sources)
                {
                    if (resource.IsSpecified && candidate.Resource.IsSpecified && candidate.Resource != resource) continue;
                    if (candidate.Kind == OperationsDiagnosisLinkKind.ConveyorConnection && resource.IsSpecified &&
                        (!AllowsConveyorResource(candidate.Source, input: false, resource) ||
                         !AllowsConveyorResource(candidate.Destination, input: true, resource))) continue;
                    ResourceId observedResource = candidate.Resource.IsSpecified ? candidate.Resource : resource;
                    InventoryId sourceInventory = Inventory(candidate.Source, input: false, candidate.Kind);
                    InventoryId destinationInventory = Inventory(candidate.Destination, input: true, candidate.Kind);
                    if (requiredInventory.IsSpecified && destinationInventory != requiredInventory)
                    { missing = true; continue; }
                    alternatives++;
                    var link = candidate with { Resource = observedResource,
                        SourceInventory = sourceInventory, DestinationInventory = destinationInventory };
                    if (!links.Contains(link))
                    {
                        totalLinks++;
                        if (links.Count < OperationsDiagnosisSnapshot.MaximumLinks) links.Add(link);
                        else { omitted++; continue; }
                    }
                    double? sourceFree = observedResource.IsSpecified && stock.Contains(sourceInventory)
                        ? stock.GetAvailableQuantity(sourceInventory, observedResource) : null;
                    Append(Row(OperationsDiagnosisSubject.SourceInventory,
                        entity, observedResource, candidate.Kind == OperationsDiagnosisLinkKind.TransportRequest
                            ? "TRANSPORT REQUEST" : "CONNECTED PORT", OperationsDiagnosisReason.None)
                        with { RequestId = candidate.RequestId, Source = candidate.Source, Destination = entity,
                            SourceFreeQuantity = sourceFree, Inventory = sourceInventory });
                    if (item.Path.Contains(candidate.Source)) { cycle = true; continue; }
                    if (item.Path.Length > OperationsDiagnosisSnapshot.MaximumTraceDepth)
                    { omitted++; continue; }
                    pending.Enqueue((candidate.Source, observedResource, [.. item.Path, candidate.Source], candidate.Kind));
                }
                if (alternatives > 1) branches += alternatives - 1;
                if (alternatives == 0) missing = true;
            }
        }
        return rows.Count == 0 ? baseline : new(scenario.Simulation.SessionId, context.Tick,
            extraction.Player, rows, links, totalRows, root, omitted, cycle, missing, branches, totalLinks);

        bool Authorized(EntityId entity) => entity.IsValid && visible.Contains(entity) &&
            entities.TryGetComponent(entity, out ControllableEntity owner) &&
            owner.Owner == extraction.Player && owner.IsControllable &&
            (!entities.TryGetComponent(entity, out CompletedBuilding building) || building.Owner == extraction.Player) &&
            (!entities.TryGetComponent(entity, out ConveyorPiece piece) || piece.Owner == extraction.Player) &&
            (!entities.TryGetComponent(entity, out UnitProductionFacility factory) || factory.Owner == extraction.Player);
        bool AllowsConveyorResource(EntityId entity, bool input, ResourceId resource)
        {
            if (!Authorized(entity)) return false;
            int mask = 0;
            for (int index = 0; index < ConveyorSimulationSystem.EligibleResources.Count; index++)
                if (ConveyorSimulationSystem.EligibleResources[index] == resource)
                {
                    mask = 1 << index;
                    break;
                }
            if (mask == 0) return false;
            if (entities.HasComponent<ConveyorFlowState>(entity)) return true;
            buildingPorts ??= ConveyorConstructionService.CapturePorts(entities, extraction.Player);
            for (int index = 0; index < buildingPorts.Count; index++)
            {
                var port = buildingPorts[index];
                if (port.Building == entity && port.Input == input &&
                    (scenario.Services.ConveyorFlow.AllowedResources(entities, port) & mask) != 0)
                    return true;
            }
            return false;
        }
        OperationsDiagnosisReason ObservedReason(ConveyorBlockObservation fact) =>
            fact.Cause.IsValid && !Authorized(fact.Cause) ? OperationsDiagnosisReason.Unknown : ConveyorReason(fact.Reason);
        InventoryId Inventory(EntityId entity, bool input, OperationsDiagnosisLinkKind kind)
        {
            if (!Authorized(entity)) return default;
            if (entities.TryGetComponent(entity, out ResourceExtractor extractor) &&
                extractor.OutputInventory != entity && !Authorized(extractor.OutputInventory)) return default;
            if (kind == OperationsDiagnosisLinkKind.TransportRequest)
                return scenario.CargoTransport.TryObserveInventory(entities, entity, input, out var inventory) ? inventory : default;
            if (entities.TryGetComponent(entity, out ConveyorFlowState flow)) return flow.Buffer;
            return ConveyorSimulationSystem.ResolveInventory(entities, entity, input);
        }
        void AddConveyor(EntityId source, EntityId destination)
        {
            if (Authorized(source) && Authorized(destination))
                Add(new(default, source, destination, default, OperationsDiagnosisLinkKind.ConveyorConnection));
        }
        void Add(OperationsDiagnosisLink link)
        {
            if (!incoming.TryGetValue(link.Destination, out var list)) incoming[link.Destination] = list = [];
            if (!list.Contains(link)) list.Add(link);
        }
        void Append(OperationsDiagnosisRow row)
        {
            totalRows++;
            if (rows.Count < OperationsDiagnosisSnapshot.MaximumRows) rows.Add(row);
        }
    }

    private static OperationsDiagnosisRow Row(OperationsDiagnosisSubject subject, EntityId entity,
        ResourceId resource, string status, OperationsDiagnosisReason reason) =>
        new(subject, entity, resource, default, status, null, null, null, null, null, null, null, null,
            default, default, default, null, null, reason);

    internal static OperationsDiagnosisReason ProductionReason(ProductionBlockReason reason) => reason switch
    {
        ProductionBlockReason.NoInput => OperationsDiagnosisReason.NoInput,
        ProductionBlockReason.NoPower => OperationsDiagnosisReason.NoPower,
        ProductionBlockReason.OutputFull => OperationsDiagnosisReason.OutputFull,
        ProductionBlockReason.Paused => OperationsDiagnosisReason.ProductionPaused,
        ProductionBlockReason.None or ProductionBlockReason.DesiredStockReached => OperationsDiagnosisReason.None,
        _ => OperationsDiagnosisReason.Unknown
    };

    internal static OperationsDiagnosisReason ConveyorReason(ConveyorBlockReason reason) => reason switch
    {
        ConveyorBlockReason.None => OperationsDiagnosisReason.None,
        ConveyorBlockReason.DestroyedRoute or ConveyorBlockReason.DamagedRoute => OperationsDiagnosisReason.DestroyedRoute,
        ConveyorBlockReason.DestroyedSupport => OperationsDiagnosisReason.DestroyedSupport,
        ConveyorBlockReason.NoPower => OperationsDiagnosisReason.NoPower,
        ConveyorBlockReason.DisconnectedPort => OperationsDiagnosisReason.DisconnectedPort,
        ConveyorBlockReason.IncompatibleResource => OperationsDiagnosisReason.IncompatibleResource,
        ConveyorBlockReason.ReservedStock => OperationsDiagnosisReason.ReservedStock,
        ConveyorBlockReason.RetainedStock => OperationsDiagnosisReason.RetainedStock,
        ConveyorBlockReason.DestinationFull => OperationsDiagnosisReason.DestinationFull,
        ConveyorBlockReason.DestinationTargetCovered => OperationsDiagnosisReason.DestinationTargetCovered,
        ConveyorBlockReason.NoInput => OperationsDiagnosisReason.NoInput,
        _ => OperationsDiagnosisReason.Unknown
    };
}
