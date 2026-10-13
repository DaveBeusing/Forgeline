using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

[Collection("Native postprocessing")]
public sealed class ResolutionScaleTests
{
    [Theory]
    [InlineData(.5f)]
    [InlineData(.75f)]
    [InlineData(1f)]
    public void NativeOverlayAndDepthRemainCorrectAcrossScaleResizeAndRejectedChanges(float scale)
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Resolution Test", 320, 240));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        Assert.True(graphics.SupportsPostProcessing);
        graphics.ConfigureSceneOutput(new(true, 1, false, scale));
        var compiler = new DxcShaderCompiler();
        string vertex = "float4 VSMain(uint id:SV_VertexID):SV_Position { float2 uv=float2((id<<1)&2,id&2); return float4(uv*float2(2,-2)+float2(-1,1),DEPTH,1); }";
        var worldVertex = compiler.Compile(vertex.Replace("DEPTH", "0.25"), GraphicsShaderStage.Vertex, "VSMain", "ScaleWorld.hlsl");
        var overlayVertex = compiler.Compile(vertex.Replace("DEPTH", "0.5"), GraphicsShaderStage.Vertex, "VSMain", "ScaleOverlay.hlsl");
        var worldPixel = compiler.Compile("float4 PSMain():SV_Target0 { return float4(.2,.4,.8,1); }", GraphicsShaderStage.Pixel, "PSMain", "ScaleColor.hlsl");
        var coveredPixel = compiler.Compile("float4 PSMain():SV_Target0 { return float4(0,1,0,1); }", GraphicsShaderStage.Pixel, "PSMain", "ScaleCovered.hlsl");
        var linePixel = compiler.Compile("float4 PSMain(float4 p:SV_Position):SV_Target0 { if(int(p.x)!=9) discard; return float4(1,0,0,1); }", GraphicsShaderStage.Pixel, "PSMain", "ScaleLine.hlsl");
        using var world = graphics.CreateGraphicsPipeline(new(worldVertex, worldPixel)
            { CullMode = GraphicsCullMode.None, DepthEnabled = true, TargetFormat = GraphicsFrameTargetFormat.Rgba16Float });
        using var covered = graphics.CreateGraphicsPipeline(new(overlayVertex, coveredPixel)
            { CullMode = GraphicsCullMode.None, DepthEnabled = true });
        using var line = graphics.CreateGraphicsPipeline(new(overlayVertex, linePixel) { CullMode = GraphicsCullMode.None });

        byte[] pixels = Capture();
        Verify(pixels, 320, 240);
        Assert.Equal(pixels, Capture());
        var previous = graphics.SceneOutput;
        Assert.Throws<ArgumentOutOfRangeException>(() => graphics.ConfigureSceneOutput(new(true, 1, false, float.NaN)));
        Assert.Equal(previous, graphics.SceneOutput);
        Assert.Equal(pixels, Capture());
        graphics.Resize(400, 300);
        graphics.RenderFrame(new(0, 0, 0, 1));
        Verify(Capture(), 400, 300);
        graphics.ConfigureSceneOutput(new(true, 1, false, 1));
        Verify(Capture(), 400, 300);
        graphics.WaitForIdle();
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        Assert.Equal(0, graphics.Diagnostics.Resources.TextureBindingFailureCount);

        byte[] Capture() => graphics.CaptureFrame(new(0, 0, 0, 1), context =>
        {
            int expectedWidth = GraphicsFramePlan.ScaledDimension(graphics.Diagnostics.Surface.Width, graphics.SceneOutput.ResolutionScale);
            Assert.Equal(expectedWidth, context.Width);
            Assert.Throws<InvalidOperationException>(() => graphics.ConfigureSceneOutput(new(true, 1, false, .5f)));
            context.SetPipeline(world); context.Draw(3);
            context.BeginPass(GraphicsFramePass.Overlay);
            Assert.Equal(graphics.Diagnostics.Surface.Width, context.Width);
            context.SetPipeline(covered); context.Draw(3);
            context.SetPipeline(line); context.Draw(3);
        });

        void Verify(byte[] data, int width, int height)
        {
            Assert.Equal(width * height * 4, data.Length);
            int center = ((height / 2) * width + width / 2) * 4;
            Assert.InRange(data[center], 122, 125);
            Assert.InRange(data[center + 1], 168, 171);
            Assert.InRange(data[center + 2], 230, 233);
            int marker = ((height / 2) * width + 9) * 4;
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, data.AsSpan(marker, 4).ToArray());
            Assert.NotEqual(255, data[marker - 4]);
            var plan = graphics.Diagnostics.Frame!.Value.Plan;
            Assert.Equal(GraphicsFramePlan.ScaledDimension(width, graphics.SceneOutput.ResolutionScale), plan.Scene!.Value.Width);
            Assert.Equal(plan.Scene.Value.Width, plan.Depth.Width);
            if (graphics.SceneOutput.ResolutionScale < 1) Assert.Equal(width, plan.OverlayDepth!.Value.Width);
            else Assert.Null(plan.OverlayDepth);
        }
    }
}
