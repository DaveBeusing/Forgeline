using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class BuildingCombatDestructionTests
{
    private static readonly PlayerId Owner = new(1);

    [Fact]
    public void RepeatedCombatCasualtiesReleaseSharedDriveAndCargoInventories()
    {
        using MatchRuntime runtime = CreateRuntime(71109);
        runtime.Simulation.AdvanceOneTick();
        int baseline = runtime.Inventories.CaptureSnapshot().Inventories.Count;
        Vector3 position = runtime.Simulation.Entities.GetComponent<WorldTransform>(
            runtime.GetBase(Owner).CommandCore).Position + Vector3.UnitX * 80;
        for (int cycle = 0; cycle < 4; cycle++)
        foreach (UnitId kind in new[] { UnitIds.MainBattleTank,
            UnitIds.CargoTruck, UnitIds.SupplyTruck })
        {
            EntityId unit = runtime.UnitFactory.Create(runtime.Services.UnitDefinitions[kind], position, Owner);
            InventoryId drive = runtime.Simulation.Entities.GetComponent<UnitFuelState>(unit).InventoryId;
            Assert.Equal(drive, runtime.Simulation.Entities.GetComponent<AmmunitionState>(unit).InventoryId);
            ApplyLethalDamage(runtime, unit);
            Assert.False(runtime.Simulation.Entities.IsAlive(unit));
            Assert.False(runtime.Inventories.Contains(drive));
            runtime.Simulation.AdvanceOneTick();
            Assert.Equal(baseline, runtime.Inventories.CaptureSnapshot().Inventories.Count);
        }
    }

    [Fact]
    public void DestroyedStartingCoreReleasesNavigationWithoutRemovingSurvivingCore()
    {
        using MatchRuntime runtime = CreateRuntime(71101);
        EntityId target = runtime.GetBase(Owner).CommandCore;
        EntityId survivor = runtime.GetBase(new PlayerId(2)).CommandCore;
        InventoryId inventory = runtime.GetBase(Owner).StartingInventory;
        Vector3 targetPosition = runtime.Simulation.Entities.GetComponent<WorldTransform>(target).Position;
        Vector3 survivorPosition = runtime.Simulation.Entities.GetComponent<WorldTransform>(survivor).Position;
        Assert.True(IsNavigationBlocked(runtime, targetPosition));
        Assert.True(IsNavigationBlocked(runtime, survivorPosition));
        NavigationGrid grid = runtime.Services.Navigation.World.Grid;
        Assert.True(grid.TryWorldToCell(targetPosition, out NavigationCellCoordinate targetCell));
        Vector3 routeStart = grid.GetCellCenter(new NavigationCellCoordinate(targetCell.X - 3, targetCell.Z));
        Vector3 routeEnd = grid.GetCellCenter(new NavigationCellCoordinate(targetCell.X + 3, targetCell.Z));
        var capabilities = new NavigationCapabilities(NavigationMovementClass.Infantry, 45, slopeCostWeight: 0);
        NavigationSearchResult blockedRoute = new HierarchicalPathfinder(runtime.Services.Navigation.World)
            .FindPath(routeStart, routeEnd, capabilities);
        Assert.True(blockedRoute.Succeeded, blockedRoute.FailureReason.ToString());
        Assert.DoesNotContain(targetCell, blockedRoute.Path!.RefinedCells);

        ApplyLethalDamage(runtime, target);

        Assert.False(runtime.Simulation.Entities.IsAlive(target));
        Assert.False(runtime.Inventories.Contains(inventory));
        Assert.False(IsNavigationBlocked(runtime, targetPosition));
        Assert.True(IsNavigationBlocked(runtime, survivorPosition));
        NavigationSearchResult openRoute = new HierarchicalPathfinder(runtime.Services.Navigation.World)
            .FindPath(routeStart, routeEnd, capabilities);
        Assert.True(openRoute.Succeeded, openRoute.FailureReason.ToString());
        // Local refinement compresses a clear horizontal route to its two endpoints; that segment
        // crosses the former core cell rather than retaining the original detour around it.
        Assert.Equal(2, openRoute.Path!.RefinedCells.Count);
        Assert.Equal(routeEnd, Assert.Single(openRoute.Path.Waypoints));
        Assert.True(openRoute.Path.Diagnostics.RouteLengthMeters < blockedRoute.Path.Diagnostics.RouteLengthMeters);
        Assert.False(runtime.Logistics.TryGetNodeForEntity(target, out _));
        var wrecks = new List<EntityId>();
        foreach (EntityId candidate in runtime.Simulation.Entities.Query<BuildingWreckPresentationIdentity>())
            wrecks.Add(candidate);
        EntityId wreck = Assert.Single(wrecks);
        Assert.False(runtime.Simulation.Entities.HasComponent<SpatialPresence>(wreck));
        Assert.False(runtime.Simulation.Entities.HasComponent<InventoryStorage>(wreck));
    }

    [Fact]
    public void DestroyedStorageLosesStockCancelsDependentConstructionAndNeverRefundsDemolition()
    {
        using MatchRuntime runtime = CreateRuntime(71102);
        EntityId storage = Build(runtime, BuildingIds.StorageDepot);
        EntityRegistry entities = runtime.Simulation.Entities;
        InventoryId store = entities.GetComponent<InventoryStorage>(storage).InventoryId;
        InventoryId core = runtime.GetBase(Owner).StartingInventory;
        BuildingDefinition power = runtime.Services.BuildingDefinitions[BuildingIds.PowerPlant];
        foreach (BuildingResourceCost cost in power.Costs)
            Assert.True(runtime.Inventories.Transfer(core, store, cost.ResourceId, cost.Quantity).Succeeded);
        Assert.True(runtime.Inventories.Transfer(core, store, ResourceIds.Steel, 37).Succeeded);

        EntityId policy = entities.CreateEntity();
        entities.AddComponent(policy, new LogisticsStockPolicy(storage, ResourceIds.Steel,
            10, 20, 50, LogisticsStockPriority.Normal));
        EntityId site = BeginBuild(runtime, BuildingIds.PowerPlant, storage);
        Assert.True(entities.HasComponent<ConstructionSite>(site));
        foreach (BuildingResourceCost cost in power.Costs)
            Assert.Equal(cost.Quantity, runtime.Inventories.GetReservedQuantity(store, cost.ResourceId));
        double coreOre = runtime.Inventories.GetQuantity(core, ResourceIds.FerrousOre);
        double coreSteel = runtime.Inventories.GetQuantity(core, ResourceIds.Steel);

        ApplyLethalDamage(runtime, storage);

        Assert.False(entities.IsAlive(storage));
        Assert.False(entities.IsAlive(site));
        Assert.False(entities.IsAlive(policy));
        Assert.False(runtime.Inventories.Contains(store));
        Assert.Equal(coreOre, runtime.Inventories.GetQuantity(core, ResourceIds.FerrousOre));
        Assert.Equal(coreSteel, runtime.Inventories.GetQuantity(core, ResourceIds.Steel));
        Assert.False(runtime.Logistics.TryGetNodeForEntity(storage, out _));
        Assert.DoesNotContain(runtime.Inventories.CaptureSnapshot().Inventories,
            item => item.InventoryId == store);
        runtime.Simulation.RunTicks(power.ConstructionTicks + 1, TestContext.Current.CancellationToken);
        Assert.False(entities.IsAlive(site));
    }

    [Fact]
    public void DestroyedProcessorClosesReservedWorkAndRemovesBothInventories()
    {
        using MatchRuntime runtime = CreateRuntime(71103);
        Build(runtime, BuildingIds.PowerPlant);
        EntityId processor = Build(runtime, BuildingIds.Smelter);
        EntityRegistry entities = runtime.Simulation.Entities;
        ProductionFacility facility = entities.GetComponent<ProductionFacility>(processor);
        ProductionRecipeDefinition recipe = runtime.Services.ProductionRecipes.Definitions
            .First(item => facility.Supports(item.RequiredCapability));
        foreach (ProductionIngredient input in recipe.Inputs)
            Assert.True(runtime.Inventories.Transfer(runtime.GetBase(Owner).StartingInventory,
                facility.InputInventory, input.ResourceId, input.Quantity * 2).Succeeded);
        var order = new QueueProductionCommand(processor, recipe.Id, runtime.Simulation.CurrentTick);
        Submit(runtime, order);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(order.Accepted);
        Assert.True(entities.GetComponent<ProductionFacility>(processor).InputsReserved);

        ApplyLethalDamage(runtime, processor);

        Assert.False(entities.IsAlive(order.RequestEntity));
        Assert.False(runtime.Inventories.Contains(facility.InputInventory));
        Assert.False(runtime.Inventories.Contains(facility.OutputInventory));
        Assert.DoesNotContain(runtime.Production.Facilities, row => row.Entity == processor);
        Assert.True(runtime.Production.Metrics.CancelledRequests > 0);
        foreach (EntityId request in entities.Query<ProductionRequest>())
            Assert.NotEqual(processor, entities.GetComponent<ProductionRequest>(request).Facility);
    }

    [Fact]
    public void DestroyedBarracksCancelsUnitReservationsAndDeduplicatesStorageAlias()
    {
        using MatchRuntime runtime = CreateRuntime(71106);
        Build(runtime, BuildingIds.PowerPlant);
        EntityId barracks = Build(runtime, BuildingIds.Barracks);
        EntityRegistry entities = runtime.Simulation.Entities;
        UnitProductionFacility facility = entities.GetComponent<UnitProductionFacility>(barracks);
        Assert.Equal(facility.InputInventory, entities.GetComponent<InventoryStorage>(barracks).InventoryId);
        UnitDefinition unit = runtime.Services.UnitDefinitions[UnitIds.RifleSquad];
        foreach (UnitResourceCost cost in unit.Costs)
            Assert.True(runtime.Inventories.Transfer(runtime.GetBase(Owner).StartingInventory,
                facility.InputInventory, cost.ResourceId, cost.Quantity).Succeeded);
        var order = new QueueUnitProductionCommand(Owner, barracks, unit.Id, runtime.Simulation.CurrentTick);
        Submit(runtime, order);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(order.Accepted);
        Assert.True(entities.GetComponent<UnitProductionFacility>(barracks).InputsReserved);

        ApplyLethalDamage(runtime, barracks);

        Assert.False(entities.IsAlive(order.RequestEntity));
        Assert.False(runtime.Inventories.Contains(facility.InputInventory));
        Assert.DoesNotContain(runtime.UnitProduction.Facilities, row => row.Entity == barracks);
        Assert.Equal(0, runtime.CountUnits(Owner, unit.Id));
        Assert.True(runtime.UnitProduction.Metrics.CancelledRequests > 0);
    }

    [Fact]
    public void DestroyedResearchSourceCancelsResearchWithoutRestoringConsumedMaterials()
    {
        using MatchRuntime runtime = CreateRuntime(71107);
        Build(runtime, BuildingIds.PowerPlant);
        EntityId storage = Build(runtime, BuildingIds.StorageDepot);
        EntityId core = runtime.GetBase(Owner).CommandCore;
        EntityRegistry entities = runtime.Simulation.Entities;
        InventoryId stock = entities.GetComponent<InventoryStorage>(storage).InventoryId;
        TechnologyDefinition technology = runtime.Services.TechnologyDefinitions[TechnologyIds.IndustrialStandardization];
        foreach (TechnologyResourceCost cost in technology.Costs)
            Assert.True(runtime.Inventories.Transfer(runtime.GetBase(Owner).StartingInventory,
                stock, cost.ResourceId, cost.Quantity).Succeeded);
        var order = PlayerTechnologyActionCommand.Start(Owner, technology.Id, core, storage,
            runtime.Simulation.CurrentTick, runtime.Services.TechnologyDefinitions);
        Submit(runtime, order);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(order.Accepted);
        Assert.True(entities.GetComponent<TechnologyResearchRequest>(order.CreatedRequestEntity).MaterialsConsumed);
        double steel = runtime.Inventories.GetQuantity(runtime.GetBase(Owner).StartingInventory, ResourceIds.Steel);

        ApplyLethalDamage(runtime, storage);

        Assert.False(entities.IsAlive(order.CreatedRequestEntity));
        Assert.True(entities.IsAlive(core));
        Assert.False(TechnologyStateQueries.IsCompleted(entities, Owner, technology.Id));
        Assert.Equal(steel, runtime.Inventories.GetQuantity(runtime.GetBase(Owner).StartingInventory, ResourceIds.Steel));
        Assert.True(runtime.Services.TechnologyResearch.Metrics.CancelledRequests > 0);
    }

    [Fact]
    public void StorageRebuildGetsFreshIdentityAndInventoryWithoutInheritingDestroyedStock()
    {
        using MatchRuntime runtime = CreateRuntime(71104);
        EntityId storage = Build(runtime, BuildingIds.StorageDepot);
        EntityRegistry entities = runtime.Simulation.Entities;
        InventoryId original = entities.GetComponent<InventoryStorage>(storage).InventoryId;
        WorldTransform transform = entities.GetComponent<WorldTransform>(storage);
        Assert.True(runtime.Inventories.Transfer(runtime.GetBase(Owner).StartingInventory,
            original, ResourceIds.Steel, 29).Succeeded);
        ApplyLethalDamage(runtime, storage);

        EntityId rebuilt = Build(runtime, BuildingIds.StorageDepot, new Vector3(transform.Position.X, 0, transform.Position.Z));
        InventoryId replacement = entities.GetComponent<InventoryStorage>(rebuilt).InventoryId;
        Assert.NotEqual(storage, rebuilt);
        Assert.False(entities.IsAlive(storage));
        Assert.NotEqual(original, replacement);
        Assert.False(runtime.Inventories.Contains(original));
        Assert.Equal(0, runtime.Inventories.GetTotalQuantity(replacement));
        Assert.True(runtime.Logistics.TryGetNodeForEntity(rebuilt, out _));
        Assert.True(IsNavigationBlocked(runtime, transform.Position));
    }

    [Fact]
    public void LoadedAdapterRemainsDisconnectedUntilExplicitReplacementAfterStorageRebuild()
    {
        using MatchRuntime runtime = CreateRuntime(71108);
        Build(runtime, BuildingIds.PowerPlant);
        EntityId storage = Build(runtime, BuildingIds.StorageDepot);
        EntityRegistry entities = runtime.Simulation.Entities;
        WorldTransform oldTransform = entities.GetComponent<WorldTransform>(storage);
        ConveyorBuildingPort port = ConveyorConstructionService.CapturePorts(entities, Owner)
            .Single(item => item.Building == storage && item.Input && item.Key == "west.primary");
        Vector3 pivot = port.Position - Vector3.UnitY * 5.8f + port.Facing * 2;
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.OutputAdapter,
            pivot, pivot, ConveyorGeometry.Facing(-port.Facing), 0, true,
            DestinationPortBuilding: storage);
        // Seed retained conveyor hardware only; the building was completed through normal Build.
        // Recovery uses the actual paid command and matching authored port at this terrain height.
        EntityId adapter = CreateAdapterFixture(runtime, storage, pivot, request.Orientation);
        InventoryId buffer = entities.GetComponent<ConveyorFlowState>(adapter).Buffer;
        InventoryId payer = runtime.GetBase(Owner).StartingInventory;
        Assert.True(runtime.Inventories.Transfer(payer, buffer, ResourceIds.Steel, 25).Succeeded);
        EntityId[] supports = ConveyorConstructionService.Capture(entities, Owner)
            .Where(item => item.Piece.Placement.IsSupport).Select(item => item.Entity).ToArray();

        ApplyLethalDamage(runtime, storage);

        Assert.True(entities.IsAlive(adapter));
        Assert.Equal(storage, entities.GetComponent<ConveyorPiece>(adapter).Attachment);
        Assert.Equal(25, runtime.Inventories.GetQuantity(buffer, ResourceIds.Steel));
        Assert.Equal(BuildingPlacementFailureReason.Obstructed,
            runtime.Services.BuildingPlacement.CreatePreview(entities, Owner, BuildingIds.StorageDepot,
                new Vector3(oldTransform.Position.X - 1, 0, oldTransform.Position.Z),
                BuildingOrientation.North).Failure);
        EntityId rebuilt = Build(runtime, BuildingIds.StorageDepot,
            new Vector3(oldTransform.Position.X, 0, oldTransform.Position.Z));
        InventoryId replacement = entities.GetComponent<InventoryStorage>(rebuilt).InventoryId;
        runtime.Simulation.RunTicks(3, TestContext.Current.CancellationToken);
        Assert.Equal(storage, entities.GetComponent<ConveyorPiece>(adapter).Attachment);
        Assert.Equal(25, runtime.Inventories.GetQuantity(buffer, ResourceIds.Steel));
        Assert.Equal(0, runtime.Inventories.GetQuantity(replacement, ResourceIds.Steel));

        double paidStock = runtime.Inventories.GetQuantity(payer, ResourceIds.Steel);
        var reconnect = new ConveyorConstructionCommand(runtime.Services.Conveyors, Owner,
            request with { Edit = ConveyorEdit.Replace, Target = adapter, DestinationPortBuilding = rebuilt },
            runtime.GetBase(Owner).CommandCore, runtime.Simulation.CurrentTick);
        Submit(runtime, reconnect);
        runtime.Simulation.AdvanceOneTick();

        Assert.True(reconnect.Accepted, reconnect.Result.ToString());
        Assert.True(entities.IsAlive(adapter));
        Assert.Equal(rebuilt, entities.GetComponent<ConveyorPiece>(adapter).Attachment);
        Assert.Equal(buffer, entities.GetComponent<ConveyorFlowState>(adapter).Buffer);
        Assert.All(supports, support => Assert.True(entities.IsAlive(support)));
        Assert.Equal(paidStock - ConveyorGeometry.Cost(ConveyorModule.OutputAdapter),
            runtime.Inventories.GetQuantity(payer, ResourceIds.Steel), 6);
        Assert.True(runtime.Inventories.GetQuantity(replacement, ResourceIds.Steel) > 0);
        Assert.Equal(25, runtime.Inventories.GetQuantity(buffer, ResourceIds.Steel) +
            runtime.Inventories.GetQuantity(replacement, ResourceIds.Steel), 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DestroyedDestinationClosesReservationsAndPreservesTruckCargo(bool loaded)
    {
        using MatchRuntime runtime = CreateRuntime(71105);
        Build(runtime, BuildingIds.PowerPlant);
        EntityId destination = Build(runtime, BuildingIds.StorageDepot);
        EntityRegistry entities = runtime.Simulation.Entities;
        Assert.True(runtime.Logistics.TryGetNodeForEntity(destination, out LogisticsNodeId node));
        InventoryId destinationInventory = entities.GetComponent<InventoryStorage>(destination).InventoryId;
        InventoryId source = runtime.GetBase(Owner).StartingInventory;
        var policy = new SetLogisticsStockPolicyCommand(destination, ResourceIds.Steel,
            20, 100, 100, runtime.Simulation.CurrentTick);
        Submit(runtime, policy);

        EntityId truck = EntityId.Invalid;
        for (int tick = 0; tick < 2_000 && !truck.IsValid; tick++)
        {
            runtime.Simulation.AdvanceOneTick();
            foreach (EntityId candidate in entities.Query<CargoTransportOrder>(QueryIterationOrder.StableByEntityIndex))
            {
                CargoTransportOrder order = entities.GetComponent<CargoTransportOrder>(candidate);
                if (order.Destination != node) continue;
                CargoTransportRuntimeState state = entities.GetComponent<CargoTransportRuntimeState>(candidate);
                if (loaded ? state.Lifecycle == CargoTransportLifecycleState.ToDestination && state.LoadedQuantity > 0 :
                    entities.HasComponent<CargoTransportReservation>(candidate))
                {
                    truck = candidate;
                    break;
                }
            }
        }

        Assert.True(truck.IsValid, $"No {(loaded ? "loaded" : "reserved")} shipment reached the fixture checkpoint.");
        CargoTransport transport = entities.GetComponent<CargoTransport>(truck);
        double carriedBefore = runtime.Inventories.GetQuantity(transport.CargoInventory, ResourceIds.Steel);
        double accounted = runtime.Inventories.GetQuantity(source, ResourceIds.Steel) + carriedBefore;
        Assert.Equal(0, runtime.Inventories.GetQuantity(destinationInventory, ResourceIds.Steel));

        ApplyLethalDamage(runtime, destination);

        Assert.True(entities.IsAlive(truck));
        Assert.False(entities.HasComponent<CargoTransportOrder>(truck));
        Assert.False(entities.HasComponent<CargoTransportReservation>(truck));
        Assert.False(entities.HasComponent<VehicleDockVisit>(truck));
        Assert.Equal(0, runtime.Inventories.GetReservedQuantity(source, ResourceIds.Steel));
        Assert.True(runtime.Inventories.Contains(transport.CargoInventory));
        Assert.True(runtime.Inventories.GetQuantity(transport.CargoInventory, ResourceIds.Steel) >= carriedBefore);
        Assert.Equal(accounted, runtime.Inventories.GetQuantity(source, ResourceIds.Steel) +
            runtime.Inventories.GetQuantity(transport.CargoInventory, ResourceIds.Steel), 6);
        Assert.Contains(runtime.AutomatedDistribution.LastDebugSnapshot.Requests,
            request => request.Destination == node && request.State == LogisticsTransportRequestState.Failed);
    }

    private static MatchRuntime CreateRuntime(ulong seed) =>
        CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, seed) with
        {
            Participants = CentralDivideScenario.CreateDefaultParticipants(
                westComputerControlled: false, eastComputerControlled: false)
        }, TestContext.Current.CancellationToken);

    private static EntityId CreateAdapterFixture(
        MatchRuntime runtime, EntityId storage, Vector3 pivot, BuildingOrientation orientation)
    {
        EntityRegistry entities = runtime.Simulation.Entities;
        var placement = new ConveyorPlacement(ConveyorModule.OutputAdapter, pivot, orientation, 0);
        EntityId adapter = entities.CreateEntity();
        entities.AddComponent(adapter, new ConveyorPiece(Owner, placement,
            ConveyorGeometry.Cost(placement.Module), storage));
        InventoryId buffer = runtime.Inventories.CreateInventory(new InventorySpecification(100,
            ConveyorSimulationSystem.EligibleResources));
        entities.AddComponent(adapter, new ConveyorFlowState(buffer, 100));
        entities.AddComponent(adapter, HealthState.Full(200));
        entities.AddComponent(adapter, entities.GetComponent<PowerNetworkMembership>(storage));
        entities.AddComponent(adapter, new PowerConsumer(2));
        AddGeometry(adapter, placement);
        var foot = new ConveyorPlacement(ConveyorModule.Support,
            placement.Sockets[0].Position - Vector3.UnitY * 5.8f, BuildingOrientation.North, 0);
        EntityId support = entities.CreateEntity();
        entities.AddComponent(support, new ConveyorPiece(Owner, foot,
            ConveyorGeometry.Cost(foot.Module), AutomaticSupport: true));
        entities.AddComponent(support, HealthState.Full(200));
        AddGeometry(support, foot);
        return adapter;

        void AddGeometry(EntityId entity, ConveyorPlacement part)
        {
            entities.AddComponent(entity, new WorldTransform(part.Bounds.Center, part.Rotation, new Vector3(18)));
            entities.AddComponent(entity, new VisualIdentity(1));
            entities.AddComponent(entity, new ControllableEntity(Owner, ControllableEntityCategory.Logistics));
            entities.AddComponent(entity, new SpatialPresence((part.Bounds.Maximum - part.Bounds.Minimum) / 2,
                new SpatialEntryMetadata(Owner.Value, (ulong)ControllableEntityCategory.Logistics, SpatialMobility.Static)));
        }
    }

    private static EntityId Build(MatchRuntime runtime, BuildingId id, Vector3? position = null)
    {
        EntityId site = BeginBuild(runtime, id, runtime.GetBase(Owner).CommandCore, position);
        uint ticks = runtime.Services.BuildingDefinitions[id].ConstructionTicks;
        runtime.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
        Assert.True(runtime.Simulation.Entities.HasComponent<CompletedBuilding>(site));
        Assert.True(runtime.Simulation.Entities.HasComponent<HealthState>(site));
        return site;
    }

    private static EntityId BeginBuild(MatchRuntime runtime, BuildingId id, EntityId payer, Vector3? position = null)
    {
        Vector3 location = position ?? FindSite(runtime, id);
        var command = new BuildCommand(Owner, id, location, BuildingOrientation.North,
            payer, runtime.Simulation.CurrentTick);
        Submit(runtime, command);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(runtime.Services.BuildingCommands.TryGetLastResult(Owner, out BuildCommandResult result));
        Assert.True(result.Accepted, $"{result.RejectionReason}: {result.PlacementFailure}");
        return result.CreatedSite;
    }

    private static Vector3 FindSite(MatchRuntime runtime, BuildingId id)
    {
        Vector3 center = runtime.Simulation.Entities.GetComponent<WorldTransform>(runtime.GetBase(Owner).CommandCore).Position;
        for (int radius = 48; radius <= 192; radius += 24)
            for (int x = -radius; x <= radius; x += 24)
                for (int z = -radius; z <= radius; z += 24)
                {
                    var position = new Vector3(center.X + x, 0, center.Z + z);
                    if (runtime.Services.BuildingPlacement.CreatePreview(runtime.Simulation.Entities,
                        Owner, id, position, BuildingOrientation.North).IsValid)
                        return position;
                }
        throw new InvalidOperationException($"No buildable test site for {id}.");
    }

    private static void Submit(MatchRuntime runtime, ISimulationCommand command) =>
        runtime.Simulation.SubmitCommand(command, new SimulationTick(runtime.Simulation.CurrentTick.Value + 1),
            new SimulationCommandSource(Owner.Value));

    private static void ApplyLethalDamage(MatchRuntime runtime, EntityId target)
    {
        // Focus the lifecycle regression on the production damage queue; combat acquisition is
        // exercised independently by construction-to-attack scenarios, without augmenting targets.
        Vector3 position = runtime.Simulation.Entities.GetComponent<WorldTransform>(target).Position;
        runtime.Services.CombatRuntime.QueueDamage(runtime.GetBase(new PlayerId(2)).CommandCore,
            target, EntityId.Invalid, DirectorateContent.WeaponIds.MainBattleCannon,
            position, Vector3.UnitZ, new DamagePayload(100_000));
        runtime.Simulation.AdvanceOneTick();
    }

    private static bool IsNavigationBlocked(MatchRuntime runtime, Vector3 position)
    {
        NavigationGrid grid = runtime.Services.Navigation.World.Grid;
        Assert.True(grid.TryWorldToCell(position, out NavigationCellCoordinate cell));
        return grid.GetCell(cell).StaticBlocked;
    }
}
