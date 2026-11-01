using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

// Controlled authored building terminals and power allocations isolate flow from economy startup.
// Conveyor geometry, supports, inventories, ownership and payment all come from construction commands.
internal sealed class ConveyorTestFixture
{
    private readonly Dictionary<PlayerId, EntityId> _payers = new();
    private readonly HashSet<InventoryId> _inventories = new();

    public ConveyorTestFixture(ulong seed = 17, int ticksPerSecond = 20, bool simulateTrucks = false,
        Func<float, float, float>? terrainHeight = null)
    {
        Simulation = new SimulationCoordinator(ticksPerSecond, seed);
        Inventories = new InventoryStore();
        Network = new LogisticsNetwork();
        Cargo = new CargoTransportSystem(Network, Inventories);
        Distribution = new AutomatedDistributionSystem(Network, Inventories, Cargo,
            retryDelayTicks: 5, fairnessAgingTicks: 20);
        Terrain = CreateTerrain(terrainHeight);
        Spatial = new SpatialGridIndex();
        Construction = new ConveyorConstructionService(Simulation, Terrain, Spatial, Inventories, CreateBattlefield());
        Recipes = InitialProductionRecipes.CreateCatalog();
        Units = DirectorateContent.CreateUnitCatalog();
        Conveyor = new ConveyorSimulationSystem(Inventories, Distribution,
            Recipes, Units);
        Production = new ProductionSystem(Recipes, Inventories);
        Simulation.RegisterSystem(Conveyor);
        Simulation.RegisterSystem(Production);
        Simulation.RegisterSystem(Distribution);
        if (simulateTrucks)
        {
            var navigation = NavigationWorld.Build(Terrain,
                gridSettings: new NavigationGridSettings { CellSizeMeters = 4 },
                sectorSettings: new NavigationSectorSettings { SectorSizeCells = 4 });
            Simulation.RegisterSystem(new HierarchicalNavigationSystem(new HierarchicalPathfinder(navigation)));
            Simulation.RegisterSystem(new GroundMovementSystem(Terrain));
            Simulation.RegisterSystem(Cargo);
        }

        EntityId match = Entities.CreateEntity();
        Entities.AddComponent(match, MatchState.Running);
        Payer = AddPayer(Player, new Vector3(24, 0, 24));
        _ = AddPayer(OtherPlayer, new Vector3(64, 0, 24));
    }

    public PlayerId Player { get; } = new(1);
    public PlayerId OtherPlayer { get; } = new(2);
    public SimulationCoordinator Simulation { get; }
    public EntityRegistry Entities => Simulation.Entities;
    public InventoryStore Inventories { get; }
    public TerrainWorld Terrain { get; }
    public SpatialGridIndex Spatial { get; }
    public LogisticsNetwork Network { get; }
    public CargoTransportSystem Cargo { get; }
    public AutomatedDistributionSystem Distribution { get; }
    public ConveyorConstructionService Construction { get; }
    public ConveyorSimulationSystem Conveyor { get; }
    public ProductionRecipeCatalog Recipes { get; }
    public UnitDefinitionCatalog Units { get; }
    public ProductionSystem Production { get; }
    public EntityId Payer { get; }
    public InventoryId PayerInventory => Inventory(Payer);
    public double TickSeconds => Simulation.Clock.TickDuration.TotalSeconds;
    public EntityId[] Pieces => Find<ConveyorPiece>();
    public EntityId[] Carriers => Find<ConveyorFlowState>();

    public EntityId[] Find<T>() where T : struct
    {
        var entities = new List<EntityId>();
        foreach (EntityId entity in Entities.Query<T>(QueryIterationOrder.StableByEntityIndex)) entities.Add(entity);
        return entities.ToArray();
    }

    public EntityId AddStorage(Vector3 origin, PlayerId? owner = null, double capacity = 1_000,
        BuildingOrientation orientation = BuildingOrientation.North) =>
        AddBuilding(BuildingIds.StorageDepot, origin, owner, capacity, orientation);

