using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class CargoFleetShortageTests
{
    private static readonly CargoFleetCapacityObservation Busy =
        new(3, 1, 0, 1, new EntityId(17, 2));

    [Theory]
    [InlineData(20)]
    [InlineData(10)]
    public void TenSecondsOfUnservedBusyFleetAreRequired(int ticksPerSecond)
    {
        var tracker = new CargoFleetShortageTracker();
        TimeSpan duration = TimeSpan.FromSeconds(1d / ticksPerSecond);
        int threshold = ticksPerSecond * 10;
        for (ulong i = 1; i < (ulong)threshold; i++)
            Assert.Null(tracker.ObserveEvidence(new(1), new(1), new(i), duration, Busy, true));
        var active = tracker.ObserveEvidence(new(1), new(1), new((ulong)threshold), duration, Busy, true);
        Assert.Equal(Busy, active);
        Assert.Equal(Busy, tracker.ObserveEvidence(new(1), new(1), new((ulong)threshold), duration, Busy, true));
    }

    [Theory]
    [InlineData(0, 1, 0, 0, false)] // Missing actual backlog
    [InlineData(3, 0, 0, 0, false)] // No operational fleet
    [InlineData(3, 1, 1, 0, false)] // One dispatchable idle truck
    [InlineData(3, 1, 0, 1, false)] // Fuel, route or FIFO blocked
    [InlineData(3, 1, 0, 0, true)]  // Active fleet entirely committed
    public void CausalEvidenceDistinguishesBusyCapacityFromOtherBottlenecks(
        int backlog, int active, int idle, int blocked, bool expected)
    {
        var tracker = new CargoFleetShortageTracker();
        bool eligible = CargoFleetShortageTracker.Qualifies(backlog, active, idle, blocked);
        Assert.Equal(expected, eligible);
        var observation = new CargoFleetCapacityObservation(backlog, active, idle,
            active + idle, new(17, 2));
        for (ulong tick = 1; tick < 200; tick++)
            Assert.Null(tracker.ObserveEvidence(new(1), new(1), new(tick),
                TimeSpan.FromMilliseconds(50), observation, eligible));
        var actual = tracker.ObserveEvidence(new(1), new(1), new(200),
            TimeSpan.FromMilliseconds(50), observation, eligible);
        if (expected) Assert.Equal(observation, actual);
        else Assert.Null(actual);
    }

    [Fact]
    public void BriefFifoOrFuelInterruptionCannotOpenAlarm()
    {
        var tracker = new CargoFleetShortageTracker();
        for (ulong tick = 1; tick <= 900; tick++)
        {
            bool fullyProductive = tick % 180 > 0 && tick % 180 < 125;
            Assert.Null(tracker.ObserveEvidence(new(1), new(1), new(tick),
                TimeSpan.FromMilliseconds(50), Busy, fullyProductive));
        }
    }

    [Fact]
    public void BacklogMustNotShrinkAcrossTheObservationWindow()
    {
        var tracker = new CargoFleetShortageTracker();
        for (ulong tick = 1; tick <= 200; tick++)
        {
            var sample = Busy with { BacklogRequests = tick < 180 ? 3 : 2 };
            Assert.Null(tracker.ObserveEvidence(new(1), new(1), new(tick),
                TimeSpan.FromMilliseconds(50), sample, true));
        }
    }

    [Fact]
    public void NormalizationReleasesAfterHysteresisAndRecurrenceReceivesNewIdentity()
    {
        var tracker = new CargoFleetShortageTracker();
        var lifecycle = new AlertLifecycleTracker();
        var experience = new PlayerExperienceSnapshot { Player = new(1) };
        AlertIdentity first = default;
        AlertIdentity second = default;
        for (ulong tick = 1; tick <= 200; tick++)
        {
            var sample = tracker.ObserveEvidence(new(1), new(1), new(tick),
                TimeSpan.FromMilliseconds(50), Busy, true);
            if (sample is { } fleet)
            {
                var alerts = lifecycle.Capture(new(1), experience with
                {
                    Tick = new(tick), Alerts = PlayerAlertState.CargoFleetShortage
                }, [], fleet);
                first = Assert.Single(alerts.Alerts).Identity;
            }
        }
        Assert.NotEqual(default, first);
        for (ulong tick = 201; tick <= 260; tick++)
        {
            var current = tracker.ObserveEvidence(new(1), new(1), new(tick),
                TimeSpan.FromMilliseconds(50), default, false);
            if (current is null)
                Assert.Empty(lifecycle.Capture(new(1), experience with { Tick = new(tick) }, []).Alerts);
        }
        for (ulong tick = 261; tick <= 500; tick++)
        {
            var current = tracker.ObserveEvidence(new(1), new(1), new(tick),
                TimeSpan.FromMilliseconds(50), Busy, true);
            if (current is not { } fleet) continue;
            second = Assert.Single(lifecycle.Capture(new(1), experience with
            {
                Tick = new(tick), Alerts = PlayerAlertState.CargoFleetShortage
            }, [], fleet).Alerts).Identity;
        }
        Assert.NotEqual(default, second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SessionChangeAndRollbackResetPendingFleetWarning()
    {
        var tracker = new CargoFleetShortageTracker();
        for (ulong tick = 1; tick <= 199; tick++)
            Assert.Null(tracker.ObserveEvidence(new(1), new(1), new(tick),
                TimeSpan.FromMilliseconds(50), Busy, true));
        Assert.Null(tracker.ObserveEvidence(new(2), new(1), new(200),
            TimeSpan.FromMilliseconds(50), Busy, true));
        Assert.Null(tracker.ObserveEvidence(new(2), new(1), new(25),
            TimeSpan.FromMilliseconds(50), Busy, true));
    }

    [Fact]
    public void FleetWarningIsReachableThroughExistingOverflowAndOwnedFocus()
    {
        var player = default(PlayerExperienceSnapshot) with
        {
            Player = new(1), Tick = new(210),
            MatchStatus = PlayerMatchStatus.Active,
            Alerts = (PlayerAlertState)63
        };
        var target = new EntityId(17, 2);
        var targets = new EntityId[6] { default, default, default, default, default, target };
        var alerts = new AlertLifecycleTracker().Capture(new(1), player, targets, Busy);
        Assert.Equal(6, alerts.Alerts.Count);
        var snapshot = new PresentationSnapshot(new SimulationTick(210),
            TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(1),
            playerExperience: player, alerts: alerts);
        var layout = GameplayHudLayout.Create(1920, 1080, 96);
        var stack = ActionableAlertLayout.Resolve(snapshot, layout);
        Assert.True(stack.Count > 0);
        Assert.True(stack.Card(stack.Count - 1).Item.Hidden > 0);
        Assert.Contains(Enumerable.Range(0, ResourcePowerHudModel.ResolveNotificationCount(player)),
            i => ActionableAlertLayout.Ordered(player, i).Kind == PlayerAlertState.CargoFleetShortage);
        Assert.Contains(alerts.Alerts, a => a.Identity.Kind == PlayerAlertState.CargoFleetShortage &&
            a.Target == target && a.Fleet == Busy);
        Assert.Null(ActionableAlertSnapshot.Resolve(new PresentationSnapshot(new SimulationTick(210),
            TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(2),
            playerExperience: player, alerts: alerts)));
    }

    [Fact]
    public void TypedAlertKeepsAuthorizedFacilityAndExactFleetFacts()
    {
        var fleet = Busy;
        var experience = new PlayerExperienceSnapshot
        {
            Player = new(1), Tick = new(200), Alerts = PlayerAlertState.CargoFleetShortage
        };
        var targets = new EntityId[6];
        targets[5] = fleet.AffectedFacility;
        var alerts = new AlertLifecycleTracker().Capture(new(5), experience, targets, fleet);
        var alert = Assert.Single(alerts.Alerts);
        Assert.Equal(PlayerAlertState.CargoFleetShortage, alert.Identity.Kind);
        Assert.Equal(fleet.AffectedFacility, alert.Target);
        Assert.Equal(fleet, alert.Fleet);
        Assert.Equal("INSUFFICIENT CARGO TRANSPORT CAPACITY", alert.Label);
        Assert.Equal(OperationsCategory.Logistics, alert.Operations);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
    }
}
