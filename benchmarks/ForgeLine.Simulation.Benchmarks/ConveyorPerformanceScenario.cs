using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Simulation.Benchmarks;

internal enum ConveyorWorkloadState { Powered, Unpowered, Blocked, Congested }
internal enum ConveyorWorkloadTopology { Linear, SplitMerge }

internal readonly record struct ConveyorWorkloadCounters(
    ulong Tick, long ProductionCycles, double ProducedSteel, double FerrousOre, double Steel,
    long CompletedDeliveries, double DeliveredSteel, double LostCargo);

internal readonly record struct ConveyorWorkloadObservation(
    double ModuleOutputQuantity, int FlowingModules, int OfflineModules, int BlockedModules,
    int CongestedModules, double TruckDistanceMeters, int ActiveTrucks);

/// <summary>Finite synthetic scale fixture using the existing production, conveyor and physical cargo authorities.</summary>
internal sealed class ConveyorPerformanceScenario : IDisposable
{
    public const int TickRate = 20;
    public const ulong Seed = 2026;
    private const int ModulesPerLane = 20;
    private const double InitialOrePerFacility = 10_000;
    private static readonly PlayerId Owner = new(1);
    private static readonly PowerNetworkId BuildingPower = new(1);
    private static readonly PowerNetworkId ConveyorPower = new(2);
    private readonly SimulationCoordinator _simulation;
    private readonly InventoryStore _inventories = new();
    private readonly LogisticsNetwork _network = new();
    private readonly ProductionSystem _production;
    private readonly CargoTransportSystem _cargo;
    private readonly AutomatedDistributionSystem _distribution;
    private readonly ConveyorProbe _conveyor;
    private readonly List<InventoryId> _inventoryIds = [];
    private readonly List<EntityId> _modules = [];
    private readonly List<EntityId> _trucks = [];
    private readonly List<Vector3> _previousTruckPositions = [];
    private readonly List<AxisAlignedBounds> _obstacles = [];
    private readonly List<(EntityId Entity, InventoryId Inventory, Vector3 Position)> _truckSources = [];
    private readonly List<(EntityId Entity, InventoryId Inventory, Vector3 Position)> _truckDestinations = [];
    private readonly TerrainWorld _terrain;
    private readonly AuthorityDigest _authorityDigest = new();

    public ConveyorPerformanceScenario(int moduleCount, ConveyorWorkloadTopology topology, ConveyorWorkloadState state)
    {
        if (moduleCount is not (100 or 1000 or 4096))
            throw new ArgumentOutOfRangeException(nameof(moduleCount));
        ModuleCount = moduleCount;
        Topology = topology;
        State = state;
        LaneCount = (moduleCount + ModulesPerLane - 1) / ModulesPerLane;
        TruckCount = state == ConveyorWorkloadState.Powered ? moduleCount switch { 100 => 1, 1000 => 4, _ => 16 } : 0;
        _simulation = new(ticksPerSecond: TickRate, seed: Seed,
            initialEntityCapacity: moduleCount + LaneCount * 8 + TruckCount * 4 + 16);
        _terrain = new(new WorldGridSettings { ChunkSizeMeters = 2048, HeightSamplesPerSide = 3 },
            [new TerrainChunk(new(0, 0), new TerrainHeightfield(3, 2048, new float[9]))]);
        var spatial = new SpatialGridIndex();
        var recipes = InitialProductionRecipes.CreateCatalog();
        _production = new(recipes, _inventories);

        // Layout and finite stock initialization are excluded from every timed window.
        AddGenerator(BuildingPower, LaneCount * 40 + TruckCount * 20);
        AddGenerator(ConveyorPower, moduleCount * 2, state != ConveyorWorkloadState.Unpowered);
        for (int lane = 0, remaining = moduleCount; remaining > 0; lane++)
        {
            int count = Math.Min(ModulesPerLane, remaining);
            CreateLane(lane, count);
            remaining -= count;
        }
        for (int index = 0; index < TruckCount; index++)
        {
            Vector3 position = _truckSources[index].Position + new Vector3(40, 0, 0);
            InventoryId inventory = Inventory(20_000);
            _truckDestinations.Add((Building(BuildingIds.StorageDepot, position, inventory), inventory, position));
        }

        HierarchicalNavigationSystem? navigation = null;
        if (TruckCount > 0)
        {
            navigation = new(new HierarchicalPathfinder(NavigationWorld.Build(_terrain, _obstacles,
                new NavigationGridSettings { CellSizeMeters = 4 },
                new NavigationSectorSettings { SectorSizeCells = 8 })));
            _simulation.RegisterSystem(navigation);
            _simulation.RegisterSystem(new GroundMovementSystem(_terrain, spatial));
            _simulation.RegisterSystem(new SpatialIndexSystem(new SpatialIndexSynchronizer(spatial)));
        }
        _cargo = new(_network, _inventories, navigation, new VehicleDockingService(_terrain, spatial, navigation));
        _distribution = new(_network, _inventories, _cargo, retryDelayTicks: 5, fairnessAgingTicks: 20);
        _conveyor = new(new ConveyorSimulationSystem(_inventories, _distribution, recipes, DirectorateContent.CreateUnitCatalog()));

        for (int index = 0; index < TruckCount; index++)
            CreateTruckRoute(_truckSources[index], _truckDestinations[index]);

        // Preserve the canonical relative phase/registration order from MatchRuntime.
        _simulation.RegisterSystem(_distribution);
        _simulation.RegisterSystem(_cargo);
        _simulation.RegisterSystem(new PowerNetworkSystem());
        _simulation.RegisterSystem(_production);
        _simulation.RegisterSystem(_conveyor);
        if (_modules.Count != moduleCount || _trucks.Count != TruckCount)
            throw new InvalidOperationException("Conveyor workload size differs from its declared size.");
    }

