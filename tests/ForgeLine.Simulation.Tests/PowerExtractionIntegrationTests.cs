using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Simulation.Tests;

public sealed class PowerExtractionIntegrationTests
{
    [Fact]
    public void BrownoutScalesExtractionThroughAuthoritativePowerState()
    {
        var power = new PowerNetworkSystem();
        var extraction = new ResourceExtractionSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(power);
        simulation.RegisterSystem(extraction);

        PowerNetworkId network = new(1);
        AddGenerator(simulation, network, 5.0);
        EntityId deposit = AddDeposit(simulation, 100.0);
        EntityId extractor = AddPoweredExtractor(
            simulation,
            deposit,
            network,
            powerDemand: 10.0);

        simulation.AdvanceOneTick();

        PowerConsumer consumer =
            simulation.Entities.GetComponent<PowerConsumer>(extractor);
        ResourceExtractor extractorState =
            simulation.Entities.GetComponent<ResourceExtractor>(extractor);
        ResourceDeposit depositState =
            simulation.Entities.GetComponent<ResourceDeposit>(deposit);

        Assert.Equal(PowerOperationalState.Brownout, consumer.State);
        Assert.Equal(ResourceExtractorState.PowerConstrained, extractorState.State);
        Assert.InRange(consumer.SupplyFraction, 0.499999999, 0.500000001);
        Assert.InRange(
            depositState.RemainingQuantity,
            99.749999999,
            99.750000001);
        Assert.InRange(
            extraction.Metrics.LastTickExtractedQuantity,
            0.249999999,
            0.250000001);
    }

    [Fact]
    public void UnpoweredExtractorPausesWithoutDrainingDeposit()
    {
        var power = new PowerNetworkSystem();
        var extraction = new ResourceExtractionSystem();
        var simulation = new SimulationCoordinator();
        simulation.RegisterSystem(power);
        simulation.RegisterSystem(extraction);

        PowerNetworkId network = new(2);
        EntityId deposit = AddDeposit(simulation, 100.0);
        EntityId extractor = AddPoweredExtractor(
            simulation,
            deposit,
            network,
            powerDemand: 10.0);

        simulation.AdvanceOneTick();

        Assert.Equal(
            ResourceExtractorState.PowerUnavailable,
            simulation.Entities.GetComponent<ResourceExtractor>(extractor).State);
        Assert.Equal(
            PowerOperationalState.Offline,
            simulation.Entities.GetComponent<PowerConsumer>(extractor).State);
        Assert.Equal(
            100.0,
            simulation.Entities.GetComponent<ResourceDeposit>(deposit).RemainingQuantity);
        Assert.Equal(0.0, extraction.Metrics.LastTickExtractedQuantity);
        Assert.Equal(1, extraction.Metrics.BlockedExtractorCount);
    }

    private static void AddGenerator(
        SimulationCoordinator simulation,
        PowerNetworkId network,
        double generation)
    {
        EntityId generator = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            generator,
            new PowerNetworkMembership(network));
        simulation.Entities.AddComponent(
            generator,
            new PowerGenerator(generation));
    }

    private static EntityId AddDeposit(
        SimulationCoordinator simulation,
        double totalQuantity)
    {
        EntityId deposit = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            deposit,
            new ResourceDeposit(
                ResourceIds.FerrousOre,
                new AxisAlignedBounds(
                    Vector3.Zero,
                    new Vector3(2.0f, 1.0f, 2.0f)),
                totalQuantity,
                baseExtractionRatePerSecond: 10.0));
        return deposit;
    }

    private static EntityId AddPoweredExtractor(
        SimulationCoordinator simulation,
        EntityId deposit,
        PowerNetworkId network,
        double powerDemand)
    {
        EntityId extractor = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(
            extractor,
            new ResourceExtractor(
                deposit,
                ResourceIds.FerrousOre,
                maximumExtractionRatePerSecond: 10.0));
        simulation.Entities.AddComponent(
            extractor,
            new PowerNetworkMembership(network));
        simulation.Entities.AddComponent(
            extractor,
            new PowerConsumer(
                powerDemand,
                PowerPriority.Industrial));
        return extractor;
    }
}
