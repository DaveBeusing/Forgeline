using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class VehicleDockingIntegrationTests
{
    [Theory]
    [InlineData(10, false, 1, false, false)]
    [InlineData(20, false, 1, false, false)]
    [InlineData(10, true, 1, false, false)]
    [InlineData(20, true, 1, false, false)]
    [InlineData(20, false, 2, false, false)]
    [InlineData(20, false, 1, true, false)]
    [InlineData(20, false, 2, false, true)]
    [InlineData(10, false, 4, false, true)]
    [InlineData(20, false, 4, false, true)]
    public void CargoDeliveryIncludesStationaryServiceAndPhysicalExit(int ticksPerSecond, bool viaBuilding, int truckCount, bool invalidateRoute, bool withSpatial)
    {
        var simulation = new SimulationCoordinator(ticksPerSecond);
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        SpatialGridIndex? spatial = withSpatial ? new SpatialGridIndex() : null;
        var cargo = new CargoTransportSystem(network, inventories, docking: new VehicleDockingService(spatial: spatial));
        simulation.RegisterSystem(new GroundMovementSystem(spatialIndex: spatial));
        if (spatial is not null) simulation.RegisterSystem(new SpatialIndexSystem(new SpatialIndexSynchronizer(spatial)));
        simulation.RegisterSystem(cargo);
        var source = CreateBuilding(simulation, inventories, Vector3.Zero);
        var destination = CreateBuilding(simulation, inventories, new(120, 0, 0));
        int sourceStock = Math.Max(100, truckCount * 40 + 20);
        Assert.True(inventories.Add(source.Inventory, ResourceIds.Steel, sourceStock).Succeeded);
        var origin = network.AddNode(source.Entity, Vector3.Zero, LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoSource | LogisticsNodeCapabilities.CargoDestination);
        var target = network.AddNode(destination.Entity, new(120, 0, 0), LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoDestination);
        EntityId waypointBuilding = EntityId.Invalid;
        if (viaBuilding)
        {
            var middle = CreateBuilding(simulation, inventories, new(60, 0, 0));
            waypointBuilding = middle.Entity;
            var waypoint = network.AddNode(middle.Entity, new(60, 0, 0), LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.CargoDestination);
            network.AddEdge(origin, waypoint, LogisticsTransportMode.GroundRoad, 60, 60, 100);
            network.AddEdge(waypoint, target, LogisticsTransportMode.GroundRoad, 60, 60, 100);
        }
        else network.AddEdge(origin, target, LogisticsTransportMode.GroundRoad, 120, 120, 100);
        EntityId truck = CargoTruckFactory.Create(simulation.Entities, inventories, new(-50, 1, 14), new(1), cargo);
        simulation.Entities.SetComponent(truck, GroundMovementState.Stationary(MathF.PI / 2));
        Assert.True(cargo.TryAssignOrder(simulation.Entities, truck, new(origin, target, ResourceIds.Steel, 40, SimulationTick.Zero), SimulationTick.Zero));
        var trucks = new List<EntityId> { truck };
        for (int index = 1; index < truckCount; index++)
        {
            var second = CargoTruckFactory.Create(simulation.Entities, inventories, new(-60 - index * 12, 1, 30 + index * 10), new(1), cargo);
            Assert.True(cargo.TryAssignOrder(simulation.Entities, second, new(origin, target, ResourceIds.Steel, 40, SimulationTick.Zero), SimulationTick.Zero));
            trucks.Add(second);
        }
        int serviceTicks = 0;
        bool sawExit = false;
        bool changedRoute = false;
        for (int tick = 0; tick < ticksPerSecond * 180; tick++)
        {
            simulation.AdvanceOneTick();
            if (waypointBuilding.IsValid)
                Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(waypointBuilding));
            if (invalidateRoute && !changedRoute && simulation.Entities.TryGetComponent(truck, out VehicleDockVisit pendingDock) &&
                pendingDock.Building == destination.Entity &&
                simulation.Entities.HasComponent<VehicleQueueEntry>(truck))
            {
                var unrelated = simulation.Entities.CreateEntity();
                network.AddNode(unrelated, new(300, 0, 100), LogisticsNodeKind.LogisticsHub, LogisticsNodeCapabilities.Storage);
                changedRoute = true;
            }
            if (simulation.Entities.TryGetComponent(truck, out VehicleDockVisit visit))
            {
                if (visit.ServiceStartedTick > 0)
                {
                    serviceTicks++;
                    var motion = simulation.Entities.GetComponent<GroundMovementState>(truck);
                    Assert.True(motion.Velocity.Length() <= 0.05f);
                    Assert.True(visit.Pose.IsSettled(simulation.Entities.GetComponent<WorldTransform>(truck), motion));
                }
                sawExit |= visit.Status == VehicleDockStatus.Exiting;
            }
            if (trucks.All(entity => !simulation.Entities.HasComponent<CargoTransportOrder>(entity))) break;
        }
        var state = simulation.Entities.GetComponent<CargoTransportRuntimeState>(truck);
        Assert.True(state.Lifecycle == CargoTransportLifecycleState.Idle,
            $"Unfinished {state}; position={simulation.Entities.GetComponent<WorldTransform>(truck).Position}; visit={VisitDescription(simulation, truck)}");
        foreach (var entity in trucks)
            Assert.False(simulation.Entities.HasComponent<CargoTransportOrder>(entity), VisitDescription(simulation, entity));
        Assert.Equal(40 * truckCount, inventories.GetQuantity(destination.Inventory, ResourceIds.Steel));
        Assert.Equal(sourceStock - 40 * truckCount, inventories.GetQuantity(source.Inventory, ResourceIds.Steel));
        Assert.True(serviceTicks >= Math.Ceiling(ticksPerSecond * 0.8));
        Assert.True(sawExit);
        if (invalidateRoute) { Assert.True(changedRoute); Assert.True(cargo.Metrics.RerouteCount > 0); }
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(source.Entity));
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(destination.Entity));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    public void PassingSupplyTruckDoesNotLoadButExplicitResupplyLoadsAndExits(int ticksPerSecond)
    {
        var simulation = new SimulationCoordinator(ticksPerSecond);
        var inventories = new InventoryStore();
        var cargo = new CargoTransportSystem(new LogisticsNetwork(), inventories);
        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(new BattlefieldSupplySystem(inventories));
        var depot = CreateBuilding(simulation, inventories, Vector3.Zero);
        simulation.Entities.AddComponent(depot.Entity, new SupplyDepot(depot.Inventory, new(1)));
        simulation.Entities.AddComponent(depot.Entity, new SupplyProvider(depot.Inventory, new(1), 20));
        Assert.True(inventories.Add(depot.Inventory, ResourceIds.Fuel, 300).Succeeded);
        Assert.True(inventories.Add(depot.Inventory, ResourceIds.Ammunition, 300).Succeeded);
        EntityId truck = SupplyTruckFactory.Create(simulation.Entities, inventories, new(0, 1, 14), new(1), cargo);
        var supply = simulation.Entities.GetComponent<SupplyTruck>(truck);
        var driveFuel = simulation.Entities.GetComponent<UnitFuelState>(truck);
        simulation.Entities.SetComponent(truck, new UnitFuelState(driveFuel.InventoryId, driveFuel.Capacity, 0));
        simulation.RunTicks(5, TestContext.Current.CancellationToken);
        Assert.Equal(0, inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel));
        var command = new ResupplyCommand(new(1), [truck], simulation.CurrentTick);
        simulation.SubmitCommand(command, simulation.CurrentTick.Next());
        simulation.RunTicks(2000, TestContext.Current.CancellationToken);
        Assert.Equal(1, command.AcceptedTargetCount);
        Assert.Equal(supply.FuelTarget, inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel));
        Assert.Equal(supply.AmmunitionTarget, inventories.GetQuantity(supply.InventoryId, ResourceIds.Ammunition));
        Assert.False(simulation.Entities.HasComponent<SupplyLoadingOrder>(truck));
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(depot.Entity));
        Assert.Equal(300, inventories.GetQuantity(depot.Inventory, ResourceIds.Fuel) + inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel));
    }

    [Fact]
    public void EmptyDriveTankAllowsDockedSupplyServiceButKeepsExitOccupied()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var cargo = new CargoTransportSystem(new LogisticsNetwork(), inventories);
        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(new BattlefieldSupplySystem(inventories));
        var depot = CreateBuilding(simulation, inventories, Vector3.Zero);
        simulation.Entities.AddComponent(depot.Entity, new SupplyDepot(depot.Inventory, new(1)));
        Assert.True(inventories.Add(depot.Inventory, ResourceIds.Fuel, 300).Succeeded);
        Assert.True(inventories.Add(depot.Inventory, ResourceIds.Ammunition, 300).Succeeded);
        EntityId truck = SupplyTruckFactory.Create(simulation.Entities, inventories, new(0, 1, 14), new(1), cargo);
        var transform = simulation.Entities.GetComponent<WorldTransform>(truck);
        simulation.Entities.SetComponent(truck, transform with
        {
            Position = new(0, simulation.Entities.GetComponent<GroundMovement>(truck).HeightOffset, 14),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)
        });
        simulation.Entities.SetComponent(truck, GroundMovementState.Stationary(MathF.PI / 2));
        var fuel = simulation.Entities.GetComponent<UnitFuelState>(truck);
        inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, fuel.Capacity);
        simulation.Entities.AddComponent(truck, new SupplyLoadingOrder(depot.Entity, simulation.CurrentTick));
        simulation.RunTicks(500, TestContext.Current.CancellationToken);
        var supply = simulation.Entities.GetComponent<SupplyTruck>(truck);
        Assert.Equal(supply.FuelTarget, inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel));
        Assert.Equal(0, inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel));
        Assert.True(simulation.Entities.HasComponent<VehicleDockSlot>(depot.Entity));
        Assert.Equal(VehicleDockStatus.Exiting, simulation.Entities.GetComponent<VehicleDockVisit>(truck).Status);
    }

    [Fact]
    public void CancellingPartialLoadReleasesOnlyTheRemainderAndPreservesManualMovementAndLoss()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo = new CargoTransportSystem(network, inventories);
        var distribution = new AutomatedDistributionSystem(network, inventories, cargo);
        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(cargo);
        simulation.RegisterSystem(distribution);
        var source = CreateBuilding(simulation, inventories, Vector3.Zero);
        var destination = CreateBuilding(simulation, inventories, new(120, 0, 0));
        Assert.True(inventories.Add(source.Inventory, ResourceIds.Steel, 100).Succeeded);
        var origin = network.AddNode(source.Entity, Vector3.Zero, LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoSource | LogisticsNodeCapabilities.CargoDestination);
        var target = network.AddNode(destination.Entity, new(120, 0, 0), LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoDestination);
        network.AddEdge(origin, target, LogisticsTransportMode.GroundRoad, 120, 120, 100);
        var policy = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(policy, new LogisticsStockPolicy(destination.Entity, ResourceIds.Steel,
            20, 80, 100, LogisticsStockPriority.Normal));
        var truck = CargoTruckFactory.Create(simulation.Entities, inventories, new(0, 1, 14), new(1), cargo);
        var transform = simulation.Entities.GetComponent<WorldTransform>(truck);
        simulation.Entities.SetComponent(truck, transform with
        {
            Position = new(0, simulation.Entities.GetComponent<GroundMovement>(truck).HeightOffset, 14),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)
        });
        simulation.Entities.SetComponent(truck, GroundMovementState.Stationary(MathF.PI / 2));
        var transport = simulation.Entities.GetComponent<CargoTransport>(truck);
        for (int tick = 0; tick < 500 && inventories.GetQuantity(transport.CargoInventory, ResourceIds.Steel) < 20; tick++)
            simulation.AdvanceOneTick();
        Assert.Equal(20, inventories.GetQuantity(transport.CargoInventory, ResourceIds.Steel));
        Assert.Equal(60, simulation.Entities.GetComponent<CargoTransportReservation>(truck).Quantity);
        var manualTarget = new Vector3(0, 1, 100);
        var move = new MoveEntitiesCommand(new(1), [truck], manualTarget, simulation.CurrentTick);
        simulation.SubmitCommand(move, simulation.CurrentTick.Next());
        simulation.AdvanceOneTick();
        Assert.Equal(manualTarget, simulation.Entities.GetComponent<MovementOrder>(truck).WorldTarget);
        Assert.False(simulation.Entities.HasComponent<CargoTransportOrder>(truck));
        Assert.False(simulation.Entities.HasComponent<CargoTransportReservation>(truck));
        Assert.Equal(80, inventories.GetQuantity(source.Inventory, ResourceIds.Steel));
        Assert.Equal(80, inventories.GetAvailableQuantity(source.Inventory, ResourceIds.Steel));
        simulation.Entities.DestroyEntity(truck);
        simulation.AdvanceOneTick();
        Assert.Equal(20, cargo.Metrics.LostQuantity);
        simulation.AdvanceOneTick();
        Assert.Equal(20, cargo.Metrics.LostQuantity);
    }

    [Fact]
    public void PhysicalDispatchAvoidsRepeatingTinyLoadsWhenAFullAffordableLoadExists()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo = new CargoTransportSystem(network, inventories);
        simulation.RegisterSystem(cargo);
        simulation.RegisterSystem(new AutomatedDistributionSystem(network, inventories, cargo));
        var small = CreateBuilding(simulation, inventories, Vector3.Zero);
        var full = CreateBuilding(simulation, inventories, new(300, 0, 0));
        var destination = CreateBuilding(simulation, inventories, new(120, 0, 0));
        Assert.True(inventories.Add(small.Inventory, ResourceIds.Steel, 20).Succeeded);
        Assert.True(inventories.Add(full.Inventory, ResourceIds.Steel, 100).Succeeded);
        var smallNode = network.AddNode(small.Entity, Vector3.Zero, LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoSource);
        var fullNode = network.AddNode(full.Entity, new(300, 0, 0), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoSource);
        var target = network.AddNode(destination.Entity, new(120, 0, 0), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoDestination);
        network.AddEdge(smallNode, target, LogisticsTransportMode.GroundRoad, 120, 120, 100);
        network.AddEdge(fullNode, target, LogisticsTransportMode.GroundRoad, 180, 180, 100);
        var policy = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(policy, new LogisticsStockPolicy(destination.Entity, ResourceIds.Steel,
            20, 80, 100, LogisticsStockPriority.Normal));
        var truck = CargoTruckFactory.Create(simulation.Entities, inventories, Vector3.Zero, new(1), cargo);
        simulation.AdvanceOneTick();
        var order = simulation.Entities.GetComponent<CargoTransportOrder>(truck);
        Assert.Equal(fullNode, order.Origin);
        Assert.Equal(80, order.RequestedQuantity);
        Assert.Equal(20, inventories.GetAvailableQuantity(small.Inventory, ResourceIds.Steel));
    }

    [Fact]
    public void FulfilledPhysicalMinimumDoesNotKeepBulkRefillAheadOfOlderDemand()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo = new CargoTransportSystem(network, inventories);
        simulation.RegisterSystem(new AutomatedDistributionSystem(network, inventories, cargo));
        var source = CreateBuilding(simulation, inventories, Vector3.Zero);
        var older = CreateBuilding(simulation, inventories, new(120, 0, 0));
        var urgent = CreateBuilding(simulation, inventories, new(240, 0, 0));
        Assert.True(inventories.Add(source.Inventory, ResourceIds.Steel, 200).Succeeded);
        var origin = network.AddNode(source.Entity, Vector3.Zero, LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoSource);
        var olderTarget = network.AddNode(older.Entity, new(120, 0, 0), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoDestination);
        var urgentTarget = network.AddNode(urgent.Entity, new(240, 0, 0), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoDestination);
        network.AddEdge(origin, olderTarget, LogisticsTransportMode.GroundRoad, 120, 120, 100);
        network.AddEdge(origin, urgentTarget, LogisticsTransportMode.GroundRoad, 240, 240, 100);
        var first = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(first, new LogisticsStockPolicy(older.Entity, ResourceIds.Steel, 20, 80, 100, LogisticsStockPriority.High));
        var second = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(second, new LogisticsStockPolicy(urgent.Entity, ResourceIds.Steel, 20, 100, 120, LogisticsStockPriority.Critical));
        simulation.AdvanceOneTick();
        Assert.True(inventories.Transfer(source.Inventory, urgent.Inventory, ResourceIds.Steel, 20).Succeeded);
        var truck = CargoTruckFactory.Create(simulation.Entities, inventories, Vector3.Zero, new(1), cargo);
        simulation.RunTicks(500, TestContext.Current.CancellationToken);
        Assert.Equal(olderTarget, simulation.Entities.GetComponent<CargoTransportOrder>(truck).Destination);
        Assert.Equal(LogisticsStockPriority.Critical, simulation.Entities.GetComponent<LogisticsStockPolicy>(second).Priority);
    }

    [Fact]
    public void EmptySupplySourceYieldsTheSharedBayForItsIncomingCargo()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo = new CargoTransportSystem(network, inventories);
        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(cargo);
        simulation.RegisterSystem(new BattlefieldSupplySystem(inventories));
        var depot = CreateBuilding(simulation, inventories, Vector3.Zero);
        var source = CreateBuilding(simulation, inventories, new(120, 0, 0));
        simulation.Entities.AddComponent(depot.Entity, new SupplyDepot(depot.Inventory, new(1)));
        Assert.True(inventories.Add(depot.Inventory, ResourceIds.Fuel, 20).Succeeded);
        Assert.True(inventories.Add(source.Inventory, ResourceIds.Ammunition, 100).Succeeded);
        var origin = network.AddNode(source.Entity, new(120, 0, 0), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoSource);
        var target = network.AddNode(depot.Entity, Vector3.Zero, LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoDestination);
        network.AddEdge(origin, target, LogisticsTransportMode.GroundRoad, 120, 120, 100);
        var supply = SupplyTruckFactory.Create(simulation.Entities, inventories, new(0, 1, 14), new(1), cargo);
        var incoming = CargoTruckFactory.Create(simulation.Entities, inventories, new(120, 1, 14), new(1), cargo);
        foreach (var entity in new[] { supply, incoming })
        {
            var transform = simulation.Entities.GetComponent<WorldTransform>(entity);
            simulation.Entities.SetComponent(entity, transform with
            {
                Position = new(transform.Position.X, simulation.Entities.GetComponent<GroundMovement>(entity).HeightOffset, transform.Position.Z),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2)
            });
            simulation.Entities.SetComponent(entity, GroundMovementState.Stationary(MathF.PI / 2));
            if (simulation.Entities.TryGetComponent(entity, out UnitFuelState fuel))
                simulation.Entities.SetComponent(entity, new UnitFuelState(fuel.InventoryId, fuel.Capacity, 0));
        }
        simulation.Entities.AddComponent(supply, new SupplyLoadingOrder(depot.Entity, simulation.CurrentTick));
        Assert.True(cargo.TryAssignOrder(simulation.Entities, incoming,
            new(origin, target, ResourceIds.Ammunition, 40, simulation.CurrentTick), simulation.CurrentTick));
        simulation.RunTicks(3600, TestContext.Current.CancellationToken);
        Assert.False(simulation.Entities.HasComponent<CargoTransportOrder>(incoming));
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(depot.Entity));
        var supplyState = simulation.Entities.GetComponent<SupplyTruck>(supply);
        double delivered = inventories.GetQuantity(supplyState.InventoryId, ResourceIds.Ammunition);
        if (simulation.Entities.TryGetComponent(incoming, out AmmunitionState recipient))
            delivered += inventories.GetQuantity(recipient.InventoryId, ResourceIds.Ammunition);
        Assert.Equal(40, delivered);
        Assert.Equal(60, inventories.GetQuantity(source.Inventory, ResourceIds.Ammunition));
        Assert.Equal(VehicleDockBlockReason.SourceEmpty, simulation.Entities.GetComponent<SupplyLoadingOrder>(supply).WaitReason);
        Assert.False(simulation.Entities.HasComponent<MovementOrder>(supply));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public void CriticalConstructionAndPoweredFactoriesPrecedeBulkProcessing(bool constructionInput, bool depotReserve, bool loadingDemand)
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo = new CargoTransportSystem(network, inventories);
        simulation.RegisterSystem(new AutomatedDistributionSystem(network, inventories, cargo));
        simulation.RegisterSystem(new UnitProductionSystem(DirectorateContent.CreateUnitCatalog(), inventories,
            new UnitFactory(simulation.Entities, inventories, cargo)));
        var source = CreateBuilding(simulation, inventories, Vector3.Zero);
        var processing = CreateBuilding(simulation, inventories, new(120, 0, 0));
        var factory = CreateBuilding(simulation, inventories, new(240, 0, 0));
        if (constructionInput) simulation.Entities.AddComponent(processing.Entity, new CommandFacility());
        ResourceId resource = depotReserve ? ResourceIds.Fuel : ResourceIds.Steel;
        if (depotReserve)
            simulation.Entities.AddComponent(processing.Entity, new SupplyDepot(processing.Inventory, new(1)));
        if (loadingDemand)
        {
            EntityId supply = SupplyTruckFactory.Create(simulation.Entities, inventories, new(120, 1, 14), new(1), cargo);
            simulation.Entities.AddComponent(supply, new SupplyLoadingOrder(processing.Entity, SimulationTick.Zero));
        }
        Assert.True(inventories.Add(source.Inventory, resource, 200).Succeeded);
        var origin = network.AddNode(source.Entity, Vector3.Zero, LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoSource);
        var olderTarget = network.AddNode(processing.Entity, new(120, 0, 0), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoDestination);
        var factoryTarget = network.AddNode(factory.Entity, new(240, 0, 0), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoDestination);
        network.AddEdge(origin, olderTarget, LogisticsTransportMode.GroundRoad, 120, 120, 100);
        network.AddEdge(origin, factoryTarget, LogisticsTransportMode.GroundRoad, 240, 240, 100);
        foreach (var target in new[] { processing, factory })
        {
            var policy = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(policy, new LogisticsStockPolicy(target.Entity, resource, 20, 80, 100, LogisticsStockPriority.Critical));
        }
        simulation.Entities.AddComponent(factory.Entity, new UnitProductionFacility(factory.Inventory,
            UnitProductionCapability.Vehicle, new(1), Vector3.UnitZ * 32, SimulationTick.Zero));
        simulation.Entities.AddComponent(factory.Entity, new PowerConsumer(1, allocatedPower: 1, state: PowerOperationalState.Powered));
        var request = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(request, new UnitProductionRequest(factory.Entity, UnitIds.ScoutVehicle,
            ProductionPriority.Normal, SimulationTick.Zero));
        simulation.AdvanceOneTick();
        Assert.Equal(UnitProductionStatus.NoInput, simulation.Entities.GetComponent<UnitProductionFacility>(factory.Entity).Status);
        var truck = CargoTruckFactory.Create(simulation.Entities, inventories, Vector3.Zero, new(1), cargo);
        simulation.RunTicks(100, TestContext.Current.CancellationToken);
        Assert.Equal(constructionInput || loadingDemand ? olderTarget : factoryTarget, simulation.Entities.GetComponent<CargoTransportOrder>(truck).Destination);
        foreach (EntityId policy in simulation.Entities.Query<LogisticsStockPolicy>())
            Assert.Equal(LogisticsStockPriority.Critical, simulation.Entities.GetComponent<LogisticsStockPolicy>(policy).Priority);
        Assert.Equal(200, inventories.GetQuantity(source.Inventory, resource));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    public void CoarseNavigationArrivalsUseSeparateWaitingPointsAndCompleteEveryDelivery(int ticksPerSecond)
    {
        var simulation = new SimulationCoordinator(ticksPerSecond);
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var settings = new WorldGridSettings { ChunkSizeMeters = 512, HeightSamplesPerSide = 33 };
        var terrain = new TerrainWorld(settings, [new TerrainChunk(new(0, 0), new TerrainHeightfield(33, 512, new float[33 * 33]))]);
        var source = CreateBuilding(simulation, inventories, new(192, 0, 224));
        var destination = CreateBuilding(simulation, inventories, new(320, 0, 224));
        var spatial = new SpatialGridIndex();
        var obstacles = new List<AxisAlignedBounds>();
        foreach (EntityId building in new[] { source.Entity, destination.Entity })
        {
            var presence = new SpatialPresence(new(10, 5, 10), new(1, 0, SpatialMobility.Static));
            simulation.Entities.AddComponent(building, presence);
            var entry = presence.CreateEntry(building, simulation.Entities.GetComponent<WorldTransform>(building));
            spatial.Upsert(entry);
            obstacles.Add(entry.Bounds);
        }
        var navigation = new HierarchicalNavigationSystem(new HierarchicalPathfinder(NavigationWorld.Build(terrain, obstacles,
            new NavigationGridSettings { CellSizeMeters = 32 }, new NavigationSectorSettings { SectorSizeCells = 4 })));
        var docks = new VehicleDockingService(terrain, spatial, navigation);
        var cargo = new CargoTransportSystem(network, inventories, docking: docks);
        simulation.RegisterSystem(new SpatialIndexSystem(new SpatialIndexSynchronizer(spatial)));
        simulation.RegisterSystem(navigation);
        simulation.RegisterSystem(new GroundMovementSystem(terrain, spatial));
        simulation.RegisterSystem(cargo);
        Assert.True(inventories.Add(source.Inventory, ResourceIds.Steel, 180).Succeeded);
        var origin = network.AddNode(source.Entity, new(192, 0, 224), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoSource);
        var target = network.AddNode(destination.Entity, new(320, 0, 224), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoDestination);
        network.AddEdge(origin, target, LogisticsTransportMode.GroundRoad, 128, 128, 100);
        var profile = DirectorateContent.CreateUnitCatalog()[UnitIds.CargoTruck];
        var trucks = new List<EntityId>();
        for (int index = 0; index < 4; index++)
        {
            var truck = CargoTruckFactory.Create(simulation.Entities, inventories, new(64, profile.Movement.HeightOffset, 176 + index * 20),
                new(1), cargo, new CargoTruckDefinition(profile.Key, profile.CargoCapacity, profile.Movement, profile.VisualScale, profile.VisualId));
            Assert.True(cargo.TryAssignOrder(simulation.Entities, truck, new(origin, target, ResourceIds.Steel, 40, SimulationTick.Zero), SimulationTick.Zero));
            trucks.Add(truck);
        }
        simulation.AdvanceOneTick();
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(source.Entity));
        for (int first = 0; first < trucks.Count; first++)
            for (int second = first + 1; second < trucks.Count; second++)
            {
                var a = simulation.Entities.GetComponent<VehicleDockVisit>(trucks[first]).Pose.ApproachStart;
                var b = simulation.Entities.GetComponent<VehicleDockVisit>(trucks[second]).Pose.ApproachStart;
                Assert.True(a.HasValue && b.HasValue);
                Assert.True(Vector3.Distance(a.GetValueOrDefault(), b.GetValueOrDefault()) >= profile.Movement.SeparationRadius * 2,
                    $"Waiting points overlap: {trucks[first]}:{a} / {trucks[second]}:{b}; separation={profile.Movement.SeparationRadius * 2}; " +
                    string.Join("; ", trucks.Select(id => $"{id}:" +
                        (simulation.Entities.TryGetComponent(id, out VehicleQueueEntry queued) ? queued.ToString() : "unregistered") +
                        (simulation.Entities.TryGetComponent(id, out VehicleQueueHolding hold) ? hold.ToString() : ""))));
            }
        for (int tick = 1; tick < ticksPerSecond * 180 && trucks.Any(entity => simulation.Entities.HasComponent<CargoTransportOrder>(entity)); tick++)
            simulation.AdvanceOneTick();
        foreach (EntityId truck in trucks)
            Assert.False(simulation.Entities.HasComponent<CargoTransportOrder>(truck), VisitDescription(simulation, truck));
        Assert.Equal(160, inventories.GetQuantity(destination.Inventory, ResourceIds.Steel));
        Assert.Equal(20, inventories.GetQuantity(source.Inventory, ResourceIds.Steel));
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(source.Entity));
        Assert.False(simulation.Entities.HasComponent<VehicleDockSlot>(destination.Entity));
    }
    [Theory]
    [InlineData(60, 60)]
    [InlineData(0, 80)]
    public void DockedSupplyKeepsCargoSeparateFromOptionalDriveTopupsButAllowsRequiredRecovery(
        double initialDriveFuel, double expectedDriveFuel)
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var cargo = new CargoTransportSystem(new LogisticsNetwork(), inventories);
        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(new BattlefieldSupplySystem(inventories));
        var depot = CreateBuilding(simulation, inventories, Vector3.Zero);
        simulation.Entities.AddComponent(depot.Entity, new SupplyDepot(depot.Inventory, new(1)));
        Assert.True(inventories.Add(depot.Inventory, ResourceIds.Fuel, 300).Succeeded);
        Assert.True(inventories.Add(depot.Inventory, ResourceIds.Ammunition, 300).Succeeded);
        EntityId truck = SupplyTruckFactory.Create(simulation.Entities, inventories, new(0, 1, 14), new(1), cargo);
        var transform = simulation.Entities.GetComponent<WorldTransform>(truck);
        simulation.Entities.SetComponent(truck, transform with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) });
        simulation.Entities.SetComponent(truck, GroundMovementState.Stationary(MathF.PI / 2));
        var fuel = simulation.Entities.GetComponent<UnitFuelState>(truck);
        Assert.True(inventories.Remove(fuel.InventoryId, ResourceIds.Fuel, fuel.Capacity - initialDriveFuel).Succeeded);
        simulation.Entities.AddComponent(truck, new SupplyLoadingOrder(depot.Entity, simulation.CurrentTick));
        for (int tick = 0; tick < 200; tick++)
        {
            simulation.AdvanceOneTick();
            if (simulation.Entities.TryGetComponent(truck, out VehicleDockVisit visit) && visit.LastTransferTick > 0) break;
        }
        var supply = simulation.Entities.GetComponent<SupplyTruck>(truck);
        Assert.True(simulation.Entities.GetComponent<VehicleDockVisit>(truck).LastTransferTick > 0);
        InventoryId recoveryInventory = inventories.CreateInventory(new InventorySpecification(100));
        Assert.True(inventories.Add(recoveryInventory, ResourceIds.Fuel, 100).Succeeded);
        EntityId recoveryProvider = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(recoveryProvider, simulation.Entities.GetComponent<WorldTransform>(truck));
        simulation.Entities.AddComponent(recoveryProvider, new SupplyProvider(recoveryInventory, new(1), 16));
        simulation.AdvanceOneTick();
        Assert.Equal(20, inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel), 6);
        Assert.Equal(expectedDriveFuel, inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel), 6);
        Assert.Equal(400 + initialDriveFuel, inventories.GetQuantity(depot.Inventory, ResourceIds.Fuel) +
            inventories.GetQuantity(recoveryInventory, ResourceIds.Fuel) +
            inventories.GetQuantity(supply.InventoryId, ResourceIds.Fuel) + inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel), 6);
    }
    [Theory]
    [InlineData(LogisticsStockPriority.Critical)]
    [InlineData(LogisticsStockPriority.High)]
    public void PhysicalFactoryPoliciesKeepTheirDeclaredTotalStockMeaningWhileInputsAreReserved(LogisticsStockPriority priority)
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var cargo = new CargoTransportSystem(network, inventories);
        simulation.RegisterSystem(new AutomatedDistributionSystem(network, inventories, cargo));
        var source = CreateBuilding(simulation, inventories, Vector3.Zero);
        var factory = CreateBuilding(simulation, inventories, new(120, 0, 0));
        Assert.True(inventories.Add(source.Inventory, ResourceIds.Fuel, 200).Succeeded);
        Assert.True(inventories.Add(factory.Inventory, ResourceIds.Fuel, 20).Succeeded);
        Assert.True(inventories.Reserve(factory.Inventory, ResourceIds.Fuel, 20).Succeeded);
        simulation.Entities.AddComponent(factory.Entity, new UnitProductionFacility(factory.Inventory,
            UnitProductionCapability.Vehicle, new(1), Vector3.UnitZ * 32, SimulationTick.Zero));
        var origin = network.AddNode(source.Entity, Vector3.Zero, LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoSource);
        var target = network.AddNode(factory.Entity, new(120, 0, 0), LogisticsNodeKind.StorageDepot, LogisticsNodeCapabilities.CargoDestination);
        network.AddEdge(origin, target, LogisticsTransportMode.GroundRoad, 120, 120, 100);
        var policy = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(policy, new LogisticsStockPolicy(factory.Entity, ResourceIds.Fuel, 20, 60, 100, priority));
        EntityId truck = CargoTruckFactory.Create(simulation.Entities, inventories, Vector3.Zero, new(1), cargo);
        simulation.AdvanceOneTick();
        Assert.False(simulation.Entities.HasComponent<CargoTransportOrder>(truck));
        Assert.Equal(200, inventories.GetQuantity(source.Inventory, ResourceIds.Fuel));
        Assert.Equal(20, inventories.GetQuantity(factory.Inventory, ResourceIds.Fuel));
        Assert.Equal(20, inventories.GetReservedQuantity(factory.Inventory, ResourceIds.Fuel));
    }
    private static string VisitDescription(SimulationCoordinator simulation, EntityId truck) =>
        simulation.Entities.TryGetComponent(truck, out VehicleDockVisit visit) ? visit.ToString() : "none";

    private static (EntityId Entity, InventoryId Inventory) CreateBuilding(SimulationCoordinator simulation, InventoryStore inventories, Vector3 position)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        InventoryId inventory = inventories.CreateInventory(new InventorySpecification(1000));
        simulation.Entities.AddComponent(entity, new WorldTransform(position, Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(entity, new CompletedBuilding(BuildingIds.StorageDepot, new(1), SimulationTick.Zero));
        simulation.Entities.AddComponent(entity, new BuildingLoadingDock(new(0, 0, 14), Vector3.UnitX, 18));
        simulation.Entities.AddComponent(entity, new InventoryStorage(inventory));
        return (entity, inventory);
    }
}
