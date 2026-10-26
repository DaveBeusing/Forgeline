using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorOverpassTests
{
    private static readonly Vector3 Center = new(128, 0, 128);

    [Theory]
    [InlineData(BuildingOrientation.North)]
    [InlineData(BuildingOrientation.East)]
    [InlineData(BuildingOrientation.South)]
    [InlineData(BuildingOrientation.West)]
    public void PaidCrossingKeepsBothMaterialStreamsIndependent(BuildingOrientation direction)
    {
        var f = new ConveyorTestFixture();
        var across = (BuildingOrientation)(((int)direction + 1) % 4);
        Vector3 lowerDirection = Vector3.Transform(Vector3.UnitZ, BuildingFootprint.GetRotation(across));
        EntityId lower = f.Build(ConveyorModule.Standard, Center, across);
        EntityId lowerEnd = f.Build(ConveyorModule.Standard, Center + lowerDirection * 8, across);
        var request = Request(direction);
        var preview = f.Construction.Preview(f.Entities, f.Player, request, f.Payer);
        Assert.True(preview.IsValid, preview.Failure.ToString());
        Assert.Equal(66, preview.Steel);
        double steel = f.Stock(f.Payer, ResourceIds.Steel);
        var built = f.Build(request);
        Assert.Equal(steel - preview.Steel, f.Stock(f.Payer, ResourceIds.Steel));
        Assert.Equal(7, built.Length);
        Assert.DoesNotContain(built, e => f.Entities.GetComponent<ConveyorPiece>(e).Placement.Module == ConveyorModule.Overpass);
        EntityId up = Assert.Single(built, e => f.Entities.GetComponent<ConveyorPiece>(e).Placement.Module == ConveyorModule.RampUp);
        EntityId top = Assert.Single(built, e => f.Entities.GetComponent<ConveyorPiece>(e).Placement.Module == ConveyorModule.Standard);
        EntityId down = Assert.Single(built, e => f.Entities.GetComponent<ConveyorPiece>(e).Placement.Module == ConveyorModule.RampDown);
        Assert.True(f.Entities.GetComponent<ConveyorPiece>(top).Placement.Bounds.Minimum.Y >
            f.Entities.GetComponent<ConveyorPiece>(lower).Placement.Bounds.Maximum.Y);
        Vector3 upperDirection = Vector3.Transform(Vector3.UnitZ, BuildingFootprint.GetRotation(direction));
        EntityId upperSink = f.AddStorage(Center + upperDirection * 23,
            orientation: (BuildingOrientation)(((int)direction + 3) % 4));
        EntityId lowerSink = f.AddStorage(Center + lowerDirection * 23,
            orientation: (BuildingOrientation)(((int)across + 3) % 4));
        EntityId upperAdapter = BuildInput(upperSink, upperDirection, direction);
        EntityId lowerAdapter = BuildInput(lowerSink, lowerDirection, across);
        f.Give(up, ResourceIds.FerrousOre, 20);
        f.Give(lower, ResourceIds.Silicates, 20);
        f.RunUntil(() => f.Stock(upperSink, ResourceIds.FerrousOre) == 20 && f.Stock(lowerSink, ResourceIds.Silicates) == 20,
            diagnostic: () => $"up={f.Flow(up)} top={f.Flow(top)} down={f.Flow(down)} lower={f.Flow(lower)} end={f.Flow(lowerEnd)} upperStock={f.Stock(upperSink, ResourceIds.FerrousOre)} lowerStock={f.Stock(lowerSink, ResourceIds.Silicates)}");
        Assert.Equal(top, f.Flow(up).OutputA);
        Assert.Equal(down, f.Flow(top).OutputA);
        Assert.Equal(lowerEnd, f.Flow(lower).OutputA);
        Assert.Equal(upperAdapter, f.Flow(down).OutputA);
        Assert.Equal(lowerAdapter, f.Flow(lowerEnd).OutputA);
        Assert.Equal(upperSink, f.Flow(upperAdapter).OutputA);
        Assert.Equal(lowerSink, f.Flow(lowerAdapter).OutputA);
        Assert.Equal(0, f.Stock(upperSink, ResourceIds.Silicates));
        Assert.Equal(0, f.Stock(lowerSink, ResourceIds.FerrousOre));
        Assert.Equal(20, f.Total(ResourceIds.Silicates));
        Assert.Equal(20, f.Total(ResourceIds.FerrousOre));

        EntityId BuildInput(EntityId sink, Vector3 forward, BuildingOrientation orientation)
        {
            Vector3 pivot = Center + forward * 14;
            return Assert.Single(f.Build(new(ConveyorEdit.Build, ConveyorModule.OutputAdapter, pivot, pivot,
                orientation, 0, true, DestinationPortBuilding: sink, SnapToSocket: true)),
                e => f.Entities.GetComponent<ConveyorPiece>(e).Placement.Module == ConveyorModule.OutputAdapter);
        }
    }

    [Fact]
    public void GroundLevelCrossingIsStillRejectedAndAnOverpassCannotCoverABuilding()
    {
        var f = new ConveyorTestFixture();
        f.Build(ConveyorModule.Standard, Center, BuildingOrientation.East);
        var blocked = Request(BuildingOrientation.North) with { Module = ConveyorModule.Standard };
        int count = f.Pieces.Length;
        double steel = f.Stock(f.Payer, ResourceIds.Steel);
        Assert.Equal(ConveyorFailure.Obstructed, f.Execute(blocked).Result);
        Assert.Equal(count, f.Pieces.Length);
        Assert.Equal(steel, f.Stock(f.Payer, ResourceIds.Steel));
        var buildingFixture = new ConveyorTestFixture();
        buildingFixture.AddStorage(Center);
        var plan = buildingFixture.Construction.Preview(buildingFixture.Entities, buildingFixture.Player,
            Request(BuildingOrientation.North), buildingFixture.Payer);
        Assert.Equal(ConveyorFailure.Obstructed, plan.Failure);
        Assert.Equal(ConveyorObstruction.Building, plan.Obstruction);
    }

    [Fact]
    public void MissingSteelAndUnsupportedHeightDoNotPartiallyBuildAnOverpass()
    {
        var f = new ConveyorTestFixture();
        double available = f.Stock(f.Payer, ResourceIds.Steel);
        Assert.True(f.Inventories.Remove(f.PayerInventory, ResourceIds.Steel, available - 65).Succeeded);
        int count = f.Pieces.Length;
        Assert.Equal(ConveyorFailure.InsufficientStock, f.Execute(Request(BuildingOrientation.North)).Result);
        Assert.Equal(65, f.Stock(f.Payer, ResourceIds.Steel));
        Assert.Equal(count, f.Pieces.Length);
        Assert.Equal(ConveyorFailure.UnsupportedTier, f.Execute(Request(BuildingOrientation.North) with { Tier = 1 }).Result);
        Assert.Equal(count, f.Pieces.Length);
    }

    [Fact]
    public void SnappedOverpassExpandsIntoAuthoredPartsAndReusesTheEndpointSupport()
    {
        var f = new ConveyorTestFixture();
        f.Build(ConveyorModule.Standard, Center - Vector3.UnitZ * 16);
        var request = Request(BuildingOrientation.North) with { SnapToSocket = true };
        var plan = f.Construction.Preview(f.Entities, f.Player, request, f.Payer);
        Assert.True(plan.IsValid, plan.Failure.ToString());
        Assert.Equal(62, plan.Steel);
        var pieces = f.Build(request);
        Assert.Equal(6, pieces.Length);
        Assert.DoesNotContain(pieces, e => f.Entities.GetComponent<ConveyorPiece>(e).Placement.Module == ConveyorModule.Overpass);
    }

    [Theory]
    [InlineData(BuildingOrientation.North)]
    [InlineData(BuildingOrientation.East)]
    [InlineData(BuildingOrientation.South)]
    [InlineData(BuildingOrientation.West)]
    public void ProspectiveBuildingPortsMatchTheCompletedBuilding(BuildingOrientation orientation)
    {
        var f = new ConveyorTestFixture();
        var service = new BuildingPlacementService(DirectorateContent.CreateBuildingCatalog(), f.Terrain, f.Spatial);
        var preview = service.CreatePreview(f.Entities, f.Player, BuildingIds.Smelter, Center, orientation);
        Assert.True(preview.IsValid, preview.Failure.ToString());
        var building = f.AddBuilding(BuildingIds.Smelter, preview.GroundPosition, orientation: orientation);
        var input = f.Port(building, true);
        var output = f.Port(building, false);
        Assert.Equal(input.Position, preview.ConveyorInput!.Value.Position);
        Assert.Equal(input.Facing, preview.ConveyorInput.Value.Facing);
        Assert.Equal(output.Position, preview.ConveyorOutput!.Value.Position);
        Assert.Equal(output.Facing, preview.ConveyorOutput.Value.Facing);
    }

    [Fact]
    public void InvalidBuildingOrientationProducesAnInvalidPreviewWithoutPorts()
    {
        var f = new ConveyorTestFixture();
        var service = new BuildingPlacementService(DirectorateContent.CreateBuildingCatalog(), f.Terrain, f.Spatial);
        var preview = service.CreatePreview(f.Entities, f.Player, BuildingIds.Smelter, Center, (BuildingOrientation)255);
        Assert.False(preview.IsValid);
        Assert.Null(preview.ConveyorInput);
        Assert.Null(preview.ConveyorOutput);
    }

    private static ConveyorBuildRequest Request(BuildingOrientation direction) =>
        new(ConveyorEdit.Build, ConveyorModule.Overpass, Center, Center, direction, 0, true);
}
