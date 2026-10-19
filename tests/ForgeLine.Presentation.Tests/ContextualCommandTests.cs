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
    public void PrimaryGeometryRendersInsideClipSpaceWithBoundedRetainedBuffers()
    {
        using var device = new SelectionOverlayRenderingTests.RecordingDevice();
        using var renderer = new PlayerActionRingHudRenderer(device,null);
        var context = new SelectionOverlayRenderingTests.RecordingContext { Width=1024, Height=720 };
        var layout = GameplayHudLayout.Create(1024,720,192,2);
        renderer.Render(context,Snapshot(),default,default,FormationTemplate.Compact,layout);
        Assert.Equal(0,renderer.LastRenderedVertexCount);
        Assert.Empty(device.Buffers);
    }

    [Fact]
    public void NormalHudNeverDrawsCountersWithoutDisablingMeasurement()
    {
        using var device = new SelectionOverlayRenderingTests.RecordingDevice();
        using var renderer = new ResourcePowerHudRenderer(device, null);
        var context = new SelectionOverlayRenderingTests.RecordingContext();
        var layout = GameplayHudLayout.Create(context.Width, context.Height, 96);
        var metrics = new RuntimeMetricsView(60, 20, RuntimeSimulationState.Running);
        renderer.Render(context, null, layout, metrics, showRuntimeMetrics: false);
        int without = renderer.LastRenderedVertexCount;
        renderer.Render(context, null, layout, metrics, showRuntimeMetrics: true);
        Assert.Equal(without, renderer.LastRenderedVertexCount);
        Assert.Equal(60, metrics.FramesPerSecond);
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
        Assert.Equal(0, ContextualCommandModel.Count(Snapshot(experienceTick: 3)));
        Assert.Equal(0, ContextualCommandModel.Count(Snapshot(sessionValue: 0, actionSession: 0)));
        Assert.Equal(0, ContextualCommandModel.Count(Snapshot(terminal: true)));
        Assert.Equal("MATCH COMPLETE", ContextualCommandModel.Status(Snapshot(terminal: true)));
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
        bool terminal = false, int pending = 0, bool core = false,
        ulong sessionValue = 1, ulong experienceTick = 4)
    {
        var tick = new SimulationTick(4);
        var session = new SimulationSessionId(sessionValue);
        var experience = default(PlayerExperienceSnapshot) with { Tick = new SimulationTick(experienceTick),
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

}