    public EntityId AddBuilding(BuildingId building, Vector3 origin, PlayerId? owner = null, double capacity = 1_000,
        BuildingOrientation orientation = BuildingOrientation.North)
    {
        PlayerId player = owner ?? Player;
        InventoryId inventory = CreateInventory(capacity);
        EntityId entity = Entities.CreateEntity();
        Vector3 dimensions = ConveyorGeometry.BuildingPortDimensions(building);
        Assert.NotEqual(Vector3.Zero, dimensions);
        Quaternion rotation = BuildingFootprint.GetRotation(orientation);
        var transform = new WorldTransform(origin + Vector3.UnitY * (dimensions.Y / 2), rotation, dimensions);
        Entities.AddComponent(entity, transform);
        Entities.AddComponent(entity, new CompletedBuilding(building, player, Simulation.CurrentTick));
        Entities.AddComponent(entity, new ControllableEntity(player, ControllableEntityCategory.Logistics));
        Entities.AddComponent(entity, new InventoryStorage(inventory));
        if (building == BuildingIds.StorageDepot)
            Entities.AddComponent(entity, new StorageDepot(inventory, new FactionId(checked((uint)player.Value))));
        Entities.AddComponent(entity, HealthState.Full(1_000));
        Entities.AddComponent(entity, new PowerConsumer(2, allocatedPower: 2, state: PowerOperationalState.Powered));
        Vector3 half = orientation is BuildingOrientation.East or BuildingOrientation.West
            ? new Vector3(dimensions.Z, dimensions.Y, dimensions.X) / 2
            : dimensions / 2;
        Spatial.Upsert(new SpatialEntry(entity, new AxisAlignedBounds(transform.Position - half, transform.Position + half),
            new SpatialEntryMetadata(player.Value, (ulong)ControllableEntityCategory.Logistics, SpatialMobility.Static)));
        return entity;
    }

    public EntityId AddProduction(BuildingId building, RecipeId recipe, Vector3 origin,
        PlayerId? owner = null, double capacity = 1_000, BuildingOrientation orientation = BuildingOrientation.North)
    {
        EntityId entity = AddBuilding(building, origin, owner, capacity, orientation);
        Entities.AddComponent(entity, new ProductionFacility(Inventory(entity), CreateInventory(capacity),
            Recipes[recipe].RequiredCapability, Simulation.CurrentTick));
        return entity;
    }

    public void AddLoadingDock(EntityId building)
    {
        BuildingId id = Entities.GetComponent<CompletedBuilding>(building).BuildingId;
        BuildingLoadingDock? dock = DirectorateContent.CreateBuildingCatalog()[id].LoadingDock;
        Assert.True(dock.HasValue);
        Entities.AddComponent(building, dock.Value);
    }

    public InventoryId CreateInventory(double capacity)
    {
        InventoryId inventory = Inventories.CreateInventory(new InventorySpecification(capacity));
        _inventories.Add(inventory);
        return inventory;
    }

    public EntityId[] Build(ConveyorBuildRequest request, PlayerId? owner = null)
    {
        Assert.Equal(ConveyorEdit.Build, request.Edit);
        HashSet<EntityId> before = Pieces.ToHashSet();
        ConveyorConstructionCommand command = Execute(request, owner);
        Assert.True(command.Accepted, $"Construction failed: {command.Result}; request: {request}");
        return Pieces.Where(entity => !before.Contains(entity)).ToArray();
    }

    public EntityId Build(ConveyorModule module, Vector3 pivot,
        BuildingOrientation orientation = BuildingOrientation.North, int tier = 0, PlayerId? owner = null)
    {
        EntityId[] added = Build(new ConveyorBuildRequest(ConveyorEdit.Build, module, pivot, pivot,
            orientation, tier, XFirst: true), owner);
        ConveyorModule actual = module == ConveyorModule.Support && tier == 1 ? ConveyorModule.RaisedSupport : module;
        return Assert.Single(added, entity => Entities.GetComponent<ConveyorPiece>(entity).Placement.Module == actual);
    }

