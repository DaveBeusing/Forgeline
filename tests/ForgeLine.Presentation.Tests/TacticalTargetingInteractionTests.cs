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

public sealed class TacticalTargetingInteractionTests
{
    private static readonly PlayerId LocalPlayer = new(1);
    private static readonly FactionId LocalFaction = new(1);
    private static readonly FactionId EnemyFaction = new(2);

    [Fact]
    public void AttackTargetingUsesCopiedIdentifiedTargetAndCapturesClick()
    {
        var selected =
            new EntityId(101, 1);
        var target =
            new EntityId(201, 1);
        PresentationSnapshot snapshot =
            CreateSnapshot(
                selected,
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
                            target,
                            IntelligenceContactKey.FromEntity(
                                target),
                            Vector3.Zero,
                            IntelligenceState.Identified,
                            new SimulationTick(4),
                            1)
                    ],
                    []));
        var camera =
            CreateCamera();
        var terrain =
            new FlatTerrain();
        var input =
            new InputState();
        var targeting =
            new RtsTacticalTargetingController();

        targeting.Begin(
            PlayerActionRequest.BeginAttackTargeting(
                [selected]),
            snapshot.SessionId);

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

        targeting.Update(
            input,
            camera,
            terrain,
            snapshot,
            1600,
            900,
            FormationTemplate.Compact);

        Assert.True(targeting.PointerCaptured);
        Assert.False(targeting.IsActive);
        Assert.True(
            targeting.TryTakeRequest(
                out PlayerActionRequest request));
        Assert.Equal(
            PlayerActionRequestKind.SubmitAttack,
            request.Kind);
        Assert.Equal(target, request.TacticalTarget);
        Assert.Equal(
            [selected],
            request.TacticalEntities);

        var buffer =
            new PresentationSnapshotBuffer();
        buffer.Publish(snapshot);
        var world =
            new RenderWorld();
        Assert.True(world.Update(buffer));
        var selection =
            new RtsSelectionController(
                new SelectionFilter(
                    LocalPlayer,
                    ControllableEntityCategory.Unit));

        selection.Update(
            input,
            camera,
            world,
            terrain,
            1600,
            900,
            1.0f,
            pointerCaptured:
                targeting.PointerCaptured);

        Assert.Equal(0, selection.Selection.Count);
    }

    [Fact]
    public void FireMissionTargetingPreservesOpaqueDetectedContact()
    {
        var selected =
            new EntityId(102, 1);
        var enemy =
            new EntityId(202, 1);
        var intelligence =
            new FactionIntelligenceStore();
        intelligence.BeginTick(
            new SimulationTick(4));
        intelligence.Observe(
            LocalFaction,
            enemy,
            new IntelligenceSignature(
                EnemyFaction,
                identityKey: 55),
            Vector3.Zero,
            IntelligenceState.Detected,
            new SimulationTick(4));

        PresentationSnapshot snapshot =
            CreateSnapshot(
                selected,
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
                    []),
                intelligence.Capture(
                    LocalFaction));
        var input =
            new InputState();
        var targeting =
            new RtsTacticalTargetingController();

        targeting.Begin(
            PlayerActionRequest.BeginFireMissionTargeting(
                [selected]),
            snapshot.SessionId);
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

        targeting.Update(
            input,
            CreateCamera(),
            new FlatTerrain(),
            snapshot,
            1600,
            900,
            FormationTemplate.Compact);

        Assert.True(
            targeting.TryTakeRequest(
                out PlayerActionRequest request));
        Assert.Equal(
            PlayerActionRequestKind.SubmitFireMissionContact,
            request.Kind);
        Assert.Equal(
            IntelligenceContactKey.FromEntity(
                enemy),
            request.TacticalContactKey);
        Assert.Equal(3, request.TacticalRounds);
    }

    [Fact]
    public void FocusLossAndSessionChangeCancelTargeting()
    {
        var selected =
            new EntityId(103, 1);
        PresentationSnapshot first =
            CreateSnapshot(
                selected,
                Tactical(selected));
        var targeting =
            new RtsTacticalTargetingController();
        var input =
            new InputState();

        targeting.Begin(
            PlayerActionRequest.BeginAttackMoveTargeting(
                [selected]),
            first.SessionId);

        input.Apply(
            PlatformInputEvent.PointerMoved(
                400,
                300));
        targeting.Update(
            input,
            CreateCamera(),
            new FlatTerrain(),
            first,
            1600,
            900,
            FormationTemplate.Line);

        input.Apply(
            PlatformInputEvent.FocusLost());
        targeting.Update(
            input,
            CreateCamera(),
            new FlatTerrain(),
            first,
            1600,
            900,
            FormationTemplate.Line);

        Assert.False(targeting.IsActive);

        targeting.Begin(
            PlayerActionRequest.BeginRetreatTargeting(
                [selected]),
            first.SessionId);

        PresentationSnapshot restarted =
            CreateSnapshot(
                selected,
                Tactical(selected),
                session:
                    new SimulationSessionId(202));

        targeting.Update(
            input,
            CreateCamera(),
            new FlatTerrain(),
            restarted,
            1600,
            900,
            FormationTemplate.Column);

        Assert.False(targeting.IsActive);
        Assert.False(
            targeting.TryTakeRequest(out _));
    }

    private static PlayerTacticalActionReadModel Tactical(
        EntityId selected) =>
        new(
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

    private static PresentationSnapshot CreateSnapshot(
        EntityId selected,
        PlayerTacticalActionReadModel tactical,
        FactionIntelligenceSnapshot? intelligence = null,
        SimulationSessionId session = default)
    {
        SimulationSessionId resolvedSession =
            session.IsSpecified
                ? session
                : new SimulationSessionId(101);
        var actions =
            new PlayerActionSnapshot(
                resolvedSession,
                new SimulationTick(4),
                [],
                0,
                null,
                null,
                tactical: tactical);

        return new PresentationSnapshot(
            new SimulationTick(4),
            TimeSpan.FromMilliseconds(50),
            1,
            [
                new RenderInstance(
                    selected,
                    new RenderTransform(
                        Vector3.Zero,
                        Quaternion.Identity,
                        new Vector3(8.0f)),
                    new RenderMeshHandle(1),
                    RenderMaterialHandle.Default,
                    RenderVisibilityMask.World,
                    selected.Index,
                    new SelectablePresentationMetadata(
                        LocalPlayer,
                        ControllableEntityCategory.Unit))
            ],
            intelligence:
                intelligence,
            sessionId:
                resolvedSession,
            playerActions:
                actions);
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

    private sealed class FlatTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds =>
            new(
                new Vector3(
                    -10_000.0f,
                    -100.0f,
                    -10_000.0f),
                new Vector3(
                    10_000.0f,
                    100.0f,
                    10_000.0f));

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
