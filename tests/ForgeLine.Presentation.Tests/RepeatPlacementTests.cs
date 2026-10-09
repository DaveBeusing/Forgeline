using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RepeatPlacementTests
{
    [Fact]
    public void RealAcceptedConstructionRequiresANewOccupiedFootprintPreviewBeforeAnyRepeat()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var player = new PlayerId(1);
        var interaction = new PresentationInteractionState();
        var gateway = new PlayerCommandGateway(scenario.Simulation, scenario.Services.BuildingCommands, scenario.BattlefieldRuntime.MatchStateEntity);
        var buffer = new PresentationSnapshotBuffer();
        scenario.Simulation.RegisterTickObserver(gateway);
        scenario.Simulation.RegisterTickObserver(new PresentationExtractor(buffer, new PresentationExtractionContext(scenario, player, interaction, gateway)));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var initial));
        var camera = SameTypeSelectionTests.Camera();
        camera.CenterOn(EarlyGameGuidanceTests.FindPlacement(scenario, BuildingIds.PowerPlant));
        var input = new InputState();
        input.Apply(PlatformInputEvent.PointerMoved(800, 450));
        var controller = new RtsBuildingPlacementController(player);
        controller.SelectBuilding(BuildingIds.PowerPlant);
        controller.Update(input, camera, scenario.Terrain, initial, interaction, 1600, 900);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var checkedLocation));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.LeftShift));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, 800, 450));
        controller.Update(input, camera, scenario.Terrain, checkedLocation, interaction, 1600, 900);
        Assert.True(controller.TryTakePlacementRequest(out var request));
        var receipt = gateway.SubmitBuild(player, request.BuildingId, request.Position, request.Orientation,
            scenario.GetBase(player).CommandCore, checkedLocation.Tick);
        Assert.True(receipt.Accepted);
        controller.ObserveSubmission(receipt, interaction);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out var result));
        Assert.Equal(PlayerCommandFeedbackState.Accepted, result.State);
        controller.ObserveResult(result, interaction);
        Assert.True(buffer.TryReadLatest(out var accepted));
        controller.Update(input, camera, scenario.Terrain, accepted, interaction, 1600, 900);
        Assert.False(controller.TryTakePlacementRequest(out _));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var rechecked));
        controller.Update(input, camera, scenario.Terrain, rechecked, interaction, 1600, 900);
        Assert.Equal(PlacementPreviewFreshness.Current, controller.PreviewFreshness);
        Assert.False(controller.Preview!.Value.IsValid);
        Assert.Equal(BuildingPlacementFailureReason.Obstructed, controller.Preview.Value.Failure);
        Assert.False(controller.TryTakePlacementRequest(out _));
        Assert.Equal(1, scenario.Simulation.Entities.GetComponentCount<ConstructionSite>());
    }

    [Fact]
    public void ShiftPlacementWaitsForMatchingAcceptanceAndFreshPreviewWhileHeldCannotDuplicate()
    {
        var f = new Fixture();
        f.Prime();
        f.Click(shift: true);
        Assert.True(f.Controller.TryTakePlacementRequest(out var request));
        Assert.True(f.Controller.AwaitingResult);
        Assert.False(f.Controller.TryTakePlacementRequest(out _));
        f.Update(f.Snapshot());
        Assert.False(f.Controller.TryTakePlacementRequest(out _));
        f.Controller.ObserveSubmission(Fixture.Receipt(), f.Interaction);
        f.Controller.ObserveResult(Fixture.Result() with { CorrelationId = new(99) }, f.Interaction);
        Assert.True(f.Controller.AwaitingResult);
        f.Controller.ObserveResult(Fixture.Result(), f.Interaction);
        Assert.False(f.Controller.AwaitingResult);
        Assert.True(f.Controller.IsActive);
        Assert.Equal(PlacementPreviewFreshness.Unavailable, f.Controller.PreviewFreshness);
        f.Update(f.Snapshot(request.PreviewRequestId));
        Assert.Equal(PlacementPreviewFreshness.Stale, f.Controller.PreviewFreshness);
        Assert.False(f.Controller.TryTakePlacementRequest(out _));
        f.Update(f.Snapshot());
        Assert.Equal(PlacementPreviewFreshness.Current, f.Controller.PreviewFreshness);
        Assert.False(f.Controller.TryTakePlacementRequest(out _));
        f.Release();
        f.Click(shift: false);
        Assert.True(f.Controller.TryTakePlacementRequest(out _));
        f.Controller.ObserveSubmission(Fixture.Receipt(), f.Interaction);
        f.Controller.ObserveResult(Fixture.Result(), f.Interaction);
        Assert.False(f.Controller.IsActive);
    }

    [Theory]
    [InlineData(BuildCommandRejectionReason.InsufficientResources)]
    [InlineData(BuildCommandRejectionReason.PlacementInvalid)]
    public void RejectionKeepsIntentButDoesNotClaimSuccessOrReusePreview(BuildCommandRejectionReason reason)
    {
        var f = new Fixture();
        f.Prime();
        f.Click(shift: true);
        Assert.True(f.Controller.TryTakePlacementRequest(out var request));
        f.Controller.ObserveSubmission(Fixture.Receipt(), f.Interaction);
        f.Controller.ObserveResult(Fixture.Result() with
        {
            State = PlayerCommandFeedbackState.Rejected,
            BuildRejection = reason,
            AcceptedTargets = 0,
            RejectedTargets = 1
        }, f.Interaction);
        Assert.True(f.Controller.IsActive);
        Assert.False(f.Controller.AwaitingResult);
        f.Release();
        f.Update(f.Snapshot(request.PreviewRequestId));
        f.Click(true, f.Snapshot(request.PreviewRequestId));
        Assert.False(f.Controller.TryTakePlacementRequest(out _));
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("resize")]
    [InlineData("scale")]
    [InlineData("session")]
    [InlineData("escape")]
    [InlineData("modal")]
    public void LifecycleCancellationDiscardsPendingRepeatAndOrientation(string transition)
    {
        var f = new Fixture();
        f.Prime();
        f.Input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F9));
        f.Update(f.Snapshot());
        Assert.Equal(BuildingOrientation.East, f.Controller.Orientation);
        f.Input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.F9));
        f.Update(f.Snapshot());
        f.Click(true);
        Assert.True(f.Controller.TryTakePlacementRequest(out _));
        f.Controller.ObserveSubmission(Fixture.Receipt(), f.Interaction);
        if (transition == "focus") f.Input.Apply(PlatformInputEvent.FocusLost());
        if (transition == "escape") f.Input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Escape));
        if (transition == "modal") f.Controller.Cancel(f.Interaction);
        else f.Update(f.Snapshot(session: transition == "session" ? 2ul : 1ul),
            width: transition == "resize" ? 1800 : 1600, scale: transition == "scale" ? 2 : 1);
        f.Controller.ObserveResult(Fixture.Result(), f.Interaction);
        Assert.False(f.Controller.IsActive);
        f.Controller.SelectBuilding(BuildingIds.PowerPlant);
        Assert.Equal(BuildingOrientation.North, f.Controller.Orientation);
    }

    [Fact]
    public void CompletedSinglePlacementRemembersSameBuildingRotationButSwitchingTypeResetsIt()
    {
        var f = new Fixture();
        f.Prime();
        f.Input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F9));
        f.Update(f.Snapshot());
        f.Input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.F9));
        f.Update(f.Snapshot());
        f.Click(false);
        Assert.True(f.Controller.TryTakePlacementRequest(out _));
        f.Controller.ObserveSubmission(Fixture.Receipt(), f.Interaction);
        f.Controller.ObserveResult(Fixture.Result(), f.Interaction);
        f.Controller.SelectBuilding(BuildingIds.PowerPlant);
        Assert.Equal(BuildingOrientation.East, f.Controller.Orientation);
        f.Controller.SelectBuilding(BuildingIds.Smelter);
        Assert.Equal(BuildingOrientation.North, f.Controller.Orientation);
    }

    [Fact]
    public void PreviewRequiresMatchingTickBuildingOrientationAndPositionAndUiOriginCannotClick()
    {
        var f = new Fixture();
        f.Prime();
        var snapshot = f.Snapshot();
        var read = snapshot.PlacementPreview!.Value;
        foreach (var invalid in new[]
        {
            read with { CompletedTick = new(1) },
            read with { Preview = read.Preview with { BuildingId = BuildingIds.Smelter } },
            read with { Preview = read.Preview with { Orientation = BuildingOrientation.East } },
            read with { Preview = read.Preview with { GroundPosition = new(1, 0, 0) } }
        })
        {
            f.Update(new(new(4), TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(1), placementPreview: invalid));
            Assert.Equal(PlacementPreviewFreshness.Stale, f.Controller.PreviewFreshness);
        }
        f.Input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, 800, 450));
        f.Controller.Update(f.Input, f.Camera, new SameTypeSelectionTests.FlatTerrain(), snapshot, f.Interaction, 1600, 900, pointerCaptured: true);
        Assert.False(f.Controller.TryTakePlacementRequest(out _));
    }

    private sealed class Fixture
    {
        internal InputState Input { get; } = new();
        internal RtsBuildingPlacementController Controller { get; } = new(new(1));
        internal PresentationInteractionState Interaction { get; } = new();
        internal RtsCamera Camera { get; } = SameTypeSelectionTests.Camera();
        internal void Prime()
        {
            Controller.SelectBuilding(BuildingIds.PowerPlant);
            Input.Apply(PlatformInputEvent.PointerMoved(800, 450));
            Update(new(new(1), TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(1)));
            Update(Snapshot());
        }
        internal PresentationSnapshot Snapshot(ulong? id = null, ulong session = 1)
        {
            var request = Interaction.Capture().PlacementRequest;
            var preview = new BuildingPlacementPreview(BuildingIds.PowerPlant, "", "Power Plant", request?.RequestedPosition ?? default,
                request?.Orientation ?? Controller.Orientation, default, true, default, default);
            return new(new(4), TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(session),
                placementPreview: new(id ?? request?.RequestId ?? 1, new(4), preview));
        }
        internal void Update(PresentationSnapshot snapshot, int width = 1600, float scale = 1) =>
            Controller.Update(Input, Camera, new SameTypeSelectionTests.FlatTerrain(), snapshot, Interaction, width, 900, displayScale: scale);
        internal void Click(bool shift, PresentationSnapshot? snapshot = null)
        {
            Input.BeginFrame();
            Input.Apply(PlatformInputEvent.KeyChanged(shift ? PlatformInputEventKind.KeyDown : PlatformInputEventKind.KeyUp, PlatformKey.LeftShift));
            Input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, 800, 450));
            Update(snapshot ?? Snapshot());
        }
        internal void Release()
        {
            Input.BeginFrame();
            Input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, 800, 450));
            Update(Snapshot());
        }
        internal static PlayerCommandSubmissionReceipt Receipt() => new(new(1), new(1), PlayerCommandKind.Construction,
            default, new(3), new(4), 1, true, default);
        internal static PlayerCommandResultReadModel Result() => new(new(1), new(1), PlayerCommandKind.Construction,
            PlayerCommandFeedbackState.Accepted, 1, 0, default, default, new(4));
    }
}
