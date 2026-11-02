using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorMaterialFairnessTests
{
    private const double Tolerance = 1e-7;

    [Theory]
    [InlineData(2, 20)]
    [InlineData(3, 20)]
    [InlineData(7, 20)]
    [InlineData(2, 30)]
    [InlineData(3, 30)]
    [InlineData(7, 30)]
    public void ContinuouslyDemandedMaterialsShareOneNominalBudget(int materials, int ticksPerSecond)
    {
        var fixture = new ConveyorTestFixture(917, ticksPerSecond);
        EntityId source = fixture.AddStorage(new(41, 0, 80), capacity: 12_000);
        EntityId destination = fixture.AddStorage(new(103, 0, 80), capacity: 12_000);
        EntityId[] carriers = fixture.Connect(source, destination);
        ResourceId[] resources = ConveyorSimulationSystem.EligibleResources.Take(materials).ToArray();
        foreach (ResourceId resource in resources) fixture.Give(source, resource, 1_000);
        double[] original = resources.Select(fixture.Total).ToArray();

        // Allow the physical pipeline to fill before measuring receiver throughput.
        fixture.Step(20);
        double[] before = resources.Select(resource => fixture.Stock(destination, resource)).ToArray();
        const int window = 120;
        for (int tick = 0; tick < window; tick++)
        {
            fixture.Step();
            Assert.All(carriers, carrier =>
            {
                ConveyorFlowState flow = fixture.Flow(carrier);
                Assert.InRange(flow.InputPerSecond, 0, 100 + Tolerance);
                Assert.InRange(flow.OutputPerSecond, 0, 100 + Tolerance);
                Assert.InRange(fixture.Inventories.GetTotalQuantity(flow.Buffer), 0, flow.Capacity + Tolerance);
            });
        }

        double[] received = resources.Select((resource, index) =>
            fixture.Stock(destination, resource) - before[index]).ToArray();
        double budget = ConveyorSimulationSystem.NominalThroughput * fixture.TickSeconds * window;
        Assert.InRange(received.Sum(), budget - Tolerance, budget + Tolerance);
        Assert.All(received, amount => Assert.InRange(Math.Abs(amount - budget / materials), 0, 1 + Tolerance));
        for (int index = 0; index < resources.Length; index++)
            Assert.Equal(original[index], fixture.Total(resources[index]), 6);
    }

    [Theory]
    [InlineData(2, 20)]
    [InlineData(3, 30)]
    public void BrownoutScalesTheCommonBudgetWithoutChangingResourceShares(int materials, int ticksPerSecond)
    {
        var fixture = new ConveyorTestFixture(17, ticksPerSecond);
        EntityId source = fixture.AddStorage(new(41, 0, 80), capacity: 12_000);
        EntityId destination = fixture.AddStorage(new(103, 0, 80), capacity: 12_000);
        EntityId[] carriers = fixture.Connect(source, destination);
        ResourceId[] resources = ConveyorSimulationSystem.EligibleResources.Take(materials).ToArray();
        foreach (ResourceId resource in resources) fixture.Give(source, resource, 1_000);
        foreach (EntityId entity in carriers.Append(source).Append(destination))
            fixture.SetPower(entity, .4);

        fixture.Step(20);
        double[] before = resources.Select(resource => fixture.Stock(destination, resource)).ToArray();
        const int window = 120;
        fixture.Step(window);
        double[] received = resources.Select((resource, index) =>
            fixture.Stock(destination, resource) - before[index]).ToArray();
        double expected = ConveyorSimulationSystem.NominalThroughput * .4 * fixture.TickSeconds * window;
        Assert.InRange(received.Sum(), expected - Tolerance, expected + Tolerance);
        Assert.All(received, amount =>
            Assert.InRange(Math.Abs(amount - expected / materials), 0, 1 + Tolerance));

        foreach (EntityId entity in carriers.Append(source).Append(destination)) fixture.SetPower(entity, 0);
        double halted = resources.Sum(resource => fixture.Stock(destination, resource));
        fixture.Step(5);
        Assert.Equal(halted, resources.Sum(resource => fixture.Stock(destination, resource)), 6);
        foreach (EntityId entity in carriers.Append(source).Append(destination)) fixture.SetPower(entity, 1);
        fixture.Step();
        Assert.InRange(resources.Sum(resource => fixture.Stock(destination, resource)) - halted,
            0, ConveyorSimulationSystem.NominalThroughput * fixture.TickSeconds + Tolerance);
    }

    [Fact]
    public void FullMaterialMaximumReassignsAllServiceAndResumesEvenSharesWhenReleased()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 80), capacity: 12_000);
        EntityId destination = fixture.AddStorage(new(103, 0, 80), capacity: 12_000);
        _ = fixture.Connect(source, destination);
        ResourceId first = ResourceIds.FerrousOre;
        ResourceId second = ResourceIds.Steel;
        fixture.Give(source, first, 1_000);
        fixture.Give(source, second, 1_000);
        double originalFirst = fixture.Total(first);
        double originalSecond = fixture.Total(second);
        EntityId cap = fixture.AddPolicy(destination, first, 0, 0, 0);

        fixture.Step(20);
        double start = fixture.Stock(destination, second);
        fixture.Step(40);
        double delivered = fixture.Stock(destination, second) - start;
        Assert.InRange(delivered, 200 - Tolerance, 200 + Tolerance);
        Assert.Equal(0, fixture.Stock(destination, first));

        fixture.Entities.SetComponent(cap,
            new LogisticsStockPolicy(destination, first, 0, 0, 0, enabled: false));
        // The former single-material backlog must clear before measuring
        // steady-state fairness at the receiving end of the physical line.
        fixture.Step(50);
        double firstBefore = fixture.Stock(destination, first);
        double secondBefore = fixture.Stock(destination, second);
        fixture.Step(40);
        double firstAfter = fixture.Stock(destination, first) - firstBefore;
        double secondAfter = fixture.Stock(destination, second) - secondBefore;
        Assert.InRange(firstAfter + secondAfter, 200 - Tolerance, 200 + Tolerance);
        Assert.InRange(Math.Abs(firstAfter - secondAfter), 0, 1 + Tolerance);
        Assert.Equal(1_000, fixture.Total(first), 6);
        Assert.Equal(1_000, fixture.Total(second), 6);
    }
}