    public int ModuleCount { get; }
    public int LaneCount { get; }
    public int TruckCount { get; }
    public ConveyorWorkloadTopology Topology { get; }
    public ConveyorWorkloadState State { get; }
    public double ConveyorMilliseconds => _conveyor.Milliseconds;
    public long ConveyorAllocatedBytes => _conveyor.AllocatedBytes;

    public void AdvanceTick() => _simulation.AdvanceOneTick();

    public void Dispose() => _authorityDigest.Dispose();

    // Sample outside the timed region. Include full entity generations and exact floating-point bits.
    public string CaptureAuthorityDigest()
    {
        AuthorityDigest digest = _authorityDigest;
        digest.Add(_simulation.CurrentTick.Value);
        digest.Add(_production.Metrics.CompletedCycles);
        digest.Add(_production.Metrics.TotalOutputQuantity);
        digest.Add(_cargo.Metrics.CompletedOrderCount);
        digest.Add(_cargo.Metrics.DeliveredQuantity);
        digest.Add(_cargo.Metrics.LostQuantity);
        digest.Add(_inventoryIds.Count);
        foreach (InventoryId inventory in _inventoryIds)
        {
            digest.Add(inventory.Value);
            digest.Add(_inventories.GetTotalQuantity(inventory));
            foreach (ResourceId resource in ConveyorSimulationSystem.EligibleResources)
            {
                digest.Add(_inventories.GetQuantity(inventory, resource));
                digest.Add(_inventories.GetReservedQuantity(inventory, resource));
                digest.Add(_inventories.GetAvailableQuantity(inventory, resource));
            }
        }
        digest.Add(_modules.Count);
        foreach (EntityId module in _modules)
        {
            ConveyorFlowState flow = _simulation.Entities.GetComponent<ConveyorFlowState>(module);
            digest.Add(module); digest.Add(flow.Buffer.Value); digest.Add(flow.Capacity);
            digest.Add(flow.InputCursor); digest.Add(flow.OutputCursor); digest.Add(flow.ResourceCursor);
            digest.Add(flow.InputPerSecond); digest.Add(flow.OutputPerSecond); digest.Add(flow.ActiveSeconds);
            digest.Add((int)flow.Status); digest.Add(flow.Network);
            digest.Add(flow.InputA); digest.Add(flow.InputB); digest.Add(flow.OutputA); digest.Add(flow.OutputB);
            digest.Add(flow.OutputAPerSecond); digest.Add(flow.OutputBPerSecond);
            digest.Add(_simulation.Entities.GetComponent<ConveyorPiece>(module).BufferedQuantity);
        }
        foreach (EntityId port in _simulation.Entities.Query<ConveyorPortArbitration>(QueryIterationOrder.StableByEntityIndex))
        {
            digest.Add(port);
            digest.Add(_simulation.Entities.GetComponent<ConveyorPortArbitration>(port).ResourceCursor);
        }
        digest.Add(_trucks.Count);
        foreach (EntityId truck in _trucks)
        {
            WorldTransform transform = _simulation.Entities.GetComponent<WorldTransform>(truck);
            CargoTransportRuntimeState cargo = _simulation.Entities.GetComponent<CargoTransportRuntimeState>(truck);
            digest.Add(truck);
            digest.Add(transform.Position.X); digest.Add(transform.Position.Y); digest.Add(transform.Position.Z);
            digest.Add((int)cargo.Lifecycle); digest.Add((int)cargo.WaitReason); digest.Add((int)cargo.FailureReason);
            digest.Add(cargo.LoadedQuantity); digest.Add(cargo.DeliveredQuantity); digest.Add(cargo.StateChangedAtTick.Value);
            bool hasDock = _simulation.Entities.TryGetComponent(truck, out VehicleDockVisit dock);
            digest.Add(hasDock ? 1 : 0);
            if (hasDock)
            {
                digest.Add(dock.Building); digest.Add(dock.Generation);
                digest.Add((int)dock.Status); digest.Add((int)dock.Reason);
            }
        }
        return digest.Finish();
    }

