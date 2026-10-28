using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OperationalAlertLifecycleTests
{
    [Fact]
    public void BriefRecoveryKeepsIdentityButConfirmedResolutionAndCooldownRearm()
    {
        var tracker = new AlertLifecycleTracker();
        var first = tracker.Capture(new(1), Experience(4, PlayerAlertState.LowPower), []);
        Assert.Empty(tracker.Capture(new(1), Experience(5), []).Changes);
        var recurring = tracker.Capture(new(1), Experience(6, PlayerAlertState.LowPower), []);
        Assert.Equal(first.Alerts[0].Identity, recurring.Alerts[0].Identity);
        Assert.Empty(recurring.Changes);
        var clear = tracker.Capture(new(1), Experience(66), []);
        Assert.Empty(clear.Alerts);
        Assert.Equal(AlertChangeKind.Resolved, Assert.Single(clear.Changes).Kind);
        var later = tracker.Capture(new(1), Experience(166, PlayerAlertState.LowPower), []);
        Assert.NotEqual(first.Alerts[0].Identity, later.Alerts[0].Identity);
    }

    [Fact]
    public void ChangedCauseAndNewCriticalEventBypassWarningCooldown()
    {
        var tracker = new AlertLifecycleTracker();
        var first = tracker.Capture(new(1), Experience(4, PlayerAlertState.LowPower), [],
            observations: [new(PlayerAlertState.LowPower, 1, "BROWNOUT")]);
        var changed = tracker.Capture(new(1), Experience(5, PlayerAlertState.LowPower), [],
            observations: [new(PlayerAlertState.LowPower, 2, "OFFLINE")]);
        Assert.NotEqual(first.Alerts[0].Identity, changed.Alerts[0].Identity);
        Assert.Equal(AlertChangeKind.CauseChanged, Assert.Single(changed.Changes).Kind);
        var critical = tracker.Capture(new(1), Experience(6, PlayerAlertState.CommandCoreUnderAttack), []);
        tracker.Capture(new(1), Experience(7), []);
        var hitAgain = tracker.Capture(new(1), Experience(8, PlayerAlertState.CommandCoreUnderAttack), []);
        Assert.NotEqual(critical.Alerts[0].Identity, hitAgain.Alerts[0].Identity);
    }

    [Fact]
    public void CoreDamageAdapterUsesOnlyCurrentActualDamageToTheOwnedCore()
    {
        EntityId core = new(10, 1), hiddenSource = new(999, 7);
        var tracker = new AlertLifecycleTracker();
        var hit = new CombatEvent(new(4), CombatEventType.DamageApplied, hiddenSource, core,
            default, default, new Vector3(999999), 1);
        var active = tracker.AdaptEvents(new(1), Experience(4), core, [hit], 20);
        Assert.True(active.Alerts.HasFlag(PlayerAlertState.CommandCoreUnderAttack));
        Assert.True(tracker.AdaptEvents(new(1), Experience(103), core, [], 20).Alerts.HasFlag(PlayerAlertState.CommandCoreUnderAttack));
        Assert.False(tracker.AdaptEvents(new(1), Experience(104), core, [], 20).Alerts.HasFlag(PlayerAlertState.CommandCoreUnderAttack));
        foreach (var invalid in new[] { hit with { Tick = new(3) }, hit with { Target = hiddenSource },
            hit with { Amount = 0 }, hit with { Amount = double.NaN }, hit with { Type = CombatEventType.ShotFired } })
            Assert.False(new AlertLifecycleTracker().AdaptEvents(new(1), Experience(4), core, [invalid], 20)
                .Alerts.HasFlag(PlayerAlertState.CommandCoreUnderAttack));
    }

    [Fact]
    public void BatchedCompletionsUseObservedCountersWithoutDelayingCriticalEvents()
    {
        var tracker = new AlertLifecycleTracker();
        var preferences = new NotificationPreferences { Mode = NotificationMode.Batched };
        var baseline = Experience(4) with { Statistics = new(4, .2, 10, 1, 0) };
        tracker.AdaptEvents(new(1), baseline, default, [], 20, preferences);
        var current = baseline with { Tick = new(5), Statistics = baseline.Statistics with { UnitsProduced = 11 } };
        var damage = new CombatEvent(new(5), CombatEventType.DamageApplied, new(99, 1), new(10, 1), default, default, Vector3.Zero, 1);
        var batch = tracker.AdaptEvents(new(1), current, new(10, 1), [damage], 20, preferences);
        Assert.False(batch.Alerts.HasFlag(PlayerAlertState.WorkCompleted));
        Assert.True(batch.Alerts.HasFlag(PlayerAlertState.CommandCoreUnderAttack));
        Assert.False(tracker.AdaptEvents(new(1), current with { Tick = new(104) }, default, [], 20, preferences).Alerts.HasFlag(PlayerAlertState.WorkCompleted));
        Assert.True(tracker.AdaptEvents(new(1), current with { Tick = new(105) }, default, [], 20, preferences).Alerts.HasFlag(PlayerAlertState.WorkCompleted));
        Assert.False(tracker.AdaptEvents(new(2), current with { Tick = new(1) }, default, [], 20, preferences).Alerts.HasFlag(PlayerAlertState.WorkCompleted));
    }

    [Fact]
    public void QuietModeKeepsCriticalRowsAndRawDiagnostics()
    {
        var experience = Experience(4, (PlayerAlertState)255);
        var tracker = new AlertLifecycleTracker();
        var alerts = tracker.Capture(new(1), experience, [], preferences: new() { Mode = NotificationMode.Quiet, ShowInformational = false });
        Assert.Equal(8, alerts.Alerts.Count);
        var stack = ActionableAlertLayout.Resolve(experience, GameplayHudLayout.Create(1920, 1080, 96), alerts: alerts);
        Assert.Equal(PlayerAlertState.CommandCoreDestroyed, stack.Card(0).Item.Kind);
        Assert.Equal(PlayerAlertState.CommandCoreUnderAttack, stack.Card(1).Item.Kind);
        for (int i = 0; i < stack.Count; i++)
            Assert.NotEqual(PlayerAlertState.WorkCompleted, stack.Card(i).Item.Kind);
    }

    [Fact]
    public void DenseConcurrentConditionsKeepStorageBoundedAcrossCauseChanges()
    {
        var tracker = new AlertLifecycleTracker();
        for (ulong tick = 1; tick <= 5000; tick++)
        {
            var alerts = tracker.Capture(new(1), Experience(tick, (PlayerAlertState)255), [],
                observations: [new(PlayerAlertState.ProductionBlocked, tick / 100, "CURRENT OWNED CAUSE")]);
            Assert.Equal(8, alerts.Alerts.Count);
            Assert.InRange(alerts.Changes.Count, 0, 8);
        }
    }

    [Fact]
    public void RemappedNextAlertConsumesShortTapOnceAndCyclesBehindOverflow()
    {
        var registry = new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.NextAlert, PlatformKey.N));
        var experience = Experience(4, PlayerAlertState.CommandCoreUnderAttack | PlayerAlertState.CommandCoreDamaged | PlayerAlertState.SupplyCritical | PlayerAlertState.ProductionBlocked | PlayerAlertState.LowPower);
        var alerts = new AlertLifecycleTracker().Capture(new(1), experience, [default, new(10, 1), new(11, 1), new(12, 1), new(20, 1), default, new(30, 1)]);
        var snapshot = new PresentationSnapshot(new(4), TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(1), playerExperience: experience, alerts: alerts);
        var input = new InputState(); var controller = new ActionableAlertController(); var layout = GameplayHudLayout.Create(1600, 900, 96);
        controller.Update(input, snapshot, layout, bindings: registry);
        Assert.True(ActionableAlertLayout.Resolve(snapshot, layout).Card(3).Item.Hidden > 0);
        Tap(); Assert.Equal(new EntityId(30, 1), controller.Update(input, snapshot, layout, bindings: registry).Target);
        Assert.False(controller.Update(input, snapshot, layout, bindings: registry).Target.IsValid);
        Tap(); Assert.Equal(new EntityId(10, 1), controller.Update(input, snapshot, layout, bindings: registry).Target);
        Tap(); Assert.Equal(new EntityId(11, 1), controller.Update(input, snapshot, layout, bindings: registry).Target);
        Tap(); Assert.Equal(new EntityId(12, 1), controller.Update(input, snapshot, layout, bindings: registry).Target);
        Tap(); controller.Update(input, snapshot, layout, blocked: true, bindings: registry);
        Assert.False(controller.Update(input, snapshot, layout, bindings: registry).Target.IsValid);
        void Tap() { input.BeginFrame(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.N)); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.N)); }
    }
    [Theory]
    [InlineData(PlatformKey.LeftShift)]
    [InlineData(PlatformKey.LeftControl)]
    public void ReleasedModifierChordDoesNotNavigate(PlatformKey modifier)
    {
        var registry = new GameplayBindingRegistry(new GameplayBindings().With(GameplayAction.NextAlert, PlatformKey.N));
        var experience = Experience(4, PlayerAlertState.CommandCoreDamaged);
        var alerts = new AlertLifecycleTracker().Capture(new(1), experience, [default, new(10, 1)]);
        var snapshot = new PresentationSnapshot(new(4), TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(1), playerExperience: experience, alerts: alerts);
        var input = new InputState(); var controller = new ActionableAlertController(); var layout = GameplayHudLayout.Create(1600, 900, 96);
        controller.Update(input, snapshot, layout, bindings: registry);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, modifier));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.N));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.N));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, modifier));
        Assert.False(controller.Update(input, snapshot, layout, bindings: registry).Target.IsValid);
    }

    private static PlayerExperienceSnapshot Experience(ulong tick, PlayerAlertState alerts = default) =>
        default(PlayerExperienceSnapshot) with { Tick = new(tick), Player = new(1), Alerts = alerts, MatchStatus = PlayerMatchStatus.Active };
}
