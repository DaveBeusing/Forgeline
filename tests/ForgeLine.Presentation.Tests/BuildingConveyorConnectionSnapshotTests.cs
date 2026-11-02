using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class BuildingConveyorConnectionSnapshotTests
{
    private static readonly PlayerId Owner = new(1);
    private static readonly EntityId Building = new(7, 1);

    [Fact]
    public void FixedPortsRequireExactComplementaryAdapterAttachment()
    {
        ConveyorBuildingPort input = InputPort();
        ConveyorBuildingPort output = OutputPort();
        var result = Capture([input, output], [Adapter(ConveyorModule.OutputAdapter, ConveyorLinkStatus.Flowing),
            Adapter(ConveyorModule.InputAdapter, ConveyorLinkStatus.Offline)]);

        Assert.Equal(1, result.ConnectedInputs);
        Assert.Equal(1, result.TotalInputs);
        Assert.Equal(1, result.ConnectedOutputs);
        Assert.Equal(1, result.TotalOutputs);
        Assert.Equal(ConveyorLinkStatus.Flowing, result.Ports[0].Transport);
        Assert.Equal(ConveyorLinkStatus.Offline, result.Ports[1].Transport);
        Assert.All(result.Ports, port => Assert.True(port.IsConnected));
    }

    [Fact]
    public void NearbySocketWithoutValidAttachmentIsDisconnected()
    {
        ConveyorOwnedPiece wrongGeneration = Adapter(ConveyorModule.OutputAdapter,
            ConveyorLinkStatus.Flowing, new EntityId(Building.Index, Building.Generation + 1));
        ConveyorOwnedPiece foreign = Adapter(ConveyorModule.OutputAdapter,
            ConveyorLinkStatus.Flowing, Building, new PlayerId(2));
        var result = Capture([InputPort()], [wrongGeneration, foreign]);

        Assert.Equal(0, result.ConnectedInputs);
        Assert.Equal(1, result.TotalInputs);
        Assert.Equal(BuildingConveyorAttachmentState.Free, result.Ports[0].Attachment);
        Assert.Null(result.Ports[0].Transport);
    }

    [Fact]
    public void WrongFacingDoesNotCountAsConnected()
    {
        var input = InputPort() with { Facing = Vector3.UnitZ };
        var result = Capture([input], [Adapter(ConveyorModule.OutputAdapter, ConveyorLinkStatus.Flowing)]);

        Assert.Equal(0, result.ConnectedInputs);
        Assert.Equal(BuildingConveyorAttachmentState.Invalid, result.Ports[0].Attachment);
        Assert.Null(result.Ports[0].Transport);
    }

    [Fact]
    public void DuplicatePhysicalAttachmentsAreBlocked()
    {
        var adapter = Adapter(ConveyorModule.OutputAdapter, ConveyorLinkStatus.Flowing);
        var result = Capture([InputPort()], [adapter, adapter with { Entity = new(22, 1) }]);

        Assert.Equal(BuildingConveyorAttachmentState.Blocked, result.Ports[0].Attachment);
        Assert.Equal(0, result.ConnectedInputs);
    }

    [Fact]
    public void FreeAdaptablePositionCountsOnceAndNeverAsBothDirections()
    {
        var input = InputPort() with { Adaptable = true, Key = "west.primary" };
        var output = input with { Input = false };
        var result = Capture([input, output], []);

        Assert.Single(result.Ports);
        Assert.Equal(BuildingConveyorPortRole.Available, result.Ports[0].Role);
        Assert.Equal(1, result.AvailablePorts);
        Assert.Equal(0, result.TotalInputs);
        Assert.Equal(0, result.TotalOutputs);
    }

    [Fact]
    public void CommittedAdaptableRoleIsNotDuplicated()
    {
        var input = InputPort() with
        {
            Adaptable = true,
            Key = "west.primary",
            Status = ConveyorPortState.ConnectedInput
        };
        var result = Capture([input], [Adapter(ConveyorModule.OutputAdapter, ConveyorLinkStatus.Congested)]);

        Assert.Single(result.Ports);
        Assert.Equal(BuildingConveyorPortRole.Input, result.Ports[0].Role);
        Assert.Equal(1, result.TotalInputs);
        Assert.Equal(1, result.ConnectedInputs);
        Assert.Equal(0, result.TotalOutputs);
        Assert.Equal(ConveyorLinkStatus.Congested, result.Ports[0].Transport);
    }

    [Fact]
    public void TransitionSnapshotsDoNotRetainOldFlowOrGeometry()
    {
        var adapter = Adapter(ConveyorModule.OutputAdapter, ConveyorLinkStatus.Damaged);
        var connected = Capture([InputPort()], [adapter]);
        var removed = Capture([InputPort()], []);
        var repaired = Capture([InputPort()], [adapter with { Status = ConveyorLinkStatus.Flowing }]);

        Assert.Equal(ConveyorLinkStatus.Damaged, connected.Ports[0].Transport);
        Assert.Equal(0, removed.ConnectedInputs);
        Assert.Null(removed.Ports[0].Transport);
        Assert.Equal(1, repaired.ConnectedInputs);
        Assert.Equal(ConveyorLinkStatus.Flowing, repaired.Ports[0].Transport);
        Assert.Equal(ConveyorLinkStatus.Damaged, connected.Ports[0].Transport);
    }

    [Fact]
    public void UnsupportedOrUnavailableBuildingHasNoInventedConnections()
    {
        var unsupported = Capture([], []);
        var unavailable = BuildingConveyorConnectionCapture.Capture(
            Building, Owner, [InputPort()], [Adapter(ConveyorModule.OutputAdapter, ConveyorLinkStatus.Flowing)],
            _ => "STEEL", unavailable: true);
        Assert.Empty(unsupported.Ports);
        Assert.False(unsupported.Unavailable);
        Assert.True(unavailable.Unavailable);
        Assert.Empty(unavailable.Ports);
    }

    [Fact]
    public void OtherOwnerPortsCannotEnterSelectedSnapshot()
    {
        var foreign = InputPort() with { Owner = new PlayerId(2) };
        var result = Capture([foreign], [Adapter(ConveyorModule.OutputAdapter, ConveyorLinkStatus.Flowing)]);
        Assert.Empty(result.Ports);
    }

    private static BuildingConveyorConnectionSnapshot Capture(
        IReadOnlyList<ConveyorBuildingPort> ports, IReadOnlyList<ConveyorOwnedPiece> pieces) =>
        BuildingConveyorConnectionCapture.Capture(Building, Owner, ports, pieces, _ => "STEEL");

    private static ConveyorBuildingPort InputPort() =>
        new(Building, Owner, new(0, 5.8f, 2), -Vector3.UnitZ, true, "west.primary");

    private static ConveyorBuildingPort OutputPort() =>
        new(Building, Owner, new(0, 5.8f, -2), Vector3.UnitZ, false, "east.primary");

    private static ConveyorOwnedPiece Adapter(ConveyorModule module, ConveyorLinkStatus status,
        EntityId attachment = default, PlayerId owner = default)
    {
        if (!attachment.IsValid) attachment = Building;
        if (!owner.IsSpecified) owner = Owner;
        return new(new(21, 1), new(owner,
            new ConveyorPlacement(module, Vector3.Zero, BuildingOrientation.North, 0),
            16, Attachment: attachment), status);
    }
}
