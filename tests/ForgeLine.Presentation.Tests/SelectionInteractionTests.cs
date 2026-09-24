using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SelectionInteractionTests
{
    private static readonly PlayerId LocalPlayer = new(1);
    private static readonly PlayerId ForeignPlayer = new(2);

    [Fact]
    public void SelectionSetSupportsSingleToggleAndReplacement()
    {
        var selection = new SelectionSet();
        var first = new EntityId(1, 1);
        var second = new EntityId(2, 1);
        var third = new EntityId(3, 1);

        selection.SetSingle(first);
        Assert.True(selection.Contains(first));
        Assert.Equal(1, selection.Count);

        selection.Toggle(second);
        Assert.True(selection.Contains(first));
        Assert.True(selection.Contains(second));

        selection.Toggle(first);
        Assert.False(selection.Contains(first));
        Assert.True(selection.Contains(second));

        selection.Replace([first, third]);
        Assert.Equal(2, selection.Count);
        Assert.True(selection.Contains(first));
        Assert.True(selection.Contains(third));
        Assert.False(selection.Contains(second));
    }

    [Fact]
    public void BoxSelectionExcludesForeignHiddenDisallowedAndOffscreenEntities()
    {
        var camera = CreateCamera();
        var filter = new SelectionFilter(
            LocalPlayer,
            ControllableEntityCategory.Unit);
        var world = CreateWorld(
            Instance(
                new EntityId(1, 1),
                Vector3.Zero,
                LocalPlayer,
                ControllableEntityCategory.Unit),
            Instance(
                new EntityId(2, 1),
                new Vector3(10.0f, 0.0f, 0.0f),
                ForeignPlayer,
                ControllableEntityCategory.Unit),
            Instance(
                new EntityId(3, 1),
                new Vector3(-10.0f, 0.0f, 0.0f),
                LocalPlayer,
                ControllableEntityCategory.Building),
            Instance(
                new EntityId(4, 1),
                new Vector3(0.0f, 0.0f, 10.0f),
                LocalPlayer,
                ControllableEntityCategory.Unit,
                RenderVisibilityMask.None),
            Instance(
                new EntityId(5, 1),
                new Vector3(50_000.0f, 0.0f, 0.0f),
                LocalPlayer,
                ControllableEntityCategory.Unit));

        EntityId[] picked = SelectionPicking.PickBox(
            camera,
            world,
            filter,
            Vector2.Zero,
            new Vector2(1600.0f, 900.0f),
            1600,
            900,
            1.0f);

        Assert.Equal(new[] { new EntityId(1, 1) }, picked);
    }

    [Fact]
    public void ClickSelectionAndRightClickCreateMovementRequest()
    {
        var camera = CreateCamera();
        var world = CreateWorld(
            Instance(
                new EntityId(9, 1),
                Vector3.Zero,
                LocalPlayer,
                ControllableEntityCategory.Unit));
        var terrain = new FlatTerrain();
        var input = new InputState();
        var controller = new RtsSelectionController(
            new SelectionFilter(
                LocalPlayer,
                ControllableEntityCategory.Unit));

        input.Apply(PlatformInputEvent.PointerMoved(800, 450));
        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Left,
                800,
                450));
        controller.Update(
            input,
            camera,
            world,
            terrain,
            1600,
            900,
            1.0f);

        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonUp,
                PlatformMouseButton.Left,
                800,
                450));
        controller.Update(
            input,
            camera,
            world,
            terrain,
            1600,
            900,
            1.0f);

        Assert.True(controller.Selection.Contains(new EntityId(9, 1)));

        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Right,
                800,
                450));
        controller.Update(
            input,
            camera,
            world,
            terrain,
            1600,
            900,
            1.0f);

        Assert.True(
            controller.TryTakeMovementRequest(
                out MovementOrderRequest request));
        Assert.Equal(new[] { new EntityId(9, 1) }, request.Entities.ToArray());
        Assert.True(float.IsFinite(request.WorldTarget.X));
        Assert.InRange(request.WorldTarget.Y, -0.001f, 0.001f);
        Assert.True(float.IsFinite(request.WorldTarget.Z));
        Assert.InRange(
            new Vector2(
                request.WorldTarget.X,
                request.WorldTarget.Z).Length(),
            0.0f,
            0.01f);
        Assert.False(controller.TryTakeMovementRequest(out _));
    }

    [Fact]
    public void ShiftClickTogglesExistingSelection()
    {
        var camera = CreateCamera();
        var entity = new EntityId(12, 1);
        var world = CreateWorld(
            Instance(
                entity,
                Vector3.Zero,
                LocalPlayer,
                ControllableEntityCategory.Unit));
        var terrain = new FlatTerrain();
        var input = new InputState();
        var controller = new RtsSelectionController(
            new SelectionFilter(
                LocalPlayer,
                ControllableEntityCategory.Unit));

        controller.Selection.SetSingle(entity);

        input.Apply(PlatformInputEvent.PointerMoved(800, 450));
        input.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                PlatformKey.LeftShift));
        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Left,
                800,
                450));
        controller.Update(
            input,
            camera,
            world,
            terrain,
            1600,
            900,
            1.0f);

        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonUp,
                PlatformMouseButton.Left,
                800,
                450));
        controller.Update(
            input,
            camera,
            world,
            terrain,
            1600,
            900,
            1.0f);

        Assert.False(controller.Selection.Contains(entity));
        Assert.Equal(0, controller.Selection.Count);
    }

    private static RtsCamera CreateCamera() =>
        new(
            new RtsCameraSettings
            {
                EdgeScrollEnabled = false,
                InitialTarget = Vector3.Zero,
                InitialDistance = 100.0f,
                PanReferenceDistance = 100.0f,
                MinimumPanSpeedScale = 1.0f,
                MaximumPanSpeedScale = 1.0f
            });

    private static RenderWorld CreateWorld(
        params RenderInstance[] instances)
    {
        var buffer = new PresentationSnapshotBuffer();
        buffer.Publish(
            new PresentationSnapshot(
                new SimulationTick(1),
                TimeSpan.FromMilliseconds(50),
                instances.Length,
                instances));

        var world = new RenderWorld();
        Assert.True(world.Update(buffer));
        return world;
    }

    private static RenderInstance Instance(
        EntityId entity,
        Vector3 position,
        PlayerId owner,
        ControllableEntityCategory category,
        RenderVisibilityMask visibility = RenderVisibilityMask.World) =>
        new(
            entity,
            new RenderTransform(
                position,
                Quaternion.Identity,
                new Vector3(8.0f)),
            new RenderMeshHandle(1),
            RenderMaterialHandle.Default,
            visibility,
            entity.Index,
            new SelectablePresentationMetadata(owner, category));

    private sealed class FlatTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds =>
            new(
                new Vector3(-10_000.0f, -100.0f, -10_000.0f),
                new Vector3(10_000.0f, 100.0f, 10_000.0f));

        public bool TrySampleHeight(
            float worldX,
            float worldZ,
            out float height)
        {
            height = 0.0f;
            return true;
        }

        public bool TrySampleNormal(
            float worldX,
            float worldZ,
            out Vector3 normal)
        {
            normal = Vector3.UnitY;
            return true;
        }
    }
}