    public void BeginMeasurement()
    {
        _conveyor.Measuring = true;
        for (int index = 0; index < _trucks.Count; index++)
            _previousTruckPositions[index] = _simulation.Entities.GetComponent<WorldTransform>(_trucks[index]).Position;
    }

    public ConveyorWorkloadCounters CaptureCounters()
    {
        double ore = 0, steel = 0;
        foreach (InventoryId inventory in _inventoryIds)
        {
            ore += _inventories.GetQuantity(inventory, ResourceIds.FerrousOre);
            steel += _inventories.GetQuantity(inventory, ResourceIds.Steel);
        }
        return new(_simulation.CurrentTick.Value, _production.Metrics.CompletedCycles,
            _production.Metrics.TotalOutputQuantity, ore, steel,
            _cargo.Metrics.CompletedOrderCount, _cargo.Metrics.DeliveredQuantity, _cargo.Metrics.LostQuantity);
    }

    public ConveyorWorkloadObservation Observe()
    {
        double output = 0, distance = 0;
        int flowing = 0, offline = 0, blocked = 0, congested = 0;
        foreach (EntityId module in _modules)
        {
            ConveyorFlowState flow = _simulation.Entities.GetComponent<ConveyorFlowState>(module);
            output += flow.OutputPerSecond / TickRate;
            flowing += flow.Status == ConveyorLinkStatus.Flowing ? 1 : 0;
            offline += flow.Status == ConveyorLinkStatus.Offline ? 1 : 0;
            blocked += flow.Status == ConveyorLinkStatus.Blocked ? 1 : 0;
            congested += flow.Status == ConveyorLinkStatus.Congested ? 1 : 0;
        }
        for (int index = 0; index < _trucks.Count; index++)
        {
            Vector3 position = _simulation.Entities.GetComponent<WorldTransform>(_trucks[index]).Position;
            distance += Vector3.Distance(position, _previousTruckPositions[index]);
            _previousTruckPositions[index] = position;
        }
        return new(output, flowing, offline, blocked, congested, distance, _cargo.Metrics.ActiveTransportCount);
    }

    public object[] CaptureTruckStates()
    {
        var states = new object[_trucks.Count];
        for (int index = 0; index < _trucks.Count; index++)
        {
            EntityId truck = _trucks[index];
            Vector3 position = _simulation.Entities.GetComponent<WorldTransform>(truck).Position;
            CargoTransport transport = _simulation.Entities.GetComponent<CargoTransport>(truck);
            CargoTransportRuntimeState cargo = _simulation.Entities.GetComponent<CargoTransportRuntimeState>(truck);
            bool hasDock = _simulation.Entities.TryGetComponent(truck, out VehicleDockVisit dock);
            states[index] = new
            {
                Entity = truck.ToString(),
                Position = new { position.X, position.Y, position.Z },
                Cargo = cargo,
                Steel = _inventories.GetQuantity(transport.CargoInventory, ResourceIds.Steel),
                DockStatus = hasDock ? dock.Status.ToString() : null,
                DockReason = hasDock ? dock.Reason.ToString() : null,
                DockBuilding = hasDock ? dock.Building.ToString() : null,
                DockService = hasDock ? new { dock.Pose.Service.X, dock.Pose.Service.Y, dock.Pose.Service.Z } : null
            };
        }
        return states;
    }

