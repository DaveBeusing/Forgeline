using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Simulation;
using ForgeLine.Platform;
using Xunit;
using static ForgeLine.Presentation.Tests.SelectionOverlayRenderingTests;

namespace ForgeLine.Presentation.Tests;

public sealed partial class HoverTooltipRenderingTests
{
    internal static PresentationSnapshot StatusSnapshot()
    {
        var original = HoverTooltipTests.Snapshot();
        var tick = new SimulationTick(1);
        var selection = PlayerSelectionSummary.Empty with {
            Count = 1, PrimaryEntity = new EntityId(11, 1), Kind = PlayerSelectionKind.Unit,
            DisplayName = "Main Battle Tank", HasHealth = true, HealthFraction = .8,
            HasSupply = true, FuelFraction = .3, AmmunitionFraction = .2,
            HasReadiness = true, Readiness = .5, HasPower = true,
            HasInventory = true, InventoryQuantity = 123,
            Work = new(PlayerWorkKind.Processing, PlayerWorkState.Blocked, .25, "Steel", "NO INPUT") };
        var experience = default(PlayerExperienceSnapshot) with { Tick = tick, Selection = selection,
            Feedback = new(PlayerCommandFeedbackKind.Production, PlayerCommandFeedbackState.Partial,
                2, 1, default, default, tick) };
        return new PresentationSnapshot(tick, TimeSpan.FromMilliseconds(50), 1, original.Instances,
            sessionId: original.SessionId, playerExperience: experience);
    }

    internal static PlayerActionPanelView StatusPanel(PresentationSnapshot snapshot) =>
        default(PlayerActionPanelView) with { Ring = new(new EntityId(11, 1), snapshot.SessionId,
            9, 10, true, true, new(new Vector2(800, 450), new Vector2(800, 450), 40, 120, 8), true) };

    internal static HoverTooltipView StatusView(PresentationSnapshot snapshot, GameplayHudLayout layout) =>
        new(snapshot.SessionId, default, default, new Vector2(800, 450), true,
            layout.ViewportWidth, layout.ViewportHeight, layout.Scale,
            SelectionStatus: true, SelectionRevision: 9, MenuRevision: 10);

    [Fact]
    public void SelectionStatusRetainsCopiedDetailsAndRejectsStaleMenuIdentity()
    {
        var snapshot = StatusSnapshot();
        var panel = StatusPanel(snapshot);
        var view = StatusView(snapshot, GameplayHudLayout.Create(1600, 900, 96));
        var content = Assert.IsType<HoverTooltipContent>(HoverTooltipResolver.Resolve(snapshot, view, panel));
        Assert.Equal(snapshot.PlayerExperience!.Value.Selection, content.Selection);
        Assert.Equal(snapshot.PlayerExperience!.Value.Feedback, content.Feedback);
        Assert.Null(HoverTooltipResolver.Resolve(snapshot, view with { MenuRevision = 11 }, panel));
        Assert.Null(HoverTooltipResolver.Resolve(snapshot, view with { SelectionRevision = 11 }, panel));
        Assert.Null(HoverTooltipResolver.Resolve(snapshot, view, panel with { SelectionPending = true }));
        Assert.Null(HoverTooltipResolver.Resolve(snapshot, view, panel with {Ring=panel.Ring with {SingleOwned=false}}));
        Assert.Null(HoverTooltipResolver.Resolve(snapshot, view, panel with {Ring=panel.Ring with {SingleOwned=false,Global=true}}));
        Assert.Null(HoverTooltipResolver.Resolve(snapshot, view, panel with { Ring = panel.Ring with { Open = false } }));
        Assert.Null(HoverTooltipResolver.Resolve(snapshot, view, panel with { Ring = panel.Ring with { Entity = new(11, 2) } }));
    }

    [Fact]
    public void SingleStatusCannotInferOperationalFactsFromAnUnrelatedGroupCache()
    {
        var snapshot=StatusSnapshot();var panel=StatusPanel(snapshot);
        var member=SelectedCombatGroupTests.Member(new(11,1),UnitIds.MainBattleTank,true);
        var selected=new SelectionSet();selected.SetSingle(member.Entity);
        var cached=SelectedCombatGroup.Create(new CombatGroupOperationalSnapshot(snapshot.Tick,[member],snapshot.SessionId),selected);
        var content=Assert.IsType<HoverTooltipContent>(HoverTooltipResolver.Resolve(snapshot,
            StatusView(snapshot,GameplayHudLayout.Create(1600,900,96)),panel,cached));
        Assert.Null(content.Group);
        Assert.Equal(snapshot.PlayerExperience!.Value.Selection,content.Selection);
    }

    [Fact]
    public void KeyboardRingExposesReadOnlyStatusWithoutPointerAndClearsOnModalOrHeldGesture()
    {
        var snapshot = StatusSnapshot();
        var panel = StatusPanel(snapshot);
        var input = new InputState();
        var controller = new HoverTooltipController();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var camera = new RtsCamera();
        var view = controller.Update(input, snapshot, default, camera, layout, panel, false, false, TimeSpan.Zero);
        Assert.True(view.Ready);
        Assert.True(view.SelectionStatus);
        Assert.False(controller.Update(input, snapshot, default, camera, layout, panel, true, false, TimeSpan.Zero).Ready);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, 800, 450));
        Assert.False(controller.Update(input, snapshot, default, camera, layout, panel, false, false, TimeSpan.Zero).Ready);
    }

    [Theory]
    [InlineData(1280, 720, 96u, .75f)]
    [InlineData(1920, 1080, 144u, 1f)]
    [InlineData(1280, 720, 192u, 2f)]
    [InlineData(3440, 1440, 192u, 2f)]
    public void SelectionStatusFitsSafeAreaAndRetainsWorkInventoryAndResult(int width, int height, uint dpi, float scale)
    {
        var snapshot = StatusSnapshot();
        var layout = GameplayHudLayout.Create(width, height, dpi, scale);
        using var graphics = new RecordingDevice();
        using var surface = new WorldHoverTooltipSurface(graphics);
        surface.Render(Context(new RecordingContext { Width = width, Height = height }, snapshot, layout,
            StatusView(snapshot, layout)) with { ActionPanel = StatusPanel(snapshot) });
        Assert.InRange(surface.LastLayoutLineCount, 12, 30);
        Assert.InRange(surface.LastBounds.X, layout.SafeArea.X, layout.SafeArea.Right);
        Assert.InRange(surface.LastBounds.Bottom, layout.SafeArea.Y, layout.SafeArea.Bottom);
        Assert.InRange(surface.LastBounds.Right, layout.SafeArea.X, layout.SafeArea.Right);
        Assert.True(surface.LastRenderedVertexCount > 0);
    }

    [Fact]
    public void WarmSelectionStatusUsesRetainedBuffersWithoutManagedAllocations()
    {
        var snapshot = StatusSnapshot();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        using var graphics = new RetainedDevice();
        using var surface = new WorldHoverTooltipSurface(graphics);
        var context = Context(new RecordingContext(), snapshot, layout, StatusView(snapshot, layout))
            with { ActionPanel = StatusPanel(snapshot) };
        for (int i = 0; i < 128; i++) surface.Render(context);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) surface.Render(context);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        Assert.True(surface.LastRenderedVertexCount > 0);
        Assert.Equal(1, graphics.BufferCount);
    }
}
