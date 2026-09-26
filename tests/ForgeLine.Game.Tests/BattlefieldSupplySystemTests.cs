using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class BattlefieldSupplySystemTests
{
    private static readonly PlayerId LocalPlayer = new(1);

    [Fact]
    public void MovementDistanceConsumesPhysicalFuel()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var supply = new BattlefieldSupplySystem(inventories);
        simulation.RegisterSystem(supply);

        EntityId unit =
            CreateSuppliedUnit(
                simulation,
                inventories,
                Vector3.Zero,
                fuelCapacity: 20.0,
                initialFuel: 10.0,
                ammunitionCapacity: 10.0,
                initialAmmunition: 10.0,
                fuelConsumptionPerMeter: 1.0);

        InventoryId inventory =
            simulation.Entities.GetComponent<UnitFuelState>(
                unit).InventoryId;

        simulation.AdvanceOneTick();

        simulation.Entities.SetComponent(
            unit,
            new WorldTransform(
                new Vector3(3.0f, 0.0f, 4.0f),
                Quaternion.Identity,
                Vector3.One));

        simulation.AdvanceOneTick();

        Assert.Equal(
            5.0,
            inventories.GetQuantity(
                inventory,
                ResourceIds.Fuel),
            precision: 6);
    }

    [Fact]
    public void ZeroFuelStopsPoweredMovementAndRefuelResumesIt()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();

        simulation.RegisterSystem(
            new GroundMovementSystem());
        simulation.RegisterSystem(
            new BattlefieldSupplySystem(inventories));

        EntityId unit =
            CreateSuppliedUnit(
                simulation,
                inventories,
                Vector3.Zero,
                fuelCapacity: 20.0,
                initialFuel: 0.0,
                ammunitionCapacity: 10.0,
                initialAmmunition: 10.0,
                fuelConsumptionPerMeter: 0.1);

        simulation.Entities.AddComponent(
            unit,
            GroundMovement.CreateDefault());
        simulation.Entities.AddComponent(
            unit,
            GroundMovementState.Stationary());
        simulation.Entities.AddComponent(
            unit,
            new MovementOrder(
                LocalPlayer,
                new Vector3(20.0f, 0.0f, 0.0f),
                SimulationTick.Zero,
                new SimulationTick(1)));

        simulation.AdvanceOneTick();

        WorldTransform blocked =
            simulation.Entities.GetComponent<WorldTransform>(unit);
        GroundMovementState blockedState =
            simulation.Entities.GetComponent<GroundMovementState>(unit);

        Assert.Equal(Vector3.Zero, blocked.Position);
        Assert.Equal(
            GroundMovementStatus.OutOfFuel,
            blockedState.Status);
        Assert.True(
            simulation.Entities.HasComponent<MovementOrder>(unit));

        InventoryId fuelInventory =
            simulation.Entities.GetComponent<UnitFuelState>(
                unit).InventoryId;
        Assert.True(
            inventories.Add(
                fuelInventory,
                ResourceIds.Fuel,
                10.0).Succeeded);

        simulation.AdvanceOneTick();
        simulation.AdvanceOneTick();

        WorldTransform resumed =
            simulation.Entities.GetComponent<WorldTransform>(unit);
        GroundMovementState resumedState =
            simulation.Entities.GetComponent<GroundMovementState>(unit);

        Assert.True(resumed.Position.X > 0.0f);
        Assert.NotEqual(
            GroundMovementStatus.OutOfFuel,
            resumedState.Status);
    }

    [Fact]
    public void AmmunitionConsumptionUsesUnitInventoryAndFailsClosedWhenEmpty()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();

        EntityId unit =
            CreateSuppliedUnit(
                simulation,
                inventories,
                Vector3.Zero,
                fuelCapacity: 10.0,
                initialFuel: 10.0,
                ammunitionCapacity: 5.0,
                initialAmmunition: 5.0,
                fuelConsumptionPerMeter: 0.0);

        AmmunitionState ammunition =
            simulation.Entities.GetComponent<AmmunitionState>(
                unit);

        Assert.True(
            AmmunitionConsumption.TryConsume(
                inventories,
                ammunition,
                3.0));
        Assert.Equal(
            2.0,
            inventories.GetQuantity(
                ammunition.InventoryId,
                ResourceIds.Ammunition));

        Assert.False(
            AmmunitionConsumption.TryConsume(
                inventories,
                ammunition,
                3.0));
        Assert.Equal(
            2.0,
            inventories.GetQuantity(
                ammunition.InventoryId,
                ResourceIds.Ammunition));
    }

    [Fact]
    public void SupplyTruckLoadsFromDepotAndHonorsRecipientPriority()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();
        var network = new ForgeLine.Logistics.LogisticsNetwork();
        var cargo = new CargoTransportSystem(
            network,
            inventories);
        var supply = new BattlefieldSupplySystem(inventories);
        simulation.RegisterSystem(supply);

        InventoryId depotInventory =
            inventories.CreateInventory(
                new InventorySpecification(500.0));
        Assert.True(
            inventories.Add(
                depotInventory,
                ResourceIds.Fuel,
                70.0).Succeeded);
        Assert.True(
            inventories.Add(
                depotInventory,
                ResourceIds.Ammunition,
                70.0).Succeeded);

        EntityId depot =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            depot,
            new WorldTransform(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            depot,
            new SupplyDepot(
                depotInventory,
                LocalPlayer));
        simulation.Entities.AddComponent(
            depot,
            new SupplyProvider(
                depotInventory,
                LocalPlayer,
                resupplyRangeMeters: 20.0f));

        EntityId truck =
            SupplyTruckFactory.Create(
                simulation.Entities,
                inventories,
                Vector3.Zero,
                LocalPlayer,
                cargo);

        EntityId critical =
            CreateSuppliedUnit(
                simulation,
                inventories,
                new Vector3(30.0f, 0.0f, 0.0f),
                fuelCapacity: 50.0,
                initialFuel: 0.0,
                ammunitionCapacity: 1.0,
                initialAmmunition: 1.0,
                fuelConsumptionPerMeter: 0.0,
                priority: BattlefieldSupplyPriority.Critical);
        EntityId low =
            CreateSuppliedUnit(
                simulation,
                inventories,
                new Vector3(30.0f, 0.0f, 1.0f),
                fuelCapacity: 50.0,
                initialFuel: 0.0,
                ammunitionCapacity: 1.0,
                initialAmmunition: 1.0,
                fuelConsumptionPerMeter: 0.0,
                priority: BattlefieldSupplyPriority.Low);

        simulation.AdvanceOneTick();

        SupplyTruck truckState =
            simulation.Entities.GetComponent<SupplyTruck>(
                truck);
        Assert.Equal(
            70.0,
            inventories.GetQuantity(
                truckState.InventoryId,
                ResourceIds.Fuel));

        simulation.Entities.SetComponent(
            truck,
            new WorldTransform(
                new Vector3(30.0f, 0.0f, 0.5f),
                Quaternion.Identity,
                new Vector3(3.2f, 2.2f, 6.4f)));

        simulation.AdvanceOneTick();

        InventoryId criticalInventory =
            simulation.Entities.GetComponent<UnitFuelState>(
                critical).InventoryId;
        InventoryId lowInventory =
            simulation.Entities.GetComponent<UnitFuelState>(
                low).InventoryId;

        Assert.Equal(
            50.0,
            inventories.GetQuantity(
                criticalInventory,
                ResourceIds.Fuel));
        Assert.Equal(
            20.0,
            inventories.GetQuantity(
                lowInventory,
                ResourceIds.Fuel));
        Assert.Equal(
            0.0,
            inventories.GetQuantity(
                truckState.InventoryId,
                ResourceIds.Fuel));
        Assert.Equal(
            0.0,
            inventories.GetQuantity(
                depotInventory,
                ResourceIds.Ammunition));
        Assert.Equal(
            70.0,
            inventories.GetQuantity(
                truckState.InventoryId,
                ResourceIds.Ammunition));

        double conservedFuel =
            inventories.GetQuantity(
                depotInventory,
                ResourceIds.Fuel) +
            inventories.GetQuantity(
                truckState.InventoryId,
                ResourceIds.Fuel) +
            inventories.GetQuantity(
                criticalInventory,
                ResourceIds.Fuel) +
            inventories.GetQuantity(
                lowInventory,
                ResourceIds.Fuel);
        Assert.Equal(
            70.0,
            conservedFuel,
            precision: 6);
    }

    [Fact]
    public void HeadlessMobileGroupResuppliesAndResumesExistingOrders()
    {
        var simulation = new SimulationCoordinator(ticksPerSecond: 20);
        var inventories = new InventoryStore();
        var supply = new BattlefieldSupplySystem(inventories);

        simulation.RegisterSystem(new GroundMovementSystem());
        simulation.RegisterSystem(supply);

        InventoryId providerInventory =
            inventories.CreateInventory(
                new InventorySpecification(300.0));
        Assert.True(
            inventories.Add(
                providerInventory,
                ResourceIds.Fuel,
                200.0).Succeeded);

        EntityId provider = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            provider,
            new WorldTransform(
                new Vector3(100.0f, 0.0f, 100.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            provider,
            new SupplyProvider(
                providerInventory,
                LocalPlayer,
                resupplyRangeMeters: 12.0f));

        EntityId first =
            CreateSuppliedUnit(
                simulation,
                inventories,
                Vector3.Zero,
                fuelCapacity: 20.0,
                initialFuel: 0.02,
                ammunitionCapacity: 10.0,
                initialAmmunition: 10.0,
                fuelConsumptionPerMeter: 1.0);
        EntityId second =
            CreateSuppliedUnit(
                simulation,
                inventories,
                new Vector3(0.0f, 0.0f, 1.0f),
                fuelCapacity: 20.0,
                initialFuel: 0.02,
                ammunitionCapacity: 10.0,
                initialAmmunition: 10.0,
                fuelConsumptionPerMeter: 1.0);

        EntityId[] group = [first, second];

        for (int index = 0; index < group.Length; index++)
        {
            EntityId unit = group[index];
            simulation.Entities.AddComponent(
                unit,
                GroundMovement.CreateDefault());
            simulation.Entities.AddComponent(
                unit,
                GroundMovementState.Stationary());
            simulation.Entities.AddComponent(
                unit,
                new MovementOrder(
                    LocalPlayer,
                    new Vector3(20.0f, 0.0f, index),
                    SimulationTick.Zero,
                    new SimulationTick(1)));
        }

        for (int tick = 0; tick < 40; tick++)
        {
            simulation.AdvanceOneTick();

            bool allStopped = true;
            for (int index = 0; index < group.Length; index++)
            {
                GroundMovementState state =
                    simulation.Entities.GetComponent<GroundMovementState>(
                        group[index]);
                allStopped &=
                    state.Status == GroundMovementStatus.OutOfFuel;
            }

            if (allStopped)
            {
                break;
            }
        }

        Vector3 firstStopped =
            simulation.Entities.GetComponent<WorldTransform>(
                first).Position;
        Vector3 secondStopped =
            simulation.Entities.GetComponent<WorldTransform>(
                second).Position;

        Assert.Equal(
            GroundMovementStatus.OutOfFuel,
            simulation.Entities.GetComponent<GroundMovementState>(
                first).Status);
        Assert.Equal(
            GroundMovementStatus.OutOfFuel,
            simulation.Entities.GetComponent<GroundMovementState>(
                second).Status);
        Assert.True(
            simulation.Entities.HasComponent<MovementOrder>(first));
        Assert.True(
            simulation.Entities.HasComponent<MovementOrder>(second));

        Vector3 providerPosition =
            (firstStopped + secondStopped) * 0.5f;
        simulation.Entities.SetComponent(
            provider,
            new WorldTransform(
                providerPosition,
                Quaternion.Identity,
                Vector3.One));

        simulation.AdvanceOneTick();
        simulation.AdvanceOneTick();

        Vector3 firstResumed =
            simulation.Entities.GetComponent<WorldTransform>(
                first).Position;
        Vector3 secondResumed =
            simulation.Entities.GetComponent<WorldTransform>(
                second).Position;

        Assert.True(
            Vector3.Distance(firstResumed, firstStopped) > 0.0f);
        Assert.True(
            Vector3.Distance(secondResumed, secondStopped) > 0.0f);
        Assert.Equal(
            BattlefieldSupplyStatus.Supplied,
            simulation.Entities.GetComponent<UnitSupplyState>(
                first).Status);
        Assert.Equal(
            BattlefieldSupplyStatus.Supplied,
            simulation.Entities.GetComponent<UnitSupplyState>(
                second).Status);
    }

    [Fact]
    public void ResupplyCommandUsesExistingMovementOrderPath()
    {
        var simulation = new SimulationCoordinator();
        var inventories = new InventoryStore();

        InventoryId depotInventory =
            inventories.CreateInventory(
                new InventorySpecification(100.0));
        EntityId depot =
            simulation.Entities.CreateEntity();
        var depotPosition =
            new Vector3(50.0f, 0.0f, 10.0f);
        simulation.Entities.AddComponent(
            depot,
            new WorldTransform(
                depotPosition,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            depot,
            new SupplyDepot(
                depotInventory,
                LocalPlayer));
        simulation.Entities.AddComponent(
            depot,
            new SupplyProvider(
                depotInventory,
                LocalPlayer,
                resupplyRangeMeters: 20.0f));

        EntityId unit =
            CreateSuppliedUnit(
                simulation,
                inventories,
                Vector3.Zero,
                fuelCapacity: 20.0,
                initialFuel: 1.0,
                ammunitionCapacity: 10.0,
                initialAmmunition: 1.0,
                fuelConsumptionPerMeter: 0.0);
        simulation.Entities.AddComponent(
            unit,
            GroundMovement.CreateDefault());

        var command =
            new ResupplyCommand(
                LocalPlayer,
                [unit],
                SimulationTick.Zero);

        simulation.SubmitCommand(
            command,
            new SimulationTick(1),
            new SimulationCommandSource(LocalPlayer.Value));
        simulation.AdvanceOneTick();

        Assert.Equal(1, command.AcceptedTargetCount);
        Assert.Equal(0, command.RejectedTargetCount);
        Assert.True(
            simulation.Entities.TryGetComponent(
                unit,
                out ResupplyOrder resupplyOrder));
        Assert.Equal(depot, resupplyOrder.Provider);
        Assert.True(
            simulation.Entities.TryGetComponent(
                unit,
                out MovementOrder movementOrder));
        Assert.Equal(
            15.0f,
            Vector3.Distance(
                movementOrder.WorldTarget,
                depotPosition),
            precision: 3);
        Assert.True(
            Vector3.Distance(
                movementOrder.WorldTarget,
                Vector3.Zero) <
            Vector3.Distance(
                depotPosition,
                Vector3.Zero));
    }

    private static EntityId CreateSuppliedUnit(
        SimulationCoordinator simulation,
        InventoryStore inventories,
        Vector3 position,
        double fuelCapacity,
        double initialFuel,
        double ammunitionCapacity,
        double initialAmmunition,
        double fuelConsumptionPerMeter,
        BattlefieldSupplyPriority priority =
            BattlefieldSupplyPriority.Normal)
    {
        EntityId entity =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new WorldTransform(
                position,
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            entity,
            new ControllableEntity(
                LocalPlayer,
                ControllableEntityCategory.Unit));

        BattlefieldSupplyFactory.AttachUnitSupply(
            simulation.Entities,
            inventories,
            entity,
            fuelCapacity,
            ammunitionCapacity,
            fuelConsumptionPerMeter,
            initialFuel,
            initialAmmunition,
            priority);

        return entity;
    }
}
