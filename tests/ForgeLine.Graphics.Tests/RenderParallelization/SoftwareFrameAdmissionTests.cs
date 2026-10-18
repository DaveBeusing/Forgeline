using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests.RenderParallelization;

public sealed class SoftwareFrameAdmissionTests
{
    [Fact]
    public void NextSoftwareFrameCollectsPriorDisposedResourcesAndPreservesEscapedContextGuards()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Software Admission", 320, 240));
        using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new GraphicsConfiguration { ForceSoftwareAdapter = true, EnableDebugLayer = false, EnableVSync = false, BufferCount = 4 });
        IGraphicsCommandContext? escaped = null;
        for (int frame = 0; frame < 12; frame++)
        {
            graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
            {
                Assert.Equal(0, graphics.Diagnostics.Health.PendingRetirementCount);
                using IGraphicsBuffer vertex = graphics.CreateBuffer(new(48, GraphicsBufferMemory.Upload));
                vertex.SetData<float>([0, 0, 0]);
                context.SetVertexBuffer(vertex, 12);
                vertex.Dispose();
                Assert.Equal(1, graphics.Diagnostics.Health.PendingRetirementCount);
                escaped = context;
            });
            Assert.Throws<ObjectDisposedException>(() => escaped!.Draw(3));
            Assert.True(graphics.Diagnostics.Frame!.Value.FrameWaitMilliseconds >= 0);
            Assert.True(graphics.Diagnostics.Frame!.Value.PresentMilliseconds >= 0);
        }
        graphics.WaitForIdle();
        Assert.Equal(0, graphics.Diagnostics.Health.PendingRetirementCount);
        Assert.Equal(0, graphics.Diagnostics.Health.LiveResourceCount);
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
    }
}
