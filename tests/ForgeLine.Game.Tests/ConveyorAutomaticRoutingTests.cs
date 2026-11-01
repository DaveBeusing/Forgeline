using System.Numerics;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Economy;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorAutomaticRoutingTests
{
    [Theory]
    [InlineData(ConveyorModule.ShortSegment)]
    [InlineData(ConveyorModule.CurveLeft)]
    [InlineData(ConveyorModule.CurveRight)]
    [InlineData(ConveyorModule.Merger)]
    [InlineData(ConveyorModule.Splitter)]
    [InlineData(ConveyorModule.RampUp)]
    [InlineData(ConveyorModule.Endcap)]
    public void SnappedPartKeepsExactSocketAndPaysForValidatedConstruction(ConveyorModule module)
    {
        var fixture = new ConveyorTestFixture();
        EntityId belt = fixture.Build(ConveyorModule.Standard, new(80, 0, 96), BuildingOrientation.East);
        var output = fixture.Entities.GetComponent<ConveyorPiece>(belt).Placement.Sockets.Single(s => !s.Input);
        var rotation = BuildingFootprint.GetRotation(BuildingOrientation.East);
        var input = ConveyorGeometry.Sockets(module).First(s => s.Input);
        Vector3 pivot = output.Position - Vector3.Transform(input.Position, rotation);
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, module, pivot, pivot,
            BuildingOrientation.East, 0, true, SnapToSocket: true);
        var preview = fixture.Construction.Preview(fixture.Entities, fixture.Player, request, fixture.Payer);
        Assert.True(preview.IsValid, preview.Failure.ToString());
        double before = fixture.Stock(fixture.Payer, ResourceIds.Steel);
        var added = fixture.Build(request);
        var piece = Assert.Single(added.Select(e => fixture.Entities.GetComponent<ConveyorPiece>(e)), p => p.Placement.Module == module);
        Assert.Equal(pivot, piece.Placement.Pivot);
        Assert.Equal(before - preview.Steel, fixture.Stock(fixture.Payer, ResourceIds.Steel), 8);
        Assert.Contains(piece.Placement.Sockets, socket => socket.Input && Vector3.DistanceSquared(socket.Position, output.Position) < .0004f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnappedAdaptersAttachToFreelyPlacedRotatedBuilding(bool input)
    {
        var fixture = new ConveyorTestFixture();
        EntityId building = fixture.AddStorage(new(72.25f, 0, 94.5f), orientation: BuildingOrientation.West);
        var port = fixture.Port(building, input);
        var orientation = ConveyorGeometry.Facing(input ? -port.Facing : port.Facing);
        var module = input ? ConveyorModule.OutputAdapter : ConveyorModule.InputAdapter;
        var local = ConveyorGeometry.Sockets(module).Single(s => s.Input != input);
        Vector3 pivot = port.Position - Vector3.Transform(local.Position, BuildingFootprint.GetRotation(orientation));
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, module, pivot, pivot, orientation, 0, true,
            SourcePortBuilding: input ? default : building, DestinationPortBuilding: input ? building : default, SnapToSocket: true);
        var added = fixture.Build(request);
        var adapter = Assert.Single(added.Select(e => fixture.Entities.GetComponent<ConveyorPiece>(e)), p => p.Placement.Module == module);
        Assert.Equal(building, adapter.Attachment);
        Assert.Equal(pivot, adapter.Placement.Pivot);
        var options = new JsonSerializerOptions { IncludeFields = true };
        Assert.True(JsonSerializer.Deserialize<ConveyorBuildRequest>(JsonSerializer.Serialize(request, options), options).SnapToSocket);
    }

    [Fact]
    public void MissingForeignOrMovedSnapTargetRejectsWithoutPayment()
    {
        var fixture = new ConveyorTestFixture();
        EntityId belt = fixture.Build(ConveyorModule.Standard, new(80, 0, 96), BuildingOrientation.East, owner: fixture.OtherPlayer);
        var output = fixture.Entities.GetComponent<ConveyorPiece>(belt).Placement.Sockets.Single(s => !s.Input);
        Vector3 pivot = output.Position - Vector3.Transform(ConveyorGeometry.Sockets(ConveyorModule.ShortSegment)[0].Position,
            BuildingFootprint.GetRotation(BuildingOrientation.East));
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.ShortSegment, pivot, pivot,
            BuildingOrientation.East, 0, true, SnapToSocket: true);
        EntityId own = fixture.Build(ConveyorModule.Standard, new(120, 0, 96), BuildingOrientation.East);
        var valid = request with { Start = pivot + Vector3.UnitX * 40, End = pivot + Vector3.UnitX * 40 };
        Assert.True(fixture.Construction.Preview(fixture.Entities, fixture.Player, valid, fixture.Payer).IsValid);
        double before = fixture.Stock(fixture.Payer, ResourceIds.Steel);
        var pieces = fixture.Pieces;
        Assert.False(fixture.Execute(request).Accepted);
        Assert.False(fixture.Execute(request with { Start = pivot + Vector3.UnitX * .5f }).Accepted);
        Assert.False(fixture.Execute(valid with { Start = valid.Start + Vector3.UnitX * .5f }).Accepted);
        Assert.False(fixture.Execute(valid with { Target = own }).Accepted);
        Assert.Equal(before, fixture.Stock(fixture.Payer, ResourceIds.Steel));
        Assert.Equal(pieces, fixture.Pieces);
    }

    [Theory]
    [InlineData(BuildingOrientation.North, BuildingOrientation.North)]
    [InlineData(BuildingOrientation.North, BuildingOrientation.East)]
    [InlineData(BuildingOrientation.North, BuildingOrientation.South)]
    [InlineData(BuildingOrientation.North, BuildingOrientation.West)]
    [InlineData(BuildingOrientation.East, BuildingOrientation.North)]
    [InlineData(BuildingOrientation.East, BuildingOrientation.East)]
    [InlineData(BuildingOrientation.East, BuildingOrientation.South)]
    [InlineData(BuildingOrientation.East, BuildingOrientation.West)]
    [InlineData(BuildingOrientation.South, BuildingOrientation.North)]
    [InlineData(BuildingOrientation.South, BuildingOrientation.East)]
    [InlineData(BuildingOrientation.South, BuildingOrientation.South)]
    [InlineData(BuildingOrientation.South, BuildingOrientation.West)]
    [InlineData(BuildingOrientation.West, BuildingOrientation.North)]
    [InlineData(BuildingOrientation.West, BuildingOrientation.East)]
    [InlineData(BuildingOrientation.West, BuildingOrientation.South)]
    [InlineData(BuildingOrientation.West, BuildingOrientation.West)]
    public void FreelyPlacedRotatedBuildingsConnectAndDeliverFiniteCargo(
        BuildingOrientation sourceOrientation, BuildingOrientation destinationOrientation)
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(72.25f, 0, 94.5f), orientation: sourceOrientation);
        EntityId destination = fixture.AddStorage(new(165.75f, 0, 153.125f), orientation: destinationOrientation);
        ConveyorBuildRequest request = Connection(fixture, source, destination);
        ConveyorPlan first = fixture.Construction.Preview(fixture.Entities, fixture.Player, request, fixture.Payer);
        ConveyorPlan repeated = fixture.Construction.Preview(fixture.Entities, fixture.Player, request, fixture.Payer);
        Assert.True(first.IsValid, $"{sourceOrientation}/{destinationOrientation}: {first.Failure}");
        Assert.Equal(first.Pieces.ToArray(), repeated.Pieces.ToArray());
        Assert.Empty(fixture.Pieces);

        fixture.Build(request);
        Assert.Contains(fixture.Carriers, entity =>
            fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement.SpanMeters > 0);
        AssertDirectedChain(fixture, source, destination);
        fixture.Give(source, ResourceIds.FerrousOre, 23.125);
        fixture.Give(source, ResourceIds.Fuel, 7);
        fixture.RunUntil(() => fixture.Stock(destination, ResourceIds.FerrousOre) >= 23.125 - 1e-8 &&
            fixture.Stock(destination, ResourceIds.Fuel) >= 7 - 1e-8,
            diagnostic: () => string.Join("; ", fixture.Carriers.Select(entity =>
                $"{entity}: {fixture.Flow(entity).Status}/{fixture.Flow(entity).Cause}")));
        Assert.Equal(23.125, fixture.Total(ResourceIds.FerrousOre), 8);
        Assert.Equal(0, fixture.Stock(source, ResourceIds.FerrousOre), 8);
        Assert.Equal(7, fixture.Total(ResourceIds.Fuel), 8);
        Assert.Equal(0, fixture.Stock(source, ResourceIds.Fuel), 8);
        Assert.Equal(7, fixture.Stock(destination, ResourceIds.Fuel), 8);
    }

    [Theory]
    [InlineData(.25f)]
    [InlineData(2.5f)]
    [InlineData(7.5f)]
    public void SmallLateralOffsetsUseConnectedDetoursInsteadOfBrokenElbows(float offset)
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(64.25f, 0, 96));
        EntityId destination = fixture.AddStorage(new(150.5f, 0, 96 + offset));
        fixture.Build(Connection(fixture, source, destination));
        AssertDirectedChain(fixture, source, destination);
        fixture.Give(source, ResourceIds.Silicates, 9.5);
        fixture.RunUntil(() => fixture.Stock(destination, ResourceIds.Silicates) >= 9.5 - 1e-8);
        Assert.Equal(9.5, fixture.Total(ResourceIds.Silicates), 8);
    }

    [Fact]
    public void EmptyAdjustedCarrierReplacementPreservesEndpointsAndResumesDelivery()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(64.25f, 0, 96));
        EntityId destination = fixture.AddStorage(new(150.5f, 0, 120.5f));
        fixture.Build(Connection(fixture, source, destination));
        fixture.Give(source, ResourceIds.FerrousOre, 10);
        fixture.RunUntil(() => fixture.Stock(destination, ResourceIds.FerrousOre) >= 10 - 1e-8);
        EntityId adjusted = fixture.Carriers.First(entity =>
            fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement.SpanMeters > 0);
        ConveyorPlacement before = fixture.Entities.GetComponent<ConveyorPiece>(adjusted).Placement;
        Assert.True(fixture.Edit(adjusted, ConveyorEdit.Replace).Accepted);
        EntityId replacement = Assert.Single(fixture.Carriers, entity =>
            fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement == before);
        Assert.NotEqual(adjusted, replacement);
        Assert.Equal(before.Sockets, fixture.Entities.GetComponent<ConveyorPiece>(replacement).Placement.Sockets);

        fixture.Give(source, ResourceIds.FerrousOre, 6.25);
        fixture.RunUntil(() => fixture.Stock(destination, ResourceIds.FerrousOre) >= 16.25 - 1e-8);
        Assert.Equal(16.25, fixture.Total(ResourceIds.FerrousOre), 8);
    }

    [Fact]
    public void ExistingFreeOutputExtendsInItsActualDirectionToBuildingInput()
    {
        var fixture = new ConveyorTestFixture();
        EntityId belt = fixture.Build(ConveyorModule.Standard, new(80, 0, 96), BuildingOrientation.East);
        EntityId destination = fixture.AddStorage(new(150.25f, 0, 115.5f));
        ConveyorSocket output = fixture.Entities.GetComponent<ConveyorPiece>(belt).Placement.Sockets[^1];
        ConveyorBuildingPort input = fixture.Port(destination, input: true);
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            output.Position - Vector3.UnitY * 5.8f, input.Position - Vector3.UnitY * 5.8f,
            BuildingOrientation.North, 0, true, DestinationPortBuilding: destination, AutomaticRouting: true);
        fixture.Build(request);
        fixture.Give(belt, ResourceIds.Ammunition, 18.5);
        fixture.RunUntil(() => fixture.Stock(destination, ResourceIds.Ammunition) >= 18.5 - 1e-8);
        Assert.Equal(18.5, fixture.Total(ResourceIds.Ammunition), 8);
        Assert.DoesNotContain(fixture.Carriers, entity =>
            fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement.Module == ConveyorModule.InputAdapter);
    }

    [Fact]
    public void OccupiedAndChangedEndpointsRejectWithoutPaymentOrPartialPieces()
    {
        var fixture = new ConveyorTestFixture();
        EntityId source = fixture.AddStorage(new(64.25f, 0, 96));
        EntityId destination = fixture.AddStorage(new(150.5f, 0, 120.5f));
        ConveyorBuildRequest request = Connection(fixture, source, destination);
        fixture.Build(request);
        EntityId[] pieces = fixture.Pieces;
        double steel = fixture.Stock(fixture.Payer, ResourceIds.Steel);
        Assert.False(fixture.Execute(request).Accepted);
        Assert.False(fixture.Execute(request with { End = request.End + Vector3.UnitY }).Accepted);
        EntityId foreign = fixture.AddStorage(new(194.25f, 0, 172.5f), fixture.OtherPlayer);
        ConveyorBuildingPort other = fixture.Port(foreign, input: true);
        Assert.False(fixture.Execute(request with
        {
            DestinationPortBuilding = foreign,
            End = other.Position - Vector3.UnitY * 5.8f
        }).Accepted);
        Assert.Equal(pieces, fixture.Pieces);
        Assert.Equal(steel, fixture.Stock(fixture.Payer, ResourceIds.Steel));
    }

    [Fact]
    public void LegacyRequestsAndNominalGeometryKeepTheirSerializedShape()
    {
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            new(40, 0, 80), new(72, 0, 80), BuildingOrientation.North, 0, true);
        var options = new JsonSerializerOptions { IncludeFields = true };
        string legacy = JsonSerializer.Serialize(request, options);
        Assert.DoesNotContain("AutomaticRouting", legacy, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomaticHeight", legacy, StringComparison.Ordinal);
        Assert.False(JsonSerializer.Deserialize<ConveyorBuildRequest>(legacy, options).AutomaticRouting);
        string automatic = JsonSerializer.Serialize(request with { AutomaticRouting = true }, options);
        Assert.True(JsonSerializer.Deserialize<ConveyorBuildRequest>(automatic, options).AutomaticRouting);
        var placement = new ConveyorPlacement(ConveyorModule.ShortSegment, new(42, 0, 80), BuildingOrientation.East, 0);
        string geometry = JsonSerializer.Serialize(placement, options);
        Assert.DoesNotContain("SpanMeters", geometry, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalScale", geometry, StringComparison.Ordinal);
        var fixture = new ConveyorTestFixture();
        fixture.Build(request);
        Assert.All(fixture.Carriers, entity =>
            Assert.Equal(0, fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement.SpanMeters));
    }

    private static ConveyorBuildRequest Connection(ConveyorTestFixture fixture, EntityId source, EntityId destination)
    {
        ConveyorBuildingPort output = fixture.Port(source, input: false);
        ConveyorBuildingPort input = fixture.Port(destination, input: true);
        return new(ConveyorEdit.Build, ConveyorModule.Route,
            output.Position - Vector3.UnitY * 5.8f, input.Position - Vector3.UnitY * 5.8f,
            BuildingOrientation.North, 0, true, SourcePortBuilding: source,
            DestinationPortBuilding: destination, AutomaticRouting: true);
    }

    private static void AssertDirectedChain(ConveyorTestFixture fixture, EntityId source, EntityId destination)
    {
        ConveyorPlacement[] placements = fixture.Carriers.Select(entity =>
            fixture.Entities.GetComponent<ConveyorPiece>(entity).Placement).ToArray();
        Assert.NotEmpty(placements);
        Assert.Equal(ConveyorModule.InputAdapter, placements[0].Module);
        Assert.Equal(ConveyorModule.OutputAdapter, placements[^1].Module);
        Assert.True(Vector3.DistanceSquared(fixture.Port(source, false).Position, placements[0].Sockets[0].Position) < .0004f);
        Assert.True(Vector3.DistanceSquared(fixture.Port(destination, true).Position, placements[^1].Sockets[^1].Position) < .0004f);
        for (int i = 1; i < placements.Length; i++)
        {
            ConveyorSocket output = placements[i - 1].Sockets[^1];
            ConveyorSocket input = placements[i].Sockets[0];
            Assert.False(output.Input);
            Assert.True(input.Input);
            Assert.True(Vector3.DistanceSquared(output.Position, input.Position) < .0004f);
            Assert.True(Vector3.Dot(output.Facing, input.Facing) < -.999f);
        }
    }
}
