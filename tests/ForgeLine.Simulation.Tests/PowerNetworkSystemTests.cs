using ForgeLine.Core;
using ForgeLine.Economy;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class PowerNetworkSystemTests
{
    [Fact]
    public void PowerProfilesCreateDataDrivenComponents()
    {
        var profile = new PowerProfileDefinition
        {
            Key = "power.fixture.hybrid",
            GenerationCapacity = 125.0,
            Demand = 25.0,
            Priority = PowerPriority.Critical
        };
        var catalog = new PowerProfileCatalog([profile]);

        Assert.Equal(1, catalog.Count);
        Assert.Equal(125.0, catalog["power.fixture.hybrid"].CreateGenerator().MaximumGeneration);

        PowerConsumer consumer = catalog["power.fixture.hybrid"].CreateConsumer();
        Assert.Equal(25.0, consumer.Demand);
        Assert.Equal(PowerPriority.Critical, consumer.Priority);
    }

    [Fact]
    public void SurplusGenerationPowersAllConsumers()
    {
        var system = new PowerNetworkSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        PowerNetworkId network = new(1);
        AddGenerator(simulation, network, 100.0);
        EntityId first = AddConsumer(
            simulation,
            network,
            20.0,
            PowerPriority.Critical);
        EntityId second = AddConsumer(
            simulation,
            network,
            30.0,
            PowerPriority.Industrial);

        simulation.AdvanceOneTick();

        AssertPowered(simulation, first, 20.0);
        AssertPowered(simulation, second, 30.0);

        PowerNetworkReadModel readModel = Assert.Single(system.Networks);
        Assert.Equal(100.0, readModel.Generation);
        Assert.Equal(50.0, readModel.Demand);
        Assert.Equal(50.0, readModel.AllocatedPower);
        Assert.Equal(50.0, readModel.SpareCapacity);
        Assert.Equal(0.0, readModel.Deficit);
    }

    [Fact]
    public void ExactCapacityPowersConsumersWithoutBrownout()
    {
        var system = new PowerNetworkSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        PowerNetworkId network = new(2);
        AddGenerator(simulation, network, 50.0);
        EntityId critical = AddConsumer(
            simulation,
            network,
            20.0,
            PowerPriority.Critical);
        EntityId industrial = AddConsumer(
            simulation,
            network,
            30.0,
            PowerPriority.Industrial);

        simulation.AdvanceOneTick();

        AssertPowered(simulation, critical, 20.0);
        AssertPowered(simulation, industrial, 30.0);
        Assert.Equal(0, system.Metrics.BrownoutConsumerCount);
        Assert.Equal(0.0, system.Metrics.TotalSpareCapacity);
        Assert.Equal(0.0, system.Metrics.TotalDeficit);
    }

    [Fact]
    public void ShortageHonorsPriorityAndSharesBrownoutWithinTier()
    {
        var system = new PowerNetworkSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        PowerNetworkId network = new(3);
        AddGenerator(simulation, network, 100.0);
        EntityId critical = AddConsumer(
            simulation,
            network,
            40.0,
            PowerPriority.Critical);
        EntityId industrialA = AddConsumer(
            simulation,
            network,
            40.0,
            PowerPriority.Industrial);
        EntityId industrialB = AddConsumer(
            simulation,
            network,
            40.0,
            PowerPriority.Industrial);
        EntityId optional = AddConsumer(
            simulation,
            network,
            10.0,
            PowerPriority.Optional);

        simulation.AdvanceOneTick();

        AssertPowered(simulation, critical, 40.0);
        AssertBrownout(simulation, industrialA, 30.0);
        AssertBrownout(simulation, industrialB, 30.0);
        AssertOffline(simulation, optional);

        PowerNetworkReadModel readModel = Assert.Single(system.Networks);
        Assert.Equal(130.0, readModel.Demand);
        Assert.Equal(100.0, readModel.AllocatedPower);
        Assert.Equal(30.0, readModel.Deficit);
        Assert.Equal(1, readModel.PoweredConsumerCount);
        Assert.Equal(2, readModel.BrownoutConsumerCount);
        Assert.Equal(1, readModel.OfflineConsumerCount);
    }

    [Fact]
    public void LogicalNetworksRemainIsolated()
    {
        var system = new PowerNetworkSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        PowerNetworkId poweredNetwork = new(10);
        PowerNetworkId isolatedNetwork = new(20);

        AddGenerator(simulation, poweredNetwork, 100.0);
        EntityId powered = AddConsumer(
            simulation,
            poweredNetwork,
            25.0,
            PowerPriority.Industrial);
        EntityId offline = AddConsumer(
            simulation,
            isolatedNetwork,
            25.0,
            PowerPriority.Critical);

        simulation.AdvanceOneTick();

        AssertPowered(simulation, powered, 25.0);
        AssertOffline(simulation, offline);
        Assert.Equal(2, system.Networks.Count);
        Assert.Equal(25.0, system.Metrics.TotalDeficit);
    }

    [Fact]
    public void RemovingGeneratorRecomputesConsumerStateNextTick()
    {
        var system = new PowerNetworkSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        PowerNetworkId network = new(30);
        EntityId generator = AddGenerator(simulation, network, 50.0);
        EntityId consumer = AddConsumer(
            simulation,
            network,
            50.0,
            PowerPriority.Critical);

        simulation.AdvanceOneTick();
        AssertPowered(simulation, consumer, 50.0);

        Assert.True(simulation.Entities.DestroyEntity(generator));
        simulation.AdvanceOneTick();

        AssertOffline(simulation, consumer);
        Assert.Equal(50.0, system.Metrics.TotalDeficit);
    }

    [Fact]
    public void ActivationChangesGenerationAndDemandAuthoritatively()
    {
        var system = new PowerNetworkSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        PowerNetworkId network = new(40);
        EntityId generator = AddGenerator(simulation, network, 40.0);
        EntityId consumer = AddConsumer(
            simulation,
            network,
            40.0,
            PowerPriority.Industrial);

        simulation.AdvanceOneTick();
        AssertPowered(simulation, consumer, 40.0);

        PowerGenerator generatorState =
            simulation.Entities.GetComponent<PowerGenerator>(generator);
        simulation.Entities.SetComponent(
            generator,
            generatorState.WithEnabled(false));
        simulation.AdvanceOneTick();
        AssertOffline(simulation, consumer);

        PowerConsumer consumerState =
            simulation.Entities.GetComponent<PowerConsumer>(consumer);
        simulation.Entities.SetComponent(
            consumer,
            consumerState.WithEnabled(false));
        simulation.AdvanceOneTick();

        Assert.Equal(0.0, system.Metrics.TotalDemand);
        Assert.Equal(1, system.Metrics.OfflineConsumerCount);
    }

    [Fact]
    public void MissingMembershipProducesExplicitOfflineState()
    {
        var system = new PowerNetworkSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(system);

        EntityId generator = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            generator,
            new PowerGenerator(100.0));

        EntityId consumer = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            consumer,
            new PowerConsumer(10.0, PowerPriority.Critical));

        simulation.AdvanceOneTick();

        Assert.Equal(
            PowerGeneratorState.Offline,
            simulation.Entities.GetComponent<PowerGenerator>(generator).State);
        AssertOffline(simulation, consumer);
        Assert.Equal(1, system.Metrics.UnassignedGeneratorCount);
        Assert.Equal(1, system.Metrics.UnassignedConsumerCount);
    }

    [Fact]
    public void RepeatedFixtureProducesIdenticalAllocation()
    {
        (PowerNetworkMetrics Metrics, PowerConsumer[] Consumers) first =
            RunDeterministicFixture();
        (PowerNetworkMetrics Metrics, PowerConsumer[] Consumers) second =
            RunDeterministicFixture();

        Assert.Equal(first.Metrics, second.Metrics);
        Assert.Equal(first.Consumers, second.Consumers);
    }

    [Fact]
    public void TenThousandConsumersRemainBoundedUnderShortage()
    {
        const int consumerCount = 10_000;

        var system = new PowerNetworkSystem();
        var simulation = new SimulationCoordinator(
            initialEntityCapacity: consumerCount + 16);
        simulation.RegisterSystem(system);

        PowerNetworkId network = new(60);
        AddGenerator(simulation, network, 7_500.0);

        EntityId first = default;
        EntityId last = default;

        for (int index = 0; index < consumerCount; index++)
        {
            EntityId entity = AddConsumer(
                simulation,
                network,
                1.0,
                PowerPriority.Industrial);
            if (index == 0)
            {
                first = entity;
            }

            last = entity;
        }

        simulation.AdvanceOneTick();

        Assert.Equal(consumerCount, system.Metrics.ConsumerCount);
        Assert.Equal(consumerCount, system.Metrics.BrownoutConsumerCount);
        Assert.Equal(7_500.0, system.Metrics.TotalAllocatedPower);
        AssertBrownout(simulation, first, 0.75);
        AssertBrownout(simulation, last, 0.75);
    }

    private static (PowerNetworkMetrics Metrics, PowerConsumer[] Consumers)
        RunDeterministicFixture()
    {
        var system = new PowerNetworkSystem();
        var simulation = new SimulationCoordinator(seed: 0x504F574552UL);
        simulation.RegisterSystem(system);

        PowerNetworkId network = new(50);
        AddGenerator(simulation, network, 75.0);

        EntityId critical = AddConsumer(
            simulation,
            network,
            20.0,
            PowerPriority.Critical);
        EntityId industrialA = AddConsumer(
            simulation,
            network,
            40.0,
            PowerPriority.Industrial);
        EntityId industrialB = AddConsumer(
            simulation,
            network,
            40.0,
            PowerPriority.Industrial);

        simulation.RunTicks(32, TestContext.Current.CancellationToken);

        return (
            system.Metrics,
            [
                simulation.Entities.GetComponent<PowerConsumer>(critical),
                simulation.Entities.GetComponent<PowerConsumer>(industrialA),
                simulation.Entities.GetComponent<PowerConsumer>(industrialB)
            ]);
    }

    private static EntityId AddGenerator(
        SimulationCoordinator simulation,
        PowerNetworkId network,
        double generation)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new PowerNetworkMembership(network));
        simulation.Entities.AddComponent(
            entity,
            new PowerGenerator(generation));
        return entity;
    }

    private static EntityId AddConsumer(
        SimulationCoordinator simulation,
        PowerNetworkId network,
        double demand,
        PowerPriority priority)
    {
        EntityId entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            entity,
            new PowerNetworkMembership(network));
        simulation.Entities.AddComponent(
            entity,
            new PowerConsumer(demand, priority));
        return entity;
    }

    private static void AssertPowered(
        SimulationCoordinator simulation,
        EntityId entity,
        double expectedAllocation)
    {
        PowerConsumer consumer =
            simulation.Entities.GetComponent<PowerConsumer>(entity);

        Assert.Equal(PowerOperationalState.Powered, consumer.State);
        Assert.Equal(expectedAllocation, consumer.AllocatedPower);
        Assert.Equal(1.0, consumer.SupplyFraction);
    }

    private static void AssertBrownout(
        SimulationCoordinator simulation,
        EntityId entity,
        double expectedAllocation)
    {
        PowerConsumer consumer =
            simulation.Entities.GetComponent<PowerConsumer>(entity);

        Assert.Equal(PowerOperationalState.Brownout, consumer.State);
        Assert.InRange(
            consumer.AllocatedPower,
            expectedAllocation - 0.000000001,
            expectedAllocation + 0.000000001);
        Assert.InRange(consumer.SupplyFraction, 0.0, 1.0);
    }

    private static void AssertOffline(
        SimulationCoordinator simulation,
        EntityId entity)
    {
        PowerConsumer consumer =
            simulation.Entities.GetComponent<PowerConsumer>(entity);

        Assert.Equal(PowerOperationalState.Offline, consumer.State);
        Assert.Equal(0.0, consumer.AllocatedPower);
        Assert.Equal(0.0, consumer.SupplyFraction);
    }
}
