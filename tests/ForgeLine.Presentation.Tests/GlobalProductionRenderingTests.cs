using ForgeLine.Graphics;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;
namespace ForgeLine.Presentation.Tests;
public sealed class GlobalProductionRenderingTests
{
    [Theory]
    [InlineData(false,0)] [InlineData(false,1)] [InlineData(false,2)] [InlineData(true,0)]
    public void QueueAndMaterialPagesRenderWithinBudgetWithoutWarmAllocation(bool collapsed,int detail)
    {
        var snapshot=GlobalProductionTests.Snapshot();
        var layout=GameplayHudLayout.Create(1600,900,96);
        var graphics=new SelectionOverlayRenderingTests.RecordingContext {Width=1600,Height=900};
        var context=new GameplayHudRenderContext(graphics,new RtsCamera(),snapshot,default,RtsInformationLayerView.Empty,
            default,default,default,CombatGroupOverviewView.Empty,default,layout,96,1,null,
            Operations:new OperationsView(true,Session:snapshot.SessionId) {Production=new(Factory:new(1,1),Request:new(90,1),DetailPage:detail,Collapsed:collapsed,Focus:1,Feedback:"")});
        using var device=new RetainedDevice(); using var renderer=new OperationsHudSurface(device);
        for(int i=0;i<128;i++) renderer.Render(context);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<256;i++) renderer.Render(context);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        Assert.InRange(renderer.LastRenderedVertexCount,1,OperationsHudSurface.MaxVertices);
        Assert.Equal(GlobalProductionLayout.Panel(layout,collapsed),renderer.LastVisiblePanelBounds);
    }
    private sealed class RetainedDevice : IGraphicsDevice
    {
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => new Pipeline(description);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) => new Buffer(description);
        public void RenderFrame(GraphicsColor color, Action<IGraphicsCommandContext>? commands = null) { }
        public void Resize(int width, int height) { }
        public void WaitForIdle() { }
        public void Dispose() { }
    }
    private sealed class Pipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    { public GraphicsPipelineDescription Description => description; public void Dispose() { } }
    private sealed class Buffer(GraphicsBufferDescription description) : IGraphicsBuffer
    { public GraphicsBufferDescription Description => description; public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged { } public void Dispose() { } }
}
