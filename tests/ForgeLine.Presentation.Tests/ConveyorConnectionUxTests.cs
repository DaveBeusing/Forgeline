using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ConveyorConnectionUxTests
{
    private const int Width = 1600, Height = 900;
    private static readonly PlayerId Player = new(1);
    private static readonly bool[] PortRoles = [true, false];

    [Fact]
    public void TwoBuildingClicksSubmitOneAutomaticRouteWithItsConfirmedPreview()
    {
        var camera = SameTypeSelectionTests.Camera();
        var source = Building(1, new(-25, 0, 0));
        var destination = Building(2, new(25, 0, 0));
        var ports = Ports(source).Concat(Ports(destination)).ToArray();
        var snapshot = Snapshot([source, destination], ports);
        var controller = new RtsConveyorConstructionController();
        var input = new InputState();
        var interaction = new PresentationInteractionState();
        controller.Begin(ConveyorModule.Route);

        Update(source.Transform.Position, true, snapshot);
        Assert.False(controller.TryTakeRequest(out _));
        Update(destination.Transform.Position, false, snapshot);
        var preview = interaction.CaptureConveyorRequest() ?? throw new InvalidOperationException("Missing route preview.");
        Assert.Equal(source.Entity, preview.Request.SourcePortBuilding);
        Assert.Equal(destination.Entity, preview.Request.DestinationPortBuilding);
        Assert.True(preview.Request.AutomaticRouting);
        var confirmed = Snapshot([source, destination], ports, requestId: preview.Id,
            preview: new(ConveyorFailure.None,
                [new(ConveyorModule.Standard, Vector3.Zero, BuildingOrientation.East, 0)], 14));

        Update(destination.Transform.Position, true, confirmed);
        Assert.True(controller.TryTakeRequest(out var request));
        Assert.Equal(preview.Request, request);
        Assert.Equal(Ports(source).Single(p => !p.Input).Position - Vector3.UnitY * 5.8f, request.Start);
        Assert.Equal(Ports(destination).Single(p => p.Input).Position - Vector3.UnitY * 5.8f, request.End);
        Assert.False(controller.TryTakeRequest(out _));

        void Update(Vector3 position, bool pressed, PresentationSnapshot current)
        {
            Vector2 point = camera.WorldToScreen(position, Width, Height).Position;
            int x = (int)MathF.Round(point.X), y = (int)MathF.Round(point.Y);
            input.BeginFrame();
            input.Apply(PlatformInputEvent.PointerMoved(x, y));
            input.Apply(PlatformInputEvent.MouseButtonChanged(pressed ? PlatformInputEventKind.MouseButtonDown :
                PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, x, y));
            controller.Update(input, camera, new SameTypeSelectionTests.FlatTerrain(), current, interaction,
                Player, Width, Height, false, 1);
        }
    }

    [Theory]
    [InlineData(BuildingOrientation.North)]
    [InlineData(BuildingOrientation.East)]
    [InlineData(BuildingOrientation.South)]
    [InlineData(BuildingOrientation.West)]
    public void BuildingBodyClickChoosesTheRequestedRoleAtTheActualRotatedPort(BuildingOrientation orientation)
    {
        var camera = SameTypeSelectionTests.Camera();
        var building = Building(1, Vector3.Zero, orientation);
        var ports = Ports(building);
        var snapshot = Snapshot([building], ports);
        var picker = new ConveyorEndpointPicker();
        Vector2 pointer = camera.WorldToScreen(building.Transform.Position, Width, Height).Position;
        foreach (bool input in new[] { false, true })
        {
            var port = ports.Single(p => p.Input == input);
            // The click is on the body, beyond the small endpoint snap radius.
            Assert.True(Vector2.Distance(pointer, camera.WorldToScreen(port.Position, Width, Height).Position) > 16);
            Assert.True(picker.TryPick(snapshot, Player, camera, pointer, Width, Height, 1, input, out var endpoint));
            Assert.Equal(building.Entity, endpoint.Building);
            Assert.Equal(port.Position - Vector3.UnitY * 5.8f, endpoint.Position);
            Assert.Equal(0, endpoint.Tier);
        }
    }

    [Fact]
    public void CloserExistingAdapterSocketWinsWithoutKeepingTheNearbyBuildingIdentity()
    {
        var camera = new RtsCamera(new RtsCameraSettings { EdgeScrollEnabled = false, InitialDistance = 240 });
        var building = Building(1, new(-20, 0, 0));
        var ports = Ports(building);
        var adapter = new ConveyorPlacement(ConveyorModule.InputAdapter, new(-8, 0, 0), BuildingOrientation.East, 0);
        var piece = new ConveyorOwnedPiece(new(2, 1), new(Player, adapter, 16, building.Entity));
        var snapshot = Snapshot([building, Piece(piece)], ports, [piece]);
        var picker = new ConveyorEndpointPicker();
        Vector2 body = camera.WorldToScreen(building.Transform.Position, Width, Height).Position;
        Assert.True(picker.TryPick(snapshot, Player, camera, body, Width, Height, 1, false, out var initial));
        Assert.Equal(building.Entity, initial.Building);

        var socket = adapter.Sockets.Single(s => !s.Input);
        Vector2 pointer = camera.WorldToScreen(socket.Position, Width, Height).Position;
        Vector2 nearby = camera.WorldToScreen(ports.Single(p => !p.Input).Position, Width, Height).Position;
        Assert.InRange(Vector2.Distance(pointer, nearby), .01f, 16);
        Assert.True(picker.TryPick(snapshot, Player, camera, pointer, Width, Height, 1, false, out var endpoint));
        Assert.False(endpoint.Building.IsValid);
        Assert.Equal(socket.Position - Vector3.UnitY * 5.8f, endpoint.Position);
    }

    [Fact]
    public void StaleForeignHiddenAndReusedEntityDataCannotBePickedOrMarked()
    {
        var camera = SameTypeSelectionTests.Camera();
        var building = Building(1, Vector3.Zero);
        var ports = Ports(building);
        var foreign = new PlayerId(2);
        PresentationSnapshot[] invalid =
        [
            Snapshot([building], ports, networkTick: 3),
            Snapshot([building], ports, experienceTick: 3),
            Snapshot([building], ports, session: 0),
            Snapshot([building], ports, experiencePlayer: foreign),
            Snapshot([building], ports.Select(p => p with { Owner = foreign }).ToArray()),
            Snapshot([building with { Selectable = new(foreign, ControllableEntityCategory.Building) }], ports),
            Snapshot([building with { Visibility = RenderVisibilityMask.None }], ports),
            Snapshot([building with { Entity = new(building.Entity.Index, 2) }], ports)
        ];
        var picker = new ConveyorEndpointPicker();
        Vector2 pointer = camera.WorldToScreen(building.Transform.Position, Width, Height).Position;
        foreach (var snapshot in invalid)
        {
            Assert.False(picker.TryPick(snapshot, Player, camera, pointer, Width, Height, 1, false, out _));
            var draw = new DebugDraw { Enabled = true };
            ConveyorNetworkOverlay.Draw(draw, snapshot, [], default, true, camera.GetMatrices(Width, Height).ViewProjection);
            Assert.Empty(draw.Labels);
            Assert.Empty(draw.Lines.ToArray());
        }
    }

    [Theory]
    [InlineData(ConveyorModule.RampUp, true, 0)]
    [InlineData(ConveyorModule.RampUp, false, 1)]
    [InlineData(ConveyorModule.RampDown, true, 1)]
    [InlineData(ConveyorModule.RampDown, false, 0)]
    public void RampEndpointRetainsItsActualSocketHeightAndTier(ConveyorModule module, bool input, int expectedTier)
    {
        var camera = SameTypeSelectionTests.Camera();
        var placement = new ConveyorPlacement(module, Vector3.Zero, BuildingOrientation.North, 0);
        var piece = new ConveyorOwnedPiece(new(3, 1), new(Player, placement, 16));
        var socket = placement.Sockets.Single(s => s.Input == input);
        var snapshot = Snapshot([Piece(piece)], [], [piece]);
        Vector2 pointer = camera.WorldToScreen(socket.Position, Width, Height).Position;
        Assert.True(new ConveyorEndpointPicker().TryPick(snapshot, Player, camera, pointer,
            Width, Height, 1, input, out var endpoint));
        Assert.Equal(expectedTier, endpoint.Tier);
        Assert.Equal(socket.Position - Vector3.UnitY * 5.8f, endpoint.Position);
        Assert.False(endpoint.Building.IsValid);
    }

    [Theory]
    [InlineData(BuildingOrientation.North)]
    [InlineData(BuildingOrientation.East)]
    [InlineData(BuildingOrientation.South)]
    [InlineData(BuildingOrientation.West)]
    public void MarkersLabelAndPointThroughTheActualRotatedInputAndOutput(BuildingOrientation orientation)
    {
        var camera = SameTypeSelectionTests.Camera();
        var building = Building(1, Vector3.Zero, orientation);
        var ports = Ports(building);
        var draw = new DebugDraw { Enabled = true };
        ConveyorNetworkOverlay.Draw(draw, Snapshot([building], ports), [], default, true,
            camera.GetMatrices(Width, Height).ViewProjection);
        Assert.Equal(2, draw.Labels.Count);
        foreach (var port in ports)
        {
            var label = Assert.Single(draw.Labels, l => l.Text == (port.Input ? "IN" : "OUT"));
            Assert.InRange(Vector3.Distance(label.Position, port.Position), 0, 2);
            var stem = Assert.Single(draw.Lines.ToArray(), l => Vector3.DistanceSquared(l.Start, l.End) > 4 &&
                MathF.Min(Vector3.DistanceSquared(l.Start, port.Position), Vector3.DistanceSquared(l.End, port.Position)) < 1);
            Vector3 flowDirection = port.Input ? -port.Facing : port.Facing;
            Assert.True(Vector3.Dot(Vector3.Normalize(stem.End - stem.Start), flowDirection) > .99f);
        }
    }

    [Fact]
    public void OffscreenPortsDoNotConsumeTheBudgetAndFocusedVisibleBuildingStaysFirst()
    {
        var camera = SameTypeSelectionTests.Camera();
        var instances = new List<RenderInstance>();
        var ports = new List<ConveyorBuildingPort>();
        for (int i = 0; i < 150; i++)
        {
            Vector3 position = i < 70 ? new(10_000 + i * 20, 0, 0) :
                new(-40 + (i - 70) % 10 * 8, 0, -24 + (i - 70) / 10 * 6);
            var building = Building((uint)i + 1, position - Vector3.UnitX * 10);
            instances.Add(building);
            ports.Add(Ports(building).Single(p => !p.Input));
        }
        var focus = ports[^1];
        EntityId[] selection = [focus.Building];
        var snapshot = Snapshot(instances.ToArray(), ports.ToArray(), selected: selection);
        var draw = new DebugDraw { Enabled = true };
        ConveyorNetworkOverlay.Draw(draw, snapshot, selection, default, true,
            camera.GetMatrices(Width, Height).ViewProjection);
        Assert.Equal(ConveyorNetworkOverlay.MaximumBuildingPorts, draw.Labels.Count);
        Assert.InRange(Vector3.Distance(draw.Labels[0].Position, focus.Position), 0, 2);
        Assert.All(draw.Labels, label => Assert.True(camera.WorldToScreen(label.Position, Width, Height).IsVisible));
    }

    [Fact]
    public void OutsideConstructionOnlyCurrentSelectedBuildingsShowTheirPorts()
    {
        var first = Building(1, new(-20, 0, 0));
        var second = Building(2, new(20, 0, 0));
        var ports = Ports(first).Concat(Ports(second)).ToArray();
        var snapshot = Snapshot([first, second], ports, selected: [second.Entity]);
        var draw = new DebugDraw { Enabled = true };
        ConveyorNetworkOverlay.Draw(draw, snapshot, [], default);
        Assert.Empty(draw.Labels);
        ConveyorNetworkOverlay.Draw(draw, snapshot, [first.Entity], default);
        Assert.Empty(draw.Labels);
        ConveyorNetworkOverlay.Draw(draw, snapshot, [second.Entity], default);
        Assert.Equal(2, draw.Labels.Count);
        Assert.All(draw.Labels, label => Assert.True(label.Position.X > 0));
    }

    private static RenderInstance Building(uint id, Vector3 ground, BuildingOrientation orientation = BuildingOrientation.North) =>
        new(new(id, 1), new(ground + Vector3.UnitY * 6, BuildingFootprint.GetRotation(orientation), new(20, 12, 20)),
            new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
            Selectable: new(Player, ControllableEntityCategory.Building));

    private static ConveyorBuildingPort[] Ports(RenderInstance building) => PortRoles.Select(input =>
    {
        float sign = input ? -1 : 1;
        Vector3 origin = building.Transform.Position - Vector3.UnitY * 6;
        return new ConveyorBuildingPort(building.Entity, Player,
            origin + Vector3.Transform(new(sign * 10, 5.8f, 0), building.Transform.Rotation),
            Vector3.Transform(Vector3.UnitX * sign, building.Transform.Rotation), input);
    }).ToArray();

    private static RenderInstance Piece(ConveyorOwnedPiece row) =>
        new(row.Entity, new(row.Piece.Placement.Pivot, row.Piece.Placement.Rotation, row.Piece.Placement.LocalScale),
            new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
            Selectable: new(Player, ControllableEntityCategory.Building),
            ConveyorFeature: new(row.Piece.Placement, "idle_no_input"));

    private static PresentationSnapshot Snapshot(RenderInstance[] instances, ConveyorBuildingPort[] ports,
        ConveyorOwnedPiece[]? pieces = null, ulong requestId = 0, ConveyorPlan? preview = null,
        ulong networkTick = 4, ulong experienceTick = 4, PlayerId? experiencePlayer = null,
        ulong session = 1, EntityId[]? selected = null) =>
        new(new SimulationTick(4), TimeSpan.FromMilliseconds(50), instances.Length, instances,
            sessionId: new(session), playerExperience: default(PlayerExperienceSnapshot) with
            { Tick = new(experienceTick), Player = experiencePlayer ?? Player, MatchStatus = PlayerMatchStatus.Active })
        {
            Conveyors = new(requestId, new(networkTick), preview, 1000, pieces ?? [], ports),
            SelectedOrders = selected is null ? null : new(new(session), new(4), selected, [], "")
        };
}