    public EntityId[] Connect(EntityId source, EntityId destination, bool xFirst = true)
    {
        ConveyorBuildingPort output = Port(source, input: false);
        ConveyorBuildingPort input = Port(destination, input: true);
        EntityId[] added = Build(new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            output.Position - Vector3.UnitY * 5.8f, input.Position - Vector3.UnitY * 5.8f,
            BuildingOrientation.North, 0, xFirst,
            SourcePortBuilding: source, DestinationPortBuilding: destination), output.Owner);
        return added.Where(entity => Entities.HasComponent<ConveyorFlowState>(entity)).ToArray();
    }

    public ConveyorConstructionCommand Edit(EntityId target, ConveyorEdit edit, ConveyorModule? replacement = null,
        PlayerId? owner = null)
    {
        ConveyorPiece piece = Entities.GetComponent<ConveyorPiece>(target);
        return Execute(new ConveyorBuildRequest(edit, replacement ?? piece.Placement.Module,
            piece.Placement.Pivot, piece.Placement.Pivot, piece.Placement.Orientation, piece.Placement.Tier,
            XFirst: true, Target: target), owner);
    }

    public ConveyorConstructionCommand Execute(ConveyorBuildRequest request, PlayerId? owner = null)
    {
        PlayerId player = owner ?? Player;
        EntityId payer = _payers[player];
        double before = Stock(payer, ResourceIds.Steel);
        ConveyorPlan preview = Construction.Preview(Entities, player, request, payer);
        HashSet<EntityId> priorCarriers = Carriers.ToHashSet();
        var command = new ConveyorConstructionCommand(Construction, player, request, payer, Simulation.CurrentTick);
        Simulation.SubmitCommand(command, new SimulationTick(Simulation.CurrentTick.Value + 1));
        Step();
        Assert.Equal(before - (command.Accepted ? preview.Steel : 0), Stock(payer, ResourceIds.Steel), 8);
        foreach (EntityId entity in Carriers.Where(entity => !priorCarriers.Contains(entity)))
        {
            _inventories.Add(Buffer(entity));
            SetPower(entity, 1);
        }
        return command;
    }

    public ConveyorBuildingPort Port(EntityId building, bool input)
    {
        PlayerId owner = Entities.GetComponent<CompletedBuilding>(building).Owner;
        return Assert.Single(ConveyorConstructionService.CapturePorts(Entities, owner),
            port => port.Building == building && port.Input == input &&
                (!port.Adaptable || port.Key == (input ? "west.primary" : "east.primary")));
    }

    public ConveyorFlowState Flow(EntityId entity) => Entities.GetComponent<ConveyorFlowState>(entity);
    public InventoryId Buffer(EntityId entity) => Flow(entity).Buffer;
    public InventoryId Inventory(EntityId entity) => Entities.TryGetComponent(entity, out ConveyorFlowState flow)
        ? flow.Buffer : Entities.GetComponent<InventoryStorage>(entity).InventoryId;
    public InventoryId Output(EntityId entity) => Entities.GetComponent<ProductionFacility>(entity).OutputInventory;
    public double Stock(EntityId entity, ResourceId resource) => Inventories.GetQuantity(Inventory(entity), resource);
    public void Give(EntityId entity, ResourceId resource, double quantity) =>
        Assert.True(Inventories.Add(Inventory(entity), resource, quantity).Succeeded);
    public double Total(ResourceId resource) => _inventories.Where(Inventories.Contains)
        .Sum(inventory => Inventories.GetQuantity(inventory, resource));

    public void SetPower(EntityId entity, double fraction)
    {
        PowerConsumer power = Entities.GetComponent<PowerConsumer>(entity);
        Entities.SetComponent(entity, new PowerConsumer(power.Demand, power.Priority, true,
            power.Demand * fraction, fraction == 0 ? PowerOperationalState.Offline :
            fraction == 1 ? PowerOperationalState.Powered : PowerOperationalState.Brownout));
    }

    public void Step(int ticks = 1) => Simulation.RunTicks(checked((ulong)ticks), TestContext.Current.CancellationToken);

    public void RunUntil(Func<bool> condition, int maximumTicks = 1_000, Func<string>? diagnostic = null)
    {
        for (int tick = 0; tick < maximumTicks && !condition(); tick++) Step();
        Assert.True(condition(), $"Condition did not hold after {maximumTicks} ticks. {diagnostic?.Invoke()}");
    }

    public EntityId AddPolicy(EntityId target, ResourceId resource, double minimum, double desired, double maximum)
    {
        EntityId policy = Entities.CreateEntity();
        Entities.AddComponent(policy, new LogisticsStockPolicy(target, resource, minimum, desired, maximum,
            LogisticsStockPriority.Normal));
        return policy;
    }

    public void AddRoad(EntityId source, EntityId destination, double capacityPerSecond = 100)
    {
        LogisticsNodeId sourceNode = RoadNode(source);
        LogisticsNodeId destinationNode = RoadNode(destination);
        double distance = Vector3.Distance(Entities.GetComponent<WorldTransform>(source).Position,
            Entities.GetComponent<WorldTransform>(destination).Position);
        Network.AddEdge(sourceNode, destinationNode, LogisticsTransportMode.GroundRoad,
            distanceMeters: distance, baseCost: distance, capacityPerSecond: capacityPerSecond);
    }

    public EntityId CreateTruck(Vector3 position, double capacity = 100)
    {
        CargoTruckDefinition defaults = CargoTruckDefinition.Default;
        var definition = new CargoTruckDefinition(defaults.Key, capacity, defaults.Movement, defaults.VisualScale);
        EntityId truck = CargoTruckFactory.Create(Entities, Inventories, position, Player, Cargo, definition);
        _inventories.Add(Inventory(truck));
        return truck;
    }

    private LogisticsNodeId RoadNode(EntityId entity)
    {
        if (Network.TryGetNodeForEntity(entity, out LogisticsNodeId existing)) return existing;
        Vector3 position = Entities.GetComponent<WorldTransform>(entity).Position with { Y = 0 };
        return Network.AddNode(entity, position, LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoSource | LogisticsNodeCapabilities.CargoDestination |
            LogisticsNodeCapabilities.Storage | LogisticsNodeCapabilities.Distribution);
    }

    private EntityId AddPayer(PlayerId owner, Vector3 origin)
    {
        EntityId payer = AddStorage(origin, owner, capacity: 200_000);
        Give(payer, ResourceIds.Steel, 100_000);
        _payers.Add(owner, payer);
        return payer;
    }

    private static TerrainWorld CreateTerrain(Func<float, float, float>? height)
    {
        var settings = new WorldGridSettings { ChunkSizeMeters = 64, HeightSamplesPerSide = 9 };
        var chunks = new List<TerrainChunk>();
        for (int x = 0; x < 4; x++)
        for (int z = 0; z < 4; z++)
        {
            var samples = new float[settings.HeightSamplesPerSide * settings.HeightSamplesPerSide];
            for (int row = 0; row < settings.HeightSamplesPerSide; row++)
            for (int column = 0; column < settings.HeightSamplesPerSide; column++)
                samples[row * settings.HeightSamplesPerSide + column] = height?.Invoke(
                    x * 64 + column * 8, z * 64 + row * 8) ?? 0;
            chunks.Add(new TerrainChunk(new ChunkCoordinate(x, z),
                new TerrainHeightfield(settings.HeightSamplesPerSide, settings.ChunkSizeMeters,
                    samples)));
        }
        return new TerrainWorld(settings, chunks);
    }

    private BattlefieldDefinition CreateBattlefield()
    {
        var area = new AxisAlignedBounds(new Vector3(0, -10, 0), new Vector3(256, 50, 256));
        return new BattlefieldDefinition(new BattlefieldMapMetadata("conveyor-tests", "Conveyor tests", 256, 256, 2),
            [new(Player, new(24, 0, 24), new(24, 0, 24), area),
             new(OtherPlayer, new(64, 0, 24), new(64, 0, 24), area)],
            [], [], [], [], [], [], [], [],
            new BattlefieldTerrainVisualDefinition("conveyor-tests", BattlefieldTerrainControlEncoding.RgbaFourLayer,
                4, 9, ["material.world.terrain.grass_ground"]));
    }
}
