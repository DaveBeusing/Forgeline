using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GameplayInteractionJourneyTests
{
    [Theory]
    [InlineData(1024, 768, 96, ControllableEntityCategory.Unit)]
    [InlineData(1280, 720, 120, ControllableEntityCategory.Building)]
    [InlineData(1600, 900, 144, ControllableEntityCategory.Unit)]
    [InlineData(1920, 1080, 192, ControllableEntityCategory.Building)]
    [InlineData(2560, 1440, 96, ControllableEntityCategory.Building)]
    [InlineData(3440, 1440, 120, ControllableEntityCategory.Unit)]
    [InlineData(3840, 2160, 144, ControllableEntityCategory.Building)]
    [InlineData(5120, 2160, 192, ControllableEntityCategory.Unit)]
    public void LoadedWorldSupportsOrdersHelpFocusResizeAndSessionReplacement(
        int width, int height, uint dpi, ControllableEntityCategory category)
    {
        var journey = new Journey(width, height, dpi, category);
        Assert.Equal(GameplayHudState.WaitingForSnapshot, GameplayHudRenderer.ResolveState(null));
        journey.Publish(1);
        journey.Frame();
        Assert.Equal(GameplayHudState.Ready, GameplayHudRenderer.ResolveState(journey.World.CurrentSnapshot));

        // Compare camera motion in its projected axes before returning to the selection fixture.
        journey.Input.BeginFrame();
        Key(journey.Input, PlatformKey.D, true);
        var target = journey.Camera.Target;
        journey.Frame();
        Assert.True(Vector3.Dot(journey.Camera.Target - target, journey.Camera.GroundRight) > 0);
        Key(journey.Input, PlatformKey.D, false);
        journey.Input.BeginFrame();
        Button(journey.Input, PlatformMouseButton.Middle, true, width / 2, height / 2);
        journey.Frame();
        journey.Input.BeginFrame();
        target = journey.Camera.Target;
        journey.Input.Apply(PlatformInputEvent.PointerMoved(width / 2 + 10, height / 2));
        journey.Frame();
        Assert.True(Vector3.Dot(journey.Camera.Target - target, journey.Camera.GroundRight) < 0);
        Button(journey.Input, PlatformMouseButton.Middle, false, width / 2, height / 2);
        journey.Input.BeginFrame();
        journey.Input.Apply(PlatformInputEvent.MouseWheel(width / 2, height / 2, 12000));
        journey.Frame();
        Assert.Equal(journey.Camera.Settings.MinimumDistance, journey.Camera.Distance);
        journey.Input.BeginFrame();
        journey.Input.Apply(PlatformInputEvent.MouseWheel(width / 2, height / 2, -12000));
        journey.Frame();
        Assert.Equal(journey.Camera.Settings.MaximumDistance, journey.Camera.Distance);
        journey.Camera.CenterOn(Vector3.Zero);
        journey.Click(PlatformMouseButton.Left, width / 2, height / 2);
        Assert.True(journey.Selection.Selection.Contains(journey.Entity));

        journey.Input.BeginFrame();
        Button(journey.Input, PlatformMouseButton.Left, true, width / 2 - 40, height / 2 - 40);
        journey.Frame();
        journey.Input.BeginFrame();
        journey.Input.Apply(PlatformInputEvent.PointerMoved(width / 2 + 40, height / 2 + 40));
        journey.Frame();
        Assert.True(journey.Selection.IsDragSelecting);
        journey.Input.BeginFrame();
        Button(journey.Input, PlatformMouseButton.Left, false, width / 2 + 40, height / 2 + 40);
        journey.Frame();
        Assert.False(journey.Selection.IsDragSelecting);
        Assert.True(journey.Selection.Selection.Contains(journey.Entity));
        journey.Click(PlatformMouseButton.Right, width / 2 + 25, height / 2);
        Assert.True(journey.Selection.TryTakeMovementRequest(out var order));
        Assert.True(order.Entities.Contains(journey.Entity));
        Assert.True(journey.Selection.CommandFeedback.IsVisible);
        Assert.False(journey.Selection.TryTakeMovementRequest(out _));

        journey.Input.BeginFrame();
        Key(journey.Input, PlatformKey.D, true);
        Button(journey.Input, PlatformMouseButton.Right, true, width / 2, height / 2);
        Key(journey.Input, PlatformKey.F1, true);
        target = journey.Camera.Target;
        journey.Frame();
        Assert.True(journey.Help.Visible);
        Assert.Equal(target, journey.Camera.Target);
        Assert.False(journey.Selection.CommandFeedback.IsVisible);
        Assert.False(journey.Selection.TryTakeMovementRequest(out _));
        journey.Input.BeginFrame();
        Key(journey.Input, PlatformKey.Escape, true);
        journey.Frame();
        Assert.False(journey.Help.Visible);
        Assert.True(journey.Help.BlocksGameplayThisFrame);
        journey.Input.BeginFrame();
        journey.Frame();
        Assert.Equal(target, journey.Camera.Target);
        Assert.True(journey.Selection.Selection.Contains(journey.Entity));
        // Physical release is necessary before fresh input may resume after the modal.
        Key(journey.Input, PlatformKey.D, false);
        Key(journey.Input, PlatformKey.F1, false);
        Key(journey.Input, PlatformKey.Escape, false);
        Button(journey.Input, PlatformMouseButton.Right, false, width / 2, height / 2);

        journey.StartDrag();
        journey.Input.BeginFrame();
        journey.Input.Apply(PlatformInputEvent.FocusLost());
        journey.Frame();
        Assert.False(journey.Selection.IsDragSelecting);
        Assert.True(journey.Selection.Selection.Contains(journey.Entity));
        journey.Click(PlatformMouseButton.Left, width / 2, height / 2);
        Assert.True(journey.Selection.Selection.Contains(journey.Entity));
        journey.StartDrag();
        journey.Input.BeginFrame();
        journey.Width += 160;
        journey.Height += 90;
        journey.Frame();
        Assert.False(journey.Selection.IsDragSelecting);
        Button(journey.Input, PlatformMouseButton.Left, false, journey.Width / 2, journey.Height / 2);
        journey.Frame();
        journey.Click(PlatformMouseButton.Left, journey.Width / 2, journey.Height / 2);
        Assert.True(journey.Selection.Selection.Contains(journey.Entity));

        var map = journey.Layout.Minimap;
        int mapX = (int)(map.X + map.Width * 0.75f);
        int mapY = (int)(map.Y + map.Height * 0.5f);
        target = journey.Camera.Target;
        journey.Click(PlatformMouseButton.Left, mapX, mapY);
        Assert.NotEqual(target, journey.Camera.Target);
        Assert.False(journey.Minimap.View.IsCameraDragging);
        Assert.True(journey.Selection.Selection.Contains(journey.Entity));
        journey.Click(PlatformMouseButton.Right, mapX, mapY);
        Assert.True(journey.Minimap.TryTakeMovementRequest(out var mapOrder));
        Assert.True(mapOrder.Entities.Contains(journey.Entity));
        Assert.False(journey.Selection.TryTakeMovementRequest(out _));

        var rates = new RuntimeMetricsSampler();
        var session = journey.World.CurrentSnapshot!.SessionId;
        rates.Sample(TimeSpan.Zero, 0, 0, session, RuntimeSimulationState.Running);
        var running = rates.Sample(TimeSpan.FromSeconds(1), 60, 20, session, RuntimeSimulationState.Running);
        Assert.Equal(60d, running.FramesPerSecond);
        Assert.Equal(20d, running.TicksPerSecond);
        var paused = rates.Sample(TimeSpan.FromSeconds(2), 120, 20, session, RuntimeSimulationState.Paused);
        Assert.Equal(60d, paused.FramesPerSecond);
        Assert.Null(paused.TicksPerSecond);
        rates.Sample(TimeSpan.FromSeconds(3), 180, 20, session, RuntimeSimulationState.Running);
        var loaded = rates.Sample(TimeSpan.FromSeconds(4), 210, 30, session, RuntimeSimulationState.Running);
        Assert.Equal(30d, loaded.FramesPerSecond);
        Assert.Equal(10d, loaded.TicksPerSecond);
        journey.Publish(2);
        journey.Input.BeginFrame();
        journey.Frame();
        Assert.Equal(0, journey.Selection.Selection.Count);
        Assert.False(journey.Selection.TryTakeMovementRequest(out _));
        Assert.False(journey.Minimap.TryTakeMovementRequest(out _));
        var restarted = rates.Sample(TimeSpan.FromSeconds(4.1), 211, 0,
            journey.World.CurrentSnapshot!.SessionId, RuntimeSimulationState.Running);
        Assert.Null(restarted.FramesPerSecond);
        Assert.Null(restarted.TicksPerSecond);
    }

    [Theory]
    [InlineData(96, PlatformMouseButton.Left)]
    [InlineData(120, PlatformMouseButton.Right)]
    [InlineData(144, PlatformMouseButton.Left)]
    [InlineData(192, PlatformMouseButton.Right)]
    public void HudOriginAndWorldReleaseOrWorldOriginAndHudReleaseNeverLeak(uint dpi, PlatformMouseButton button)
    {
        var journey = new Journey(1600, 900, dpi, ControllableEntityCategory.Unit);
        journey.Publish(1);
        journey.Frame();
        journey.Selection.Selection.SetSingle(journey.Entity);
        var bar = journey.Layout.TopStatusBar;
        int hudX = (int)(bar.X + 10), hudY = (int)(bar.Y + 10);
        foreach (bool startsInHud in new[] { true, false })
        {
            journey.Input.BeginFrame();
            Button(journey.Input, button, true, startsInHud ? hudX : 800, startsInHud ? hudY : 450);
            if (!startsInHud)
            {
                journey.Frame();
                journey.Input.BeginFrame();
            }
            Button(journey.Input, button, false, startsInHud ? 800 : hudX, startsInHud ? 450 : hudY);
            journey.Frame();
            Assert.True(journey.Hud.PointerCaptured);
            Assert.False(journey.Selection.IsDragSelecting);
            Assert.False(journey.Selection.TryTakeMovementRequest(out _));
            Assert.True(journey.Selection.Selection.Contains(journey.Entity));
        }
    }

    private static void Key(InputState input, PlatformKey key, bool down) =>
        input.Apply(PlatformInputEvent.KeyChanged(down ? PlatformInputEventKind.KeyDown : PlatformInputEventKind.KeyUp, key));

    private static void Button(InputState input, PlatformMouseButton button, bool down, int x, int y) =>
        input.Apply(PlatformInputEvent.MouseButtonChanged(down ? PlatformInputEventKind.MouseButtonDown : PlatformInputEventKind.MouseButtonUp, button, x, y));

    private sealed class Journey(int width, int height, uint dpi, ControllableEntityCategory category)
    {
        private readonly PresentationSnapshotBuffer _snapshots = new();
        private readonly FlatTerrain _terrain = new();
        private readonly RtsCameraActionMapper _mapper = new();
        public int Width { get; set; } = width;
        public int Height { get; set; } = height;
        public EntityId Entity { get; } = new(1, 1);
        public InputState Input { get; } = new();
        public RtsCamera Camera { get; } = new(new RtsCameraSettings { EdgeScrollEnabled = false });
        public RenderWorld World { get; } = new();
        public RtsSelectionController Selection { get; } = new(new SelectionFilter(new PlayerId(1),
            ControllableEntityCategory.Unit | ControllableEntityCategory.Building));
        public GameplayHelpController Help { get; } = new();
        public HudInteractionContext Hud { get; } = new();
        public RtsMinimapInteractionController Minimap { get; } = new();
        public GameplayHudLayout Layout => GameplayHudLayout.Create(Width, Height, dpi);

        public void Publish(ulong session)
        {
            var instance = new RenderInstance(Entity, new RenderTransform(Vector3.Zero, Quaternion.Identity, new Vector3(8)),
                new RenderMeshHandle(1), RenderMaterialHandle.Default, RenderVisibilityMask.World, 1,
                new SelectablePresentationMetadata(new PlayerId(1), category));
            _snapshots.Publish(new PresentationSnapshot(new SimulationTick(1), TimeSpan.FromMilliseconds(50), 1, [instance],
                sessionId: new SimulationSessionId(session), playerExperience: default(PlayerExperienceSnapshot)));
            Assert.True(World.Update(_snapshots));
        }

        public void Frame()
        {
            bool helpChanged = Help.Update(Input, true);
            if (helpChanged || Input.FocusLostThisFrame) Selection.CancelPointerInteraction();
            Hud.BeginFrame(World.CurrentSnapshot?.SessionId ?? default);
            Hud.CapturePointer(Help.BlocksGameplayThisFrame);
            Hud.CaptureKeyboard(Help.BlocksGameplayThisFrame);
            Minimap.Update(Input, Camera, _terrain, World.CurrentSnapshot, Layout, Selection.Selection.Entities,
                TacticalTargetingMode.None, FormationTemplate.Compact, inputBlocked: Hud.PointerCaptured);
            Hud.CapturePointer(Minimap.PointerCaptured);
            Hud.CapturePointer(HudInteractionContext.CapturesWorldPointer(Input, Layout, true, actionDockExpanded: false));
            Camera.Update(Hud.KeyboardCaptured ? default : _mapper.Map(Input, Hud.PointerCaptured), 1f / 60, Width, Height);
            Selection.Update(Input, Camera, World, _terrain, Width, Height, 1,
                pointerCaptured: Hud.PointerCaptured, pointerScale: Layout.Scale);
        }

        public void Click(PlatformMouseButton button, int x, int y)
        {
            Input.BeginFrame();
            Button(Input, button, true, x, y);
            Button(Input, button, false, x, y);
            Frame();
        }

        public void StartDrag()
        {
            Input.BeginFrame();
            Button(Input, PlatformMouseButton.Left, true, Width / 2 - 40, Height / 2 - 40);
            Frame();
            Input.BeginFrame();
            Input.Apply(PlatformInputEvent.PointerMoved(Width / 2 + 40, Height / 2 + 40));
            Frame();
            Assert.True(Selection.IsDragSelecting);
        }
    }

    private sealed class FlatTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => new(new Vector3(-1000), new Vector3(1000));
        public bool TrySampleHeight(float x, float z, out float height) { height = 0; return true; }
        public bool TrySampleNormal(float x, float z, out Vector3 normal) { normal = Vector3.UnitY; return true; }
    }
}
