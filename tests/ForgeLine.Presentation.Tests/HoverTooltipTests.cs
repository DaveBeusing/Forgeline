using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Intelligence;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class HoverTooltipTests
{
    private static readonly EntityId Entity = new(11, 1);
    private static readonly SimulationSessionId Session = new(1);
    private static readonly GameplayHudLayout Layout = GameplayHudLayout.Create(1600, 900, 96);

    [Fact]
    public void DelayUsesElapsedPresentationTimeAndResetsForMotionIdentityAndSession()
    {
        var controller = new HoverTooltipController();
        var input = PointerAt(800, 450);
        var snapshot = Snapshot();
        Assert.False(Update(controller, input, snapshot, TimeSpan.FromSeconds(1)).Ready);
        Assert.False(Update(controller, input, snapshot, TimeSpan.FromMilliseconds(174)).Ready);
        Assert.True(Update(controller, input, snapshot, TimeSpan.FromMilliseconds(1)).Ready);
        input.Apply(PlatformInputEvent.PointerMoved(810, 450));
        Assert.False(Update(controller, input, snapshot, TimeSpan.FromMilliseconds(10)).Ready);
        Assert.True(Update(controller, input, snapshot, HoverTooltipController.DefaultDelay).Ready);
        var replaced = Snapshot(new SimulationSessionId(2));
        Assert.False(Update(controller, input, replaced, HoverTooltipController.DefaultDelay).Ready);
        Assert.False(controller.Update(input, replaced, new EntityId(Entity.Index, 2), new RtsCamera(), Layout,
            default, false, false, HoverTooltipController.DefaultDelay).Ready);
    }

    [Theory]
    [InlineData(PlatformMouseButton.Left)]
    [InlineData(PlatformMouseButton.Middle)]
    [InlineData(PlatformMouseButton.Right)]
    public void HeldGesturesSuppressAndRequireANewDelay(PlatformMouseButton button)
    {
        var controller = new HoverTooltipController();
        var input = PointerAt(800, 450);
        var snapshot = Snapshot();
        Update(controller, input, snapshot, TimeSpan.Zero);
        Assert.True(Update(controller, input, snapshot, HoverTooltipController.DefaultDelay).Ready);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, button, 800, 450));
        Assert.False(Update(controller, input, snapshot, HoverTooltipController.DefaultDelay).Entity.IsValid);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, button, 800, 450));
        Assert.False(Update(controller, input, snapshot, HoverTooltipController.DefaultDelay).Ready);
    }

    [Fact]
    public void ModalCaptureFocusLossMissingViewportAndDisplayChangesCancel()
    {
        var controller = new HoverTooltipController();
        var input = PointerAt(800, 450);
        var snapshot = Snapshot();
        Update(controller, input, snapshot, TimeSpan.Zero);
        Assert.True(Update(controller, input, snapshot, HoverTooltipController.DefaultDelay).Ready);
        Assert.False(controller.Update(input, snapshot, Entity, new RtsCamera(), Layout, default,
            true, false, HoverTooltipController.DefaultDelay).Entity.IsValid);
        Assert.False(controller.Update(input, snapshot, Entity, new RtsCamera(), Layout, default,
            false, true, HoverTooltipController.DefaultDelay).Entity.IsValid);
        Update(controller, input, snapshot, TimeSpan.Zero);
        Assert.False(controller.Update(input, snapshot, Entity, new RtsCamera(), GameplayHudLayout.Create(1600, 900, 144),
            default, false, false, HoverTooltipController.DefaultDelay).Ready);
        Assert.False(controller.Update(input, snapshot, Entity, new RtsCamera(), GameplayHudLayout.Create(0, 0, 96),
            default, false, false, HoverTooltipController.DefaultDelay).Ready);
        input.Apply(PlatformInputEvent.FocusLost());
        Assert.False(Update(controller, input, snapshot, HoverTooltipController.DefaultDelay).Entity.IsValid);
    }

    [Fact]
    public void DockHasPrecedenceAndCopiedCostsReasonsAndClosureAreConsistent()
    {
        var costs = new[] { new PlayerActionResourceAmount(ResourceIds.Steel, "Steel", 25, 10) };
        var actions = new PlayerActionSnapshot(Session, new SimulationTick(1),
            [new PlayerConstructionActionReadModel(BuildingIds.Extractor, "Mine / Extractor", costs, true)], 0, null, null);
        var snapshot = Snapshot(actions: actions);
        var panel = default(PlayerActionPanelView) with { Mode = PlayerActionPanelMode.Construction };
        var card = PlayerActionDockInteractionLayout.GetCardRect(Layout, 0);
        var input = PointerAt((int)(card.X + card.Width / 2), (int)(card.Y + card.Height / 2));
        var controller = new HoverTooltipController();
        controller.Update(input, snapshot, Entity, new RtsCamera(), Layout, panel, false, true, TimeSpan.Zero);
        var view = controller.Update(input, snapshot, Entity, new RtsCamera(), Layout, panel, false, true,
            HoverTooltipController.DefaultDelay);
        Assert.False(view.Entity.IsValid);
        var content = Assert.IsType<HoverTooltipContent>(HoverTooltipResolver.Resolve(snapshot, view, panel));
        Assert.Equal("Mine / Extractor", content.Title);
        Assert.Equal("MISSING MATERIALS", content.Status);
        Assert.Equal(costs, content.Costs);
        Assert.Contains("DEPOSIT", content.Requirement);
        Assert.Null(HoverTooltipResolver.Resolve(snapshot, view, default));
    }

    [Fact]
    public void RadarContactUsesOpaqueCopiedPositionAndClearsOnIntelligenceLoss()
    {
        var intelligence = new FactionIntelligenceStore();
        intelligence.BeginTick(new SimulationTick(1));
        intelligence.Observe(new FactionId(1), Entity, new IntelligenceSignature(new FactionId(2), 999),
            Vector3.Zero, IntelligenceState.Detected, new SimulationTick(1));
        var snapshot = Snapshot(intelligence: intelligence.Capture(new FactionId(1)));
        var input = PointerAt(800, 450);
        var camera = new RtsCamera();
        var projected = camera.WorldToScreen(Vector3.Zero, 1600, 900);
        input.Apply(PlatformInputEvent.PointerMoved((int)projected.Position.X, (int)projected.Position.Y));
        var controller = new HoverTooltipController();
        controller.Update(input, snapshot, EntityId.Invalid, camera, Layout, default, false, false, TimeSpan.Zero);
        var view = controller.Update(input, snapshot, EntityId.Invalid, camera, Layout, default, false, false,
            HoverTooltipController.DefaultDelay);
        Assert.True(view.Contact.IsSpecified);
        Assert.False(view.Entity.IsValid);
        var content = Assert.IsType<HoverTooltipContent>(HoverTooltipResolver.Resolve(snapshot, view, default));
        Assert.Equal("UNKNOWN CONTACT", content.Title);
        Assert.Null(content.World);
        Assert.Null(content.Costs);
        Assert.Null(HoverTooltipResolver.Resolve(Snapshot(), view, default));
    }

    [Fact]
    public void DelayedStaleSummaryCannotReviveDestroyedReusedOrOtherSessionEntity()
    {
        var snapshot = Snapshot();
        var view = new HoverTooltipView(Session, Entity, default, new Vector2(800, 450), true, 1600, 900, 1);
        Assert.NotNull(HoverTooltipResolver.Resolve(snapshot, view, default));
        Assert.Null(HoverTooltipResolver.Resolve(snapshot, view with { Entity = new EntityId(Entity.Index, 2) }, default));
        Assert.Null(HoverTooltipResolver.Resolve(snapshot, view with { SessionId = new SimulationSessionId(2) }, default));
        var empty = new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 0, [],
            sessionId: Session, playerExperience: snapshot.PlayerExperience, hover: snapshot.Hover);
        Assert.Null(HoverTooltipResolver.Resolve(empty, view, default));
        var stale = new PresentationSnapshot(new SimulationTick(2), TimeSpan.FromMilliseconds(50), 1, snapshot.Instances,
            sessionId: Session, playerExperience: snapshot.PlayerExperience, hover: snapshot.Hover);
        Assert.Null(HoverTooltipResolver.Resolve(stale, view, default));
    }

    [Theory]
    [InlineData(1600, 900, 96u, .75f)]
    [InlineData(1600, 900, 144u, 1f)]
    [InlineData(1600, 900, 192u, 2f)]
    [InlineData(5120, 2160, 144u, 2f)]
    public void PlacementFitsSafeBoundsAtEveryEdgeAndAvoidsPointer(int width, int height, uint dpi, float scale)
    {
        var layout = GameplayHudLayout.Create(width, height, dpi, scale);
        foreach (var point in new[] { new Vector2(0, 0), new Vector2(width - 1, 0),
            new Vector2(0, height - 1), new Vector2(width - 1, height - 1), new Vector2(width / 2, height / 2) })
        {
            var rect = HoverTooltipPlacement.Resolve(point, layout, 330 * layout.Scale, 180 * layout.Scale);
            Assert.True(rect.X >= layout.SafeArea.X && rect.Y >= layout.SafeArea.Y);
            Assert.True(rect.Right <= layout.SafeArea.Right && rect.Bottom <= layout.SafeArea.Bottom);
            Assert.False(rect.Contains(point));
        }
    }

    internal static PresentationSnapshot Snapshot(SimulationSessionId? session = null, PlayerActionSnapshot? actions = null,
        FactionIntelligenceSnapshot? intelligence = null)
    {
        var details = PlayerSelectionSummary.Empty with
        { Count = 1, PrimaryEntity = Entity, DisplayName = "Main Battle Tank", Kind = PlayerSelectionKind.Unit, HasHealth = true, HealthFraction = .8 };
        var summary = new PlayerHoverSummary(Entity, session ?? Session, new SimulationTick(1), PlayerHoverCategory.Unit,
            details.DisplayName, RtsUiIcon.UnitArmor, details);
        return new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1,
            [new RenderInstance(Entity, new RenderTransform(Vector3.Zero, Quaternion.Identity, Vector3.One),
                new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World)],
            sessionId: session ?? Session, playerExperience: default(PlayerExperienceSnapshot),
            hover: summary, playerActions: actions, intelligence: intelligence);
    }

    private static InputState PointerAt(int x, int y)
    {
        var input = new InputState();
        input.Apply(PlatformInputEvent.PointerMoved(x, y));
        return input;
    }

    private static HoverTooltipView Update(HoverTooltipController controller, InputState input,
        PresentationSnapshot snapshot, TimeSpan elapsed) =>
        controller.Update(input, snapshot, Entity, new RtsCamera(), Layout, default, false, false, elapsed);
}
