using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ActionableAlertTests
{
    [Fact]
    public void RepeatedConditionsKeepIdentityResolveAndRecurWithoutAliasing()
    {
        var tracker = new AlertLifecycleTracker();
        var first = tracker.Capture(new(1), Experience(4, PlayerAlertState.LowPower), []);
        for (ulong tick = 5; tick < 100; tick++)
            Assert.Equal(first.Alerts[0].Identity, tracker.Capture(new(1), Experience(tick, PlayerAlertState.LowPower), []).Alerts[0].Identity);
        Assert.Empty(tracker.Capture(new(1), Experience(100), []).Alerts);
        var next = tracker.Capture(new(1), Experience(101, PlayerAlertState.LowPower), []);
        Assert.NotEqual(first.Alerts[0].Identity, next.Alerts[0].Identity);
        Assert.Single(first.Alerts);
        Assert.Throws<NotSupportedException>(() => ((IList<ActionableAlert>)first.Alerts).Clear());
    }
    [Theory]
    [InlineData(2, 5)]
    [InlineData(1, 2)]
    public void RestartAndSaveRestoreRollbackBeginFreshIdentity(ulong session, ulong tick)
    {
        var tracker = new AlertLifecycleTracker();
        var before = tracker.Capture(new(1), Experience(4, PlayerAlertState.LowPower), []);
        var after = tracker.Capture(new(session), Experience(tick, PlayerAlertState.LowPower), []);
        Assert.NotEqual(before.Alerts[0].Identity, after.Alerts[0].Identity);
    }
    [Fact]
    public void MultipleSeveritiesHaveStableCriticalBeforeWarningOrderAndBoundedStorage()
    {
        var tracker = new AlertLifecycleTracker();
        var alerts = tracker.Capture(new(1), Experience(4, (PlayerAlertState)31), []);
        Assert.Equal(5, alerts.Alerts.Count);
        Assert.All(alerts.Alerts.Take(3), row => Assert.Equal(AlertSeverity.Critical, row.Severity));
        Assert.All(alerts.Alerts.Skip(3), row => Assert.Equal(AlertSeverity.Warning, row.Severity));
        Assert.Equal(5, new ActionableAlertSnapshot(new(1), new(4), new(1), Enumerable.Repeat(alerts.Alerts[0], 1000).ToArray()).Alerts.Count);
    }
    [Fact]
    public void OwnedCoreDestinationIsRetainedAndDestroyedTargetIsUnavailable()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var buffer = WorldHoverExtractionTests.Observe(scenario, new());
        var core = scenario.GetBase(new(1)).CommandCore;
        var enemy = scenario.GetBase(new(2)).CommandCore;
        var health = scenario.Simulation.Entities.GetComponent<HealthState>(core);
        scenario.Simulation.Entities.SetComponent(core, new HealthState(health.Maximum * .5, health.Maximum));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var before));
        var damaged = Assert.Single(before.Alerts!.Alerts, row => row.Identity.Kind == PlayerAlertState.CommandCoreDamaged);
        Assert.Equal(core, damaged.Target); Assert.DoesNotContain(before.Alerts.Alerts, row => row.Target == enemy);
        var selection = new SelectionSet(); selection.SetSingle(damaged.Target);
        Assert.True(RtsCameraFocusController.TryGroup(before, new(1), selection, out _));
        scenario.Simulation.Entities.DestroyEntity(core);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var after));
        Assert.DoesNotContain(after.Alerts!.Alerts, row => row.Target == core);
        Assert.Contains(after.Alerts.Alerts, row => row.Identity.Kind == PlayerAlertState.CommandCoreDestroyed && !row.Target.IsValid);
        Assert.False(RtsCameraFocusController.TryGroup(after, new(1), selection, out _));
        Assert.Equal(core, damaged.Target);
    }
    [Theory]
    [InlineData(5, 1, 1)]
    [InlineData(4, 2, 1)]
    [InlineData(4, 1, 2)]
    public void StaleTickSessionAndPlayerRejectDestinations(ulong tick, ulong session, uint player)
    {
        var alerts = new AlertLifecycleTracker().Capture(new(1), Experience(4, PlayerAlertState.LowPower), []);
        Assert.Null(ActionableAlertSnapshot.Resolve(Snapshot(alerts, tick, session, player)));
    }
    [Fact]
    public void ShortClickFocusesOnceWithoutOrdersAndUnavailableTargetOpensOperations()
    {
        var tracker = new AlertLifecycleTracker(); var target = new EntityId(2, 7);
        var alerts = tracker.Capture(new(1), Experience(4, PlayerAlertState.LowPower), [default, default, default, default, target]);
        var snapshot = Snapshot(alerts); var layout = GameplayHudLayout.Create(1600, 900, 96);
        var controller = new ActionableAlertController(); var input = new InputState();
        controller.Update(input, snapshot, layout);
        Click(input, ActionableAlertLayout.Row(layout, 0));
        var result = controller.Update(input, snapshot, layout);
        Assert.True(result.Captured); Assert.Equal(target, result.Target); Assert.False(result.OpenOperations);
        Assert.False(controller.Update(input, snapshot, layout).Target.IsValid);
        var missing = Snapshot(tracker.Capture(new(1), Experience(4, PlayerAlertState.LowPower), []));
        Click(input, ActionableAlertLayout.Row(layout, 0));
        var fallback = controller.Update(input, missing, layout);
        Assert.True(fallback.OpenOperations); Assert.Equal(OperationsCategory.Power, fallback.Category);
        Assert.Contains("UNAVAILABLE", controller.Feedback);
    }
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ModalTerminalAndResizeConsumeClickEdges(bool blocked, bool terminal, bool resize)
    {
        var alerts = new AlertLifecycleTracker().Capture(new(1), Experience(4, PlayerAlertState.LowPower), []);
        var controller = new ActionableAlertController(); var input = new InputState();
        var layout = GameplayHudLayout.Create(1600, 900, 96); var snapshot = Snapshot(alerts);
        controller.Update(input, snapshot, layout);
        Click(input, ActionableAlertLayout.Row(layout, 0));
        var result = controller.Update(input, terminal ? Snapshot(alerts, terminal: true) : snapshot,
            resize ? GameplayHudLayout.Create(1920, 1080, 144) : layout, blocked);
        Assert.False(result.OpenOperations); Assert.False(result.Target.IsValid);
        Assert.False(controller.Update(input, snapshot, layout).OpenOperations);
    }
    [Fact]
    public void SkippedPausedFramesConsumeEdgesAndDoNotResolveUnchangedConditions()
    {
        var alerts = new AlertLifecycleTracker().Capture(new(1), Experience(4, PlayerAlertState.LowPower), []);
        var controller = new ActionableAlertController(); var input = new InputState(); var snapshot = Snapshot(alerts);
        var layout = GameplayHudLayout.Create(1600, 900, 96); controller.Update(input, snapshot, layout);
        Click(input, ActionableAlertLayout.Row(layout, 0)); controller.CancelInput(input);
        Assert.False(controller.Update(input, snapshot, layout).OpenOperations);
        Assert.Single(alerts.Alerts);
    }
    [Theory]
    [InlineData(1024, 720, 96, 1)]
    [InlineData(1600, 900, 144, 1)]
    [InlineData(1920, 1080, 192, 2)]
    [InlineData(3840, 2160, 192, 1)]
    public void NotificationRowsRespectSharedGeometryAndOverflow(int width, int height, uint dpi, float scale)
    {
        var layout = GameplayHudLayout.Create(width, height, dpi, scale);
        var experience = Experience(4, (PlayerAlertState)31);
        int count = (int)(layout.AlertStack.Height / (21 * layout.Scale));
        if (count == 0)
        {
            Assert.Equal(default, ActionableAlertLayout.Item(experience, layout, 0));
            Assert.True(layout.AlertStack.Height < 21 * layout.Scale);
            return;
        }
        Assert.Equal(PlayerAlertState.CommandCoreDestroyed, ActionableAlertLayout.Item(experience, layout, 0).Kind);
        if (count > 1) Assert.True(ActionableAlertLayout.Item(experience, layout, count - 1).Hidden > 0);
        else Assert.Equal(PlayerAlertState.CommandCoreDestroyed, ActionableAlertLayout.Item(experience, layout, 0).Kind);
        for (int i = 0; i < count; i++) Assert.True(layout.AlertStack.Contains(new Vector2(ActionableAlertLayout.Row(layout, i).Right, ActionableAlertLayout.Row(layout, i).Bottom)));
    }
    [Fact]
    public void WarmIdleAlertInputHasZeroAllocation()
    {
        var controller = new ActionableAlertController(); var input = new InputState(); var layout = GameplayHudLayout.Create(1600, 900, 96);
        var snapshot = Snapshot(new AlertLifecycleTracker().Capture(new(1), Experience(4, (PlayerAlertState)31), []));
        for (int i = 0; i < 128; i++) controller.Update(input, snapshot, layout);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) controller.Update(input, snapshot, layout);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    [Fact]
    public void RowGapsCapturePointerWithoutActivatingInvisibleControls()
    {
        var controller = new ActionableAlertController(); var input = new InputState(); var layout = GameplayHudLayout.Create(1600, 900, 96);
        var snapshot = Snapshot(new AlertLifecycleTracker().Capture(new(1), Experience(4, PlayerAlertState.LowPower), []));
        controller.Update(input, snapshot, layout);
        var gap = new HudRect(layout.AlertStack.X, layout.AlertStack.Y + 19 * layout.Scale, layout.AlertStack.Width, layout.Scale);
        Click(input, gap);
        var result = controller.Update(input, snapshot, layout);
        Assert.True(result.Captured); Assert.False(result.OpenOperations); Assert.False(result.Target.IsValid);
    }
    private static PlayerExperienceSnapshot Experience(ulong tick = 4, PlayerAlertState flags = default) =>
        default(PlayerExperienceSnapshot) with { Tick = new(tick), Player = new(1), Alerts = flags, MatchStatus = PlayerMatchStatus.Active };
    private static PresentationSnapshot Snapshot(ActionableAlertSnapshot alerts, ulong tick = 4, ulong session = 1, uint player = 1, bool terminal = false) =>
        new(new(tick), TimeSpan.FromSeconds(.05), 0, [], sessionId: new(session), playerExperience: Experience(tick,
            alerts.Alerts.Aggregate(PlayerAlertState.None, (flags, row) => flags | row.Identity.Kind)) with
        { Player = new(player), MatchStatus = terminal ? PlayerMatchStatus.Defeat : PlayerMatchStatus.Active }, alerts: alerts);
    private static void Click(InputState input, HudRect rect)
    {
        input.BeginFrame(); int x = (int)(rect.X + rect.Width / 2), y = (int)(rect.Y + rect.Height / 2);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, x, y));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, x, y));
    }
}
