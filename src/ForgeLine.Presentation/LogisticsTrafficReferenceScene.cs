using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class LogisticsTrafficReferenceScene
{
    private static readonly JsonSerializerOptions FingerprintOptions = new() { IncludeFields = true };
    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, RuntimeAssetCatalog? runtimeAssets = null)
    {
        bool sustained = kind == VisualReferenceSceneKind.LogisticsSustainedProduction;
        CargoTransportLifecycleState requested = kind switch
        {
            VisualReferenceSceneKind.LogisticsLoadingProduction => CargoTransportLifecycleState.Loading,
            VisualReferenceSceneKind.LogisticsTransitProduction => CargoTransportLifecycleState.ToDestination,
            VisualReferenceSceneKind.LogisticsUnloadingProduction => CargoTransportLifecycleState.Unloading,
            VisualReferenceSceneKind.LogisticsSustainedProduction => CargoTransportLifecycleState.Idle,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var simulation = new SimulationCoordinator(seed: VisualReferenceScene.Seed);
        var inventories = new InventoryStore();
        var network = new LogisticsNetwork();
        var buildings = DirectorateContent.CreateBuildingCatalog();
        var obstacles = new List<AxisAlignedBounds>();
        InventoryId sourceInventory = inventories.CreateInventory(new InventorySpecification(5000));
        InventoryId destinationInventory = inventories.CreateInventory(new InventorySpecification(2500));
        InventoryId hubInventory = inventories.CreateInventory(new InventorySpecification(3000));
        if (!inventories.Add(sourceInventory, ResourceIds.FerrousOre, 100).Succeeded)
            throw new InvalidOperationException("Traffic source inventory could not be filled.");
        LogisticsNodeId source = Building(BuildingIds.StorageDepot, new(600,0,520), sourceInventory, LogisticsNodeKind.StorageDepot);
        LogisticsNodeId hub = Building(BuildingIds.LogisticsHub, new(650,0,600), hubInventory, LogisticsNodeKind.LogisticsHub);
        LogisticsNodeId destination = Building(BuildingIds.SupplyDepot, new(750,0,520), destinationInventory, LogisticsNodeKind.SupplyDepot);
        network.AddEdge(source, hub, LogisticsTransportMode.GroundRoad, distanceMeters: 94.34, baseCost: 94.34, capacityPerSecond: 100);
        network.AddEdge(hub, destination, LogisticsTransportMode.GroundRoad, distanceMeters: 128.06, baseCost: 128.06, capacityPerSecond: 100);
        if (!inventories.Add(destinationInventory, ResourceIds.Fuel, 200).Succeeded || !inventories.Add(destinationInventory, ResourceIds.Ammunition, 200).Succeeded)
            throw new InvalidOperationException("Supply fixture inventory could not be filled.");
        var navigation = NavigationWorld.Build(terrain, obstacles,
            gridSettings: new NavigationGridSettings { CellSizeMeters = 4 },
            sectorSettings: new NavigationSectorSettings { SectorSizeCells = 8 });
        var routing = new HierarchicalNavigationSystem(new HierarchicalPathfinder(navigation));
        var spatial = new SpatialGridIndex();
        var docks = new VehicleDockingService(terrain, spatial, routing);
        var transport = new CargoTransportSystem(network, inventories, navigation: routing, docking: docks);
        simulation.RegisterSystem(routing);
        simulation.RegisterSystem(new SpatialIndexSystem(new SpatialIndexSynchronizer(spatial)));
        simulation.RegisterSystem(new GroundMovementSystem(terrain, spatial));
        simulation.RegisterSystem(transport);
        simulation.RegisterSystem(new BattlefieldSupplySystem(inventories, docks));
        var intelligence = new FactionIntelligenceStore(new() { CellSizeMeters = 32 });
        simulation.RegisterSystem(new BattlefieldIntelligenceSystem(intelligence));
        var snapshots = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(snapshots, intelligence, new(1), terrain.WorldBounds, runtimeAssets));
        var definition = DirectorateContent.CreateUnitCatalog()[UnitIds.CargoTruck];
        Vector3 start = ServicePoint(source, definition.VisualScale);
        start.Y += definition.Movement.HeightOffset;
        EntityId truck = CargoTruckFactory.Create(simulation.Entities, inventories, start, new(1), transport,
            new CargoTruckDefinition(definition.Key, definition.CargoCapacity, definition.Movement, definition.VisualScale, definition.VisualId));
        simulation.Entities.AddComponent(truck, new UnitIdentity(UnitIds.CargoTruck, new(1)));
        Align(truck);
        var supplyDefinition = DirectorateContent.CreateUnitCatalog()[UnitIds.SupplyTruck];
        Vector3 supplyStart = ServicePoint(destination, supplyDefinition.VisualScale);
        supplyStart.Y += supplyDefinition.Movement.HeightOffset;
        EntityId supplyTruck = CargoTruckFactory.Create(simulation.Entities,inventories,supplyStart,new(1),transport,
            new CargoTruckDefinition(supplyDefinition.Key,supplyDefinition.CargoCapacity,supplyDefinition.Movement,supplyDefinition.VisualScale,supplyDefinition.VisualId));
        simulation.Entities.AddComponent(supplyTruck,new UnitIdentity(UnitIds.SupplyTruck,new(1)));
        InventoryId supplyInventory = simulation.Entities.GetComponent<CargoTransport>(supplyTruck).CargoInventory;
        simulation.Entities.AddComponent(supplyTruck,new SupplyTruck(supplyInventory,new(1),supplyDefinition.SupplyLoadRangeMeters,supplyDefinition.SupplyRangeMeters,supplyDefinition.SupplyFuelTarget,supplyDefinition.SupplyAmmunitionTarget));
        Align(supplyTruck);
        if (!network.TryGetNode(destination, out var supplyNode)) throw new InvalidOperationException("Missing supply source.");
        simulation.Entities.AddComponent(supplyTruck, new SupplyLoadingOrder(supplyNode.Entity, SimulationTick.Zero));
        EntityId sensor = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(sensor, new WorldTransform(Ground(new(675,0,560)), Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(sensor, new VisualSensorState(new(1), rangeMeters: 500));
        if (!transport.TryAssignOrder(simulation.Entities, truck,
                new CargoTransportOrder(source,destination,ResourceIds.FerrousOre,50,SimulationTick.Zero),SimulationTick.Zero))
            throw new InvalidOperationException("Traffic order was rejected.");
        EntityId waitingTruck = EntityId.Invalid;
        if (kind == VisualReferenceSceneKind.LogisticsLoadingProduction)
        {
            Vector3 waitingPosition = ServicePoint(source, definition.VisualScale) - Vector3.UnitX * 30;
            waitingPosition.Y += definition.Movement.HeightOffset;
            waitingTruck = CargoTruckFactory.Create(simulation.Entities, inventories, waitingPosition, new(1), transport,
                new CargoTruckDefinition(definition.Key, definition.CargoCapacity, definition.Movement, definition.VisualScale, definition.VisualId));
            simulation.Entities.AddComponent(waitingTruck, new UnitIdentity(UnitIds.CargoTruck, new(1)));
            Align(waitingTruck);
            if (!transport.TryAssignOrder(simulation.Entities, waitingTruck,
                    new CargoTransportOrder(source, destination, ResourceIds.FerrousOre, 20, SimulationTick.Zero), SimulationTick.Zero))
                throw new InvalidOperationException("Waiting traffic order was rejected.");
        }
        CargoTransportRuntimeState state = CargoTransportRuntimeState.Idle;
        for (int tick = 0; tick < 4000; tick++)
        {
            simulation.AdvanceOneTick();
            state = simulation.Entities.GetComponent<CargoTransportRuntimeState>(truck);
            bool loadingService = simulation.Entities.TryGetComponent(truck, out VehicleDockVisit cargoVisit) &&
                cargoVisit.ServiceStartedTick > 0 && cargoVisit.ServiceProgress > 0 &&
                inventories.GetQuantity(supplyInventory, ResourceIds.Fuel) > 0;
            if (!sustained && state.Lifecycle == requested &&
                (requested != CargoTransportLifecycleState.Loading || loadingService) &&
                (requested != CargoTransportLifecycleState.ToDestination || simulation.Entities.GetComponent<GroundMovementState>(truck).Velocity.LengthSquared() > .01f)) break;
            if (sustained)
            {
                if (tick >= 1023 && transport.Metrics.CompletedOrderCount >= 2 && state.Lifecycle == CargoTransportLifecycleState.Idle) break;
                if (state.Lifecycle == CargoTransportLifecycleState.Idle)
                {
                    bool forward = inventories.GetQuantity(sourceInventory,ResourceIds.FerrousOre) >= 50;
                    if (!transport.TryAssignOrder(simulation.Entities,truck,
                        new CargoTransportOrder(forward ? source : destination,forward ? destination : source,ResourceIds.FerrousOre,50,simulation.Clock.CurrentTick),simulation.Clock.CurrentTick))
                        throw new InvalidOperationException("Repeated traffic order was rejected.");
                }
            }
            if (state.Lifecycle == CargoTransportLifecycleState.Failed)
                throw new InvalidOperationException($"Traffic failed: {state.FailureReason}.");
        }
        if (!sustained && state.Lifecycle != requested)
            throw new InvalidOperationException($"Traffic did not reach {requested}: {state.Lifecycle}/{state.WaitReason}.");
        if (waitingTruck.IsValid &&
            (!simulation.Entities.TryGetComponent(waitingTruck, out VehicleDockVisit waiting) || waiting.Status != VehicleDockStatus.Busy ||
             simulation.Entities.GetComponent<GroundMovementState>(waitingTruck).Velocity.LengthSquared() > 0.05f * 0.05f ||
             simulation.Entities.GetComponent<VehicleDockVisit>(truck).Pose.Contains(simulation.Entities.GetComponent<WorldTransform>(waitingTruck).Position, waiting.Pose.HalfLength)))
            throw new InvalidOperationException("The second truck must wait outside the exclusive lane.");
        if (!snapshots.TryReadLatest(out var snapshot)) throw new InvalidOperationException("Traffic snapshot was not published.");
        double conserved = inventories.GetQuantity(sourceInventory,ResourceIds.FerrousOre) +
            inventories.GetQuantity(destinationInventory,ResourceIds.FerrousOre) +
            inventories.GetQuantity(simulation.Entities.GetComponent<CargoTransport>(truck).CargoInventory,ResourceIds.FerrousOre);
        if (waitingTruck.IsValid) conserved += inventories.GetQuantity(
            simulation.Entities.GetComponent<CargoTransport>(waitingTruck).CargoInventory, ResourceIds.FerrousOre);
        if (Math.Abs(conserved-100)>1e-6) throw new InvalidOperationException("Traffic did not conserve inventory.");
        if (sustained && transport.Metrics.CompletedOrderCount < 2)
            throw new InvalidOperationException($"Repeated traffic completed {transport.Metrics.CompletedOrderCount} deliveries: {state}; pose={simulation.Entities.GetComponent<WorldTransform>(truck)}; dock={(simulation.Entities.TryGetComponent(truck, out VehicleDockVisit unfinished) ? unfinished.ToString() : "none")}.");
        if (inventories.GetQuantity(supplyInventory,ResourceIds.Fuel) <= 0)
            throw new InvalidOperationException("Supply truck did not physically load from its depot.");
        if (kind == VisualReferenceSceneKind.LogisticsTransitProduction &&
            (!simulation.Entities.HasComponent<SupplyLoadingOrder>(supplyTruck) ||
             !simulation.Entities.TryGetComponent(supplyTruck, out VehicleDockVisit supplyVisit) ||
             supplyVisit.Status is not (VehicleDockStatus.Docked or VehicleDockStatus.Servicing)))
            throw new InvalidOperationException("The transit inspection requires concurrent physical supply loading.");
        Vector3 target = kind switch
        {
            VisualReferenceSceneKind.LogisticsLoadingProduction or VisualReferenceSceneKind.LogisticsUnloadingProduction =>
                Ground(simulation.Entities.GetComponent<WorldTransform>(truck).Position),
            VisualReferenceSceneKind.LogisticsTransitProduction =>
                Ground(simulation.Entities.GetComponent<WorldTransform>(supplyTruck).Position),
            _ => Ground(new(675,0,560))
        };
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = VisualReferenceScene.FixtureVersion, kind, target, seed = VisualReferenceScene.Seed,
            tick = snapshot.Tick.Value, instances = snapshot.Instances.ToArray(), state, conserved,
            completedOrders = transport.Metrics.CompletedOrderCount, supplyFuel = inventories.GetQuantity(supplyInventory,ResourceIds.Fuel),
            sourceQuantity = inventories.GetQuantity(sourceInventory,ResourceIds.FerrousOre),
            destinationQuantity = inventories.GetQuantity(destinationInventory,ResourceIds.FerrousOre)
        }, FingerprintOptions)));
        return new(kind,target,snapshot,default,fingerprint);

        Vector3 Ground(Vector3 point)
        {
            if (!terrain.TrySampleHeight(point.X,point.Z,out float height))
                throw new InvalidOperationException("Traffic point lies outside terrain.");
            return point with { Y = height };
        }
        Vector3 ServicePoint(LogisticsNodeId nodeId, Vector3 scale)
        {
            if (!network.TryGetNode(nodeId, out var node) ||
                !VehicleDockPose.TryCreate(simulation.Entities.GetComponent<BuildingLoadingDock>(node.Entity),
                    simulation.Entities.GetComponent<WorldTransform>(node.Entity),
                    WorldTransform.Identity with { Scale = scale }, out var pose))
                throw new InvalidOperationException("Traffic requires authored loading geometry.");
            return Ground(pose.Service);
        }
        void Align(EntityId entity)
        {
            var transform = simulation.Entities.GetComponent<WorldTransform>(entity);
            simulation.Entities.SetComponent(entity, transform with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) });
            simulation.Entities.SetComponent(entity, GroundMovementState.Stationary(MathF.PI / 2));
        }
        LogisticsNodeId Building(BuildingId id, Vector3 position, InventoryId inventory, LogisticsNodeKind nodeKind)
        {
            var footprint = buildings[id].Footprint;
            Vector3 scale = new(footprint.Width,footprint.Height,footprint.Depth);
            Vector3 ground = Ground(position);
            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity,new WorldTransform(ground+new Vector3(0,scale.Y*.5f,0),Quaternion.Identity,scale));
            simulation.Entities.AddComponent(entity,new VisualIdentity(buildings[id].VisualId));
            simulation.Entities.AddComponent(entity,new CompletedBuilding(id,new(1),SimulationTick.Zero));
            simulation.Entities.AddComponent(entity, buildings[id].LoadingDock ?? throw new InvalidOperationException("Traffic building has no dock."));
            simulation.Entities.AddComponent(entity,new ControllableEntity(new(1),ControllableEntityCategory.Building));
            simulation.Entities.AddComponent(entity,new SpatialPresence(scale*.5f,new SpatialEntryMetadata(1,0,SpatialMobility.Static)));
            simulation.Entities.AddComponent(entity,new InventoryStorage(inventory));
            if (id == BuildingIds.StorageDepot) simulation.Entities.AddComponent(entity,new StorageDepot(inventory,new FactionId(1)));
            if (id == BuildingIds.SupplyDepot) simulation.Entities.AddComponent(entity,new SupplyDepot(inventory,new PlayerId(1)));
            if (id == BuildingIds.LogisticsHub) simulation.Entities.AddComponent(entity,new LogisticsHub(inventory,new FactionId(1)));
            obstacles.Add(new(ground-new Vector3(scale.X*.5f,0,scale.Z*.5f),ground+new Vector3(scale.X*.5f,scale.Y,scale.Z*.5f)));
            return network.AddNode(entity,ground,nodeKind,LogisticsNodeCapabilities.CargoSource|LogisticsNodeCapabilities.CargoDestination|LogisticsNodeCapabilities.Storage|LogisticsNodeCapabilities.Distribution);
        }
    }
}
