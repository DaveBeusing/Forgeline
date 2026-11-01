using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorFlowTests
{
    private const double Tolerance = 1e-8;

    [Theory]
    [InlineData(17UL, 20)]
    [InlineData(917UL, 30)]
    public void MixedResourcesShareEveryBufferAndTransferBudget(ulong seed, int ticksPerSecond)
    {
        var fixture = new ConveyorTestFixture(seed, ticksPerSecond);
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        EntityId[] line = fixture.Connect(source, destination);
        ResourceId[] resources = ConveyorSimulationSystem.EligibleResources.ToArray();
        foreach (ResourceId resource in resources) fixture.Give(source, resource, 100.125);
        double[] totals = resources.Select(fixture.Total).ToArray();

        // All eligible resources, including interim fluids, share the same finite 100/s budget.
        for (int tick = 0; tick < 10; tick++)
        {
            double before = fixture.Inventories.GetTotalQuantity(fixture.Inventory(source));
            fixture.Step();
            Near(100 * fixture.TickSeconds,
                before - fixture.Inventories.GetTotalQuantity(fixture.Inventory(source)));
            AssertFlowBounds(fixture);
            AssertTotals(fixture, resources, totals);
        }
        foreach (ResourceId resource in resources) Assert.True(fixture.Stock(source, resource) < 100.125);

        fixture.RunUntil(() => resources.All(resource => fixture.Stock(destination, resource) >= 100.125 - Tolerance));
        foreach (ResourceId resource in resources) Near(100.125, fixture.Stock(destination, resource));
        Assert.All(line, entity => Near(0, fixture.Inventories.GetTotalQuantity(fixture.Buffer(entity))));
        AssertTotals(fixture, resources, totals);
    }

    [Theory]
    [InlineData(17UL, 20)]
    [InlineData(917UL, 30)]
    public void PositiveSubEpsilonCargoMovesExactlyOneLinkPerTick(ulong seed, int ticksPerSecond)
    {
        var fixture = new ConveyorTestFixture(seed, ticksPerSecond);
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        EntityId[] line = fixture.Connect(source, destination);
        const double cargo = 1e-10;
        fixture.Give(source, ResourceIds.FerrousOre, cargo);

        for (int hop = 0; hop < line.Length; hop++)
        {
            fixture.Step();
            Assert.Equal(0, fixture.Stock(source, ResourceIds.FerrousOre));
            Assert.Equal(0, fixture.Stock(destination, ResourceIds.FerrousOre));
            for (int index = 0; index < line.Length; index++)
                Assert.Equal(index == hop ? cargo : 0, fixture.Stock(line[index], ResourceIds.FerrousOre));
            Assert.Equal(cargo, fixture.Total(ResourceIds.FerrousOre));
        }
        fixture.Step();
        Assert.Equal(cargo, fixture.Stock(destination, ResourceIds.FerrousOre));
        Assert.All(line, entity => Assert.Equal(0, fixture.Stock(entity, ResourceIds.FerrousOre)));
    }

    [Theory]
    [InlineData(17UL, 20)]
    [InlineData(917UL, 30)]
    public void MergerSharesIngressAndKeepsSaturatedInputsWithinOneQuantum(ulong seed, int ticksPerSecond)
    {
        var fixture = new ConveyorTestFixture(seed, ticksPerSecond);
        EntityId destination = fixture.AddStorage(new Vector3(128, 0, 155), orientation: BuildingOrientation.West);
        EntityId merger = fixture.Build(ConveyorModule.Merger, new Vector3(128, 0, 128));
        EntityId left = fixture.Build(ConveyorModule.Standard, new Vector3(124, 0, 120));
        EntityId right = fixture.Build(ConveyorModule.Standard, new Vector3(132, 0, 120));
        BuildToDestination(fixture, new Vector3(128, 0, 132), destination);
        fixture.Give(left, ResourceIds.FerrousOre, 200);
        fixture.Give(right, ResourceIds.Silicates, 200);

        double expectedTransferred = 0;
        for (int tick = 0; tick < 20; tick++)
        {
            fixture.Step();
            expectedTransferred += 100 * fixture.TickSeconds;
            Near(100, fixture.Flow(merger).InputPerSecond);
            AssertFlowBounds(fixture);
            double leftSent = 200 - fixture.Stock(left, ResourceIds.FerrousOre);
            double rightSent = 200 - fixture.Stock(right, ResourceIds.Silicates);
            Near(expectedTransferred, leftSent + rightSent);
            Assert.InRange(Math.Abs(leftSent - rightSent), 0, 1 + Tolerance);
            Near(200, fixture.Total(ResourceIds.FerrousOre));
            Near(200, fixture.Total(ResourceIds.Silicates));
        }
        Assert.True(fixture.Stock(destination, ResourceIds.FerrousOre) > 0);
        Assert.True(fixture.Stock(destination, ResourceIds.Silicates) > 0);
    }

    [Theory]
    [InlineData(17UL, 20, false)]
    [InlineData(917UL, 30, false)]
    [InlineData(17UL, 20, true)]
    [InlineData(917UL, 30, true)]
    public void SplitterSharesEgressAndReusesCapacityWhenOneBranchIsBlocked(ulong seed, int ticksPerSecond, bool blockLeft)
    {
        var fixture = new ConveyorTestFixture(seed, ticksPerSecond);
        EntityId destination = fixture.AddStorage(new Vector3(128, 0, 139), orientation: BuildingOrientation.West);
        EntityId splitter = fixture.Build(ConveyorModule.Splitter, new Vector3(128, 0, 96));
        EntityId left = fixture.Build(ConveyorModule.Standard, new Vector3(124, 0, 104));
        EntityId right = fixture.Build(ConveyorModule.Standard, new Vector3(132, 0, 104));
        _ = fixture.Build(ConveyorModule.Merger, new Vector3(128, 0, 112));
        BuildToDestination(fixture, new Vector3(128, 0, 116), destination);
        fixture.Give(splitter, ResourceIds.FerrousOre, 100);
        if (blockLeft)
        {
            fixture.Give(left, ResourceIds.Silicates, 200);
            fixture.SetPower(left, 0);
        }

        double sentLeft = 0, sentRight = 0;
        for (int tick = 0; tick < 15; tick++)
        {
            fixture.Step();
            ConveyorFlowState flow = fixture.Flow(splitter);
            Near(100, flow.OutputPerSecond);
            sentLeft += flow.OutputAPerSecond * fixture.TickSeconds;
            sentRight += flow.OutputBPerSecond * fixture.TickSeconds;
            Near((tick + 1) * 100 * fixture.TickSeconds, sentLeft + sentRight);
            if (blockLeft)
            {
                Near(0, sentLeft);
                Near(100, flow.OutputBPerSecond);
                Near(200, fixture.Stock(left, ResourceIds.Silicates));
            }
            else Assert.InRange(Math.Abs(sentLeft - sentRight), 0, 1 + Tolerance);
            AssertFlowBounds(fixture);
            Near(100, fixture.Total(ResourceIds.FerrousOre));
            Near(blockLeft ? 200 : 0, fixture.Total(ResourceIds.Silicates));
        }
        Assert.True(fixture.Stock(destination, ResourceIds.FerrousOre) > 0);
        Assert.True(fixture.Flow(right).OutputPerSecond > 0);
    }

    [Theory]
    [InlineData(17UL, 20)]
    [InlineData(917UL, 30)]
    public void FractionalResourceRemaindersDoNotWasteAvailableMergerCapacity(ulong seed, int ticksPerSecond)
    {
        var fixture = new ConveyorTestFixture(seed, ticksPerSecond);
        EntityId destination = fixture.AddStorage(new Vector3(128, 0, 155), orientation: BuildingOrientation.West);
        EntityId merger = fixture.Build(ConveyorModule.Merger, new Vector3(128, 0, 128));
        EntityId left = fixture.Build(ConveyorModule.Standard, new Vector3(124, 0, 120));
        EntityId right = fixture.Build(ConveyorModule.Standard, new Vector3(132, 0, 120));
        BuildToDestination(fixture, new Vector3(128, 0, 132), destination);
        foreach (EntityId input in new[] { left, right })
        foreach (ResourceId resource in ConveyorSimulationSystem.EligibleResources)
            fixture.Give(input, resource, resource == ResourceIds.Ammunition ? 100 : 0.01);
        double[] totals = ConveyorSimulationSystem.EligibleResources.Select(fixture.Total).ToArray();

        fixture.Step();

        Near(100, fixture.Flow(merger).InputPerSecond);
        Near(100 * fixture.TickSeconds, fixture.Inventories.GetTotalQuantity(fixture.Buffer(merger)));
        AssertFlowBounds(fixture);
        AssertTotals(fixture, ConveyorSimulationSystem.EligibleResources.ToArray(), totals);
    }

    [Theory]
    [InlineData(17UL, 20)]
    [InlineData(917UL, 30)]
    public void ReservedSourceStockCannotBeSpentByTheConveyor(ulong seed, int ticksPerSecond)
    {
        var fixture = new ConveyorTestFixture(seed, ticksPerSecond);
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        _ = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 100.25);
        Assert.True(fixture.Inventories.Reserve(fixture.Inventory(source), ResourceIds.FerrousOre, 40.125).Succeeded);

        fixture.RunUntil(() => fixture.Stock(destination, ResourceIds.FerrousOre) >= 60.125 - Tolerance);
        fixture.Step(20);
        Near(40.125, fixture.Stock(source, ResourceIds.FerrousOre));
        Near(40.125, fixture.Inventories.GetReservedQuantity(fixture.Inventory(source), ResourceIds.FerrousOre));
        Near(60.125, fixture.Stock(destination, ResourceIds.FerrousOre));
        Near(100.25, fixture.Total(ResourceIds.FerrousOre));
        Assert.True(fixture.Inventories.ConsumeReserved(fixture.Inventory(source), ResourceIds.FerrousOre, 40.125).Succeeded);
        fixture.Step();
        Near(60.125, fixture.Total(ResourceIds.FerrousOre));
        Near(0, fixture.Inventories.GetReservedQuantity(fixture.Inventory(source), ResourceIds.FerrousOre));
    }

    [Fact]
    public void ConveyorReceiptsRetirePendingDemandBeforeATruckBecomesAvailable()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        _ = fixture.Connect(source, destination);
        fixture.AddRoad(source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 100);
        fixture.AddPolicy(destination, ResourceIds.FerrousOre, 50, 80, 100);
        fixture.Step();
        Assert.Equal(LogisticsTransportRequestState.RetryPending,
            Assert.Single(fixture.Distribution.LastDebugSnapshot.Requests).State);
        Near(0, fixture.Distribution.GetCommittedInbound(fixture.Entities, destination, ResourceIds.FerrousOre));

        fixture.RunUntil(() => fixture.Stock(destination, ResourceIds.FerrousOre) >= 80 - Tolerance);
        EntityId truck = fixture.CreateTruck(new Vector3(41, 0, 80));
        fixture.Step(40);
        Near(80, fixture.Stock(destination, ResourceIds.FerrousOre));
        Near(100, fixture.Total(ResourceIds.FerrousOre));
        Near(0, fixture.Inventories.GetReservedQuantity(fixture.Inventory(source), ResourceIds.FerrousOre));
        Assert.False(fixture.Entities.HasComponent<CargoTransportOrder>(truck));
        Assert.Empty(fixture.Distribution.LastDebugSnapshot.Requests);
        Assert.Equal(0, fixture.Distribution.Metrics.CompletedRequestCount);
        Assert.Equal(0, fixture.Cargo.Metrics.CompletedOrderCount);
    }

    [Fact]
    public void AssignedTruckReservesItsActualLoadAndLeavesTheRestForConveyors()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        EntityId remote = fixture.AddStorage(new Vector3(212, 0, 200));
        _ = fixture.Connect(source, destination);
        fixture.AddRoad(remote, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 100);
        fixture.Give(remote, ResourceIds.FerrousOre, 100);
        fixture.AddPolicy(destination, ResourceIds.FerrousOre, 50, 80, 100);
        EntityId truck = fixture.CreateTruck(new Vector3(212, 0, 200), capacity: 40);
        fixture.Step();

        var assigned = Assert.Single(fixture.Distribution.LastDebugSnapshot.Requests);
        Assert.Equal(LogisticsTransportRequestState.Assigned, assigned.State);
        Near(80, assigned.RequestedQuantity);
        Near(40, assigned.ReservedQuantity);
        Near(40, fixture.Entities.GetComponent<CargoTransportOrder>(truck).RequestedQuantity);
        Near(40, fixture.Distribution.GetCommittedInbound(fixture.Entities, destination, ResourceIds.FerrousOre));
        fixture.RunUntil(() => fixture.Stock(destination, ResourceIds.FerrousOre) >= 40 - Tolerance);
        fixture.Step(40);
        Near(40, fixture.Stock(destination, ResourceIds.FerrousOre));
        Near(40, fixture.Inventories.GetReservedQuantity(fixture.Inventory(remote), ResourceIds.FerrousOre));
        Near(200, fixture.Total(ResourceIds.FerrousOre));
        Assert.Equal(assigned.RequestId, Assert.Single(fixture.Distribution.LastDebugSnapshot.Requests).RequestId);
    }

    [Theory]
    [InlineData(17UL, 20)]
    [InlineData(917UL, 30)]
    public void InTransitAndPartiallyUnloadedCargoAreCountedOnceWithConcurrentBelts(ulong seed, int ticksPerSecond)
    {
        var fixture = new ConveyorTestFixture(seed, ticksPerSecond, simulateTrucks: true);
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80), capacity: 100);
        EntityId remote = fixture.AddStorage(new Vector3(212, 0, 200));
        fixture.AddLoadingDock(remote);
        fixture.AddLoadingDock(destination);
        _ = fixture.Connect(source, destination);
        fixture.AddRoad(remote, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 100);
        fixture.Give(remote, ResourceIds.FerrousOre, 100);
        fixture.AddPolicy(destination, ResourceIds.FerrousOre, 50, 80, 100);
        EntityId truck = fixture.CreateTruck(new Vector3(212, 0, 200), capacity: 40);

        string TruckDiagnostic() => $"Cargo: {fixture.Entities.GetComponent<CargoTransportRuntimeState>(truck)}; " +
            $"position: {fixture.Entities.GetComponent<WorldTransform>(truck).Position}; " +
            $"stock: {fixture.Stock(truck, ResourceIds.FerrousOre)}";
        // Keep the physical travel/service watchdog equal in simulated time at both fixed rates.
        int watchdogTicks = ticksPerSecond * 60;
        fixture.RunUntil(() => fixture.Stock(truck, ResourceIds.FerrousOre) >= 40 - Tolerance &&
            !fixture.Entities.HasComponent<CargoTransportReservation>(truck),
            maximumTicks: watchdogTicks, diagnostic: TruckDiagnostic);
        fixture.Step();
        Assert.True(Assert.Single(fixture.Distribution.LastDebugSnapshot.Requests).State == LogisticsTransportRequestState.InTransit,
            TruckDiagnostic());
        fixture.RunUntil(() => fixture.Stock(destination, ResourceIds.FerrousOre) >= 40 - Tolerance,
            maximumTicks: watchdogTicks, diagnostic: TruckDiagnostic);
        Near(40, fixture.Stock(truck, ResourceIds.FerrousOre));
        Near(40, fixture.Distribution.GetCommittedInbound(fixture.Entities, destination, ResourceIds.FerrousOre));
        Near(0, fixture.Inventories.GetReservedQuantity(fixture.Inventory(remote), ResourceIds.FerrousOre));

        // A real capacity change forces the physical truck to unload only half of its committed load.
        fixture.Give(destination, ResourceIds.Silicates, 40);
        fixture.RunUntil(() => fixture.Entities.GetComponent<CargoTransportRuntimeState>(truck).DeliveredQuantity >= 20,
            maximumTicks: watchdogTicks, diagnostic: TruckDiagnostic);
        Near(60, fixture.Stock(destination, ResourceIds.FerrousOre));
        Near(20, fixture.Stock(truck, ResourceIds.FerrousOre));
        Near(20, fixture.Distribution.GetCommittedInbound(fixture.Entities, destination, ResourceIds.FerrousOre));
        Near(200, fixture.Total(ResourceIds.FerrousOre));
        Assert.True(fixture.Inventories.Remove(fixture.Inventory(destination), ResourceIds.Silicates, 40).Succeeded);

        fixture.RunUntil(() => fixture.Distribution.Metrics.CompletedRequestCount == 1,
            maximumTicks: watchdogTicks, diagnostic: TruckDiagnostic);
        fixture.Step(40);
        Near(80, fixture.Stock(destination, ResourceIds.FerrousOre));
        Near(0, fixture.Stock(truck, ResourceIds.FerrousOre));
        Near(0, fixture.Distribution.GetCommittedInbound(fixture.Entities, destination, ResourceIds.FerrousOre));
        Near(200, fixture.Total(ResourceIds.FerrousOre));
        Assert.Equal(1, fixture.Cargo.Metrics.CompletedOrderCount);
        Assert.Equal(1, fixture.Distribution.Metrics.CompletedRequestCount);
        Assert.Empty(fixture.Distribution.LastDebugSnapshot.Requests);
    }

    [Fact]
    public void RetargetingAPolicyDoesNotMoveAnAlreadyCommittedShipment()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId original = fixture.AddStorage(new Vector3(103, 0, 80));
        EntityId replacement = fixture.AddStorage(new Vector3(160, 0, 160));
        fixture.AddRoad(source, original);
        fixture.AddRoad(source, replacement);
        fixture.Give(source, ResourceIds.FerrousOre, 100);
        EntityId policy = fixture.AddPolicy(original, ResourceIds.FerrousOre, 50, 80, 100);
        EntityId truck = fixture.CreateTruck(new Vector3(41, 0, 80), capacity: 40);
        fixture.Step();
        CargoTransportOrder order = fixture.Entities.GetComponent<CargoTransportOrder>(truck);
        fixture.Entities.SetComponent(policy, new LogisticsStockPolicy(replacement, ResourceIds.FerrousOre,
            50, 80, 100, LogisticsStockPriority.Normal));
        fixture.Step();

        Assert.Equal(order, fixture.Entities.GetComponent<CargoTransportOrder>(truck));
        Near(40, fixture.Distribution.GetCommittedInbound(fixture.Entities, original, ResourceIds.FerrousOre));
        Near(0, fixture.Distribution.GetCommittedInbound(fixture.Entities, replacement, ResourceIds.FerrousOre));
        Assert.Single(fixture.Distribution.LastDebugSnapshot.Requests);
    }

    [Fact]
    public void ClosedCycleWithNoCompatibleSinkDoesNotAdmitSourceStock()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(124, 0, 101), orientation: BuildingOrientation.West);
        _ = fixture.Build(ConveyorModule.Merger, new Vector3(128, 0, 128));
        _ = fixture.Build(ConveyorModule.Standard, new Vector3(128, 0, 136));
        _ = fixture.Build(ConveyorModule.CurveLeft, new Vector3(132, 0, 140));
        _ = fixture.Build(ConveyorModule.Standard, new Vector3(136, 0, 144), BuildingOrientation.East);
        _ = fixture.Build(ConveyorModule.CurveLeft, new Vector3(140, 0, 140), BuildingOrientation.East);
        _ = fixture.Build(ConveyorModule.Standard, new Vector3(144, 0, 136), BuildingOrientation.South);
        _ = fixture.Build(ConveyorModule.Standard, new Vector3(144, 0, 128), BuildingOrientation.South);
        _ = fixture.Build(ConveyorModule.Standard, new Vector3(144, 0, 120), BuildingOrientation.South);
        _ = fixture.Build(ConveyorModule.CurveLeft, new Vector3(140, 0, 116), BuildingOrientation.South);
        _ = fixture.Build(new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            new Vector3(140, 0, 112), new Vector3(136, 0, 112), BuildingOrientation.West, 0, true));
        _ = fixture.Build(ConveyorModule.CurveLeft, new Vector3(136, 0, 116), BuildingOrientation.West);
        _ = fixture.Build(ConveyorModule.Standard, new Vector3(132, 0, 120));
        ConveyorBuildingPort output = fixture.Port(source, input: false);
        _ = fixture.Build(new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            output.Position - Vector3.UnitY * 5.8f, new Vector3(124, 0, 124),
            BuildingOrientation.North, 0, true, SourcePortBuilding: source));
        fixture.Give(source, ResourceIds.FerrousOre, 100);
        fixture.Step(40);

        Near(100, fixture.Stock(source, ResourceIds.FerrousOre));
        Near(100, fixture.Total(ResourceIds.FerrousOre));
        Assert.All(fixture.Carriers, entity =>
        {
            Near(0, fixture.Inventories.GetTotalQuantity(fixture.Buffer(entity)));
            Assert.True(fixture.Flow(entity).InputA.IsValid);
            Assert.True(fixture.Flow(entity).OutputA.IsValid);
            Assert.Equal(ConveyorLinkStatus.Blocked, fixture.Flow(entity).Status);
        });
    }

    [Fact]
    public void PaidConstructionRejectsMatingAnotherOwnersSocket()
    {
        var fixture = new ConveyorTestFixture();
        _ = fixture.Build(ConveyorModule.Standard, new Vector3(128, 0, 128));
        int pieces = fixture.Pieces.Length;
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Standard,
            new Vector3(128, 0, 136), new Vector3(128, 0, 136), BuildingOrientation.North, 0, true);
        ConveyorConstructionCommand rejected = fixture.Execute(request, fixture.OtherPlayer);
        Assert.False(rejected.Accepted);
        Assert.Equal(ConveyorFailure.PortMismatch, rejected.Result);
        Assert.Equal(pieces, fixture.Pieces.Length);
    }

    private static void BuildToDestination(ConveyorTestFixture fixture, Vector3 start, EntityId destination)
    {
        ConveyorBuildingPort input = fixture.Port(destination, input: true);
        _ = fixture.Build(new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            start, input.Position - Vector3.UnitY * 5.8f, BuildingOrientation.North, 0, true,
            DestinationPortBuilding: destination));
    }

    private static void AssertFlowBounds(ConveyorTestFixture fixture)
    {
        foreach (EntityId entity in fixture.Carriers)
        {
            ConveyorFlowState flow = fixture.Flow(entity);
            Assert.InRange(flow.InputPerSecond, 0, 100 + Tolerance);
            Assert.InRange(flow.OutputPerSecond, 0, 100 + Tolerance);
            Near(flow.OutputPerSecond, flow.OutputAPerSecond + flow.OutputBPerSecond);
            Assert.InRange(fixture.Inventories.GetTotalQuantity(flow.Buffer), 0, flow.Capacity + Tolerance);
            foreach (InventoryResourceQuantity resource in fixture.Inventories.GetResourceQuantities(flow.Buffer))
            {
                Assert.InRange(resource.Quantity, 0, flow.Capacity + Tolerance);
                Assert.InRange(resource.ReservedQuantity, 0, resource.Quantity + Tolerance);
            }
        }
    }

    private static void AssertTotals(ConveyorTestFixture fixture, ResourceId[] resources, double[] expected)
    {
        for (int index = 0; index < resources.Length; index++) Near(expected[index], fixture.Total(resources[index]));
    }

    private static void Near(double expected, double actual) =>
        Assert.InRange(actual, expected - Tolerance, expected + Tolerance);
}
