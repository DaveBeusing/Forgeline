using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class AuthoritativeCommandMarkerTests
{
    [Theory]
    [InlineData(PlayerCommandFeedbackState.Accepted, RtsCommandMarkerState.Accepted)]
    [InlineData(PlayerCommandFeedbackState.Partial, RtsCommandMarkerState.Partial)]
    [InlineData(PlayerCommandFeedbackState.Rejected, RtsCommandMarkerState.Rejected)]
    public void OnlyMatchingSessionAndCorrelationResolvePendingMarker(PlayerCommandFeedbackState outcome, RtsCommandMarkerState expected)
    {
        var marker = Pending();
        marker.ObserveSubmission(40, Receipt());
        marker.ObserveResult(Result(outcome) with { SessionId = new(2) });
        marker.ObserveResult(Result(outcome) with { CorrelationId = new(12) });
        marker.Advance(TimeSpan.FromSeconds(10));
        Assert.True(marker.IsVisible);
        Assert.Equal(RtsCommandMarkerState.Pending, marker.State);
        marker.ObserveResult(Result(outcome));
        Assert.Equal(expected, marker.View.State);
        Assert.Equal(expected != RtsCommandMarkerState.Rejected, marker.IsValid);
        marker.Advance(TimeSpan.FromSeconds(1));
        Assert.False(marker.IsVisible);
    }

    [Fact]
    public void ResultBeforeReceiptAndSupersededHostCompletionsAreHandled()
    {
        var marker = Pending();
        marker.ObserveResult(Result(PlayerCommandFeedbackState.Rejected));
        marker.ObserveSubmission(39, Receipt());
        Assert.Equal(RtsCommandMarkerState.Pending, marker.State);
        marker.ObserveSubmission(40, Receipt());
        Assert.Equal(RtsCommandMarkerState.Rejected, marker.State);
        marker.Show(Vector3.One, true);
        marker.TrackSubmission(41, new(1));
        marker.ObserveSubmission(40, Receipt());
        marker.ObserveResult(Result(PlayerCommandFeedbackState.Accepted));
        Assert.Equal(RtsCommandMarkerState.Pending, marker.State);
        marker.ObserveSubmission(41, Receipt() with { CorrelationId = new(12) });
        Assert.Equal(RtsCommandMarkerState.Pending, marker.State);
        marker.ObserveResult(Result(PlayerCommandFeedbackState.Accepted) with { CorrelationId = new(12) });
        Assert.Equal(RtsCommandMarkerState.Accepted, marker.State);
        marker.Clear();
        marker.ObserveResult(Result(PlayerCommandFeedbackState.Rejected));
        Assert.False(marker.IsVisible);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HostOrGatewayRejectionCannotLeaveAcceptedOrEndlessPendingMarker(bool receipt)
    {
        var marker = Pending();
        marker.ObserveSubmission(40, receipt ? Receipt() with { Accepted = false, Failure = PlayerCommandSubmissionFailure.BoundaryFull } : null);
        Assert.Equal(RtsCommandMarkerState.Rejected, marker.State);
        marker.Advance(TimeSpan.FromSeconds(1));
        Assert.False(marker.IsVisible);
    }

    private static RtsCommandFeedback Pending()
    {
        var marker = new RtsCommandFeedback();
        marker.Show(Vector3.One, true);
        Assert.Equal(RtsCommandMarkerState.Pending, marker.State);
        marker.TrackSubmission(40, new(1));
        return marker;
    }
    private static PlayerCommandSubmissionReceipt Receipt() => new(new(1), new(11), PlayerCommandKind.Movement,
        default, new(4), new(5), 1, true, PlayerCommandSubmissionFailure.None);
    private static PlayerCommandResultReadModel Result(PlayerCommandFeedbackState outcome) => new(new(1), new(11),
        PlayerCommandKind.Movement, outcome, outcome == PlayerCommandFeedbackState.Rejected ? 0 : 1,
        outcome == PlayerCommandFeedbackState.Accepted ? 0 : 1, default, default, new(5));
}