    private void CreateLane(int lane, int count)
    {
        Vector3 origin = new(32 + lane % 12 * 160, 0, 32 + lane / 12 * 96);
        InventoryId input = Inventory(20_000);
        InventoryId output = Inventory(20_000);
        Add(input, ResourceIds.FerrousOre, InitialOrePerFacility);
        EntityId source = Building(BuildingIds.Smelter, origin, output);
        _simulation.Entities.AddComponent(source, new ProductionFacility(input, output,
            ProductionCapability.SteelProcessing, SimulationTick.Zero));
        _simulation.SubmitCommand(new QueueProductionCommand(source, RecipeIds.Steel, SimulationTick.Zero,
            mode: ProductionRequestMode.Repeat), new SimulationTick(1));
        Policy(source, ResourceIds.FerrousOre, 0, 1000, 20_000);

        // All geometry uses the production sockets; East rotates +Z transport onto +X.
        float next = origin.X + 9;
        Module(ConveyorModule.InputAdapter, new(next + 2, 0, origin.Z), source);
        next += 4;
        int used = 1;
        if (Topology == ConveyorWorkloadTopology.SplitMerge)
        {
            Module(ConveyorModule.Splitter, new(next + 4, 0, origin.Z));
            Module(ConveyorModule.Standard, new(next + 12, 0, origin.Z - 4));
            Module(ConveyorModule.Standard, new(next + 12, 0, origin.Z + 4));
            Module(ConveyorModule.Merger, new(next + 20, 0, origin.Z));
            next += 24;
            used += 4;
        }
        while (used < count - 1)
        {
            Module(ConveyorModule.ShortSegment, new(next + 2, 0, origin.Z));
            next += 4;
            used++;
        }
        // Direction-adaptable four-position storage ports can legitimately
        // accept the reversed adapter. The blocked workload therefore offsets
        // the receiving building so no physical socket can mate.
        Vector3 destinationPosition = new(next + 11, 0,
            origin.Z + (State == ConveyorWorkloadState.Blocked ? 1.0f : 0.0f));
        InventoryId destinationInventory = Inventory(State == ConveyorWorkloadState.Congested ? 100 : 20_000);
        EntityId destination = Building(BuildingIds.StorageDepot, destinationPosition, destinationInventory,
            State == ConveyorWorkloadState.Blocked);
        Module(ConveyorModule.OutputAdapter, new(next + 2, 0, origin.Z), destination);
        if (State == ConveyorWorkloadState.Congested)
            Add(destinationInventory, ResourceIds.Steel, 100);
        if (lane < TruckCount)
            _truckSources.Add((destination, destinationInventory, destinationPosition));
        else
            Policy(destination, ResourceIds.Steel, 0, 10_000, 20_000);
    }

