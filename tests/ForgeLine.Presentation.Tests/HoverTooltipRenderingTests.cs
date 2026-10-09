using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
using static ForgeLine.Presentation.Tests.SelectionOverlayRenderingTests;

namespace ForgeLine.Presentation.Tests;

public sealed class HoverTooltipRenderingTests
{
    [Theory]
    [InlineData(1600, 900, 96u, .75f)]
    [InlineData(1600, 900, 144u, 1f)]
    [InlineData(1600, 900, 192u, 2f)]
    [InlineData(5120, 2160, 144u, 2f)]
    public void TooltipGeometryClipsAndRetainsBuffersAcrossDisplayProfiles(int width, int height, uint dpi, float scale)
    {
        using var graphics = new RecordingDevice();
        using var surface = new WorldHoverTooltipSurface(graphics);
        var layout = GameplayHudLayout.Create(width, height, dpi, scale);
        var view = new HoverTooltipView(new SimulationSessionId(1), new EntityId(11, 1), default,
            new Vector2(width - 1, height - 1), true, width, height, layout.Scale);
        var context = Context(new RecordingContext { Width = width, Height = height }, HoverTooltipTests.Snapshot(), layout, view);
        surface.Render(context);
        Assert.InRange(surface.LastRenderedVertexCount, 13, WorldHoverTooltipSurface.MaxVertices);
        Assert.False(surface.LastBounds.Contains(view.PointerPosition));
        Assert.All(graphics.Buffer!.Vertices, vertex =>
        {
            Assert.InRange(vertex.Position.X, -1, 1);
            Assert.InRange(vertex.Position.Y, -1, 1);
        });
        Assert.True(graphics.Pipeline!.AlphaBlendEnabled);
        Assert.False(graphics.Pipeline.DepthEnabled);
        Assert.Equal(GraphicsCullMode.None, graphics.Pipeline.CullMode);
        var buffer = graphics.Buffer;
        surface.Render(context);
        Assert.Same(buffer, graphics.Buffer);
        surface.Render(context with { HoverTooltip = default });
        Assert.Equal(0, surface.LastRenderedVertexCount);
        surface.Render(context with { InformationLayer = RtsInformationLayerView.Empty with { IsDragSelecting = true } });
        Assert.Equal(0, surface.LastRenderedVertexCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmWorldAndDockRenderingHaveZeroManagedAllocations(bool dock)
    {
        using var graphics = new RetainedDevice();
        using var surface = new WorldHoverTooltipSurface(graphics);
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var actions = new PlayerActionSnapshot(new SimulationSessionId(1), new SimulationTick(1),
            [new PlayerConstructionActionReadModel(BuildingIds.Extractor, "Mine / Extractor",
                [new PlayerActionResourceAmount(ResourceIds.Steel, "Steel", 25, 10)], true)], 0, null, null);
        var snapshot = HoverTooltipTests.Snapshot(actions: actions);
        var panel = dock ? default(PlayerActionPanelView) with { Mode = PlayerActionPanelMode.Construction } : default;
        var view = new HoverTooltipView(snapshot.SessionId, new EntityId(11, 1), default, new Vector2(800, 450),
            true, 1600, 900, 1, panel.Mode, dock ? (int)PlayerActionDockControlKind.Item : 0, 0);
        var context = Context(new RecordingContext(), snapshot, layout, view) with { ActionPanel = panel };
        for (int i = 0; i < 128; i++) surface.Render(context);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) surface.Render(context);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(0, bytes);
        Assert.InRange(surface.LastRenderedVertexCount, 13, WorldHoverTooltipSurface.MaxVertices);
        Assert.Equal(1, graphics.BufferCount);
    }

    private static GameplayHudRenderContext Context(IGraphicsCommandContext graphics, PresentationSnapshot snapshot,
        GameplayHudLayout layout, HoverTooltipView view) =>
        new(graphics, new RtsCamera(), snapshot, new AxisAlignedBounds(new Vector3(-1000), new Vector3(1000)),
            RtsInformationLayerView.Empty, default, default, FormationTemplate.Compact, CombatGroupOverviewView.Empty,
            default, layout, 96, 1, null, HoverTooltip: view);

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
