using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class GameplayRebindingTests
{
    private static readonly GameplayBindingRegistry Bindings = new(new GameplayBindings()
        .With(GameplayAction.Build, PlatformKey.G).With(GameplayAction.NextItem, PlatformKey.I).With(GameplayAction.Activate, PlatformKey.J));
    [Fact] public void ReboundKeyboardJourneyChoosesAndRequestsExistingBuildingOnce()
    {
        var controller = new PlayerActionPanelController(Bindings);
        var input = new InputState(); var snapshot = Snapshot();
        controller.Update(input, snapshot, 1600, 900);
        ClickKey(input, PlatformKey.B); controller.Update(input, snapshot, 1600, 900);
        Assert.Equal(PlayerActionPanelMode.Closed, controller.Mode);
        ClickKey(input, PlatformKey.G); controller.Update(input, snapshot, 1600, 900);
        Assert.Equal(PlayerActionPanelMode.Construction, controller.Mode);
        controller.Update(input, snapshot, 1600, 900);
        Assert.Equal(PlayerActionPanelMode.Construction, controller.Mode);
        ClickKey(input, PlatformKey.I); controller.Update(input, snapshot, 1600, 900);
        Assert.Equal(1, controller.SelectedIndex);
        ClickKey(input, PlatformKey.J); controller.Update(input, snapshot, 1600, 900);
        Assert.True(controller.TryTakeRequest(out var request));
        Assert.Equal(PlayerActionRequestKind.BeginBuildingPlacement, request.Kind);
        Assert.Equal(BuildingIds.VehicleFactory, request.BuildingId);
        controller.Update(input, snapshot, 1600, 900);
        Assert.False(controller.TryTakeRequest(out _));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void DisplayOrFocusTransitionSuppressesHeldReboundKey(bool resize)
    {
        var controller = new PlayerActionPanelController(Bindings); var input = new InputState();
        controller.Update(input, Snapshot(), 1600, 900);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.G));
        if (!resize) input.Apply(PlatformInputEvent.FocusLost());
        controller.Update(input, Snapshot(), resize ? 1920 : 1600, 900);
        controller.Update(input, Snapshot(), resize ? 1920 : 1600, 900);
        Assert.Equal(PlayerActionPanelMode.Closed, controller.Mode);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.G));
        ClickKey(input, PlatformKey.G);
        controller.Update(input, Snapshot(), resize ? 1920 : 1600, 900);
        Assert.Equal(PlayerActionPanelMode.Construction, controller.Mode);
    }
    [Fact] public void CapturedCommandsAndViewShareExactReboundPrompts()
    {
        var snapshot = Snapshot();
        Assert.True(ContextualCommandModel.TryGet(snapshot, 0, out var command, bindings: Bindings));
        Assert.Equal("G", command.Shortcut);
        var controller = new PlayerActionPanelController(Bindings);
        Assert.Same(Bindings, controller.CreateView(1600, 900, snapshot.PlayerActions).Bindings);
        Assert.Equal("G BUILD / COMMAND CORE", Bindings.Text("B BUILD / COMMAND CORE"));
        Assert.Equal("K / I / J", Bindings.TacticalPrompt);
    }
    [Fact] public void LocalPanelCancellationExplainsAndExpiresWithoutSubmittingRequest()
    {
        var controller = new PlayerActionPanelController(Bindings); var input = new InputState();
        controller.Update(input, Snapshot(), 1600, 900);
        ClickKey(input, PlatformKey.G); controller.Update(input, Snapshot(), 1600, 900);
        ClickKey(input, PlatformKey.Escape); controller.Update(input, Snapshot(), 1600, 900);
        Assert.Equal("CANCELLED - PANEL CLOSED", controller.Feedback);
        Assert.False(controller.TryTakeRequest(out _));
        controller.AdvanceFeedback(TimeSpan.FromSeconds(3));
        Assert.Empty(controller.Feedback);
    }
    internal static PresentationSnapshot Snapshot()
    {
        var session = new SimulationSessionId(1); var tick = new SimulationTick(1);
        var experience = default(PlayerExperienceSnapshot) with { Tick = tick, Player = new PlayerId(1),
            Selection = PlayerSelectionSummary.Empty with { Count = 1, Kind = PlayerSelectionKind.Building, CommonBuildingId = BuildingIds.CommandCore } };
        var actions = new PlayerActionSnapshot(session, tick,
            [new(BuildingIds.PowerPlant, "Power Plant", [], true), new(BuildingIds.VehicleFactory, "Vehicle Factory with a deliberately long qualification label", [], true)], 0, null, null);
        return new(tick, TimeSpan.Zero, 1, [], sessionId: session, playerExperience: experience, playerActions: actions);
    }
    private static void ClickKey(InputState input, PlatformKey key)
    {
        input.BeginFrame();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, key));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, key));
    }
}
