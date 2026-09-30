using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class WorldInspectionTests
{
    [Fact]
    public void ResourceDepositCanBeInspectedWithoutEnteringCommandSelection()
    {
        var camera =
            new RtsCamera(
                new RtsCameraSettings
                {
                    EdgeScrollEnabled = false,
                    InitialTarget = Vector3.Zero,
                    InitialDistance = 100.0f,
                    PanReferenceDistance = 100.0f,
                    MinimumPanSpeedScale = 1.0f,
                    MaximumPanSpeedScale = 1.0f
                });

        var entity =
            new EntityId(7, 1);
        var instance =
            new RenderInstance(
                entity,
                new RenderTransform(
                    Vector3.Zero,
                    Quaternion.Identity,
                    new Vector3(12.0f)),
                new RenderMeshHandle(4_001),
                RenderMaterialHandle.Default,
                RenderVisibilityMask.World,
                entity.Index,
                SelectablePresentationMetadata.None,
                new WorldFeaturePresentationMetadata(
                    WorldVisualId.ResourceFerrousOre,
                    WorldPresentationKind.ResourceDeposit,
                    ResourceDepositPresentationState.Untouched,
                    Inspectable: true));

        var buffer =
            new PresentationSnapshotBuffer();
        buffer.Publish(
            new PresentationSnapshot(
                new SimulationTick(1),
                TimeSpan.FromMilliseconds(50),
                1,
                [instance]));

        var world =
            new RenderWorld();
        Assert.True(
            world.Update(
                buffer));

        var controller =
            new RtsSelectionController(
                new SelectionFilter(
                    new PlayerId(1),
                    ControllableEntityCategory.Unit));
        var input =
            new InputState();

        input.Apply(
            PlatformInputEvent.PointerMoved(
                800,
                450));
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
            new FlatTerrain(),
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
            new FlatTerrain(),
            1600,
            900,
            1.0f);

        Assert.Equal(
            entity,
            controller.InspectedEntity);
        Assert.Equal(
            0,
            controller.Selection.Count);
    }

    private sealed class FlatTerrain : ForgeLine.World.ITerrainQuery
    {
        public ForgeLine.World.AxisAlignedBounds WorldBounds =>
            new(
                new Vector3(-1_000.0f),
                new Vector3(1_000.0f));

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
