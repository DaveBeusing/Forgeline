using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class RefineryConveyorFlowTests
{
    private const double Tolerance = 1e-7;

    [Theory]
    [InlineData(17UL, 20)]
    [InlineData(917UL, 30)]
    public void PaidBeltsDeliverVolatilesAndRealPoweredRefineryExportsFuel(ulong seed, int tickRate)
    {
        var fixture = new ConveyorTestFixture(seed, tickRate);
        EntityId source = fixture.AddStorage(new(41, 0, 96));
        EntityId refinery = fixture.AddProduction(BuildingIds.Refinery, RecipeIds.Fuel, new(73, 0, 96));
        EntityId destination = fixture.AddStorage(new(105, 0, 96));
        fixture.Give(source, ResourceIds.Volatiles, 25);
        EntityId[] inputLine = fixture.Connect(source, refinery);
        EntityId[] outputLine = fixture.Connect(refinery, destination);
        Assert.NotEmpty(inputLine);
        Assert.NotEmpty(outputLine);
        Assert.All(inputLine.Concat(outputLine), id => Assert.True(fixture.Inventories.Contains(fixture.Buffer(id))));

        ProductionFacility processor = fixture.Entities.GetComponent<ProductionFacility>(refinery);
        double initialVolatiles = fixture.Total(ResourceIds.Volatiles);
        double initialFuel = fixture.Total(ResourceIds.Fuel);
        Queue(fixture, refinery, 2);
        bool sawInputBuffer = false, sawOutputBuffer = false;
        for (int tick = 0; tick < 250 && fixture.Stock(destination, ResourceIds.Fuel) < 20 - Tolerance; tick++)
        {
            fixture.Step();
            sawInputBuffer |= inputLine.Any(id => fixture.Stock(id, ResourceIds.Volatiles) > 0);
            sawOutputBuffer |= outputLine.Any(id => fixture.Stock(id, ResourceIds.Fuel) > 0);
            processor = fixture.Entities.GetComponent<ProductionFacility>(refinery);
            AssertNear(initialVolatiles - processor.CompletedCycles * 10, fixture.Total(ResourceIds.Volatiles));
            AssertNear(initialFuel + processor.CompletedCycles * 10, fixture.Total(ResourceIds.Fuel));
            Assert.All(fixture.Carriers, id => Assert.InRange(
                fixture.Inventories.GetTotalQuantity(fixture.Buffer(id)), 0,
                fixture.Flow(id).Capacity + Tolerance));
        }

        Assert.Equal(2UL, processor.CompletedCycles);
        AssertNear(20, fixture.Stock(destination, ResourceIds.Fuel));
        AssertNear(5, fixture.Total(ResourceIds.Volatiles));
        AssertNear(20, fixture.Total(ResourceIds.Fuel) - initialFuel);
        AssertNear(0, fixture.Stock(destination, ResourceIds.Volatiles));
        AssertNear(0, fixture.Stock(source, ResourceIds.Fuel));
        Assert.True(sawInputBuffer);
        Assert.True(sawOutputBuffer);
    }

    [Theory]
    [InlineData(BuildingOrientation.North)]
    [InlineData(BuildingOrientation.East)]
    [InlineData(BuildingOrientation.South)]
    [InlineData(BuildingOrientation.West)]
    public void RefineryExposesExactlyOneRotatedVolatilesInputAndOneFuelOutput(BuildingOrientation orientation)
    {
        var fixture = new ConveyorTestFixture();
        EntityId refinery = fixture.AddProduction(BuildingIds.Refinery, RecipeIds.Fuel,
            new(100, 0, 112), orientation: orientation);
        var ports = ConveyorConstructionService.CapturePorts(fixture.Entities, fixture.Player)
            .Where(p => p.Building == refinery).ToArray();
        Assert.Equal(2, ports.Length);
        ConveyorBuildingPort incoming = Assert.Single(ports, p => p.Input);
        ConveyorBuildingPort outgoing = Assert.Single(ports, p => !p.Input);
        Assert.False(incoming.Adaptable);
        Assert.False(outgoing.Adaptable);
        Assert.Equal("input", incoming.Key);
        Assert.Equal("output", outgoing.Key);
        WorldTransform pose = fixture.Entities.GetComponent<WorldTransform>(refinery);
        Vector3 ground = pose.Position - Vector3.UnitY * 5;
        Assert.True(ConveyorGeometry.TryGetBuildingPort(BuildingIds.Refinery,
            ground, pose.Rotation, input: true, out ConveyorSocket expectedInput));
        Assert.True(ConveyorGeometry.TryGetBuildingPort(BuildingIds.Refinery,
            ground, pose.Rotation, input: false, out ConveyorSocket expectedOutput));
        Assert.Equal(expectedInput.Position, incoming.Position);
        Assert.Equal(expectedOutput.Position, outgoing.Position);
        Assert.Equal(expectedInput.Facing, incoming.Facing);
        Assert.Equal(expectedOutput.Facing, outgoing.Facing);

        int actualInputMask = fixture.Conveyor.AllowedResources(fixture.Entities, incoming);
        int actualOutputMask = fixture.Conveyor.AllowedResources(fixture.Entities, outgoing);
        Assert.Equal(1 << ConveyorSimulationSystem.EligibleResources.ToList().IndexOf(ResourceIds.Volatiles), actualInputMask);
        Assert.Equal(1 << ConveyorSimulationSystem.EligibleResources.ToList().IndexOf(ResourceIds.Fuel), actualOutputMask);
        var definition = DirectorateContent.CreateBuildingCatalog()[BuildingIds.Refinery];
        Assert.Equal(actualInputMask, fixture.Conveyor.PreviewAllowedResources(definition, true, default));
        Assert.Equal(actualOutputMask, fixture.Conveyor.PreviewAllowedResources(definition, false, default));
    }

    [Fact]
    public void RefineryWithoutPowerCannotReceiveOrManufactureFuel()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 96));
        EntityId refinery = fixture.AddProduction(BuildingIds.Refinery, RecipeIds.Fuel, new(73, 0, 96));
        EntityId destination = fixture.AddStorage(new(105, 0, 96));
        fixture.Give(source, ResourceIds.Volatiles, 10);
        _ = fixture.Connect(source, refinery);
        _ = fixture.Connect(refinery, destination);
        Queue(fixture, refinery, 1);
        fixture.SetPower(refinery, 0);
        fixture.Step(45);
        AssertNear(10, fixture.Total(ResourceIds.Volatiles));
        AssertNear(0, fixture.Total(ResourceIds.Fuel));
        Assert.Equal(0UL, fixture.Entities.GetComponent<ProductionFacility>(refinery).CompletedCycles);
        fixture.SetPower(refinery, 1);
        fixture.RunUntil(() => fixture.Stock(destination, ResourceIds.Fuel) >= 10 - Tolerance, 240);
        AssertNear(10, fixture.Stock(destination, ResourceIds.Fuel));
        AssertNear(0, fixture.Total(ResourceIds.Volatiles));
    }

    [Fact]
    public void IncompatibleRefineryResourcesStayInOwnedSourceInventory()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 96));
        EntityId refinery = fixture.AddProduction(BuildingIds.Refinery, RecipeIds.Fuel, new(73, 0, 96));
        fixture.Give(source, ResourceIds.Steel, 15);
        fixture.Give(source, ResourceIds.Fuel, 12);
        _ = fixture.Connect(source, refinery);
        fixture.Step(60);
        AssertNear(15, fixture.Stock(source, ResourceIds.Steel));
        AssertNear(12, fixture.Stock(source, ResourceIds.Fuel));
        AssertNear(0, fixture.Inventories.GetQuantity(
            fixture.Entities.GetComponent<ProductionFacility>(refinery).InputInventory, ResourceIds.Steel));
        AssertNear(0, fixture.Total(ResourceIds.Volatiles));
    }

    [Fact]
    public void ReservedVolatilesStayProtectedAndUnsupportedFuelSinkIsBlocked()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 96));
        EntityId refinery = fixture.AddProduction(BuildingIds.Refinery, RecipeIds.Fuel, new(73, 0, 96));
        fixture.Give(source, ResourceIds.Volatiles, 20);
        Assert.True(fixture.Inventories.Reserve(fixture.Inventory(source), ResourceIds.Volatiles, 20).Succeeded);
        _ = fixture.Connect(source, refinery);
        fixture.Step(35);
        AssertNear(20, fixture.Stock(source, ResourceIds.Volatiles));
        AssertNear(0, fixture.Stock(refinery, ResourceIds.Volatiles));

        EntityId supply = fixture.AddBuilding(BuildingIds.SupplyDepot, new(105, 0, 96));
        var port = Assert.Single(ConveyorConstructionService.CapturePorts(fixture.Entities, fixture.Player),
            p => p.Building == supply && p.Input);
        Assert.Equal(0, fixture.Conveyor.AllowedResources(fixture.Entities, port) &
            (1 << ConveyorSimulationSystem.EligibleResources.ToList().IndexOf(ResourceIds.Fuel)));
    }

    private static void Queue(ConveyorTestFixture fixture, EntityId refinery, int count)
    {
        for (int i = 0; i < count; i++)
            fixture.Simulation.SubmitCommand(
                PlayerProductionActionCommand.Queue(fixture.Player, refinery, RecipeIds.Fuel, fixture.Simulation.CurrentTick),
                fixture.Simulation.CurrentTick.Next());
    }

    private static void AssertNear(double expected, double actual) =>
        Assert.InRange(actual, expected - Tolerance, expected + Tolerance);
}
