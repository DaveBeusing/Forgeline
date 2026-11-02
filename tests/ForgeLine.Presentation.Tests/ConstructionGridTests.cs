using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ConstructionGridTests
{
    [Theory]
    [InlineData(1.25f, 2.75f, 2f, 4f)]
    [InlineData(-1.25f, -2.75f, -2f, -4f)]
    [InlineData(8.1f, -8.1f, 10f, -8f)]
    public void BuildingPointerUsesTheConveyorGridAndSamplesItsSnappedTerrain(float x, float z, float expectedX, float expectedZ)
    {
        var terrain = new SlopedTerrain();
        var camera = SameTypeSelectionTests.Camera();
        camera.CenterOn(new(x, 0, z));
        var controller = new RtsBuildingPlacementController(new(1));
        controller.SelectBuilding(BuildingIds.Smelter);
        var input = new InputState();
        input.Apply(PlatformInputEvent.PointerMoved(800, 450));
        var interaction = new PresentationInteractionState();
        controller.Update(input, camera, terrain, null, interaction, 1600, 900);
        var request = interaction.Capture().PlacementRequest;
        Assert.NotNull(request);
        Assert.Equal(expectedX, request.Value.RequestedPosition.X, 3);
        Assert.Equal(expectedZ, request.Value.RequestedPosition.Z, 3);
        Assert.Equal(SlopedTerrain.Height(expectedX, expectedZ), request.Value.RequestedPosition.Y, 3);
        Assert.Equal(ConstructionGrid.SnapBuilding(request.Value.RequestedPosition,
            InitialBuildingDefinitions.CreateCatalog()[BuildingIds.Smelter].Footprint, BuildingOrientation.North),
            request.Value.RequestedPosition);
        Assert.Equal(request.Value.RequestedPosition, controller.GridCenter);
        controller.Cancel(interaction);
        Assert.Null(controller.GridCenter);
    }

    [Theory]
    [InlineData(BuildingOrientation.North)]
    [InlineData(BuildingOrientation.East)]
    [InlineData(BuildingOrientation.South)]
    [InlineData(BuildingOrientation.West)]
    public void UnevenBuildingFootprintsReserveCompleteCellsWithSymmetricMargins(BuildingOrientation orientation)
    {
        var footprint = new BuildingFootprint(14, 18, 8);
        Vector3 center = ConstructionGrid.SnapBuilding(new(-13.6f, 3, 17.1f), footprint, orientation);
        var envelope = ConstructionGrid.BuildingEnvelope(center, footprint, orientation);
        var cells = ConstructionGrid.BuildingCells(footprint, orientation);
        Vector3 physicalHalf = footprint.GetHalfExtents(orientation);

        Assert.Equal(cells.Width * 4f, envelope.Maximum.X - envelope.Minimum.X);
        Assert.Equal(cells.Depth * 4f, envelope.Maximum.Z - envelope.Minimum.Z);
        Assert.Equal(MathF.Round(envelope.Minimum.X / 4f) * 4f, envelope.Minimum.X);
        Assert.Equal(MathF.Round(envelope.Minimum.Z / 4f) * 4f, envelope.Minimum.Z);
        Assert.Equal(MathF.Round(envelope.Maximum.X / 4f) * 4f, envelope.Maximum.X);
        Assert.Equal(MathF.Round(envelope.Maximum.Z / 4f) * 4f, envelope.Maximum.Z);
        Assert.True(envelope.Minimum.X <= center.X - physicalHalf.X);
        Assert.True(envelope.Maximum.X >= center.X + physicalHalf.X);
        Assert.True(ConstructionGrid.IsBuildingAligned(center, footprint, orientation));
        Assert.False(ConstructionGrid.IsBuildingAligned(center + new Vector3(1, 0, 0), footprint, orientation));
    }

    [Fact]
    public void VisibleGridIsBoundedAndFollowsTerrainAtTheSharedCoordinates()
    {
        var draw = new DebugDraw { Enabled = true };
        ConstructionGridOverlay.Draw(draw, new SlopedTerrain(), new(1.25f, 0, 2.75f));
        Assert.Equal(ConstructionGridOverlay.MaximumLines, draw.Lines.Length);
        foreach (var line in draw.Lines)
            foreach (var point in new[] { line.Start, line.End })
            {
                Assert.Equal(ConstructionGrid.Snap(point).X, point.X);
                Assert.Equal(ConstructionGrid.Snap(point).Z, point.Z);
                Assert.Equal(SlopedTerrain.Height(point.X, point.Z) + .12f, point.Y, 4);
            }
        draw.Clear(); draw.Enabled = false;
        ConstructionGridOverlay.Draw(draw, new SlopedTerrain(), Vector3.Zero);
        Assert.Equal(0, draw.Lines.Length);
    }

    [Fact]
    public void GhostShowsRotatedMaterialPortsAndSeparateCargoAccess()
    {
        var ground = new Vector3(12, 3, 8);
        var rotation = BuildingFootprint.GetRotation(BuildingOrientation.East);
        Assert.True(ConveyorGeometry.TryGetBuildingPort(BuildingIds.Smelter, ground, rotation, true, out var input));
        Assert.True(ConveyorGeometry.TryGetBuildingPort(BuildingIds.Smelter, ground, rotation, false, out var output));
        var preview = new BuildingPlacementPreview(BuildingIds.Smelter, "smelter", "Smelter", ground,
            BuildingOrientation.East, new(ground, ground + new Vector3(18, 10, 16)), true, default, default)
        { ConveyorInput = input, ConveyorOutput = output, LoadingCorridor = new(ground, ground + new Vector3(10, 1, 8)) };
        var draw = new DebugDraw { Enabled = true };
        BuildingConstructionDebugVisualization.DrawPreview(draw, preview, Vector4.One, Vector4.One);
        Assert.Contains(draw.Labels, label => label.Text == "IN");
        Assert.Contains(draw.Labels, label => label.Text == "OUT");
        Assert.Contains(draw.Labels, label => label.Text == "CARGO IN");
        Assert.Contains(draw.Labels, label => label.Text == "CARGO OUT");
        Assert.Contains(draw.Lines.ToArray(), line => Vector3.Dot(Vector3.Normalize(line.End - line.Start), -input.Facing) > .99f);
    }

    [Fact]
    public void OverpassIsSelectableThroughTheConstructionMenu()
    {
        var controller = new RtsBuildingPlacementController(new(1));
        controller.SelectBuilding(new(ConveyorGeometry.MenuBase + (uint)ConveyorModule.Overpass));
        Assert.True(controller.Conveyor.IsActive);
        Assert.Equal(ConveyorModule.Overpass, controller.Conveyor.Module);
        Assert.Equal(0, controller.Conveyor.Tier);
    }

    private sealed class SlopedTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => new(new(-1000, -1000, -1000), new(1000, 1000, 1000));
        internal static float Height(float x, float z) => .1f * x + .05f * z;
        public bool TrySampleHeight(float x, float z, out float height) { height = Height(x, z); return true; }
        public bool TrySampleNormal(float x, float z, out Vector3 normal) { normal = Vector3.UnitY; return true; }
    }
}
