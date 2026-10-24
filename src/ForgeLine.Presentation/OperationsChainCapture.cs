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
        var visible = new HashSet<EntityId>();
        foreach (ref readonly var instance in instances.AsSpan())
            if (OperationsWorldOverlay.IsOwnedVisible(instance, extraction.Player)) visible.Add(instance.Entity);
        if (!Authorized(root)) return entities.HasComponent<VisualIdentity>(root) ? null : baseline;

        var incoming = new Dictionary<EntityId, List<OperationsDiagnosisLink>>();
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
                if (request.State is LogisticsTransportRequestState.Completed or LogisticsTransportRequestState.Failed ||
                    !scenario.Logistics.TryGetNode(request.Origin, out var origin) ||
                    !scenario.Logistics.TryGetNode(request.Destination, out var destination) ||
                    !Authorized(origin.Entity) || !Authorized(destination.Entity)) continue;
                Add(new(request.RequestId, origin.Entity, destination.Entity, request.ResourceId));
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
        var visited = new HashSet<(EntityId, ResourceId)>();
        var pending = new Queue<(EntityId Entity, ResourceId Resource, EntityId[] Path)>();
        pending.Enqueue((root, default, [root]));
        int steps = 0, omitted = 0, branches = 0, totalRows = rows.Count, totalLinks = 0;
        bool cycle = false, missing = false;
        while (pending.TryDequeue(out var item))
        {
            if (!visited.Add((item.Entity, item.Resource))) continue;
            if (steps++ > OperationsDiagnosisSnapshot.MaximumTraceDepth) { omitted++; continue; }
            if (!Authorized(item.Entity)) { missing = true; continue; }
            EntityId entity = item.Entity;
            var resources = new List<ResourceId>();
            if (entities.TryGetComponent(entity, out ProductionFacility production))
            {
                if (entity != root)
                    Append(Row(OperationsDiagnosisSubject.Production, entity, default,
                        production.Status.ToString(), ProductionReason(production.BlockReason)));
                if (production.BlockReason == ProductionBlockReason.NoInput &&
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
            if (entities.TryGetComponent(entity, out ConveyorFlowState conveyor))
            {
                var primary = OperationsDiagnosisReason.Unknown;
                EntityId cause = default;
                var secondary = OperationsDiagnosisReason.None;
                var tertiary = OperationsDiagnosisReason.None;
                if (currentFlow && scenario.Services.ConveyorFlow.TryGetObservation(entity, out var fact))
                {
                    primary = ConveyorReason(fact.First.Reason);
                    secondary = ConveyorReason(fact.Second.Reason);
                    tertiary = ConveyorReason(fact.Third.Reason);
                    if (Authorized(fact.First.Cause)) cause = fact.First.Cause;
                }
                Append(Row(OperationsDiagnosisSubject.Conveyor, entity, item.Resource, conveyor.Status.ToString(), primary)
                    with { CauseEntity = cause, Inventory = conveyor.Buffer,
                        SecondaryReason = secondary, TertiaryReason = tertiary });
            }
            if (resources.Count == 0 && item.Resource.IsSpecified) resources.Add(item.Resource);
            if (resources.Count == 0) resources.Add(default);
            foreach (ResourceId resource in resources)
            {
                if (!incoming.TryGetValue(entity, out var sources))
                {
                    if (resource.IsSpecified) missing = true;
                    continue;
                }
                int alternatives = 0;
                foreach (var candidate in sources)
                {
                    if (resource.IsSpecified && candidate.Resource.IsSpecified && candidate.Resource != resource) continue;
                    alternatives++;
                    ResourceId observedResource = candidate.Resource.IsSpecified ? candidate.Resource : resource;
                    InventoryId sourceInventory = Inventory(candidate.Source, input: false);
                    InventoryId destinationInventory = Inventory(candidate.Destination, input: true);
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
                    Append(Row(candidate.Kind == OperationsDiagnosisLinkKind.TransportRequest
                            ? OperationsDiagnosisSubject.TransportRequest : OperationsDiagnosisSubject.Conveyor,
                        entity, observedResource, candidate.Kind == OperationsDiagnosisLinkKind.TransportRequest
                            ? "TRANSPORT REQUEST" : "CONNECTED PORT", OperationsDiagnosisReason.None)
                        with { RequestId = candidate.RequestId, Source = candidate.Source, Destination = entity,
                            SourceFreeQuantity = sourceFree, Inventory = sourceInventory });
                    if (item.Path.Contains(candidate.Source)) { cycle = true; continue; }
                    if (item.Path.Length > OperationsDiagnosisSnapshot.MaximumTraceDepth)
                    { omitted++; continue; }
                    pending.Enqueue((candidate.Source, observedResource, [.. item.Path, candidate.Source]));
                }
                if (alternatives > 1) branches += alternatives - 1;
                if (alternatives == 0) missing = true;
            }
        }
        return rows.Count == 0 ? baseline : new(scenario.Simulation.SessionId, context.Tick,
            extraction.Player, rows, links, totalRows, root, omitted, cycle, missing, branches, totalLinks);

        bool Authorized(EntityId entity) => entity.IsValid && visible.Contains(entity) &&
            entities.TryGetComponent(entity, out ControllableEntity owner) &&
            owner.Owner == extraction.Player && owner.IsControllable;
        InventoryId Inventory(EntityId entity, bool input)
        {
            if (!Authorized(entity)) return default;
            if (entities.TryGetComponent(entity, out ConveyorFlowState flow)) return flow.Buffer;
            if (entities.TryGetComponent(entity, out ProductionFacility facility))
                return input ? facility.InputInventory : facility.OutputInventory;
            if (input && entities.TryGetComponent(entity, out UnitProductionFacility factory)) return factory.InputInventory;
            if (entities.TryGetComponent(entity, out ResourceExtractor extractor) &&
                Authorized(extractor.OutputInventory) &&
                entities.TryGetComponent(extractor.OutputInventory, out InventoryStorage output)) return output.InventoryId;
            if (entities.TryGetComponent(entity, out InventoryStorage storage)) return storage.InventoryId;
            if (entities.TryGetComponent(entity, out SupplyDepot depot)) return depot.InventoryId;
            if (entities.TryGetComponent(entity, out LogisticsHub hub)) return hub.InventoryId;
            return default;
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
        ConveyorBlockReason.NoInput => OperationsDiagnosisReason.NoInput,
        _ => OperationsDiagnosisReason.Unknown
    };
}
