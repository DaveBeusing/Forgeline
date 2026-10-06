using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class BuildingPlacementShortcutModifierTests
{
    [Fact]
    public void ShiftModifiedDevelopmentShortcutDoesNotSelectBuilding()
    {
        var input =
            new InputState();
        var controller =
            new RtsBuildingPlacementController(
                new PlayerId(
                    1));
        var interaction =
            new PresentationInteractionState();
        var camera =
            new RtsCamera(
                new RtsCameraSettings
                {
                    EdgeScrollEnabled = false
                });
        var terrain =
            new FlatTerrain();

        input.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                PlatformKey.LeftShift));
        input.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                PlatformKey.F4));

        controller.Update(
            input,
            camera,
            terrain,
            snapshot: null,
            interaction,
            1600,
            900);

        Assert.False(
            controller.IsActive);
        Assert.Equal(
            BuildingId.None,
            controller.ActiveBuilding);
    }

    [Fact]
    public void UnmodifiedPlacementShortcutStillSelectsBuilding()
    {
        var input =
            new InputState();
        var controller =
            new RtsBuildingPlacementController(
                new PlayerId(
                    1));
        var interaction =
            new PresentationInteractionState();
        var camera =
            new RtsCamera(
                new RtsCameraSettings
                {
                    EdgeScrollEnabled = false
                });
        var terrain =
            new FlatTerrain();

        input.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                PlatformKey.F4));

        controller.Update(
            input,
            camera,
            terrain,
            snapshot: null,
            interaction,
            1600,
            900);

        Assert.True(
            controller.IsActive);
        Assert.Equal(
            BuildingIds.CommandCore,
            controller.ActiveBuilding);
    }

    private sealed class FlatTerrain :
        ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds =>
            new(
                new Vector3(
                    -1000.0f,
                    -100.0f,
                    -1000.0f),
                new Vector3(
                    1000.0f,
                    100.0f,
                    1000.0f));

        public bool TrySampleHeight(
            float worldX,
            float worldZ,
            out float height)
        {
            height =
                0.0f;
            return true;
        }

        public bool TrySampleNormal(
            float worldX,
            float worldZ,
            out Vector3 normal)
        {
            normal =
                Vector3.UnitY;
            return true;
        }
    }
}
