using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class NonModalHudInteractionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void WorldSelectionClosesAllMenusOnTheSameGesture(bool shortClick, bool shift)
    {
        var f = new Fixture(); f.Open();
        if (shift) f.Key(PlatformKey.LeftShift, true);
        f.Button(PlatformMouseButton.Left, true, 800, 450);
        if (shortClick) f.Button(PlatformMouseButton.Left, false, 800, 450);
        if (shift) f.Key(PlatformKey.LeftShift, false);
        f.Frame();
        Assert.False(f.Menus.HasOpenMenus(f.Dock, f.Operations));
        Assert.False(f.Menus.PointerCaptured);
        Assert.Equal(shift, f.Menus.LastGesture.Shift);
        Assert.Equal(f.Snapshot.SessionId, f.Menus.LastGesture.Session);
        if (!shortClick) { f.Input.BeginFrame(); f.Button(PlatformMouseButton.Left, false, 800, 450); f.Frame(); }
        Assert.True(f.Selection.Selection.Contains(f.Entity));
        Assert.Equal(0, f.Moves);
    }

    [Fact]
    public void WorldDragBeginsWithoutAnExtraClickAndCameraRemainsAvailable()
    {
        var f = new Fixture(); f.Open();
        f.Button(PlatformMouseButton.Left, true, 760, 410); f.Frame();
        f.Input.BeginFrame(); f.Input.Apply(PlatformInputEvent.PointerMoved(840, 490)); f.Frame();
        Assert.True(f.Selection.IsDragSelecting);
        Assert.False(f.Dock.HasKeyboardFocus);
        Assert.False(f.Operations.View.Open);
        var mapper = new RtsCameraActionMapper();
        f.Key(PlatformKey.D, true); var before = f.Camera.Target;
        f.Camera.Update(mapper.Map(f.Input, f.Menus.PointerCaptured), 1f / 60, 1600, 900);
        Assert.NotEqual(before, f.Camera.Target);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RightClickClosesMenusAndMovesExactlyOnce(bool shortClick)
    {
        var f = new Fixture(); f.Open();
        f.Button(PlatformMouseButton.Right, true, 800, 450);
        if (shortClick) f.Button(PlatformMouseButton.Right, false, 800, 450);
        f.Frame(); f.Input.BeginFrame(); f.Frame();
        Assert.Equal(1, f.Moves);
        Assert.False(f.Menus.HasOpenMenus(f.Dock, f.Operations));
        Assert.Equal(f.Entity, Assert.Single(f.LastMove!.Entities.ToArray()));
        Assert.Equal(f.Snapshot.SessionId, f.Menus.LastGesture.Session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetingOutsideClickConfirmsOnceAndRightClickOnlyCancels(bool cancel)
    {
        var f = new Fixture(); f.Open();
        f.Targeting.Begin(PlayerActionRequest.BeginAttackMoveTargeting([f.Entity]), f.Snapshot.SessionId);
        var button = cancel ? PlatformMouseButton.Right : PlatformMouseButton.Left;
        f.Button(button, true, 800, 450); f.Button(button, false, 800, 450); f.Frame();
        Assert.False(f.Targeting.IsActive);
        Assert.Equal(cancel ? 0 : 1, f.Actions);
        Assert.Equal(0, f.Moves);
        if (!cancel) Assert.Equal(PlayerActionRequestKind.SubmitAttackMove, f.LastAction.Kind);
        f.Input.BeginFrame(); f.Frame(); Assert.Equal(cancel ? 0 : 1, f.Actions);
    }

    [Fact]
    public void DisabledControlAndMenuOriginReleaseCannotLeakIntoWorld()
    {
        var f = new Fixture(); f.Open();
        var control = PlayerActionDockInteractionLayout.GetFooterButtonRect(f.Layout, 0);
        f.Button(PlatformMouseButton.Left, true, (int)(control.X + 2), (int)(control.Y + 2)); f.Frame();
        Assert.True(f.Menus.PointerCaptured);
        Assert.False(f.Dock.TryTakeRequest(out _));
        f.Input.BeginFrame(); f.Button(PlatformMouseButton.Left, false, 800, 450); f.Frame();
        Assert.True(f.Menus.PointerCaptured);
        Assert.False(f.Selection.IsDragSelecting);
        Assert.Equal(0, f.Moves); Assert.Equal(0, f.Actions);
        f.Input.BeginFrame(); f.Frame(); Assert.False(f.Menus.PointerCaptured);
    }

    [Fact]
    public void EmptyDockGapClosesButVisibleTabConsumesItsGesture()
    {
        var f = new Fixture(); f.Open();
        var gap = PlayerActionDockInteractionLayout.GetCardsRect(f.Layout);
        Assert.False(f.Dock.ContainsControl(new(gap.X + 2, gap.Y + 2), f.Snapshot, f.Layout));
        f.Button(PlatformMouseButton.Left, true, (int)(gap.X + 2), (int)(gap.Y + 2)); f.Frame();
        Assert.False(f.Menus.HasOpenMenus(f.Dock, f.Operations));
        Assert.False(f.Menus.PointerCaptured);
        f.Input.BeginFrame(); f.Button(PlatformMouseButton.Left, false, 800, 450); f.Frame();
        f.Input.BeginFrame(); var tab = PlayerActionDockInteractionLayout.GetModeButtonRect(f.Layout, 0);
        f.Button(PlatformMouseButton.Left, true, (int)(tab.X + 2), (int)(tab.Y + 2)); f.Frame();
        Assert.Equal(PlayerActionPanelMode.Construction, f.Dock.Mode);
        Assert.True(f.Menus.PointerCaptured);
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("resize")]
    [InlineData("dpi")]
    [InlineData("session")]
    [InlineData("selection")]
    [InlineData("modal")]
    public void LifecycleChangesDismissAndCancelHeldGestures(string transition)
    {
        var f = new Fixture(); f.Open(); var panel = OperationsLayout.Panel(f.Layout);
        f.Button(PlatformMouseButton.Left, true, (int)(panel.X + 4), (int)(panel.Y + 4)); f.Frame();
        f.Input.BeginFrame();
        switch (transition)
        {
            case "focus": f.Input.Apply(PlatformInputEvent.FocusLost()); break;
            case "resize": f.Width += 160; break;
            case "dpi": f.Dpi = 144; break;
            case "session": f.Publish(2); break;
            case "selection": f.Selection.Selection.Clear(); break;
            case "modal": f.Modal = true; break;
        }
        f.Frame(); Assert.False(f.Menus.HasOpenMenus(f.Dock, f.Operations));
        f.Input.BeginFrame(); f.Button(PlatformMouseButton.Left, false, 800, 450); f.Frame();
        Assert.Equal(0, f.Moves); Assert.Equal(0, f.Actions); Assert.False(f.Selection.IsDragSelecting);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapeClosesWholeStackButCannotBypassRealModal(bool modal)
    {
        var f = new Fixture(); f.Open(); f.Key(PlatformKey.Escape, true); f.Key(PlatformKey.Escape, false);
        bool consumed = f.Menus.ConsumeEscape(f.Input, f.Dock, f.Operations, f.Interaction, modal);
        Assert.Equal(!modal, consumed);
        Assert.Equal(modal, f.Menus.HasOpenMenus(f.Dock, f.Operations));
        if (!modal) Assert.False(f.Input.WasKeyPressed(PlatformKey.Escape));
    }

    [Fact]
    public void ContextMenuGapsAndClosedAnimationOwnNoInputAndCannotActivateOnRelease()
    {
        var f = new Fixture(); var menu = new RingMenu(); f.Menus.RegisterContextMenu(menu);
        menu.IsOpen = true;
        f.Button(PlatformMouseButton.Left, true, 800, 450); f.Frame();
        Assert.False(menu.IsOpen); Assert.Equal(1, menu.Dismissals); Assert.False(f.Menus.PointerCaptured);
        menu.IsOpen = true;
        f.Input.BeginFrame(); f.Button(PlatformMouseButton.Left, false, 810, 450); f.Frame();
        Assert.True(menu.IsOpen); Assert.False(f.Menus.PointerCaptured);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlacementOutsideClickRetainsPreviewAndConfirmsOnceOrOnlyCancels(bool cancel)
    {
        var f = new Fixture();
        var placement = new RtsBuildingPlacementController(new(1));
        placement.SelectBuilding(BuildingIds.PowerPlant);
        placement.Update(f.Input, f.Camera, new FlatTerrain(), f.Snapshot, f.Interaction, 1600, 900);
        // Session synchronization clears old intent; select in the current session.
        placement.SelectBuilding(BuildingIds.PowerPlant);
        placement.Update(f.Input, f.Camera, new FlatTerrain(), f.Snapshot, f.Interaction, 1600, 900);
        var query = f.Interaction.Capture().PlacementRequest!.Value;
        var preview = new BuildingPlacementPreview(BuildingIds.PowerPlant, "", "Power Plant", query.RequestedPosition,
            query.Orientation, default, true, default, default);
        var snapshot = new PresentationSnapshot(new(4), TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(1),
            placementPreview: new(query.RequestId, new(4), preview));
        f.Open();
        var button = cancel ? PlatformMouseButton.Right : PlatformMouseButton.Left;
        f.Button(button, true, 800, 450); f.Button(button, false, 800, 450);
        f.Menus.Update(f.Input, snapshot, f.Layout, f.Selection.Selection, f.Dock, f.Operations, f.Interaction, true, false);
        Assert.False(f.Menus.HasOpenMenus(f.Dock, f.Operations));
        placement.Update(f.Input, f.Camera, new FlatTerrain(), snapshot, f.Interaction, 1600, 900, f.Menus.PointerCaptured);
        Assert.Equal(!cancel, placement.TryTakePlacementRequest(out var request));
        if (!cancel) { Assert.Equal(query.RequestId, request.PreviewRequestId); Assert.Equal(BuildingIds.PowerPlant, request.BuildingId); }
        Assert.False(placement.TryTakePlacementRequest(out _));
        Assert.Equal(0, f.Moves);
    }

    [Fact]
    public void BriefMenuPressReleasedOutsideCancelsBeforeActivatingAndDoesNotMove()
    {
        var f = new Fixture(); var tab = PlayerActionDockInteractionLayout.GetModeButtonRect(f.Layout, 0);
        f.Button(PlatformMouseButton.Left, true, (int)(tab.X + 2), (int)(tab.Y + 2));
        f.Button(PlatformMouseButton.Left, false, 800, 450); f.Frame();
        Assert.True(f.Menus.PointerCaptured);
        Assert.Equal(PlayerActionPanelMode.Closed, f.Dock.Mode);
        Assert.False(f.Dock.TryTakeRequest(out _)); Assert.Equal(0, f.Moves);
    }

    [Fact]
    public void RepeatedWorldGesturesRemainDistinctAndButtonChangesNeverDuplicateOrders()
    {
        var f = new Fixture();
        for (int i = 0; i < 2; i++)
        {
            f.Open(); f.Button(PlatformMouseButton.Right, true, 800, 450); f.Frame();
            f.Input.BeginFrame(); f.Button(PlatformMouseButton.Right, false, 800, 450); f.Frame(); f.Input.BeginFrame();
        }
        Assert.Equal(2, f.Moves);
        f.Open(); f.Button(PlatformMouseButton.Left, true, 800, 450); f.Frame();
        f.Input.BeginFrame(); f.Button(PlatformMouseButton.Right, true, 800, 450); f.Frame();
        Assert.Equal(3, f.Moves);
        f.Input.BeginFrame(); f.Frame(); Assert.Equal(3, f.Moves);
    }

    [Fact]
    public void PersistentHudAndOnboardingAreNotDismissedByTheCoordinator()
    {
        var f = new Fixture(); f.Open();
        var bar = f.Layout.TopStatusBar;
        f.Button(PlatformMouseButton.Left, true, (int)(bar.X + 2), (int)(bar.Y + 2)); f.Frame();
        Assert.True(f.Menus.PointerCaptured); Assert.True(f.Dock.Mode != PlayerActionPanelMode.Closed);
        f.Input.BeginFrame(); f.Button(PlatformMouseButton.Left, false, 800, 450); f.Frame();
        f.Input.BeginFrame(); var guidance = GameplayGuidanceLayout.Resolve(f.Layout);
        f.Button(PlatformMouseButton.Left, true, (int)(guidance.X + 2), (int)(guidance.Y + 2));
        f.Menus.Update(f.Input, f.Snapshot, f.Layout, f.Selection.Selection, f.Dock, f.Operations, f.Interaction, true, true);
        Assert.True(f.Menus.PointerCaptured);
    }

    [Fact]
    public void SelectionRevisionTracksRealIdentityChangesWithoutPerFrameEnumeration()
    {
        var selection = new SelectionSet(); var entity = new EntityId(1, 1);
        selection.Clear(); Assert.Equal(0ul, selection.Revision);
        selection.SetSingle(entity); var revision = selection.Revision;
        selection.SetSingle(entity); selection.Replace([entity]); selection.Add(entity);
        Assert.Equal(revision, selection.Revision);
        selection.SetSingle(new(1, 2)); Assert.True(selection.Revision > revision);
        revision = selection.Revision; selection.Clear(); Assert.True(selection.Revision > revision);
    }

    [Fact]
    public void WarmMenuRoutingAllocatesNoManagedMemory()
    {
        var f = new Fixture(); f.Open();
        for (int i = 0; i < 100; i++) f.Menus.Update(f.Input, f.Snapshot, f.Layout, f.Selection.Selection,
            f.Dock, f.Operations, f.Interaction, true, false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) f.Menus.Update(f.Input, f.Snapshot, f.Layout, f.Selection.Selection,
            f.Dock, f.Operations, f.Interaction, true, false);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void MissingCurrentTargetingSourceCancelsWithoutSubmittingAStaleEntity()
    {
        var f = new Fixture(); f.Targeting.Begin(PlayerActionRequest.BeginAttackMoveTargeting([f.Entity]), new(1));
        var tactical = new PlayerTacticalActionReadModel([], 0, 0, 0, 0, 0, false, false, default, default, [], []);
        var actions = new PlayerActionSnapshot(new(1), new(2), [], 0, null, null, tactical: tactical);
        var snapshot = new PresentationSnapshot(new(2), TimeSpan.FromMilliseconds(100), 0, [], sessionId: new(1), playerActions: actions);
        f.Button(PlatformMouseButton.Left, true, 800, 450);
        f.Targeting.Update(f.Input, f.Camera, new FlatTerrain(), snapshot, 1600, 900, FormationTemplate.Compact);
        Assert.False(f.Targeting.IsActive); Assert.False(f.Targeting.TryTakeRequest(out _));
    }

    private sealed class RingMenu : ITransientHudMenu
    {
        public bool IsOpen { get; set; }
        public int Dismissals { get; private set; }
        public bool ContainsControl(Vector2 point, in GameplayHudLayout layout) => point.X is >= 810 and <= 830 && point.Y is >= 440 and <= 460;
        public void Dismiss(InputState input) { IsOpen = false; Dismissals++; }
    }

    private sealed class Fixture
    {
        public readonly InputState Input = new();
        public readonly TransientHudMenuController Menus = new();
        public readonly PlayerActionPanelController Dock = new();
        public readonly OperationsController Operations = new();
        public readonly PresentationInteractionState Interaction = new();
        public readonly RtsSelectionController Selection = new(new SelectionFilter(new(1), ControllableEntityCategory.Unit));
        public readonly RtsTacticalTargetingController Targeting = new();
        public readonly RtsCamera Camera = new(new RtsCameraSettings { EdgeScrollEnabled = false });
        public readonly RenderWorld World = new();
        public readonly EntityId Entity = new(1, 1);
        public int Width = 1600;
        public uint Dpi = 96;
        public bool Modal;
        public int Moves, Actions;
        public MovementOrderRequest? LastMove;
        public PlayerActionRequest LastAction;
        public PresentationSnapshot Snapshot = null!;
        public GameplayHudLayout Layout => GameplayHudLayout.Create(Width, 900, Dpi);
        public Fixture()
        {
            Publish(1); Input.Apply(PlatformInputEvent.PointerMoved(800, 450)); Frame();
            Selection.Selection.SetSingle(Entity); Frame(); Input.BeginFrame();
        }
        public void Publish(ulong session)
        {
            var instance = new RenderInstance(Entity, new RenderTransform(Vector3.Zero, Quaternion.Identity, new Vector3(8)),
                new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World, 1,
                new SelectablePresentationMetadata(new PlayerId(1), ControllableEntityCategory.Unit));
            Snapshot = new(new(1), TimeSpan.FromMilliseconds(50), 1, [instance], sessionId: new(session),
                playerExperience: default(PlayerExperienceSnapshot));
            var buffer = new PresentationSnapshotBuffer(); buffer.Publish(Snapshot); World.Update(buffer);
        }
        public void Open()
        {
            Dock.OpenOperationsControls(PlayerActionPanelMode.Production);
            Operations.OpenCategory(OperationsCategory.All, Snapshot.SessionId, Interaction);
        }
        public void Frame()
        {
            Menus.Update(Input, Snapshot, Layout, Selection.Selection, Dock, Operations, Interaction, true, false, Modal);
            if (!Modal) Dock.Update(Input, Snapshot, Width, 900, Dpi, currentSelection: Selection.Selection);
            var op = Operations.Update(Input, Snapshot, Layout, Interaction, blocked: Modal);
            bool captured = Menus.PointerCaptured || Dock.PointerCaptured || op.Captured;
            bool targeting = Targeting.IsActive;
            Targeting.Update(Input, Camera, new FlatTerrain(), Snapshot, Width, 900, FormationTemplate.Compact, captured);
            if (Targeting.TryTakeRequest(out var action)) { Actions++; LastAction = action; }
            Selection.Update(Input, Camera, World, new FlatTerrain(), Width, 900, 1, captured || targeting);
            if (Selection.TryTakeMovementRequest(out var move)) { Moves++; LastMove = move; }
        }
        public void Key(PlatformKey key, bool down) => Input.Apply(PlatformInputEvent.KeyChanged(down ? PlatformInputEventKind.KeyDown : PlatformInputEventKind.KeyUp, key));
        public void Button(PlatformMouseButton button, bool down, int x, int y) => Input.Apply(PlatformInputEvent.MouseButtonChanged(down ? PlatformInputEventKind.MouseButtonDown : PlatformInputEventKind.MouseButtonUp, button, x, y));
    }
    private sealed class FlatTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => new(new Vector3(-1000), new Vector3(1000));
        public bool TrySampleHeight(float x, float z, out float height) { height = 0; return true; }
        public bool TrySampleNormal(float x, float z, out Vector3 normal) { normal = Vector3.UnitY; return true; }
    }
}
