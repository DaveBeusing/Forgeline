using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorDemolitionTests
{
    [Fact]
    public void DeleteCommandRemovesEmptyConveyorBufferAndUnusedFeetWithoutRefund()
    {
        var fixture = CreateFixture(out var demolition);
        EntityId target = fixture.Build(ConveyorModule.Standard, new(160, 0, 80));
        var buffer = fixture.Buffer(target);
        double steel = fixture.Stock(fixture.Payer, ResourceIds.Steel);
        int refreshes = 0;
        fixture.Construction.NavigationChanged = _ => refreshes++;

        var result = Demolish(fixture, demolition, fixture.Player, target);

        Assert.True(Assert.Single(result.Targets).Accepted);
        Assert.Empty(result.Targets[0].Refunded);
        Assert.False(fixture.Entities.IsAlive(target));
        Assert.False(fixture.Inventories.Contains(buffer));
        Assert.False(fixture.Spatial.TryGetEntry(target, out _));
        Assert.Empty(fixture.Pieces);
        Assert.Equal(steel, fixture.Stock(fixture.Payer, ResourceIds.Steel));
        Assert.Equal(1, refreshes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteCommandPreservesActualCargoAndReservations(bool reserved)
    {
        var fixture = CreateFixture(out var demolition);
        EntityId target = fixture.Build(ConveyorModule.Standard, new(160, 0, 80));
        fixture.Give(target, ResourceIds.FerrousOre, 20);
        var buffer = fixture.Buffer(target);
        if (reserved)
            Assert.True(fixture.Inventories.Reserve(buffer, ResourceIds.FerrousOre, 20).Succeeded);

        var result = Demolish(fixture, demolition, fixture.Player, target);

        var rejected = Assert.Single(result.Targets);
        Assert.Equal(BuildingDemolitionFailure.ConveyorRemovalRejected, rejected.Failure);
        Assert.Equal(ConveyorFailure.Occupied, rejected.ConveyorFailure);
        Assert.True(fixture.Entities.IsAlive(target));
        Assert.Equal(20, fixture.Stock(target, ResourceIds.FerrousOre));
        Assert.Equal(reserved ? 20 : 0, fixture.Inventories.GetReservedQuantity(buffer, ResourceIds.FerrousOre));
    }

    [Fact]
    public void DeleteCommandCannotRemoveForeignConveyors()
    {
        var fixture = CreateFixture(out var demolition);
        EntityId target = fixture.Build(ConveyorModule.Standard, new(160, 0, 80));
        var foreign = Demolish(fixture, demolition, fixture.OtherPlayer, target);
        Assert.Equal(BuildingDemolitionFailure.NotOwned, Assert.Single(foreign.Targets).Failure);
        Assert.True(fixture.Entities.IsAlive(target));
    }

    [Fact]
    public void DeleteCommandCannotRemoveLoadBearingSupports()
    {
        var fixture = CreateFixture(out var demolition);
        EntityId target = fixture.Build(ConveyorModule.Standard, new(160, 0, 80));
        EntityId support = fixture.Pieces.First(entity =>
            fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement.IsSupport);
        var blocked = Demolish(fixture, demolition, fixture.Player, support);
        Assert.Equal(ConveyorFailure.Obstructed, Assert.Single(blocked.Targets).ConveyorFailure);
        Assert.True(fixture.Entities.IsAlive(target));
        Assert.True(fixture.Entities.IsAlive(support));
    }

    private static ConveyorTestFixture CreateFixture(out BuildingDemolitionSystem demolition)
    {
        var fixture = new ConveyorTestFixture();
        demolition = new BuildingDemolitionSystem(fixture.Inventories, fixture.Spatial,
            new Dictionary<PlayerId, EntityId> { [fixture.Player] = fixture.Payer }, fixture.Construction);
        fixture.Simulation.RegisterSystem(demolition);
        return fixture;
    }

    private static BuildingDemolitionBatchResult Demolish(
        ConveyorTestFixture fixture, BuildingDemolitionSystem system, PlayerId issuer, EntityId target)
    {
        var correlation = new PlayerCommandCorrelationId(1);
        var command = new DemolishBuildingsCommand(issuer, [target], fixture.Simulation.CurrentTick, correlation);
        fixture.Simulation.SubmitCommand(command, new(fixture.Simulation.CurrentTick.Value + 1));
        fixture.Step();
        Assert.True(system.TryTakeResult(correlation, out var result));
        return result;
    }
}
