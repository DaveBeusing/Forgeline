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
        var transport = new CargoTransportSystem(network, inventories);
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
        simulation.RegisterSystem(new HierarchicalNavigationSystem(new HierarchicalPathfinder(navigation)));
        simulation.RegisterSystem(new GroundMovementSystem(terrain));
        simulation.RegisterSystem(transport);
        simulation.RegisterSystem(new BattlefieldSupplySystem(inventories));
        var intelligence = new FactionIntelligenceStore(new() { CellSizeMeters = 32 });
        simulation.RegisterSystem(new BattlefieldIntelligenceSystem(intelligence));
        var snapshots = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(snapshots, intelligence, new(1), terrain.WorldBounds, runtimeAssets));
        var definition = DirectorateContent.CreateUnitCatalog()[UnitIds.CargoTruck];
        Vector3 start = Ground(new(545,0,520));
        start.Y += definition.Movement.HeightOffset;
        EntityId truck = CargoTruckFactory.Create(simulation.Entities, inventories, start, new(1), transport,
            new CargoTruckDefinition(definition.Key, definition.CargoCapacity, definition.Movement, definition.VisualScale, definition.VisualId));
        simulation.Entities.AddComponent(truck, new UnitIdentity(UnitIds.CargoTruck, new(1)));
        var supplyDefinition = DirectorateContent.CreateUnitCatalog()[UnitIds.SupplyTruck];
        Vector3 supplyStart = Ground(new(750,0,538));
        supplyStart.Y += supplyDefinition.Movement.HeightOffset;
        EntityId supplyTruck = CargoTruckFactory.Create(simulation.Entities,inventories,supplyStart,new(1),transport,
            new CargoTruckDefinition(supplyDefinition.Key,supplyDefinition.CargoCapacity,supplyDefinition.Movement,supplyDefinition.VisualScale,supplyDefinition.VisualId));
        simulation.Entities.AddComponent(supplyTruck,new UnitIdentity(UnitIds.SupplyTruck,new(1)));
        InventoryId supplyInventory = simulation.Entities.GetComponent<CargoTransport>(supplyTruck).CargoInventory;
        simulation.Entities.AddComponent(supplyTruck,new SupplyTruck(supplyInventory,new(1),supplyDefinition.SupplyLoadRangeMeters,supplyDefinition.SupplyRangeMeters,supplyDefinition.SupplyFuelTarget,supplyDefinition.SupplyAmmunitionTarget));
        EntityId sensor = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(sensor, new WorldTransform(Ground(new(675,0,560)), Quaternion.Identity, Vector3.One));
        simulation.Entities.AddComponent(sensor, new VisualSensorState(new(1), rangeMeters: 500));
        if (!transport.TryAssignOrder(simulation.Entities, truck,
                new CargoTransportOrder(source,destination,ResourceIds.FerrousOre,50,SimulationTick.Zero),SimulationTick.Zero))
            throw new InvalidOperationException("Traffic order was rejected.");
        CargoTransportRuntimeState state = CargoTransportRuntimeState.Idle;
        for (int tick = 0; tick < 4000; tick++)
        {
            simulation.AdvanceOneTick();
            state = simulation.Entities.GetComponent<CargoTransportRuntimeState>(truck);
            if (!sustained && state.Lifecycle == requested &&
                (requested != CargoTransportLifecycleState.ToDestination || simulation.Entities.GetComponent<GroundMovementState>(truck).Velocity.LengthSquared() > .01f)) break;
            if (sustained)
            {
                if (tick == 1023) break;
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
        if (!snapshots.TryReadLatest(out var snapshot)) throw new InvalidOperationException("Traffic snapshot was not published.");
        double conserved = inventories.GetQuantity(sourceInventory,ResourceIds.FerrousOre) +
            inventories.GetQuantity(destinationInventory,ResourceIds.FerrousOre) +
            inventories.GetQuantity(simulation.Entities.GetComponent<CargoTransport>(truck).CargoInventory,ResourceIds.FerrousOre);
        if (Math.Abs(conserved-100)>1e-6) throw new InvalidOperationException("Traffic did not conserve inventory.");
        if (sustained && transport.Metrics.CompletedOrderCount < 2)
            throw new InvalidOperationException("Repeated traffic did not complete two physical deliveries.");
        if (inventories.GetQuantity(supplyInventory,ResourceIds.Fuel) <= 0)
            throw new InvalidOperationException("Supply truck did not physically load from its depot.");
        Vector3 target = Ground(new(675,0,560));
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
        LogisticsNodeId Building(BuildingId id, Vector3 position, InventoryId inventory, LogisticsNodeKind nodeKind)
        {
            var footprint = buildings[id].Footprint;
            Vector3 scale = new(footprint.Width,footprint.Height,footprint.Depth);
            Vector3 ground = Ground(position);
            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity,new WorldTransform(ground+new Vector3(0,scale.Y*.5f,0),Quaternion.Identity,scale));
            simulation.Entities.AddComponent(entity,new VisualIdentity(buildings[id].VisualId));
            simulation.Entities.AddComponent(entity,new CompletedBuilding(id,new(1),SimulationTick.Zero));
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
