using System.Numerics;
using BenchmarkDotNet.Attributes;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Navigation;

namespace ForgeLine.Simulation.Benchmarks;

/// <summary>
/// Isolates fixed-tick convoy coordination from the existing physical cargo,
/// docking and navigation benchmarks. Each parameter uses the same number
/// of independently identified trucks and optional combat escorts.
/// </summary>
[MemoryDiagnoser]
public class CargoConvoyBenchmarks
{
    private SimulationCoordinator _simulation = null!;
    private CargoConvoySystem _convoys = null!;
    private PlayerId _owner;

    [Params(10, 50, 100)]
    public int TruckAndEscortCount { get; set; }

    [IterationSetup]
    public void Setup()
    {
        _owner = new PlayerId(1);
        _simulation = new SimulationCoordinator(
            ticksPerSecond: 20,
            initialEntityCapacity: TruckAndEscortCount * 2 + 8);

        var network = new LogisticsNetwork();
        var inventories = new InventoryStore();
        var cargo = new CargoTransportSystem(network, inventories);
        _convoys = new CargoConvoySystem(network);
        _simulation.RegisterSystem(_convoys);

        EntityId sourceEntity = _simulation.Entities.CreateEntity();
        EntityId destinationEntity = _simulation.Entities.CreateEntity();
        var originPosition = new Vector3(4, 0, 4);
        var destinationPosition = new Vector3(100, 0, 4);
        LogisticsNodeId origin = network.AddNode(
            sourceEntity, originPosition, LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoSource | LogisticsNodeCapabilities.Storage);
        LogisticsNodeId destination = network.AddNode(
            destinationEntity, destinationPosition, LogisticsNodeKind.StorageDepot,
            LogisticsNodeCapabilities.CargoDestination | LogisticsNodeCapabilities.Storage);
        network.AddEdge(origin, destination, LogisticsTransportMode.GroundRoad,
            distanceMeters: 96, baseCost: 96, capacityPerSecond: 1000);

        LogisticsRoute route = network.FindRoute(origin, destination,
            LogisticsRouteCostPolicy.Default).Route ??
            throw new InvalidOperationException("Convoy benchmark needs a valid regional route.");

        var trucks = new EntityId[TruckAndEscortCount];
        var escorts = new EntityId[TruckAndEscortCount];
        for (int i = 0; i < TruckAndEscortCount; i++)
        {
            Vector3 truckPosition = originPosition +
                new Vector3(i % 10 * 0.35f, 0, i / 10 * 0.35f);
            EntityId truck = CargoTruckFactory.Create(
                _simulation.Entities, inventories, truckPosition, _owner, cargo);
            _simulation.Entities.AddComponent(truck,
                new CargoTransportOrder(origin, destination, ResourceIds.FerrousOre,
                    10, SimulationTick.Zero));
            _simulation.Entities.AddComponent(truck,
                new CargoTransportRouteState(route, NextSegmentIndex: 0));
            _simulation.Entities.SetComponent(truck,
                new CargoTransportRuntimeState(
                    CargoTransportLifecycleState.ToDestination,
                    CargoTransportWaitReason.None,
                    CargoTransportFailureReason.None,
                    origin, network.Version,
                    LoadedQuantity: 10, DeliveredQuantity: 0,
                    StateChangedAtTick: SimulationTick.Zero));
            trucks[i] = truck;

            EntityId escort = _simulation.Entities.CreateEntity();
            _simulation.Entities.AddComponent(escort,
                new ControllableEntity(_owner, ControllableEntityCategory.Unit));
            _simulation.Entities.AddComponent(escort, new Combatant(new FactionId(1)));
            _simulation.Entities.AddComponent(escort,
                new WorldTransform(truckPosition + new Vector3(0, 0, 12),
                    Quaternion.Identity, Vector3.One));
            _simulation.Entities.AddComponent(escort, GroundMovement.CreateDefault());
            _simulation.Entities.AddComponent(escort, GroundMovementState.Stationary());
            _simulation.Entities.AddComponent(escort,
                new NavigationAgent(NavigationMovementClass.Wheeled));
            escorts[i] = escort;
        }

        var create = PlayerLogisticsActionCommand.CreateCargoConvoy(
            _owner, trucks, escorts, _simulation.CurrentTick);
        _simulation.SubmitCommand(create,
            _simulation.CurrentTick.Next(),
            new SimulationCommandSource(_owner.Value));
        _simulation.AdvanceOneTick();
        if (!create.Accepted)
            throw new InvalidOperationException("Unable to admit convoy benchmark membership.");
    }

    [Benchmark]
    public int CoordinateTwentyFixedTicks()
    {
        _simulation.RunTicks(20, CancellationToken.None);
        return _simulation.Entities.GetComponentCount<CargoConvoyMember>();
    }

    [Benchmark]
    public int CaptureOwnerFilteredConvoy()
    {
        return _convoys.CaptureOwned(_simulation.Entities, _owner,
            _simulation.CurrentTick).Count;
    }
}
