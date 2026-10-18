using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests.RenderParallelization;

public sealed class SceneClearOptimizationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommonSceneClearHasNoWarningsAcrossBuffersAndScaleRecreation(bool requireDebugLayer)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Scene Clear", 320, 240));
        using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        {
            ForceSoftwareAdapter = true, EnableDebugLayer = requireDebugLayer, EnableVSync = false, BufferCount = 4
        });
        Assert.SkipWhen(requireDebugLayer && !graphics.Diagnostics.Debug.Enabled, "D3D12 debug layer is unavailable.");
        foreach (float scale in new[] { 1f, .5f, 1f })
        {
            graphics.ConfigureSceneOutput(new(true, 1, true, scale));
            for (int frame = 0; frame < 8; frame++) graphics.RenderFrame(GraphicsColor.ForgeLineClear);
            graphics.WaitForIdle();
            GraphicsDiagnostics diagnostics = graphics.Diagnostics;
            Assert.Equal(0, diagnostics.Debug.WarningCount);
            Assert.Equal(0, diagnostics.Debug.ErrorCount);
            Assert.Equal(0, diagnostics.Health.SubmissionFaultCount);
        }
    }
}
