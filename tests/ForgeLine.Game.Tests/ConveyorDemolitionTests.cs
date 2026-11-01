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

    [Fact]
    public void DeleteCommandRecoversExactlyHalfOfUnreservedCargo()
    {
        var fixture = CreateFixture(out var demolition);
        EntityId target = fixture.Build(ConveyorModule.Standard, new(160, 0, 80));
        fixture.Give(target, ResourceIds.FerrousOre, 20);
        InventoryId buffer = fixture.Buffer(target);
        double original = fixture.Stock(fixture.Payer, ResourceIds.FerrousOre);

        var result = Demolish(fixture, demolition, fixture.Player, target);

        var accepted = Assert.Single(result.Targets);
        Assert.True(accepted.Accepted);
        Assert.Empty(accepted.Refunded);
        ConveyorCargoRecovery recovery = Assert.Single(Assert.IsType<ConveyorCargoRecovery[]>(accepted.CargoRecovery));
        Assert.Equal(ResourceIds.FerrousOre, recovery.ResourceId);
        Assert.Equal(20, recovery.Held);
        Assert.Equal(10, recovery.Recovered);
        Assert.Equal(10, recovery.Lost);
        Assert.False(fixture.Entities.IsAlive(target));
        Assert.False(fixture.Inventories.Contains(buffer));
        Assert.Equal(original + 10, fixture.Stock(fixture.Payer, ResourceIds.FerrousOre));
        Assert.Equal(0, fixture.Inventories.GetTotalQuantity(
            ConveyorSalvageStock.FindInventory(fixture.Entities, fixture.Player)));
    }

    [Fact]
    public void ReservedCargoRejectsDemolitionWithoutMutation()
    {
        var fixture = CreateFixture(out var demolition);
        EntityId target = fixture.Build(ConveyorModule.Standard, new(160, 0, 80));
        fixture.Give(target, ResourceIds.FerrousOre, 20);
        InventoryId buffer = fixture.Buffer(target);
        Assert.True(fixture.Inventories.Reserve(buffer, ResourceIds.FerrousOre, 20).Succeeded);

        var rejected = Assert.Single(Demolish(fixture, demolition, fixture.Player, target).Targets);
        Assert.Equal(BuildingDemolitionFailure.ConveyorRemovalRejected, rejected.Failure);
        Assert.Equal(ConveyorFailure.Occupied, rejected.ConveyorFailure);
        Assert.True(fixture.Entities.IsAlive(target));
        Assert.Equal(20, fixture.Stock(target, ResourceIds.FerrousOre));
        Assert.Equal(20, fixture.Inventories.GetReservedQuantity(buffer, ResourceIds.FerrousOre));
        Assert.False(ConveyorSalvageStock.FindInventory(fixture.Entities, fixture.Player).IsSpecified);
    }

    [Fact]
    public void FullStorageRetainsRecoveryAndDrainsWhenSpaceIsFreed()
    {
        var fixture = CreateFixture(out var demolition);
        EntityId target = fixture.Build(ConveyorModule.Standard, new(160, 0, 80));
        fixture.Give(target, ResourceIds.FerrousOre, 20);
        double room = fixture.Inventories.GetRemainingCapacity(fixture.PayerInventory);
        fixture.Give(fixture.Payer, ResourceIds.Steel, room);

        var accepted = Assert.Single(Demolish(fixture, demolition, fixture.Player, target).Targets);
        Assert.True(accepted.Accepted);
        InventoryId salvage = ConveyorSalvageStock.FindInventory(fixture.Entities, fixture.Player);
        Assert.True(salvage.IsSpecified);
        Assert.Equal(10, fixture.Inventories.GetQuantity(salvage, ResourceIds.FerrousOre));
        Assert.Equal(0, fixture.Stock(fixture.Payer, ResourceIds.FerrousOre));

        Assert.True(fixture.Inventories.Remove(fixture.PayerInventory, ResourceIds.Steel, 10).Succeeded);
        fixture.Step();
        Assert.Equal(10, fixture.Stock(fixture.Payer, ResourceIds.FerrousOre));
        Assert.Equal(0, fixture.Inventories.GetQuantity(salvage, ResourceIds.FerrousOre));
    }

    [Fact]
    public void FractionalMixedCargoIsAccountedPerResourceAndRepeatedDeleteCannotDuplicate()
    {
        var fixture = CreateFixture(out var demolition);
        EntityId target = fixture.Build(ConveyorModule.Merger, new(160, 0, 80));
        fixture.Give(target, ResourceIds.FerrousOre, 2.5);
        fixture.Give(target, ResourceIds.Electronics, 3.25);
        double steel = fixture.Stock(fixture.Payer, ResourceIds.Steel);

        var first = Assert.Single(Demolish(fixture, demolition, fixture.Player, target).Targets);
        Assert.True(first.Accepted);
        var cargo = Assert.IsType<ConveyorCargoRecovery[]>(first.CargoRecovery);
        Assert.Equal(2, cargo.Length);
        Assert.Equal(1.25, cargo.Single(item => item.ResourceId == ResourceIds.FerrousOre).Recovered);
        Assert.Equal(1.625, cargo.Single(item => item.ResourceId == ResourceIds.Electronics).Recovered);
        Assert.Equal(steel, fixture.Stock(fixture.Payer, ResourceIds.Steel));
        var repeated = Assert.Single(Demolish(fixture, demolition, fixture.Player, target).Targets);
        Assert.Equal(BuildingDemolitionFailure.InvalidTarget, repeated.Failure);
        Assert.Equal(1.25, fixture.Stock(fixture.Payer, ResourceIds.FerrousOre));
        Assert.Equal(1.625, fixture.Stock(fixture.Payer, ResourceIds.Electronics));
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
