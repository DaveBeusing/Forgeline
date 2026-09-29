using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SimulationPresentationReadModelTests
{
    private static readonly PlayerId LocalPlayer = new(1);

    [Fact]
    public void CompletedTickSnapshotOwnsPlayerStateAndSessionIdentity()
    {
        using VerticalSliceScenario scenario = CreateScenario(4201);
        var buffer = new PresentationSnapshotBuffer();
        var interaction = new PresentationInteractionState();
        var gateway = new PlayerCommandGateway(
            scenario.Simulation,
            scenario.Services.BuildingCommands,
            scenario.BattlefieldRuntime.MatchStateEntity);

        interaction.SetSelection([scenario.West.CommandCore]);
        scenario.Simulation.RegisterTickObserver(gateway);
        scenario.Simulation.RegisterTickObserver(
            new PresentationExtractor(
                buffer,
                new PresentationExtractionContext(
                    scenario,
                    LocalPlayer,
                    interaction,
                    gateway)));

        scenario.Simulation.AdvanceOneTick();

        Assert.True(buffer.TryReadLatest(out PresentationSnapshot snapshot));
        Assert.Equal(scenario.Simulation.SessionId, snapshot.SessionId);
        Assert.True(snapshot.PlayerExperience.HasValue);
        Assert.Equal(snapshot.Tick, snapshot.PlayerExperience.Value.Tick);
        Assert.Equal(
            scenario.West.CommandCore,
            snapshot.PlayerExperience.Value.Selection.PrimaryEntity);

        PlayerExperienceSnapshot captured = snapshot.PlayerExperience.Value;
        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(
                scenario.West.CommandCore));

        Assert.Equal(captured, snapshot.PlayerExperience.Value);
    }

    [Fact]
    public void PlacementControllerRejectsStalePreview()
    {
        var interaction = new PresentationInteractionState();
        var controller = new RtsBuildingPlacementController(LocalPlayer);
        var input = new InputState();
        RtsCamera camera = CreateCamera();
        var terrain = new FlatTerrain();

        input.Apply(PlatformInputEvent.KeyChanged(
            PlatformInputEventKind.KeyDown,
            PlatformKey.F5));
        input.Apply(PlatformInputEvent.PointerMoved(800, 450));

        controller.Update(
            input,
            camera,
            terrain,
            snapshot: null,
            interaction,
            1600,
            900);

        BuildingPlacementPreview preview = ValidPreview();
        PresentationSnapshot stale = SnapshotWithPreview(
            requestId: 999,
            tick: 1,
            preview);

        controller.Update(
            input,
            camera,
            terrain,
            stale,
            interaction,
            1600,
            900);

        Assert.Equal(
            PlacementPreviewFreshness.Stale,
            controller.PreviewFreshness);

        input.Apply(PlatformInputEvent.MouseButtonChanged(
            PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Left,
            800,
            450));
        controller.Update(
            input,
            camera,
            terrain,
            stale,
            interaction,
            1600,
            900);

        Assert.False(controller.TryTakePlacementRequest(out _));
    }

    [Fact]
    public void NewSessionResetsInterpolationAndSelection()
    {
        var buffer = new PresentationSnapshotBuffer();
        var world = new RenderWorld();
        var entity = new EntityId(7, 1);
        var controller = new RtsSelectionController(
            new SelectionFilter(
                LocalPlayer,
                ControllableEntityCategory.Unit));
        var input = new InputState();
        RtsCamera camera = CreateCamera();
        var terrain = new FlatTerrain();

        buffer.Publish(
            Snapshot(
                new SimulationSessionId(11),
                8,
                entity,
                Vector3.Zero));
        Assert.True(world.Update(buffer));

        controller.Update(
            input,
            camera,
            world,
            terrain,
            1600,
            900,
            1.0f);
        controller.Selection.SetSingle(entity);

        buffer.Publish(
            Snapshot(
                new SimulationSessionId(12),
                1,
                entity,
                new Vector3(100.0f, 0.0f, 0.0f)));
        Assert.True(world.Update(buffer));

        RenderInstance current = world.GetInterpolatedInstance(0, 0.5f);
        Assert.Equal(100.0f, current.Transform.Position.X);
        Assert.Null(world.PreviousSnapshot);

        controller.Update(
            input,
            camera,
            world,
            terrain,
            1600,
            900,
            1.0f);
        Assert.Equal(0, controller.Selection.Count);
    }

    private static VerticalSliceScenario CreateScenario(ulong seed)
    {
        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateHeadless(
                VerticalSliceScenarioProfile.Gameplay,
                seed) with
            {
                Participants =
                    VerticalSliceRuntimeSettings.CreateDefaultParticipants(
                        westComputerControlled: false,
                        eastComputerControlled: false)
            };

        return VerticalSliceScenario.Create(
            runtime,
            TestContext.Current.CancellationToken);
    }

    private static PresentationSnapshot SnapshotWithPreview(
        ulong requestId,
        ulong tick,
        BuildingPlacementPreview preview) =>
        new(
            new SimulationTick(tick),
            TimeSpan.FromMilliseconds(50),
            0,
            ReadOnlySpan<RenderInstance>.Empty,
            placementPreview:
                new BuildingPlacementPreviewReadModel(
                    requestId,
                    new SimulationTick(tick),
                    preview));

    private static PresentationSnapshot Snapshot(
        SimulationSessionId sessionId,
        ulong tick,
        EntityId entity,
        Vector3 position) =>
        new(
            new SimulationTick(tick),
            TimeSpan.FromMilliseconds(50),
            1,
            [
                new RenderInstance(
                    entity,
                    new RenderTransform(
                        position,
                        Quaternion.Identity,
                        Vector3.One),
                    new RenderMeshHandle(1),
                    RenderMaterialHandle.Default,
                    RenderVisibilityMask.World,
                    entity.Index,
                    new SelectablePresentationMetadata(
                        LocalPlayer,
                        ControllableEntityCategory.Unit))
            ],
            sessionId: sessionId);

    private static BuildingPlacementPreview ValidPreview() =>
        new(
            BuildingIds.PowerPlant,
            "building.power_plant",
            "Power Plant",
            Vector3.Zero,
            BuildingOrientation.North,
            new AxisAlignedBounds(
                new Vector3(-4.0f, 0.0f, -4.0f),
                new Vector3(4.0f, 8.0f, 4.0f)),
            true,
            BuildingPlacementFailureReason.None,
            EntityId.Invalid);

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
                new Vector3(-10_000.0f, -100.0f, -10_000.0f),
                new Vector3(10_000.0f, 100.0f, 10_000.0f));

        public bool TrySampleHeight(float worldX, float worldZ, out float height)
        {
            height = 0.0f;
            return true;
        }

        public bool TrySampleNormal(float worldX, float worldZ, out Vector3 normal)
        {
            normal = Vector3.UnitY;
            return true;
        }
    }
}
