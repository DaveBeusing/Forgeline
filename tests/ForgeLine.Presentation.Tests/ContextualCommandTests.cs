using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ContextualCommandTests
{
    [Fact]
    public void CoreBuildControlOpensExistingDockAndRetainsEveryAdvancedMode()
    {
        var snapshot = Snapshot(kind: PlayerSelectionKind.Building, eligible: 0, core: true);
        Assert.Equal(1, ContextualCommandModel.Count(snapshot));
        Assert.True(ContextualCommandModel.TryGet(snapshot, 0, out var command));
        Assert.True(command.OpensMode);
        Assert.Equal("B", command.Shortcut);
        var input = new InputState();
        var controller = new PlayerActionPanelController();
        Click(input, 0);
        controller.Update(input, snapshot, 1600, 900);
        Assert.Equal(PlayerActionPanelMode.Construction, controller.Mode);
        Assert.False(controller.TryTakeRequest(out _));
        Assert.Equal(7, PlayerActionDockInteractionLayout.ModeButtonCount);
    }

    [Fact]
    public void PrimaryGeometryRendersInsideClipSpaceWithBoundedRetainedBuffers()
    {
        using var device = new SelectionOverlayRenderingTests.RecordingDevice();
        using var renderer = new PlayerActionDockHudRenderer(device, null);
        var context = new SelectionOverlayRenderingTests.RecordingContext { Width = 1024, Height = 720 };
        var layout = GameplayHudLayout.Create(1024, 720, 192, 2);
        renderer.Render(context, Snapshot(), default, default, FormationTemplate.Compact, layout);
        Assert.InRange(renderer.LastRenderedVertexCount, 1, 262144);
        Assert.All(device.Buffer!.Vertices, vertex =>
        {
            Assert.InRange(vertex.Position.X, -1, 1);
            Assert.InRange(vertex.Position.Y, -1, 1);
        });
    }

    [Fact]
    public void RuntimeCountersAreOptInWithoutDisablingMeasurement()
    {
        using var device = new SelectionOverlayRenderingTests.RecordingDevice();
        using var renderer = new ResourcePowerHudRenderer(device, null);
        var context = new SelectionOverlayRenderingTests.RecordingContext();
        var layout = GameplayHudLayout.Create(context.Width, context.Height, 96);
        var metrics = new RuntimeMetricsView(60, 20, RuntimeSimulationState.Running);
        renderer.Render(context, null, layout, metrics, showRuntimeMetrics: false);
        int without = renderer.LastRenderedVertexCount;
        renderer.Render(context, null, layout, metrics, showRuntimeMetrics: true);
        Assert.True(renderer.LastRenderedVertexCount > without);
        Assert.Equal(60, metrics.FramesPerSecond);
    }

    [Theory]
    [InlineData(1024, 720, 96, 1f)]
    [InlineData(1024, 720, 192, 2f)]
    [InlineData(1600, 900, 144, 1.25f)]
    [InlineData(1920, 1080, 96, 1f)]
    [InlineData(3840, 2160, 192, 2f)]
    public void GeometryCaptureAndRenderedButtonsShareBounds(int width, int height, uint dpi, float scale)
    {
        var layout = GameplayHudLayout.Create(width, height, dpi, scale);
        var snapshot = Snapshot();
        Assert.False(layout.PrimaryCommands.IsEmpty);
        Assert.False(layout.PrimaryCommands.Intersects(layout.SelectionInspector));
        Assert.False(layout.PrimaryCommands.Intersects(layout.Minimap));
        Assert.False(layout.PrimaryCommands.Intersects(layout.ActionDock));
        for (int i = 0; i < 6; i++)
        {
            var button = ContextualCommandModel.Button(layout, i);
            Assert.True(layout.PrimaryCommands.Contains(Center(button)));
            Assert.True(button.Bottom <= layout.PrimaryCommands.Bottom);
            Assert.True(HudInteractionContext.BlocksWorldPointer(Center(button), layout, true, false));
            Assert.True(ContextualCommandModel.TryHit(Center(button), snapshot, layout, out var command));
            Assert.Equal(i, command.ItemIndex);
        }
    }

    [Fact]
    public void AvailabilityUsesExistingTacticalReasonsAndMixedEligibility()
    {
        var snapshot = Snapshot(kind: PlayerSelectionKind.Mixed);
        Assert.Contains("ELIGIBLE", ContextualCommandModel.Status(snapshot));
        Assert.Equal(6, ContextualCommandModel.Count(snapshot));
        Assert.True(ContextualCommandModel.TryGet(snapshot, 0, out var attack));
        Assert.False(attack.Availability.CanActivate);
        Assert.Equal("NO IDENTIFIED TARGET", attack.Availability.DisabledReason);
        Assert.True(ContextualCommandModel.TryGet(snapshot, 2, out var stop));
        Assert.True(stop.Availability.CanActivate);
        Assert.True(ContextualCommandModel.TryGet(snapshot, 5, out var fire));
        Assert.Equal("NO ARTILLERY", fire.Availability.DisabledReason);
    }

    [Fact]
    public void EmptyImmobileMissingStaleAndTerminalCannotDispatch()
    {
        Assert.Equal(0, ContextualCommandModel.Count(null));
        Assert.Equal(0, ContextualCommandModel.Count(Snapshot(count: 0)));
        Assert.Equal(0, ContextualCommandModel.Count(Snapshot(kind: PlayerSelectionKind.Building, eligible: 0)));
        Assert.Equal(0, ContextualCommandModel.Count(Snapshot(actionTick: 3)));
        Assert.Equal(0, ContextualCommandModel.Count(Snapshot(actionSession: 2)));
        Assert.Equal(0, ContextualCommandModel.Count(Snapshot(terminal: true)));
        Assert.Equal("MATCH COMPLETE", ContextualCommandModel.Status(Snapshot(terminal: true)));
    }

    [Fact]
    public void QuickPressDispatchesOnceAndPendingSnapshotDisablesIt()
    {
        var snapshot = Snapshot();
        var controller = new PlayerActionPanelController();
        var input = new InputState();
        Click(input, 2);
        controller.Update(input, snapshot, 1600, 900);
        Assert.True(controller.PointerCaptured);
        Assert.True(controller.TryTakeRequest(out var request));
        Assert.Equal(PlayerActionRequestKind.SubmitStopCombat, request.Kind);
        input.BeginFrame();
        Click(input, 2);
        controller.Update(input, snapshot, 1600, 900);
        Assert.False(controller.TryTakeRequest(out _));
        Assert.True(ContextualCommandModel.TryGet(Snapshot(pending: 1), 2, out var pending));
        Assert.Equal("REQUEST PENDING", pending.Availability.DisabledReason);
    }

    [Fact]
    public void DisabledResizeAndStalePressesNeverSubmit()
    {
        var controller = new PlayerActionPanelController();
        var input = new InputState();
        Click(input, 0);
        controller.Update(input, Snapshot(), 1600, 900);
        Assert.False(controller.TryTakeRequest(out _));
        input.BeginFrame();
        Click(input, 2);
        controller.Update(input, Snapshot(), 1920, 1080);
        Assert.False(controller.TryTakeRequest(out _));
        input.BeginFrame();
        Click(input, 2);
        controller.Update(input, Snapshot(actionTick: 3), 1600, 900);
        Assert.False(controller.TryTakeRequest(out _));
    }

    [Fact]
    public void ContextualTooltipExplainsDisabledReasonWithoutOpeningAdvancedDock()
    {
        var snapshot = Snapshot();
        var view = new HoverTooltipView(snapshot.SessionId, EntityId.Invalid, default, Vector2.Zero,
            true, 1600, 900, 1, PlayerActionPanelMode.Tactical, (int)PlayerActionDockControlKind.Item, 0, true);
        var content = HoverTooltipResolver.Resolve(snapshot, view, default);
        Assert.NotNull(content);
        Assert.Equal("NO IDENTIFIED TARGET", content.Value.Status);
        Assert.Null(HoverTooltipResolver.Resolve(Snapshot(actionTick: 3), view, default));
    }

    [Fact]
    public void RepeatedContextMappingHasNoWarmAllocations()
    {
        var snapshot = Snapshot();
        for (int i = 0; i < 128; i++) _ = ContextualCommandModel.Count(snapshot);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
            for (int j = 0; ContextualCommandModel.TryGet(snapshot, j, out _); j++) { }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static PresentationSnapshot Snapshot(PlayerSelectionKind kind = PlayerSelectionKind.Unit,
        int count = 1, int eligible = 1, ulong actionTick = 4, ulong actionSession = 1,
        bool terminal = false, int pending = 0, bool core = false)
    {
        var tick = new SimulationTick(4);
        var session = new SimulationSessionId(1);
        var experience = default(PlayerExperienceSnapshot) with { Tick = tick,
            Selection = PlayerSelectionSummary.Empty with { Count = count, Kind = kind,
                CommonBuildingId = core ? BuildingIds.CommandCore : default },
            MatchStatus = terminal ? PlayerMatchStatus.Victory : default };
        var tactical = new PlayerTacticalActionReadModel([new EntityId(1, 1)], count, eligible, 0,
            0, 0, false, false, default(CombatOrderKind), default, [], []);
        var actions = new PlayerActionSnapshot(new SimulationSessionId(actionSession), new SimulationTick(actionTick),
            core ? [new PlayerConstructionActionReadModel(BuildingIds.PowerPlant, "Power Plant", [], false)] : [],
            pending, null, null, tactical: tactical);
        return new PresentationSnapshot(tick, TimeSpan.Zero, 1, [], sessionId: session,
            playerExperience: experience, playerActions: actions);
    }

    private static Vector2 Center(HudRect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    private static void Click(InputState input, int index)
    {
        var point = Center(ContextualCommandModel.Button(GameplayHudLayout.Create(1600, 900, 96), index));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Left, (int)point.X, (int)point.Y));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp,
            PlatformMouseButton.Left, (int)point.X, (int)point.Y));
    }
}
