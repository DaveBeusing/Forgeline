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
        Click(input, ActionableAlertLayout.Resolve(snapshot, layout).Card(0).Action);
        var result = controller.Update(input, snapshot, layout);
        Assert.True(result.Captured); Assert.Equal(target, result.Target); Assert.False(result.OpenOperations);
        Assert.False(controller.Update(input, snapshot, layout).Target.IsValid);
        var missing = Snapshot(tracker.Capture(new(1), Experience(4, PlayerAlertState.LowPower), []));
        Click(input, ActionableAlertLayout.Resolve(snapshot, layout).Card(0).Action);
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
        Click(input, ActionableAlertLayout.Resolve(snapshot, layout).Card(0).Action);
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
        Click(input, ActionableAlertLayout.Resolve(snapshot, layout).Card(0).Action); controller.CancelInput(input);
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
        var stack = ActionableAlertLayout.Resolve(experience, layout);
        Assert.InRange(stack.Count, 1, 4);
        Assert.Equal(PlayerAlertState.CommandCoreDestroyed, stack.First.Item.Kind);
        Assert.True(stack.Card(stack.Count - 1).Item.Hidden > 0);
        for (int i = 0; i < stack.Count; i++)
        {
            Assert.True(stack.Bounds.Contains(new Vector2(stack.Card(i).Bounds.Right, stack.Card(i).Bounds.Bottom)));
            Assert.Equal(i, stack.Hit(new(stack.Card(i).Bounds.X + 1, stack.Card(i).Bounds.Y + 1)));
        }
    }
    [Fact]
    public void RepeatedFocusCyclesThroughCurrentAuthorizedTargetsAndSkipsUnavailable()
    {
        var tracker = new AlertLifecycleTracker();
        var kinds = PlayerAlertState.CommandCoreDamaged | PlayerAlertState.SupplyCritical | PlayerAlertState.LowPower;
        EntityId first = new(4, 1), second = new(5, 2), third = new(6, 3);
        var alerts = tracker.Capture(new(1), Experience(4, kinds), [default, first, second, default, third]);
        var snapshot = Snapshot(alerts);
        var hud = GameplayHudLayout.Create(1920, 1080, 96);
        var controller = new ActionableAlertController(); var input = new InputState();
        controller.Update(input, snapshot, hud);
        var action = ActionableAlertLayout.Resolve(snapshot, hud).Card(0).Action;
        foreach (EntityId expected in new[] { first, second, third, first })
        {
            Click(input, action);
            var result = controller.Update(input, snapshot, hud);
            Assert.Equal(expected, result.Target);
            Assert.False(result.OpenOperations);
            Assert.False(controller.Expansion.Open);
        }
        var changed = alerts.Alerts.Select(alert => alert.Identity.Kind == PlayerAlertState.SupplyCritical
            ? alert with { Target = default } : alert).ToArray();
        snapshot = Snapshot(new ActionableAlertSnapshot(new(1), new(4), new(1), changed));
        Click(input, action);
        Assert.Equal(first, controller.Update(input, snapshot, hud).Target);
        Click(input, action);
        Assert.Equal(third, controller.Update(input, snapshot, hud).Target);
    }

    [Fact]
    public void FocusCyclingIncludesCurrentTargetsBehindTheMoreOverflowCard()
    {
        var tracker = new AlertLifecycleTracker();
        var targets = new EntityId[]
        {
            new(11, 1), new(12, 1), new(13, 1), new(14, 1), new(15, 1)
        };
        var snapshot = Snapshot(tracker.Capture(new(1), Experience(4, (PlayerAlertState)31), targets));
        var layout = GameplayHudLayout.Create(1920, 1080, 96);
        var stack = ActionableAlertLayout.Resolve(snapshot, layout);
        Assert.Equal(4, stack.Count);
        Assert.True(stack.Overflow.Item.Hidden > 0);
        var controller = new ActionableAlertController(); var input = new InputState();
        controller.Update(input, snapshot, layout);
        foreach (var expected in targets)
        {
            Click(input, stack.First.Action);
            Assert.Equal(expected, controller.Update(input, snapshot, layout).Target);
        }
        Click(input, stack.First.Action);
        Assert.Equal(targets[0], controller.Update(input, snapshot, layout).Target);
    }

    [Fact]
    public void CardSurfaceTogglesSingleDetailWithoutActivatingFocusOrOperations()
    {
        var alerts = new AlertLifecycleTracker().Capture(new(1),
            Experience(4, PlayerAlertState.LowPower), [default, default, default, default, new EntityId(8, 2)]);
        var snapshot = Snapshot(alerts);
        var hud = GameplayHudLayout.Create(1920, 1080, 96);
        var controller = new ActionableAlertController(); var input = new InputState();
        controller.Update(input, snapshot, hud);
        var card = ActionableAlertLayout.Resolve(snapshot, hud).First;
        Click(input, new HudRect(card.Bounds.X + 8, card.Bounds.Y + 3, 10, 10));
        var opened = controller.Update(input, snapshot, hud);
        Assert.True(opened.Captured); Assert.False(opened.Target.IsValid); Assert.False(opened.OpenOperations);
        Assert.True(controller.Expansion.Open);
        controller.Update(input, snapshot, hud, seconds: .25);
        var expanded = ActionableAlertLayout.Resolve(snapshot, hud, expansion: controller.Expansion);
        Assert.False(expanded.First.ExpansionBounds.IsEmpty);
        Assert.True(expanded.First.InteractiveBounds.Contains(
            new Vector2(expanded.First.ExpansionBounds.X + 8, expanded.First.ExpansionBounds.Y + 8)));
        Click(input, card.Action);
        var focused = controller.Update(input, snapshot, hud);
        Assert.Equal(new EntityId(8, 2), focused.Target);
        Assert.True(controller.Expansion.Open);
        Click(input, new HudRect(card.Bounds.X + 8, card.Bounds.Y + 3, 10, 10));
        var closed = controller.Update(input, snapshot, hud);
        Assert.True(closed.Captured); Assert.False(closed.Target.IsValid);
        Assert.False(controller.Expansion.Open);
        var outgoing = ActionableAlertLayout.Resolve(snapshot, hud, expansion: controller.Expansion);
        Assert.False(outgoing.First.ExpansionBounds.IsEmpty);
        Assert.False(outgoing.Contains(new Vector2(outgoing.First.ExpansionBounds.X + 8,
            outgoing.First.ExpansionBounds.Y + 8)));
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
    public void RowGapsLeaveWorldInputAvailable()
    {
        var controller = new ActionableAlertController(); var input = new InputState(); var layout = GameplayHudLayout.Create(1600, 900, 96);
        var snapshot = Snapshot(new AlertLifecycleTracker().Capture(new(1), Experience(4, PlayerAlertState.LowPower), []));
        controller.Update(input, snapshot, layout);
        var card = ActionableAlertLayout.Resolve(snapshot, layout).Card(0).Bounds;
        var gap = new HudRect(card.X, card.Bottom + 1, card.Width, 1);
        Click(input, gap);
        var result = controller.Update(input, snapshot, layout);
        Assert.False(result.Captured); Assert.False(result.OpenOperations); Assert.False(result.Target.IsValid);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeldPressWaitsForReleaseAndRejectsResolvedOrRecurringIdentity(bool resolved)
    {
        var tracker = new AlertLifecycleTracker(); var controller = new ActionableAlertController(); var input = new InputState();
        var snapshot = Snapshot(tracker.Capture(new(1), Experience(4, PlayerAlertState.LowPower), []));
        var layout = GameplayHudLayout.Create(1600, 900, 96); var rect = ActionableAlertLayout.Resolve(snapshot, layout).Card(0).Action;
        controller.Update(input, snapshot, layout);
        int x = (int)(rect.X + rect.Width / 2), y = (int)(rect.Y + rect.Height / 2);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, x, y));
        Assert.False(controller.Update(input, snapshot, layout).OpenOperations);
        if (resolved)
        {
            tracker.Capture(new(1), Experience(5), []);
            snapshot = Snapshot(tracker.Capture(new(1), Experience(6, PlayerAlertState.LowPower), []), tick: 6);
        }
        input.BeginFrame(); input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, x, y));
        Assert.Equal(!resolved, controller.Update(input, snapshot, layout).OpenOperations);
        Assert.False(controller.Update(input, snapshot, layout).OpenOperations);
    }
    [Fact]
    public void EmptyRowsAndRightOfCardsNeverCaptureWorldInput()
    {
        var layout = GameplayHudLayout.Create(1920, 1080, 96);
        var experience = Experience(4, PlayerAlertState.LowPower);
        var rect = ActionableAlertLayout.Resolve(experience, layout).Card(0).Bounds;
        Assert.True(ActionableAlertLayout.Contains(experience, layout, new(rect.X + 10, rect.Y + 5)));
        Assert.False(ActionableAlertLayout.Contains(experience, layout, new(rect.Right + 1, rect.Y + 5)));
        Assert.False(ActionableAlertLayout.Contains(experience, layout, new(rect.X + 10, rect.Bottom + 5)));
        Assert.False(ActionableAlertLayout.Contains(Experience(), layout, new(rect.X + 10, rect.Y + 5)));
        var controller = new ActionableAlertController(); var operations = new OperationsController();
        var state = new PresentationInteractionState(); var input = new InputState();
        var snapshot = Snapshot(new AlertLifecycleTracker().Capture(new(1), experience, []));
        controller.Update(input, snapshot, layout); operations.Update(input, snapshot, layout, state);
        Click(input, new(rect.Right + 10, rect.Y, 10, 10));
        Assert.False(controller.Update(input, snapshot, layout).Captured);
        Assert.False(operations.Update(input, snapshot, layout, state).Captured);
        Assert.False(operations.View.Open);
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
