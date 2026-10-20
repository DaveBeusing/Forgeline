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
    [InlineData(0, 0, 0, 0)]
    [InlineData(3, 0, 1, 1)]
    [InlineData(3, 1, 1, 2)]
    [InlineData(3, 1, 0, 1)]
    public void ExplicitEvidenceRejectsUnprovenOrAvailableFleet(
        int backlog, int active, int idle, int effective)
    {
        var tracker = new CargoFleetShortageTracker();
        var observation = new CargoFleetCapacityObservation(backlog, active, idle, effective, new(17, 2));
        for (ulong tick = 1; tick <= 199; tick++)
            Assert.Null(tracker.ObserveEvidence(new(1), new(1), new(tick),
                TimeSpan.FromMilliseconds(50), observation, false));
        Assert.Null(tracker.ObserveEvidence(new(1), new(1), new(200),
            TimeSpan.FromMilliseconds(50), observation, false));
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
