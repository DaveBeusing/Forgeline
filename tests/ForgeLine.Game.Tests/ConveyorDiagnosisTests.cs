using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorDiagnosisTests
{


    [Fact]
    public void TypedInventoryObservationUsesTheActualTransportResolver()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 80));
        EntityId destination = fixture.AddStorage(new(103, 0, 80));
        InventoryId vehicleInventory = fixture.Inventory(source);
        InventoryId conveyorInventory = fixture.CreateInventory(100);
        fixture.Entities.AddComponent(source, new SupplyDepot(conveyorInventory, fixture.Player));
        fixture.AddRoad(source, destination);
        Assert.Equal(conveyorInventory, ConveyorSimulationSystem.ResolveInventory(fixture.Entities, source, input: false));
        Assert.True(fixture.Cargo.TryObserveInventory(fixture.Entities, source, input: false, out var observed));
        Assert.Equal(vehicleInventory, observed);
        Assert.False(fixture.Cargo.TryObserveInventory(fixture.Entities,
            new EntityId(source.Index, source.Generation + 1), input: false, out _));
    }
    [Fact]
    public void CoveredDestinationTargetIsNotReportedAsPhysicallyFullStorage()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 80));
        EntityId destination = fixture.AddStorage(new(103, 0, 80));
        EntityId[] line = fixture.Connect(source, destination);
        fixture.AddPolicy(destination, ResourceIds.Steel, 0, 10, 20);
        fixture.Give(destination, ResourceIds.Steel, 10);
        fixture.Give(source, ResourceIds.Steel, 20);
        fixture.Step(80);
        EntityId last = line.Single(entity => fixture.Flow(entity).OutputA == destination);
        Assert.True(fixture.Conveyor.TryGetObservation(last, out var blocked));
        Assert.Equal(new(ConveyorBlockReason.DestinationTargetCovered, destination, ResourceIds.Steel), blocked.First);
        Assert.True(fixture.Inventories.GetAddableQuantity(fixture.Inventory(destination), ResourceIds.Steel, 1) > 0);
        Assert.Equal(10, fixture.Stock(destination, ResourceIds.Steel));
    }
    [Fact]
    public void ReservedSourceAndFullDestinationHaveDifferentCauseIdentities()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 80));
        EntityId destination = fixture.AddStorage(new(103, 0, 80), capacity: 10);
        EntityId[] line = fixture.Connect(source, destination);
        fixture.Give(source, ResourceIds.Steel, 100);
        Assert.True(fixture.Inventories.Reserve(fixture.Inventory(source), ResourceIds.Steel, 100).Succeeded);
        fixture.Step();
        EntityId first = line.Single(entity => fixture.Flow(entity).InputA == source);
        Assert.True(fixture.Conveyor.TryGetObservation(first, out var reserved));
        Assert.Equal(new(ConveyorBlockReason.ReservedStock, source, ResourceIds.Steel), reserved.First);
        Assert.True(fixture.Inventories.ReleaseReservation(fixture.Inventory(source), ResourceIds.Steel, 100).Succeeded);
        fixture.Give(destination, ResourceIds.Steel, 10);
        fixture.Step(80);
        EntityId last = line.Single(entity => fixture.Flow(entity).OutputA == destination);
        Assert.True(fixture.Conveyor.TryGetObservation(last, out var full));
        Assert.Equal(new(ConveyorBlockReason.DestinationFull, destination, ResourceIds.Steel), full.First);
        Assert.Equal(100, fixture.Stock(source, ResourceIds.Steel) +
            line.Sum(entity => fixture.Stock(entity, ResourceIds.Steel)));
    }

    [Fact]
    public void DestroyedSupportAndDamagedSpanKeepTheirOwnFullIdentity()
    {
        var fixture = new ConveyorTestFixture();
        EntityId piece = fixture.Build(ConveyorModule.Standard, new(80, 0, 80));
        EntityId support = fixture.Pieces.First(entity => fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement.IsSupport);
        fixture.Entities.SetComponent(support, new HealthState(0, 100));
        fixture.Step();
        Assert.True(fixture.Conveyor.TryGetObservation(piece, out var stopped));
        Assert.Equal(new(ConveyorBlockReason.DestroyedSupport, support), stopped.First);
        fixture.Entities.SetComponent(support, HealthState.Full(100));
        fixture.Entities.SetComponent(piece, new HealthState(50, 100));
        fixture.Step();
        Assert.True(fixture.Conveyor.TryGetObservation(piece, out var damaged));
        Assert.Contains(new ConveyorBlockObservation(ConveyorBlockReason.DamagedRoute, piece),
            new[] { damaged.First, damaged.Second, damaged.Third });
    }

    [Fact]
    public void RetainedIncompatibleCargoNamesTheRejectingBuildingPort()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(41, 0, 80));
        EntityId depot = fixture.AddBuilding(BuildingIds.SupplyDepot, new(104, 0, 80));
        EntityId[] line = fixture.Connect(source, depot);
        EntityId last = line.Single(entity => fixture.Flow(entity).OutputA == depot);
        fixture.Give(source, ResourceIds.Silicates, 10);
        Assert.True(fixture.Inventories.Transfer(fixture.Inventory(source), fixture.Buffer(last), ResourceIds.Silicates, 10).Succeeded);
        fixture.Step();
        Assert.True(fixture.Conveyor.TryGetObservation(last, out var rejected));
        Assert.Equal(new(ConveyorBlockReason.IncompatibleResource, depot, ResourceIds.Silicates), rejected.First);
        Assert.Equal(10, fixture.Stock(last, ResourceIds.Silicates));
    }

    [Fact]
    public void ImmediatePowerAndUnmatedPortAreIndependentStableObservations()
    {
        var fixture = new ConveyorTestFixture();
        EntityId piece = fixture.Build(ConveyorModule.ShortSegment, new Vector3(80, 0, 80));
        fixture.SetPower(piece, 0);
        fixture.Step();
        Assert.True(fixture.Conveyor.TryGetObservation(piece, out var stopped));
        Assert.Equal(ConveyorBlockReason.NoPower, stopped.First.Reason);
        Assert.Equal(piece, stopped.First.Cause);
        Assert.Equal(ConveyorBlockReason.DisconnectedPort, stopped.Second.Reason);
        fixture.SetPower(piece, 1);
        fixture.Step();
        Assert.True(fixture.Conveyor.TryGetObservation(piece, out var recovered));
        Assert.Equal(ConveyorBlockReason.DisconnectedPort, recovered.First.Reason);
        Assert.Equal(ConveyorBlockReason.NoPower, stopped.First.Reason);
        Assert.Equal(fixture.Simulation.CurrentTick, fixture.Conveyor.ObservationTick);
    }

    [Fact]
    public void ConnectedEmptySourceIdentifiesActualFullGenerationEndpoint()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new Vector3(41, 0, 80));
        EntityId destination = fixture.AddStorage(new Vector3(103, 0, 80));
        EntityId[] line = fixture.Connect(source, destination);
        fixture.Step();
        var observation = line.Select(e => fixture.Conveyor.TryGetObservation(e, out var value) ? value : default)
            .Single(value => value.First.Reason == ConveyorBlockReason.NoInput && value.First.Cause == source);
        Assert.Equal(source, observation.First.Cause);
        fixture.Give(source, ResourceIds.Steel, 20);
        fixture.Step();
        Assert.Contains(line, entity => fixture.Flow(entity).InputPerSecond > 0);
        Assert.Equal(ConveyorBlockReason.NoInput, observation.First.Reason);
        fixture.SetPower(source, 0);
        fixture.Step(20);
        EntityId inlet = line.Single(entity => fixture.Flow(entity).InputA == source);
        Assert.True(fixture.Conveyor.TryGetObservation(inlet, out var powerBlocked));
        Assert.Equal(new(ConveyorBlockReason.NoPower, source), powerBlocked.First);
        Assert.True(fixture.Stock(source, ResourceIds.Steel) > 0);
    }
}
