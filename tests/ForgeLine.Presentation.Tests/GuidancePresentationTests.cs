using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
using static ForgeLine.Presentation.Tests.SelectionOverlayRenderingTests;

namespace ForgeLine.Presentation.Tests;

public sealed class GuidancePresentationTests
{
    public static TheoryData<int, int, uint, float> Displays
    {
        get
        {
            var cases = new TheoryData<int, int, uint, float>();
            foreach (var size in new[] { (1024, 720), (1600, 900), (2560, 1440), (3440, 1440) })
                foreach (uint dpi in new[] { 96u, 144u })
                    foreach (float scale in new[] { .75f, 1f, 2f }) cases.Add(size.Item1, size.Item2, dpi, scale);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Displays))]
    public void CompactGuideFitsBesideAlertsActionsAndSelection(int width, int height, uint dpi, float uiScale)
    {
        using var device = new RecordingDevice();
        using var renderer = new RtsInformationOverlayRenderer(device);
        var context = new RecordingContext { Width = width, Height = height };
        var snapshot = EarlyGameGuidanceTests.Snapshot(PlayerGuidanceMilestone.CommandCore);
        var guide = new EarlyGameGuidanceController().Update(snapshot, true);
        var layout = GameplayHudLayout.Create(width, height, dpi, uiScale);
        renderer.Render(context, new RtsCamera(), snapshot, new AxisAlignedBounds(new Vector3(-1000), new Vector3(1000)),
            RtsInformationLayerView.Empty, CombatGroupOverviewView.Empty, dpi, uiScale, Ux(guide));
        Assert.True(renderer.LastGuidanceVertexCount > 0);
        var panel = renderer.LastGuidanceBounds;
        Assert.True(layout.SafeArea.Contains(new Vector2(panel.X, panel.Y)));
        Assert.True(layout.SafeArea.Contains(new Vector2(panel.Right, panel.Bottom)));
        foreach (var other in new[] { layout.AlertStack, layout.ActionDock, layout.SelectionInspector, layout.Minimap, layout.TopStatusBar })
            Assert.False(panel.Intersects(other));
        Assert.All(device.Buffers.SelectMany(buffer => buffer.Vertices), vertex =>
        {
            Assert.True(float.IsFinite(vertex.Position.X));
            Assert.InRange(vertex.Position.X, -1f, 1f);
            Assert.InRange(vertex.Position.Y, -1f, 1f);
        });
    }

    [Theory]
    [InlineData(PreAlphaUxMode.Help)]
    [InlineData(PreAlphaUxMode.Paused)]
    [InlineData(PreAlphaUxMode.MatchSetup)]
    public void ModalSurfaceSuppressesGuide(PreAlphaUxMode mode)
    {
        using var device = new RecordingDevice();
        using var renderer = new RtsInformationOverlayRenderer(device);
        var snapshot = EarlyGameGuidanceTests.Snapshot(0);
        renderer.Render(new RecordingContext(), new RtsCamera(), snapshot, default, RtsInformationLayerView.Empty,
            CombatGroupOverviewView.Empty, 96, preAlphaUx: Ux(new EarlyGameGuidanceController().Update(snapshot, true)) with { Mode = mode });
        Assert.Equal(0, renderer.LastGuidanceVertexCount);
    }

    [Fact]
    public void GuideCapturesPressOriginAndSessionReplacementDiscardsRenderedText()
    {
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var rect = GameplayGuidanceLayout.Resolve(layout);
        var input = new InputState();
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,
            PlatformMouseButton.Left, (int)rect.X + 10, (int)rect.Y + 10));
        input.Apply(PlatformInputEvent.PointerMoved(800, 500));
        Assert.True(HudInteractionContext.CapturesWorldPointer(input, layout, true, guidanceVisible: true));
        Assert.False(HudInteractionContext.CapturesWorldPointer(input, layout, true, guidanceVisible: false));
        using var device = new RecordingDevice();
        using var renderer = new RtsInformationOverlayRenderer(device);
        var snapshot = EarlyGameGuidanceTests.Snapshot(0);
        renderer.Render(new RecordingContext(), new RtsCamera(), EarlyGameGuidanceTests.Snapshot(0, session: 2), default,
            RtsInformationLayerView.Empty, CombatGroupOverviewView.Empty, 96,
            preAlphaUx: Ux(new EarlyGameGuidanceController().Update(snapshot, true)));
        Assert.Equal(0, renderer.LastGuidanceVertexCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveGuideAndPlacementWithAmountsHaveZeroWarmAllocations(bool placement)
    {
        using var device = new RetainedDevice();
        using var renderer = new RtsInformationOverlayRenderer(device);
        var snapshot = EarlyGameGuidanceTests.Snapshot(PlayerGuidanceMilestone.CommandCore);
        var guide = new EarlyGameGuidanceController().Update(snapshot, true);
        var costs = new[] { new PlayerActionResourceAmount(ResourceIds.Steel, "Steel", 250, 80) };
        var ux = Ux(guide) with
        {
            Placement = placement ? new(PlacementContextState.MissingMaterials, "Vehicle Factory",
            "MISSING CORE MATERIALS", "REPLENISH COMMAND CORE CONSTRUCTION STOCK", costs, snapshot.SessionId, snapshot.Tick) : default
        };
        var context = new RecordingContext();
        var camera = new RtsCamera();
        var controller = new EarlyGameGuidanceController();
        for (int i = 0; i < 128; i++) renderer.Render(context, camera, snapshot, default,
            RtsInformationLayerView.Empty, CombatGroupOverviewView.Empty, 96, preAlphaUx: ux);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            ux = ux with { Guidance = controller.Update(snapshot, true) };
            renderer.Render(context, camera, snapshot, default,
                RtsInformationLayerView.Empty, CombatGroupOverviewView.Empty, 96, preAlphaUx: ux);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        Assert.True(renderer.LastGuidanceVertexCount > 0);
        Assert.Equal(1, device.BufferCount);
    }

    private static PreAlphaUxView Ux(EarlyGameGuidanceView guide) => default(PreAlphaUxView) with { ShowOnboarding = true, Guidance = guide };

    private sealed class RetainedDevice : IGraphicsDevice
    {
        public int BufferCount { get; private set; }
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => new Pipeline(description);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) { BufferCount++; return new Buffer(description); }
        public void RenderFrame(GraphicsColor color, Action<IGraphicsCommandContext>? commands = null) { }
        public void Resize(int width, int height) { }
        public void WaitForIdle() { }
        public void Dispose() { }
    }
    private sealed class Pipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    {
        public GraphicsPipelineDescription Description => description;
        public void Dispose() { }
    }
    private sealed class Buffer(GraphicsBufferDescription description) : IGraphicsBuffer
    {
        public GraphicsBufferDescription Description => description;
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged { }
        public void Dispose() { }
    }
}
