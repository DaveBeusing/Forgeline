using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ConveyorSnapSelectorTests
{
    [Fact]
    public void EqualDistanceUsesFullIdentityRegardlessOfCandidateOrder()
    {
        var first = Endpoint(new EntityId(4, 2), "output", 0);
        var second = Endpoint(new EntityId(4, 3), "output", 0) with { Position = new Vector3(8, 0, 0) };
        var a = Candidate(second, 25);
        var b = Candidate(first, 25);
        Assert.True(ConveyorSnapSelector.TrySelect([a, b], null, 1, out var initial));
        Assert.True(ConveyorSnapSelector.TrySelect([b, a], null, 1, out var reordered));
        Assert.Equal(first, initial);
        Assert.Equal(initial, reordered);
    }

    [Fact]
    public void SocketOrdinalBreaksTiesWithoutSnapshotOrdering()
    {
        EntityId source = new(9, 1);
        var later = Endpoint(source, "", 2) with { Position = new Vector3(8, 0, 0) };
        var earlier = Endpoint(source, "", 1);
        Assert.True(ConveyorSnapSelector.TrySelect(
            [Candidate(later, 0), Candidate(earlier, 0)], null, 1, out var selected));
        Assert.Equal(earlier, selected);
    }

    [Fact]
    public void HysteresisRetainsOnlyLiveSocketAndSwitchesWhenClearlyCloser()
    {
        var previous = Endpoint(new EntityId(10, 1), "east.primary", 0);
        var other = Endpoint(new EntityId(11, 1), "west.primary", 0) with { Position = new Vector3(8, 0, 0) };
        Assert.True(ConveyorSnapSelector.TrySelect(
            [Candidate(other, 16), Candidate(previous, 25)], previous, 1, out var held));
        Assert.Equal(previous, held);
        Assert.True(ConveyorSnapSelector.TrySelect(
            [Candidate(other, 0), Candidate(previous, 100)], previous, 1, out var switched));
        Assert.Equal(other, switched);
        Assert.True(ConveyorSnapSelector.TrySelect(
            [Candidate(other, 9)], previous, 1, out var vanished));
        Assert.Equal(other, vanished);
    }

    [Fact]
    public void OverlappingPhysicalEndpointsAreAmbiguousAndFailClosed()
    {
        var a = Endpoint(new EntityId(7, 1), "", 0);
        var b = Endpoint(new EntityId(8, 1), "", 0);
        Assert.False(ConveyorSnapSelector.TrySelect(
            [Candidate(a, 0), Candidate(b, 0)], null, 1,
            out _, out bool ambiguous));
        Assert.True(ambiguous);
    }

    [Fact]
    public void StaleOrOutOfRangeSocketCannotRemainSelected()
    {
        var earlier = Endpoint(new EntityId(12, 1), "a", 0);
        var reused = Endpoint(new EntityId(12, 2), "a", 0) with { Position = new Vector3(8, 0, 0) };
        Assert.False(ConveyorSnapSelector.TrySelect(
            [Candidate(earlier, 35 * 35)], earlier, 1, out _));
        Assert.True(ConveyorSnapSelector.TrySelect(
            [Candidate(reused, 1)], earlier, 1, out var selected));
        Assert.Equal(reused, selected);
        Assert.False(ConveyorSnapSelector.TrySelect([], earlier, 1, out _));
    }

    [Theory]
    [InlineData(ConveyorModule.ShortSegment, 0)]
    [InlineData(ConveyorModule.Standard, 0)]
    [InlineData(ConveyorModule.CurveLeft, 0)]
    [InlineData(ConveyorModule.CurveRight, 0)]
    [InlineData(ConveyorModule.Merger, 0)]
    [InlineData(ConveyorModule.Splitter, 0)]
    [InlineData(ConveyorModule.RampUp, 0)]
    [InlineData(ConveyorModule.RampDown, 1)]
    [InlineData(ConveyorModule.InputAdapter, 0)]
    [InlineData(ConveyorModule.Endcap, 0)]
    public void ModuleAlignmentKeepsExactOppositeFacingSocket(ConveyorModule module, int mateTier)
    {
        var mate = new ConveyorEndpoint(new Vector3(32, mateTier * 2, 48),
            EntityId.Invalid, mateTier, Vector3.UnitZ, false,
            Source: new EntityId(20, 1));
        Assert.True(ConveyorSnapSelector.TryAlign(module, mate, out var placement));
        Assert.InRange(placement.Tier, 0, 1);
        Assert.Contains(placement.Sockets, socket =>
            socket.Input && Vector3.DistanceSquared(socket.Position,
                mate.Position + Vector3.UnitY * 5.8f) < .0004f &&
            Vector3.Dot(socket.Facing, mate.Facing) < -.999f);
    }

    [Fact]
    public void UnsupportedTierFailsAlignmentWithoutCreatingOffGridCoordinates()
    {
        var lowSocket = new ConveyorEndpoint(Vector3.Zero,
            EntityId.Invalid, 0, Vector3.UnitZ, false);
        Assert.False(ConveyorSnapSelector.TryAlign(ConveyorModule.RampDown,
            lowSocket, out _));
    }

    [Fact]
    public void GridDeadbandOnlyChangesPresentationCandidateNotSocketPosition()
    {
        Vector3 old = new(4, 1, 8);
        Assert.Equal(new Vector3(4, 3, 8),
            ConveyorSnapSelector.SnapGrid(new Vector3(6.2f, 3, 8), old));
        Assert.Equal(new Vector3(8, 3, 8),
            ConveyorSnapSelector.SnapGrid(new Vector3(6.5f, 3, 8), old));
        Assert.Equal(new Vector3(8, 3, 8),
            ConveyorSnapSelector.SnapGrid(new Vector3(6.2f, 3, 8), null));
    }

    private static ConveyorEndpoint Endpoint(EntityId id, string key, int ordinal) =>
        new(Vector3.Zero, id, 0, Vector3.UnitX, false,
            Source: id, SocketKey: key, SocketOrdinal: ordinal);

    private static ConveyorSnapSelector.Candidate Candidate(ConveyorEndpoint endpoint, float distance) =>
        new(endpoint, endpoint.Source, endpoint.SocketKey, endpoint.SocketOrdinal, distance, false);
}
