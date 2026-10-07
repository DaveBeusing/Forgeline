using System.Numerics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RtsMinimapInteractionTests
{
    [Fact]
    public void CoordinateMappingRoundTripsCanonicalWorldBounds()
    {
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1600,
                900,
                96);
        var bounds =
            new AxisAlignedBounds(
                new Vector3(
                    -256.0f,
                    -20.0f,
                    128.0f),
                new Vector3(
                    768.0f,
                    80.0f,
                    1_152.0f));
        Vector3 world =
            new(
                384.0f,
                0.0f,
                896.0f);
        Vector2 pointer =
            RtsMinimapInteractionLayout.MapWorldToPointer(
                world,
                layout,
                bounds);

        Assert.True(
            RtsMinimapInteractionLayout.TryMapPointerToWorld(
                pointer,
                layout,
                bounds,
                out Vector3 mapped));
        Assert.InRange(
            mapped.X,
            world.X - 0.01f,
            world.X + 0.01f);
        Assert.InRange(
            mapped.Z,
            world.Z - 0.01f,
            world.Z + 0.01f);
    }

    [Fact]
    public void PrimaryPointerJumpsAndDragsCameraInsideMap()
    {
        var terrain =
            new FlatTerrain();
        var camera =
            new RtsCamera(
                new RtsCameraSettings
                {
                    EdgeScrollEnabled = false,
                    InitialTarget = Vector3.Zero
                });
        var controller =
            new RtsMinimapInteractionController();
        var input =
            new InputState();
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1600,
                900,
                96);
        PresentationSnapshot snapshot =
            new(
                new SimulationTick(1),
                TimeSpan.FromMilliseconds(50),
                0,
                ReadOnlySpan<RenderInstance>.Empty,
                sessionId:
                    new SimulationSessionId(1));
        HudRect map =
            RtsMinimapInteractionLayout.GetMapRect(
                layout);
        Vector2 first =
            new(
                map.X +
                    map.Width *
                    0.25f,
                map.Y +
                    map.Height *
                    0.75f);
        Vector2 second =
            new(
                map.X +
                    map.Width *
                    0.75f,
                map.Y +
                    map.Height *
                    0.25f);

        MovePointer(
            input,
            first);
        SetPrimary(
            input,
            true,
            first);
        controller.UpdateCamera(
            input,
            camera,
            terrain,
            snapshot,
            layout);

        Vector3 afterJump =
            camera.Target;
        Assert.True(
            controller.PointerCaptured);
        Assert.True(
            controller.View.IsCameraDragging);

        MovePointer(
            input,
            second);
        controller.UpdateCamera(
            input,
            camera,
            terrain,
            snapshot,
            layout);

        Assert.NotEqual(
            afterJump,
            camera.Target);
        Assert.InRange(
            camera.Target.Y,
            4.99f,
            5.01f);

        SetPrimary(
            input,
            false,
            second);
        controller.UpdateCamera(
            input,
            camera,
            terrain,
            snapshot,
            layout);

        Assert.False(
            controller.View.IsCameraDragging);
    }

    [Fact]
    public void OverlayCycleIncludesPowerBeforeAll()
    {
        var controller =
            new RtsInformationLayerController();

        controller.CycleOverlay();
        controller.CycleOverlay();
        controller.CycleOverlay();
        controller.CycleOverlay();
        controller.CycleOverlay();

        Assert.Equal(
            StrategicOverlayMode.Power,
            controller.OverlayMode);

        controller.CycleOverlay();

        Assert.Equal(
            StrategicOverlayMode.All,
            controller.OverlayMode);
    }

    private static void MovePointer(
        InputState input,
        Vector2 point) =>
        input.Apply(
            PlatformInputEvent.PointerMoved(
                checked((int)MathF.Round(point.X)),
                checked((int)MathF.Round(point.Y))));

    private static void SetPrimary(
        InputState input,
        bool down,
        Vector2 point) =>
        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                down
                    ? PlatformInputEventKind.MouseButtonDown
                    : PlatformInputEventKind.MouseButtonUp,
                PlatformMouseButton.Left,
                checked((int)MathF.Round(point.X)),
                checked((int)MathF.Round(point.Y))));

    private sealed class FlatTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds =>
            new(
                Vector3.Zero,
                new Vector3(
                    1_024.0f,
                    100.0f,
                    1_024.0f));

        public bool TrySampleHeight(
            float worldX,
            float worldZ,
            out float height)
        {
            bool inside =
                worldX >= 0.0f &&
                worldX <= 1_024.0f &&
                worldZ >= 0.0f &&
                worldZ <= 1_024.0f;
            height =
                inside
                    ? 5.0f
                    : 0.0f;
            return inside;
        }
    }
}
