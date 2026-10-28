using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class PolicyTemplateKeyboardTests
{
    [Theory]
    [InlineData(GameplayAction.NextItem)]
    [InlineData(GameplayAction.Activate)]
    [InlineData(GameplayAction.PrimarySetting)]
    [InlineData(GameplayAction.SecondarySetting)]
    [InlineData(GameplayAction.Decrease)]
    [InlineData(GameplayAction.Increase)]
    public void DownAndUpBetweenUpdatesAreConsumedExactlyOnce(GameplayAction action)
    {
        var held = Create(); var tap = Create();
        var key = GameplayBindingRegistry.Default.Key(action);
        held.Input.Apply(new PlatformInputEvent(PlatformInputEventKind.KeyDown, Key: key, MouseButton: PlatformMouseButton.None, PointerX: 0, PointerY: 0, WheelDelta: 0));
        tap.Input.Apply(new PlatformInputEvent(PlatformInputEventKind.KeyDown, Key: key, MouseButton: PlatformMouseButton.None, PointerX: 0, PointerY: 0, WheelDelta: 0));
        tap.Input.Apply(new PlatformInputEvent(PlatformInputEventKind.KeyUp, Key: key, MouseButton: PlatformMouseButton.None, PointerX: 0, PointerY: 0, WheelDelta: 0));
        Update(held); Update(tap);
        Assert.Equal(held.Panel.View, tap.Panel.View);
        var after = tap.Panel.View;
        Update(tap); Assert.Equal(after, tap.Panel.View);
        held.Input.Apply(new PlatformInputEvent(PlatformInputEventKind.KeyDown, Key: key, MouseButton: PlatformMouseButton.None, PointerX: 0, PointerY: 0, WheelDelta: 0));
        Update(held); Assert.Equal(after, held.Panel.View);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BlockedAndSuppressedPressesCannotReplay(bool blocked)
    {
        var f = Create(); var key = GameplayBindingRegistry.Default.Key(GameplayAction.NextItem);
        f.Input.Apply(new PlatformInputEvent(PlatformInputEventKind.KeyDown, Key: key, MouseButton: PlatformMouseButton.None, PointerX: 0, PointerY: 0, WheelDelta: 0));
        if (!blocked) f.Input.SuppressKeyUntilRelease(key);
        Update(f, blocked); f.Input.BeginFrame();
        f.Input.Apply(new PlatformInputEvent(PlatformInputEventKind.KeyUp, Key: key, MouseButton: PlatformMouseButton.None, PointerX: 0, PointerY: 0, WheelDelta: 0));
        Update(f); Assert.Equal(1, f.Panel.View.Focus);
        f.Input.Apply(new PlatformInputEvent(PlatformInputEventKind.KeyDown, Key: key, MouseButton: PlatformMouseButton.None, PointerX: 0, PointerY: 0, WheelDelta: 0));
        f.Input.Apply(new PlatformInputEvent(PlatformInputEventKind.KeyUp, Key: key, MouseButton: PlatformMouseButton.None, PointerX: 0, PointerY: 0, WheelDelta: 0));
        Update(f); Assert.Equal(2, f.Panel.View.Focus);
    }

    [Fact]
    public void SessionBoundaryConsumesAnOldTapBeforeOpeningTheNewPanel()
    {
        var f = Create(); var key = GameplayBindingRegistry.Default.Key(GameplayAction.NextItem);
        f.Input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, key));
        f.Input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, key));
        var snapshot = new PresentationSnapshot(SimulationTick.Zero, TimeSpan.Zero, 0,
            ReadOnlySpan<RenderInstance>.Empty, sessionId: new(1));
        Update((f.Panel, f.Input, snapshot));
        Assert.False(f.Panel.View.Open); Assert.Equal(1, f.Panel.View.Focus);
        f.Panel.Open(); f.Input.BeginFrame();
        f.Input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, key));
        f.Input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, key));
        Update((f.Panel, f.Input, snapshot)); Assert.Equal(2, f.Panel.View.Focus);
    }

    private static (PolicyTemplatePanelController Panel, InputState Input, PresentationSnapshot Snapshot) Create()
    {
        var panel = new PolicyTemplatePanelController(GameplayBindingRegistry.Default);
        var input = new InputState();
        var snapshot = new PresentationSnapshot(SimulationTick.Zero, TimeSpan.Zero, 0, ReadOnlySpan<RenderInstance>.Empty);
        panel.Reset(snapshot.SessionId); panel.Open();
        var result = (panel, input, snapshot); Update(result); return result;
    }
    private static void Update((PolicyTemplatePanelController Panel, InputState Input, PresentationSnapshot Snapshot) f,
        bool blocked = false) => f.Panel.Update(f.Input, f.Snapshot, GameplayHudLayout.Create(1600, 900, 96), blocked, null);
}
