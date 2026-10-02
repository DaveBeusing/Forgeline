using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class RepairRecoverySystemTests
{
    private static readonly PlayerId Owner = new(1);

    [Fact]
    public void RepairConsumesPhysicalSteelAndRestoresHealth()
    {
        var simulation =
            new SimulationCoordinator(
                ticksPerSecond: 20);
        var inventories =
            new InventoryStore();
        var recovery =
            new RepairRecoverySystem(
                inventories);
        simulation.RegisterSystem(recovery);

        InventoryId providerInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    totalCapacity: 100.0));
        Assert.True(
            inventories.Add(
                providerInventory,
                ResourceIds.Steel,
                10.0).Succeeded);

        EntityId provider =
            CreateProvider(
                simulation,
                providerInventory,
                Vector3.Zero);

        EntityId unit =
            CreateDamagedUnit(
                simulation,
                new Vector3(5.0f, 0.0f, 0.0f),
                currentHealth: 50.0);

        simulation.AdvanceOneTick();

        HealthState health =
            simulation.Entities.GetComponent<HealthState>(
                unit);
        RepairRecoveryState state =
            simulation.Entities.GetComponent<RepairRecoveryState>(
                unit);

        Assert.Equal(55.0, health.Current, precision: 6);
        Assert.Equal(9.0,
            inventories.GetQuantity(
                providerInventory,
                ResourceIds.Steel),
            precision: 6);
        Assert.Equal(provider, state.Provider);
        Assert.Equal(
            RepairRecoveryStatus.Repairing,
            state.Status);
        Assert.Equal(5.0, state.HealthRestoredThisTick, precision: 6);
        Assert.Equal(1.0, state.ResourceConsumedThisTick, precision: 6);
    }

    [Fact]
    public void RepairStopsWhenProviderHasNoMaterial()
    {
        var simulation =
            new SimulationCoordinator();
        var inventories =
            new InventoryStore();
        var recovery =
            new RepairRecoverySystem(
                inventories);
        simulation.RegisterSystem(recovery);

        InventoryId providerInventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    totalCapacity: 100.0));
        EntityId provider =
            CreateProvider(
                simulation,
                providerInventory,
                Vector3.Zero);
        EntityId unit =
            CreateDamagedUnit(
                simulation,
                new Vector3(5.0f, 0.0f, 0.0f),
                currentHealth: 50.0);

        simulation.AdvanceOneTick();

        Assert.Equal(
            50.0,
            simulation.Entities.GetComponent<HealthState>(
                unit).Current,
            precision: 6);
        RepairRecoveryState state =
            simulation.Entities.GetComponent<RepairRecoveryState>(
                unit);
        Assert.Equal(provider, state.Provider);
        Assert.Equal(
            RepairRecoveryStatus.NoMaterial,
            state.Status);
        Assert.Equal(
            1,
            recovery.Metrics.MaterialBlockedUnits);
    }

    [Fact]
    public void RetreatPlannerPrefersNearestCombinedRepairAndSupplyProvider()
    {
        var simulation =
            new SimulationCoordinator();
        var inventories =
            new InventoryStore();

        InventoryId nearInventory =
            inventories.CreateInventory(
                new InventorySpecification(100.0));
        InventoryId farInventory =
            inventories.CreateInventory(
                new InventorySpecification(100.0));

        InventoryId supplyOnlyInventory =
            inventories.CreateInventory(
                new InventorySpecification(100.0));
        EntityId supplyOnly =
            simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            supplyOnly,
            new WorldTransform(
                new Vector3(30.0f, 0.0f, 0.0f),
                Quaternion.Identity,
                Vector3.One));
        simulation.Entities.AddComponent(
            supplyOnly,
            new SupplyProvider(
                supplyOnlyInventory,
                Owner,
                resupplyRangeMeters: 30.0f));

        EntityId near =
            CreateProvider(
                simulation,
                nearInventory,
                new Vector3(100.0f, 0.0f, 0.0f));
        simulation.Entities.AddComponent(
            near,
            new SupplyProvider(
                nearInventory,
                Owner,
                resupplyRangeMeters: 30.0f));

        EntityId far =
            CreateProvider(
                simulation,
                farInventory,
                new Vector3(300.0f, 0.0f, 0.0f));
        simulation.Entities.AddComponent(
            far,
            new SupplyProvider(
                farInventory,
                Owner,
                resupplyRangeMeters: 30.0f));

        EntityId first =
            CreateDamagedUnit(
                simulation,
                Vector3.Zero,
                currentHealth: 80.0);
        EntityId second =
            CreateDamagedUnit(
                simulation,
                new Vector3(20.0f, 0.0f, 0.0f),
                currentHealth: 80.0);

        Assert.True(
            RetreatRecoveryPlanner.TryResolve(
                simulation.Entities,
                Owner,
                [first, second],
                out EntityId provider,
                out Vector3 destination,
                out RetreatRecoveryReason reason));

        Assert.Equal(near, provider);
        Assert.Equal(
            RetreatRecoveryReason.RepairAndSupply,
            reason);
        Assert.True(
            Vector3.Distance(
                destination,
                new Vector3(100.0f, 0.0f, 0.0f)) <
            30.0f);
    }

    private static EntityId CreateProvider(
        SimulationCoordinator simulation,
        InventoryId inventory,
        Vector3 position)
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
            new RepairProvider(
                inventory,
                Owner,
                repairRangeMeters: 25.0f,
                healthPerTick: 5.0,
                ResourceIds.Steel,
                resourcePerHealth: 0.2));
        return entity;
    }

    private static EntityId CreateDamagedUnit(
        SimulationCoordinator simulation,
        Vector3 position,
        double currentHealth)
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
                Owner,
                ControllableEntityCategory.Unit));
        simulation.Entities.AddComponent(
            entity,
            new Combatant(
                new FactionId(1)));
        simulation.Entities.AddComponent(
            entity,
            new HealthState(
                currentHealth,
                maximum: 100.0));
        return entity;
    }
}
