using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

[Collection("Native postprocessing")]
public sealed class DepthReconstructionTests
{
    [Theory]
    [InlineData(.5f)]
    [InlineData(.75f)]
    public void ScaledDepthEdgesRetainSurfaceColorAndNativeThinOverlay(float scale)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE Depth Reconstruction Test", 320, 240));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        graphics.ConfigureSceneOutput(new(true, 1, false, scale));
        var compiler = new DxcShaderCompiler();
        var vertex = compiler.Compile("float4 VSMain(uint id:SV_VertexID):SV_Position { float2 uv=float2((id<<1)&2,id&2); return float4(uv*float2(2,-2)+float2(-1,1),.5,1); }",
            GraphicsShaderStage.Vertex, "VSMain", "ReconstructionVertex.hlsl");
        var pixel = compiler.Compile("struct Result { float4 color:SV_Target0; float depth:SV_Depth; }; Result PSMain(float4 p:SV_Position) { Result r; bool left=p.x<80; r.color=float4(left?float3(1,0,0):float3(0,0,1),1); r.depth=left?.2:.8; return r; }",
            GraphicsShaderStage.Pixel, "PSMain", "ReconstructionPixel.hlsl");
        using var world = graphics.CreateGraphicsPipeline(new(vertex, pixel) { TargetFormat = GraphicsFrameTargetFormat.Rgba16Float, CullMode = GraphicsCullMode.None, DepthEnabled = true });
        var ui = compiler.Compile("float4 PSMain():SV_Target0 { return float4(0,1,0,1); }", GraphicsShaderStage.Pixel, "PSMain", "ReconstructionOverlay.hlsl");
        using var overlay = graphics.CreateGraphicsPipeline(new(vertex, ui) { CullMode = GraphicsCullMode.None, DepthEnabled = false });
        byte[] image = Capture();
        Assert.Equal(image, Capture());
        int width = graphics.Diagnostics.Surface.Width;
        int boundary = (int)(80 / scale);
        for (int x = boundary - 2; x <= boundary + 2; x++)
        {
            int offset = (100 * width + x) * 4;
            Assert.Equal(0, image[offset + 1]);
            Assert.True(image[offset] >= 254 && image[offset + 2] == 0 || image[offset] == 0 && image[offset + 2] >= 254, $"Mixed surface at x={x}: {image[offset]},{image[offset + 1]},{image[offset + 2]}");
        }
        for (int x = 0; x < width; x++) Assert.Equal(new byte[] { 0, 255, 0, 255 }, image.AsSpan((20 * width + x) * 4, 4).ToArray());
        Assert.Equal(0, graphics.Diagnostics.PostProcessing!.Value.HistoryTargets);
        Assert.True(graphics.Diagnostics.Device.Advanced.MeshShaderQuerySucceeded);
        Assert.True(graphics.Diagnostics.Device.Advanced.RaytracingQuerySucceeded);
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        graphics.Resize(0, 0);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        graphics.Resize(400, 300);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        _ = Capture();
        Assert.Equal(400, graphics.Diagnostics.Surface.Width);
        byte[] Capture() => graphics.CaptureFrame(GraphicsColor.ForgeLineClear, c =>
        {
            c.SetPipeline(world); c.Draw(3);
            c.BeginPass(GraphicsFramePass.Overlay); c.SetPipeline(overlay);
            c.SetScissor(0, 20, c.Width, 21); c.Draw(3);
        });
    }
}
