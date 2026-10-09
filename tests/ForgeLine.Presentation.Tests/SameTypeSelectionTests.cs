using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SameTypeSelectionTests
{
    [Fact]
    public void DoubleClickSelectsOnlyVisibleOwnedSameStableTypeAndNeverIssuesMovement()
    {
        var first = Unit(1, Vector3.Zero);
        var peer = Unit(2, new(12, 0, 0));
        var foreign = Unit(3, new(-12, 0, 0)) with { Selectable = new(new PlayerId(2), ControllableEntityCategory.Unit) };
        var hidden = Unit(4, new(20, 0, 0)) with { Visibility = RenderVisibilityMask.None };
        var offscreen = Unit(5, new(10_000, 0, 0));
        var otherType = Unit(6, new(25, 0, 0)) with { UnitFeature = new(UnitIds.MainBattleTank, default) };
        var unknown = Unit(7, new(30, 0, 0)) with { UnitFeature = default };
        var wreck = Unit(8, new(35, 0, 0)) with { UnitFeature = new(UnitIds.ScoutVehicle, UnitPresentationDamageState.Wreck) };
        var world = World(first, peer, foreign, hidden, offscreen, otherType, unknown, wreck);
        var input = new InputState();
        var camera = Camera();
        var controller = Controller();
        Click(input, controller, camera, world);
        Assert.Equal(1, controller.Selection.Count);
        Click(input, controller, camera, world, elapsed: TimeSpan.FromMilliseconds(100));
        Assert.Equal(new[] { first.Entity, peer.Entity }, controller.Selection.ToArray());
        Assert.False(controller.TryTakeMovementRequest(out _));
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("capture")]
    [InlineData("focus")]
    [InlineData("scale")]
    [InlineData("resize")]
    [InlineData("camera")]
    [InlineData("generation")]
    [InlineData("session")]
    [InlineData("shift")]
    public void InterruptedClicksCannotExpandSelection(string interruption)
    {
        var first = Unit(1, Vector3.Zero);
        var peer = Unit(2, new(12, 0, 0));
        var world = World(first, peer);
        var camera = Camera();
        var controller = Controller();
        var input = new InputState();
        Click(input, controller, camera, world);
        input.BeginFrame();
        if (interruption == "focus") input.Apply(PlatformInputEvent.FocusLost());
        if (interruption == "capture" || interruption == "focus")
            controller.Update(input, camera, world, new FlatTerrain(), 1600, 900, 1, pointerCaptured: true);
        if (interruption == "camera") camera.CenterOn(new(1, 0, 0));
        if (interruption == "generation") world = World(first with { Entity = new(1, 2) }, peer);
        if (interruption == "session") world = World(2, first, peer);
        if (interruption == "shift") input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.LeftShift));
        Click(input, controller, camera, world, interruption == "scale" ? 2 : 1,
            interruption == "resize" ? 1800 : 1600,
            TimeSpan.FromMilliseconds(interruption == "timeout" ? 351 : 100));
        Assert.False(controller.Selection.Contains(peer.Entity));
    }

    [Fact]
    public void ForeignObjectInFrontDoesNotPermitSelectingOwnedUnitBehindIt()
    {
        var camera = Camera();
        var front = Unit(2, camera.Position * .05f) with { Selectable = new(new PlayerId(2), ControllableEntityCategory.Unit) };
        var world = World(Unit(1, Vector3.Zero), front);
        var input = new InputState();
        var controller = Controller();
        Click(input, controller, camera, world);
        Click(input, controller, camera, world);
        Assert.Empty(controller.Selection.Entities);
    }

    internal static RtsCamera Camera() => new(new RtsCameraSettings { EdgeScrollEnabled = false, InitialTarget = Vector3.Zero, InitialDistance = 100 });
    private static RtsSelectionController Controller() => new(new(new PlayerId(1), ControllableEntityCategory.Unit | ControllableEntityCategory.Building));
    internal static RenderInstance Unit(uint id, Vector3 position) => new(new(id, 1),
        new(position, Quaternion.Identity, new Vector3(6)), new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
        Selectable: new(new PlayerId(1), ControllableEntityCategory.Unit, CanMove: true),
        UnitFeature: new(UnitIds.ScoutVehicle, default));
    private static RenderWorld World(params RenderInstance[] instances) => World(1, instances);
    private static RenderWorld World(ulong session, params RenderInstance[] instances)
    {
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(new PresentationSnapshot(new(1), TimeSpan.FromMilliseconds(50), instances.Length, instances, sessionId: new(session)));
        var world = new RenderWorld();
        world.Update(buffer);
        return world;
    }
    private static void Click(InputState input, RtsSelectionController controller, RtsCamera camera, RenderWorld world,
        float scale = 1, int width = 1600, TimeSpan elapsed = default)
    {
        input.BeginFrame();
        var p = camera.WorldToScreen(Vector3.Zero, width, 900).Position;
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, (int)p.X, (int)p.Y));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, (int)p.X, (int)p.Y));
        controller.Update(input, camera, world, new FlatTerrain(), width, 900, 1, pointerScale: scale, elapsed: elapsed);
    }
    internal sealed class FlatTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => new(new(-20_000, -100, -20_000), new(20_000, 100, 20_000));
        public bool TrySampleHeight(float x, float z, out float height) { height = 0; return true; }
        public bool TrySampleNormal(float x, float z, out Vector3 normal) { normal = Vector3.UnitY; return true; }
    }
}
