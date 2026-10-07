using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Intelligence;
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
        controller.Update(
            input,
            camera,
            terrain,
            snapshot,
            layout,
            [],
            TacticalTargetingMode.None,
            FormationTemplate.Compact);

        Vector3 afterJump =
            camera.Target;
        Assert.True(
            controller.PointerCaptured);
        Assert.True(
            controller.View.IsCameraDragging);

        MovePointer(
            input,
            second);
        controller.Update(
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
        controller.Update(
            input,
            camera,
            terrain,
            snapshot,
            layout);

        Assert.False(
            controller.View.IsCameraDragging);
    }

    [Fact]
    public void SecondaryPointerCreatesMovementRequestForCurrentSelection()
    {
        var terrain =
            new FlatTerrain();
        var camera =
            new RtsCamera(
                new RtsCameraSettings
                {
                    EdgeScrollEnabled = false
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
            Snapshot();
        EntityId selected =
            new(44, 1);
        HudRect map =
            RtsMinimapInteractionLayout.GetMapRect(
                layout);
        Vector2 pointer =
            new(
                map.X +
                    map.Width *
                    0.6f,
                map.Y +
                    map.Height *
                    0.4f);

        MovePointer(
            input,
            pointer);
        SetSecondary(
            input,
            true,
            pointer);
        controller.Update(
            input,
            camera,
            terrain,
            snapshot,
            layout,
            [selected],
            TacticalTargetingMode.None,
            FormationTemplate.Line);

        Assert.True(
            controller.PointerCaptured);
        Assert.True(
            controller.TryTakeMovementRequest(
                out MovementOrderRequest request));
        Assert.Equal(
            new[] { selected },
            request.Entities.ToArray());
    }

    [Fact]
    public void AttackMinimapUsesOnlyIdentifiedTacticalTargets()
    {
        EntityId selected =
            new(45, 1);
        EntityId identified =
            new(46, 1);
        var tactical =
            new PlayerTacticalActionReadModel(
                [selected],
                1,
                1,
                0,
                0,
                0,
                false,
                false,
                default,
                default,
                [
                    new PlayerTacticalTargetReadModel(
                        identified,
                        IntelligenceContactKey.FromEntity(
                            identified),
                        new Vector3(
                            640.0f,
                            5.0f,
                            640.0f),
                        IntelligenceState.Identified,
                        new SimulationTick(1),
                        1)
                ],
                []);
        PresentationSnapshot snapshot =
            Snapshot(
                tactical: tactical);
        var controller =
            new RtsMinimapInteractionController();
        var input =
            new InputState();
        var terrain =
            new FlatTerrain();
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1600,
                900,
                96);
        Vector2 pointer =
            RtsMinimapInteractionLayout.MapWorldToPointer(
                new Vector3(
                    640.0f,
                    0.0f,
                    640.0f),
                layout,
                terrain.WorldBounds);

        MovePointer(
            input,
            pointer);
        SetPrimary(
            input,
            true,
            pointer);
        controller.Update(
            input,
            new RtsCamera(),
            terrain,
            snapshot,
            layout,
            [selected],
            TacticalTargetingMode.Attack,
            FormationTemplate.Compact);

        Assert.True(
            controller.TryTakeActionRequest(
                out PlayerActionRequest request));
        Assert.Equal(
            PlayerActionRequestKind.SubmitAttack,
            request.Kind);
        Assert.Equal(
            identified,
            request.TacticalTarget);
    }

    [Fact]
    public void DetectedContactCannotLeakEntityIdIntoAttackButSupportsOpaqueFireMission()
    {
        EntityId selected =
            new(47, 1);
        EntityId hiddenEnemy =
            new(48, 1);
        var intelligenceStore =
            new FactionIntelligenceStore();
        var localFaction =
            new FactionId(1);
        intelligenceStore.BeginTick(
            new SimulationTick(1));
        intelligenceStore.Observe(
            localFaction,
            hiddenEnemy,
            new IntelligenceSignature(
                new FactionId(2),
                700),
            new Vector3(
                512.0f,
                0.0f,
                512.0f),
            IntelligenceState.Detected,
            new SimulationTick(1));
        var tactical =
            new PlayerTacticalActionReadModel(
                [selected],
                1,
                1,
                0,
                0,
                0,
                false,
                false,
                default,
                default,
                [],
                []);
        var terrain =
            new FlatTerrain();
        PresentationSnapshot snapshot =
            Snapshot(
                tactical,
                intelligenceStore.Capture(
                    localFaction,
                    terrain.WorldBounds));
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                1600,
                900,
                96);
        Vector2 pointer =
            RtsMinimapInteractionLayout.MapWorldToPointer(
                new Vector3(
                    512.0f,
                    0.0f,
                    512.0f),
                layout,
                terrain.WorldBounds);

        var attack =
            new RtsMinimapInteractionController();
        var attackInput =
            new InputState();
        MovePointer(
            attackInput,
            pointer);
        SetPrimary(
            attackInput,
            true,
            pointer);
        attack.Update(
            attackInput,
            new RtsCamera(),
            terrain,
            snapshot,
            layout,
            [selected],
            TacticalTargetingMode.Attack,
            FormationTemplate.Compact);

        Assert.False(
            attack.TryTakeActionRequest(
                out _));

        var fireMission =
            new RtsMinimapInteractionController();
        var fireInput =
            new InputState();
        MovePointer(
            fireInput,
            pointer);
        SetPrimary(
            fireInput,
            true,
            pointer);
        fireMission.Update(
            fireInput,
            new RtsCamera(),
            terrain,
            snapshot,
            layout,
            [selected],
            TacticalTargetingMode.FireMission,
            FormationTemplate.Compact);

        Assert.True(
            fireMission.TryTakeActionRequest(
                out PlayerActionRequest request));
        Assert.Equal(
            PlayerActionRequestKind.SubmitFireMissionContact,
            request.Kind);
        Assert.False(
            request.TacticalTarget.IsValid);
        Assert.Equal(
            IntelligenceContactKey.FromEntity(
                hiddenEnemy),
            request.TacticalContactKey);
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

    private static PresentationSnapshot Snapshot(
        PlayerTacticalActionReadModel? tactical = null,
        FactionIntelligenceSnapshot? intelligence = null)
    {
        var session =
            new SimulationSessionId(1);
        PlayerActionSnapshot? actions =
            tactical is null
                ? null
                : new PlayerActionSnapshot(
                    session,
                    new SimulationTick(1),
                    [],
                    0,
                    null,
                    null,
                    tactical:
                        tactical);

        return new PresentationSnapshot(
            new SimulationTick(1),
            TimeSpan.FromMilliseconds(50),
            0,
            ReadOnlySpan<RenderInstance>.Empty,
            intelligence:
                intelligence,
            sessionId:
                session,
            playerActions:
                actions);
    }

    private static void MovePointer(
        InputState input,
        Vector2 point) =>
        input.Apply(
            PlatformInputEvent.PointerMoved(
                checked((int)MathF.Round(point.X)),
                checked((int)MathF.Round(point.Y))));

    private static void SetSecondary(
        InputState input,
        bool down,
        Vector2 point) =>
        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                down
                    ? PlatformInputEventKind.MouseButtonDown
                    : PlatformInputEventKind.MouseButtonUp,
                PlatformMouseButton.Right,
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
