using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GameplayGuidanceInteractionTests
{
    private static readonly GameplayHudLayout Layout = GameplayHudLayout.Create(1920, 1080, 96);
    private static readonly SimulationSessionId Session = new(1);
    private static void Update(GameplayGuidanceInteraction guide, InputState input, double ms,
        bool enabled = true, bool animate = true, bool blocked = false) =>
        guide.Update(input, Session, enabled, Layout, TimeSpan.FromMilliseconds(ms), animate, blocked);
    private static void Click(InputState input, float x, float y)
    {
        input.BeginFrame();
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, (int)x, (int)y));
    }
    private static void Release(InputState input)
    {
        input.BeginFrame();
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, 1, 1));
    }

    [Fact]
    public void ClickCollapsesAndTabRestoresWithoutWorldInputOrASecondClick()
    {
        var input = new InputState(); var guide = new GameplayGuidanceInteraction();
        Update(guide, input, 0);
        Click(input, Layout.OnboardingBounds.X + 10, Layout.OnboardingBounds.Y + 10);
        Update(guide, input, 10);
        Assert.True(guide.Hidden); Assert.True(guide.BlocksGameplayThisFrame);
        Assert.False(input.IsMouseButtonDown(PlatformMouseButton.Left));
        Release(input); Update(guide, input, 120); Update(guide, input, 230);
        Assert.Equal(OnboardingPhase.Collapsed, guide.View.Phase);
        Assert.True(guide.View.VisiblePanel.IsEmpty);
        Assert.False(guide.View.Contains(new(Layout.OnboardingBounds.Right - 1, Layout.OnboardingBounds.Y + 10)));
        Click(input, guide.View.Tab.X + 5, guide.View.Tab.Y + 5); Update(guide, input, 240);
        Assert.False(guide.Hidden); Assert.True(guide.BlocksGameplayThisFrame);
        Release(input); Update(guide, input, 350); Update(guide, input, 460);
        Assert.Equal(OnboardingPhase.Expanded, guide.View.Phase);
        Assert.Equal(GameplayGuidanceLayout.Resolve(Layout), guide.View.VisiblePanel);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(144)]
    public void SamplingUsesPresentationTimeAndExactSmoothEndpoints(int hz)
    {
        var input = new InputState(); var guide = new GameplayGuidanceInteraction();
        Update(guide, input, 0); Click(input, Layout.SafeArea.X + 5, Layout.SafeArea.Y + 5); Update(guide, input, 1);
        Release(input);
        for (double t = 1000.0 / hz; t < 110; t += 1000.0 / hz) Update(guide, input, 1 + t);
        Update(guide, input, 111);
        Assert.Equal(.5f, guide.View.Collapse, 5);
        Update(guide, input, 221);
        Assert.Equal(1, guide.View.Collapse); Assert.Equal(OnboardingPhase.Collapsed, guide.View.Phase);
    }

    [Fact]
    public void RapidReversalStartsAtPaintedPositionAndHasOneTransition()
    {
        var input = new InputState(); var guide = new GameplayGuidanceInteraction();
        Update(guide, input, 0); Click(input, Layout.SafeArea.X + 5, Layout.SafeArea.Y + 5); Update(guide, input, 1);
        Release(input); Update(guide, input, 111); var halfway = guide.View;
        Click(input, halfway.Tab.X + 5, halfway.Tab.Y + 5); Update(guide, input, 111);
        Assert.Equal(halfway.Panel, guide.View.Panel); Assert.Equal(OnboardingPhase.Expanding, guide.View.Phase);
        Release(input); Update(guide, input, 221); Assert.Equal(.25f, guide.View.Collapse, 5);
        Update(guide, input, 331); Assert.Equal(0, guide.View.Collapse);
    }

    [Theory]
    [InlineData("rollback")] [InlineData("stall")] [InlineData("focus")] [InlineData("resize")] [InlineData("reduced")]
    public void InterruptedMotionSettlesToLogicalTarget(string interruption)
    {
        var input = new InputState(); var guide = new GameplayGuidanceInteraction();
        Update(guide, input, 0); Click(input, Layout.SafeArea.X + 5, Layout.SafeArea.Y + 5); Update(guide, input, 10);
        Release(input); Update(guide, input, 65);
        if (interruption == "focus") input.Apply(PlatformInputEvent.FocusLost());
        var layout = interruption == "resize" ? GameplayHudLayout.Create(1280, 720, 192, 2) : Layout;
        guide.Update(input, Session, true, layout, TimeSpan.FromMilliseconds(interruption == "rollback" ? 1 : interruption == "stall" ? 1000 : 70), interruption != "reduced");
        Assert.Equal(1, guide.View.Collapse);
        Assert.Equal(OnboardingPresentation.Resolve(layout, 1).Tab, guide.View.Tab);
    }

    [Fact]
    public void DisabledAndBlockedHintsHaveNoSurfaceAndNewSessionRestartsExpanded()
    {
        var input = new InputState(); var guide = new GameplayGuidanceInteraction();
        Update(guide, input, 0); Click(input, Layout.SafeArea.X + 5, Layout.SafeArea.Y + 5); Update(guide, input, 1, animate: false);
        Release(input); Update(guide, input, 2, enabled: false);
        Assert.False(guide.View.Visible); Assert.True(guide.Hidden);
        Update(guide, input, 3); Assert.Equal(OnboardingPhase.Collapsed, guide.View.Phase);
        Update(guide, input, 4, blocked: true); Assert.False(guide.View.Visible);
        guide.Update(input, new(2), true, Layout, TimeSpan.FromMilliseconds(5));
        Assert.Equal(OnboardingPhase.Expanded, guide.View.Phase);
    }

    [Fact]
    public void WorldDragCannotBeTakenByAnIncomingPanelAndKeyboardNeedsFocus()
    {
        var input = new InputState(); var guide = new GameplayGuidanceInteraction();
        Update(guide, input, 0);
        Click(input, 1000, 500); Update(guide, input, 1);
        input.BeginFrame(); input.Apply(PlatformInputEvent.PointerMoved((int)Layout.SafeArea.X + 10, (int)Layout.SafeArea.Y + 10));
        Update(guide, input, 2); Assert.False(guide.Hidden); Assert.False(guide.BlocksGameplayThisFrame);
        Release(input); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Enter));
        Update(guide, input, 3); Assert.False(guide.Hidden);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.Enter)); input.BeginFrame();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Tab)); Update(guide, input, 4);
        Assert.True(guide.View.Focused);
        input.BeginFrame(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Space)); Update(guide, input, 5);
        Assert.True(guide.Hidden); Assert.True(guide.BlocksGameplayThisFrame);
    }

    [Fact]
    public void DisablingDuringASlideSettlesWithoutLosingTheSessionTarget()
    {
        var input = new InputState(); var guide = new GameplayGuidanceInteraction();
        Update(guide, input, 0); Click(input, Layout.SafeArea.X + 5, Layout.SafeArea.Y + 5); Update(guide, input, 1);
        Release(input); Update(guide, input, 56); Assert.InRange(guide.View.Collapse, .15f, .16f);
        Update(guide, input, 57, enabled: false); Assert.False(guide.View.Visible);
        Update(guide, input, 58); Assert.Equal(OnboardingPhase.Collapsed, guide.View.Phase);
    }

    [Fact]
    public void OutsideClickEscapeAndHeldPressAcrossResizeCannotToggleTheGuide()
    {
        var input = new InputState(); var guide = new GameplayGuidanceInteraction();
        Update(guide, input, 0);
        Click(input, 1000, 500); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Escape));
        Update(guide, input, 1); Assert.False(guide.Hidden);
        Release(input); Click(input, Layout.SafeArea.X + 5, Layout.SafeArea.Y + 5);
        guide.Update(input, Session, true, GameplayHudLayout.Create(1280, 720, 144, 2), TimeSpan.FromMilliseconds(2), true);
        Assert.False(guide.Hidden);
        input.BeginFrame(); Update(guide, input, 3); Assert.False(guide.Hidden);
    }

    [Fact]
    public void DockKeyboardOwnershipAndDisabledContentCannotAcquireFocus()
    {
        var input = new InputState(); var guide = new GameplayGuidanceInteraction();
        Update(guide, input, 0);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Tab));
        guide.Update(input, Session, true, Layout, TimeSpan.FromMilliseconds(1), true, keyboardFocusAvailable: false);
        Assert.False(guide.View.Focused); Assert.True(input.IsKeyDown(PlatformKey.Tab));
        input.BeginFrame(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.Space));
        Update(guide, input, 2); Assert.False(guide.Hidden); Assert.True(input.IsKeyDown(PlatformKey.Space));
        guide.Update(input, SimulationSessionId.None, false, Layout, TimeSpan.FromMilliseconds(3));
        Assert.False(guide.View.Visible);
    }

    [Fact]
    public void ToggleConsumesTheFirstGestureAndDoesNotOpenHelpOrDispatchActions()
    {
        var input = new InputState();
        var guide = new GameplayGuidanceInteraction();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.LeftShift));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F12));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.B));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, 800, 450));
        guide.Update(input, new SimulationSessionId(1), true);
        Assert.True(guide.Hidden);
        Assert.True(guide.BlocksGameplayThisFrame);
        Assert.False(input.IsKeyDown(PlatformKey.B));
        Assert.False(input.IsMouseButtonDown(PlatformMouseButton.Left));
        guide.Update(input, new SimulationSessionId(1), true);
        Assert.False(guide.BlocksGameplayThisFrame);
        Assert.True(guide.Hidden);
        input.Reset();
        guide.Update(input, new SimulationSessionId(2), true);
        Assert.False(guide.Hidden);
    }

    [Fact]
    public void DisabledOnboardingAndModalFramesIgnoreToggleAndUnshiftedF12DoesNothing()
    {
        var input = new InputState();
        var guide = new GameplayGuidanceInteraction();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.LeftShift));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F12));
        guide.Update(input, new SimulationSessionId(1), false);
        Assert.False(guide.Hidden);
        Assert.False(guide.BlocksGameplayThisFrame);
        guide.Update(input, new SimulationSessionId(1), true);
        Assert.False(guide.Hidden); // Held input across enable/modal boundaries cannot toggle.
        input.Reset();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.F12));
        guide.Update(input, new SimulationSessionId(1), true);
        Assert.False(guide.Hidden);
        Assert.False(guide.BlocksGameplayThisFrame);
    }
    [Fact]
    public void DetailedControlsOwnShortClicksBeforeWholeCardMinimizeAndHonorRebindings()
    {
        var input = new InputState(); var guide = new GameplayGuidanceInteraction();
        var bindings = new ForgeLine.Input.GameplayBindingRegistry(new ForgeLine.Input.GameplayBindings()
            .With(ForgeLine.Input.GameplayAction.NextItem, PlatformKey.G).With(ForgeLine.Input.GameplayAction.Activate, PlatformKey.J));
        guide.Update(input, Session, true, Layout, detailed: true, bindings: bindings);
        var next = GuidanceCardLayout.Control(guide.View, 1);
        Click(input, next.X + 3, next.Y + 3);
        guide.Update(input, Session, true, Layout, TimeSpan.FromMilliseconds(1), detailed: true, bindings: bindings);
        Assert.Equal(GuidanceNavigation.NextSection, guide.NavigationThisFrame);
        Assert.False(guide.Hidden);
        Assert.True(guide.BlocksGameplayThisFrame);
        Assert.False(input.IsMouseButtonDown(PlatformMouseButton.Left));
        Release(input);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.G));
        guide.Update(input, Session, true, Layout, TimeSpan.FromMilliseconds(2), detailed: true, bindings: bindings);
        input.BeginFrame(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.J));
        guide.Update(input, Session, true, Layout, TimeSpan.FromMilliseconds(3), detailed: true, bindings: bindings);
        Assert.Equal(GuidanceNavigation.Read, guide.NavigationThisFrame);
        Assert.False(guide.Hidden);
    }

}
