using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class AutomaticResupplyDecisionSystemTests
{
    private static readonly PlayerId Owner =
        new(1);

    [Fact]
    public void HighPriorityRecipientClaimsMobileProviderBeforeEarlierNormalRecipient()
    {
        var simulation =
            new SimulationCoordinator();
        var inventories =
            new InventoryStore();
        simulation.RegisterSystem(
            new AutomaticResupplyDecisionSystem(
                inventories));

        var cargo =
            new CargoTransportSystem(
                new LogisticsNetwork(),
                inventories);
        EntityId provider =
            SupplyTruckFactory.Create(
                simulation.Entities,
                inventories,
                Vector3.Zero,
                Owner,
                cargo);
        SupplyTruck truck =
            simulation.Entities.GetComponent<SupplyTruck>(
                provider);

        Assert.True(
            inventories.Add(
                truck.InventoryId,
                ResourceIds.Fuel,
                110.0).Succeeded);

        EntityId normal =
            CreateRecipient(
                simulation,
                inventories,
                new Vector3(
                    1.0f,
                    0.0f,
                    0.0f),
                BattlefieldSupplyPriority.Normal);
        EntityId high =
            CreateRecipient(
                simulation,
                inventories,
                new Vector3(
                    2.0f,
                    0.0f,
                    0.0f),
                BattlefieldSupplyPriority.High);

        Assert.True(
            normal < high);

        simulation.AdvanceOneTick();

        Assert.False(
            simulation.Entities.HasComponent<ResupplyOrder>(
                normal));
        Assert.Equal(
            provider,
            simulation.Entities
                .GetComponent<ResupplyOrder>(
                    high)
                .Provider);
    }

    private static EntityId CreateRecipient(
        SimulationCoordinator simulation,
        InventoryStore inventories,
        Vector3 position,
        BattlefieldSupplyPriority priority)
    {
        EntityId entity =
            simulation.Entities.CreateEntity();
        InventoryId inventory =
            inventories.CreateInventory(
                new InventorySpecification(
                    20.0));

        Assert.True(
            inventories.Add(
                inventory,
                ResourceIds.Fuel,
                2.0).Succeeded);

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
            new UnitFuelState(
                inventory,
                capacity: 20.0,
                consumptionPerMeter: 0.1));
        simulation.Entities.AddComponent(
            entity,
            new AutomaticResupplyPolicy(
                ammunitionThreshold: 0.2,
                fuelThreshold: 0.55,
                enabled: true));
        simulation.Entities.AddComponent(
            entity,
            new UnitSupplyPriority(
                priority));

        return entity;
    }
}