    private EntityId Building(BuildingId id, Vector3 position, InventoryId inventory, bool reverse = false)
    {
        Vector3 dimensions = ConveyorGeometry.BuildingPortDimensions(id);
        EntityId entity = _simulation.Entities.CreateEntity();
        var transform = new WorldTransform(position + Vector3.UnitY * dimensions.Y * .5f,
            reverse ? Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI) : Quaternion.Identity, dimensions);
        var presence = new SpatialPresence(dimensions * .5f, new(Owner.Value, 2, SpatialMobility.Static));
        _simulation.Entities.AddComponent(entity, transform);
        _simulation.Entities.AddComponent(entity, presence);
        _simulation.Entities.AddComponent(entity, new CompletedBuilding(id, Owner, SimulationTick.Zero));
        _simulation.Entities.AddComponent(entity, new InventoryStorage(inventory));
        _simulation.Entities.AddComponent(entity, new StorageDepot(inventory, new FactionId(1)));
        _simulation.Entities.AddComponent(entity, new PowerNetworkMembership(BuildingPower));
        _simulation.Entities.AddComponent(entity, new PowerConsumer(10));
        _simulation.Entities.AddComponent(entity, new BuildingLoadingDock(new(0, -dimensions.Y * .5f, dimensions.Z * .5f + 4.5f), Vector3.UnitX, 12));
        _obstacles.Add(presence.CreateEntry(entity, transform).Bounds);
        return entity;
    }

    private void Module(ConveyorModule kind, Vector3 position, EntityId attachment = default)
    {
        EntityId entity = _simulation.Entities.CreateEntity();
        double capacity = ConveyorSimulationSystem.Capacity(kind);
        InventoryId buffer = Inventory(capacity);
        if (State == ConveyorWorkloadState.Congested)
            Add(buffer, ResourceIds.Steel, capacity);
        _simulation.Entities.AddComponent(entity, new ConveyorPiece(Owner,
            new(kind, position, BuildingOrientation.East, 0), ConveyorGeometry.Cost(kind), attachment));
        _simulation.Entities.AddComponent(entity, new ConveyorFlowState(buffer, capacity));
        _simulation.Entities.AddComponent(entity, new PowerNetworkMembership(ConveyorPower));
        _simulation.Entities.AddComponent(entity, new PowerConsumer(1));
        _modules.Add(entity);
    }

    private void CreateTruckRoute((EntityId Entity, InventoryId Inventory, Vector3 Position) source,
        (EntityId Entity, InventoryId Inventory, Vector3 Position) destination)
    {
        LogisticsNodeId origin = _network.AddNode(source.Entity, source.Position, LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoSource | LogisticsNodeCapabilities.Storage);
        LogisticsNodeId target = _network.AddNode(destination.Entity, destination.Position, LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoDestination | LogisticsNodeCapabilities.Storage);
        _network.AddEdge(origin, target, LogisticsTransportMode.GroundRoad, 40, 40, 100);
        Policy(destination.Entity, ResourceIds.Steel, 10_000, 10_000, 20_000);
        Vector3 spawn = source.Position + new Vector3(-24, CargoTruckDefinition.Default.Movement.HeightOffset, 11.5f);
        EntityId truck = CargoTruckFactory.Create(_simulation.Entities, _inventories, spawn, Owner, _cargo);
        _inventoryIds.Add(_simulation.Entities.GetComponent<CargoTransport>(truck).CargoInventory);
        _trucks.Add(truck);
        _previousTruckPositions.Add(spawn);
    }

    private InventoryId Inventory(double capacity)
    {
        InventoryId inventory = _inventories.CreateInventory(new InventorySpecification(capacity));
        _inventoryIds.Add(inventory);
        return inventory;
    }

    private void Add(InventoryId inventory, ResourceId resource, double quantity)
    {
        if (!_inventories.Add(inventory, resource, quantity).Succeeded)
            throw new InvalidOperationException("Unable to initialize finite conveyor workload stock.");
    }

    private void Policy(EntityId entity, ResourceId resource, double minimum, double target, double maximum)
    {
        EntityId policy = _simulation.Entities.CreateEntity();
        _simulation.Entities.AddComponent(policy, new LogisticsStockPolicy(entity, resource, minimum, target, maximum));
    }

    private void AddGenerator(PowerNetworkId network, double amount, bool enabled = true)
    {
        EntityId generator = _simulation.Entities.CreateEntity();
        _simulation.Entities.AddComponent(generator, new PowerNetworkMembership(network));
        _simulation.Entities.AddComponent(generator, new PowerGenerator(amount, enabled));
    }

    private sealed class ConveyorProbe(ConveyorSimulationSystem system) : ISimulationSystem
    {
        public SimulationPhase Phase => system.Phase;
        public bool Measuring { get; set; }
        public double Milliseconds { get; private set; }
        public long AllocatedBytes { get; private set; }

        public void Execute(SimulationContext context)
        {
            if (!Measuring) { system.Execute(context); return; }
            long allocated = GC.GetTotalAllocatedBytes(precise: true);
            long started = Stopwatch.GetTimestamp();
            system.Execute(context);
            Milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            AllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        }
    }

    private sealed class AuthorityDigest : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly byte[] _buffer = new byte[8192];
        private int _length;

        public void Add(EntityId value) { Add(value.Index); Add(value.Generation); }
        public void Add(int value) => Add((long)value);
        public void Add(uint value) => Add((ulong)value);
        public void Add(long value) => Add(unchecked((ulong)value));
        public void Add(double value) => Add(BitConverter.DoubleToInt64Bits(value));
        public void Add(ulong value)
        {
            if (_length == _buffer.Length) Flush();
            BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_length, sizeof(ulong)), value);
            _length += sizeof(ulong);
        }
        public string Finish()
        {
            Flush();
            Span<byte> bytes = stackalloc byte[32];
            if (!_hash.TryGetHashAndReset(bytes, out int written) || written != bytes.Length)
                throw new InvalidOperationException("Unable to capture conveyor authority digest.");
            return Convert.ToHexString(bytes);
        }
        public void Dispose() => _hash.Dispose();
        private void Flush() { _hash.AppendData(_buffer.AsSpan(0, _length)); _length = 0; }
    }
}
