using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class BuildingConveyorPortTests
{
    [Theory]
    [InlineData(false, BuildingOrientation.North)]
    [InlineData(false, BuildingOrientation.East)]
    [InlineData(false, BuildingOrientation.South)]
    [InlineData(false, BuildingOrientation.West)]
    [InlineData(true, BuildingOrientation.North)]
    [InlineData(true, BuildingOrientation.East)]
    [InlineData(true, BuildingOrientation.South)]
    [InlineData(true, BuildingOrientation.West)]
    public void CommandAndStorageKeepFourKeyedPhysicalPositions(bool commandCore, BuildingOrientation facing)
    {
        var fixture = new ConveyorTestFixture();
        BuildingId id = commandCore ? BuildingIds.CommandCore : BuildingIds.StorageDepot;
        EntityId building = fixture.AddBuilding(id, new(110, 0, 110), orientation: facing);
        var ports = ConveyorConstructionService.CapturePorts(fixture.Entities, fixture.Player)
            .Where(p => p.Building == building).ToArray();
        var groups = ports.GroupBy(p => p.Key).ToArray();

        Assert.Equal(4, groups.Length);
        Assert.Equal(new[] { "west.primary", "east.primary", "west.secondary", "east.secondary" },
            groups.Select(g => g.Key));
        Assert.All(groups, group =>
        {
            Assert.Equal(2, group.Count());
            Assert.Contains(group, p => p.Input);
            Assert.Contains(group, p => !p.Input);
            Assert.All(group, p =>
            {
                Assert.True(p.Adaptable);
                Assert.Equal(ConveyorPortState.Free, p.Status);
                Assert.Equal(fixture.Inventory(building),
                    ConveyorSimulationSystem.ResolveInventory(fixture.Entities, building, p.Input));
            });
        });
        Assert.Equal(4, groups.Select(g => g.First().Position).Distinct().Count());

        var transform = fixture.Entities.GetComponent<WorldTransform>(building);
        Vector3 ground = transform.Position - Vector3.UnitY *
            (ConveyorGeometry.BuildingPortDimensions(id).Y / 2);
        Assert.True(ConveyorGeometry.TryGetBuildingPort(id, ground, transform.Rotation, true, out var oldIn));
        Assert.True(ConveyorGeometry.TryGetBuildingPort(id, ground, transform.Rotation, false, out var oldOut));
        Assert.Equal(oldIn.Position, groups.Single(g => g.Key == "west.primary").First().Position);
        Assert.Equal(oldOut.Position, groups.Single(g => g.Key == "east.primary").First().Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OccupiedPortLocksDirectionAndRejectsSecondAdapter(bool input)
    {
        var fixture = new ConveyorTestFixture();
        EntityId building = fixture.AddStorage(new(110, 0, 110), orientation: BuildingOrientation.West);
        ConveyorBuildingPort port = ConveyorConstructionService.CapturePorts(fixture.Entities, fixture.Player)
            .Single(p => p.Building == building && p.Key == "west.secondary" && p.Input == input);
        ConveyorModule module = input ? ConveyorModule.OutputAdapter : ConveyorModule.InputAdapter;
        BuildingOrientation facing = ConveyorGeometry.Facing(input ? -port.Facing : port.Facing);
        ConveyorSocket local = ConveyorGeometry.Sockets(module).Single(s => s.Input != input);
        Vector3 pivot = port.Position - Vector3.Transform(local.Position, BuildingFootprint.GetRotation(facing));
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, module, pivot, pivot,
            facing, 0, true, SourcePortBuilding: input ? default : building,
            DestinationPortBuilding: input ? building : default, SnapToSocket: true);

        fixture.Build(request);
        var connected = ConveyorConstructionService.CapturePorts(fixture.Entities, fixture.Player)
            .Where(p => p.Building == building && p.Key == "west.secondary").ToArray();
        ConveyorBuildingPort occupied = Assert.Single(connected);
        Assert.Equal(input, occupied.Input);
        Assert.Equal(input ? ConveyorPortState.ConnectedInput : ConveyorPortState.ConnectedOutput,
            occupied.Status);
        Assert.Equal(ConveyorFailure.PortMismatch,
            fixture.Construction.Preview(fixture.Entities, fixture.Player, request, fixture.Payer).Failure);

        bool opposite = !input;
        ConveyorModule oppositeModule = opposite ? ConveyorModule.OutputAdapter : ConveyorModule.InputAdapter;
        BuildingOrientation oppositeFacing = ConveyorGeometry.Facing(opposite ? -port.Facing : port.Facing);
        ConveyorSocket oppositeSocket = ConveyorGeometry.Sockets(oppositeModule).Single(s => s.Input != opposite);
        Vector3 oppositePivot = port.Position -
            Vector3.Transform(oppositeSocket.Position, BuildingFootprint.GetRotation(oppositeFacing));
        var competing = new ConveyorBuildRequest(ConveyorEdit.Build, oppositeModule,
            oppositePivot, oppositePivot, oppositeFacing, 0, true,
            SourcePortBuilding: opposite ? default : building,
            DestinationPortBuilding: opposite ? building : default, SnapToSocket: true);
        Assert.Equal(ConveyorFailure.PortMismatch,
            fixture.Construction.Preview(fixture.Entities, fixture.Player, competing, fixture.Payer).Failure);
    }

    [Fact]
    public void FourConcurrentPortsShareOneFiniteInventory()
    {
        var fixture = new ConveyorTestFixture();
        EntityId building = fixture.AddStorage(new(110, 0, 110));
        var positions = ConveyorConstructionService.CapturePorts(fixture.Entities, fixture.Player)
            .Where(p => p.Building == building && p.Input).ToArray();
        Assert.Equal(4, positions.Length);

        foreach (ConveyorBuildingPort port in positions)
        {
            // Every physical port is independently addressable without creating new stock stores.
            BuildingOrientation facing = ConveyorGeometry.Facing(-port.Facing);
            ConveyorSocket local = ConveyorGeometry.Sockets(ConveyorModule.OutputAdapter)
                .Single(s => !s.Input);
            Vector3 pivot = port.Position -
                Vector3.Transform(local.Position, BuildingFootprint.GetRotation(facing));
            var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.OutputAdapter,
                pivot, pivot, facing, 0, true, DestinationPortBuilding: building, SnapToSocket: true);
            fixture.Build(request);
        }

        var attached = ConveyorConstructionService.CapturePorts(fixture.Entities, fixture.Player)
            .Where(p => p.Building == building).ToArray();
        Assert.Equal(4, attached.Length);
        Assert.All(attached, p => Assert.Equal(ConveyorPortState.ConnectedInput, p.Status));

        fixture.Give(building, ResourceIds.Steel, 30);
        double total = fixture.Total(ResourceIds.Steel);
        fixture.Step(5);
        Assert.Equal(total, fixture.Total(ResourceIds.Steel), 8);
    }
}