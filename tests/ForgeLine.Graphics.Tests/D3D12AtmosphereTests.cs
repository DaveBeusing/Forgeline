using System.Numerics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class D3D12AtmosphereTests
{
    [Theory]
    [InlineData(true, AtmosphereQuality.Distance)]
    [InlineData(false, AtmosphereQuality.Distance)]
    [InlineData(true, AtmosphereQuality.Height)]
    [InlineData(false, AtmosphereQuality.Height)]
    public void DepthExtinctionMatchesAnalyticDistanceAndPreservesClearAndOverlay(bool warp, AtmosphereQuality quality)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Atmosphere Depth Test", 320, 240));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = warp, EnableDebugLayer = false, EnableVSync = false });
        Assert.Throws<InvalidOperationException>(() => graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality = quality }));
        graphics.ConfigureSceneOutput(new(true, 1, false));
        var settings = GraphicsAtmosphereSettings.Default with
        { Quality = quality, Color = Vector3.One, Density = 0.005f, StartDistance = 0, HeightFalloff = 0, ZoomCompensation = 0 };
        graphics.ConfigureAtmosphere(settings);
        byte[] fog = Capture(true);
        Assert.Equal(fog, Capture(true));
        byte[] missing = Capture(false);
        Assert.False(graphics.Diagnostics.Atmosphere!.Value.Applied);
        Assert.NotNull(graphics.Diagnostics.Atmosphere!.Value.UnavailableReason);
        int center = (120 * 320 + 160) * 4;
        float expectedLinear = 1 - MathF.Exp(-100.5f * 0.005f);
        int expectedByte = (int)MathF.Round((1.055f * MathF.Pow(expectedLinear, 1 / 2.4f) - 0.055f) * 255);
        Assert.InRange(Math.Abs(fog[center] - expectedByte), 0, 1);
        Assert.Equal(0, missing[center]);
        int clear = (120 * 320 + 20) * 4;
        int overlay = (20 * 320 + 280) * 4;
        Assert.Equal(missing.AsSpan(clear, 4).ToArray(), fog.AsSpan(clear, 4).ToArray());
        Assert.Equal(missing.AsSpan(overlay, 4).ToArray(), fog.AsSpan(overlay, 4).ToArray());
        graphics.ConfigureAtmosphere(settings with { Density = 0 });
        Assert.Equal(missing, Capture(true));
        Assert.Equal(4, graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        Assert.Throws<InvalidOperationException>(() => graphics.ConfigureSceneOutput(new(false, 1, false)));
        graphics.Resize(0, 0);
        ulong submitted = graphics.Diagnostics.Surface.SubmittedFrameCount;
        graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        Assert.Equal(submitted, graphics.Diagnostics.Surface.SubmittedFrameCount);
        graphics.Resize(400, 300); graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        Assert.Equal(400, graphics.Diagnostics.Frame!.Value.Plan.Depth.Width);
        Assert.Equal(4, graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        Assert.False(graphics.Diagnostics.Atmosphere!.Value.Applied);
        graphics.ConfigureAtmosphere(settings with { Quality = AtmosphereQuality.Disabled });
        Assert.Equal(3, graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        graphics.ConfigureSceneOutput(new(false, 1, false));
        Assert.Equal(0, graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);

        byte[] Capture(bool view)
        {
            var compiler = new DxcShaderCompiler();
            var vertex = compiler.Compile("""
                float4 VSMain(uint id : SV_VertexID) : SV_Position {
                    float2 uv = float2((id << 1) & 2, id & 2);
                    return float4(uv*float2(2,-2)+float2(-1,1),0.5,1);
                }
                """, GraphicsShaderStage.Vertex, "VSMain", "AtmosphereDepthFixtureVertex.hlsl");
            using var world = graphics.CreateGraphicsPipeline(new(vertex,
                compiler.Compile("float4 PSMain() : SV_Target0 { return float4(0,0,0,1); }",
                    GraphicsShaderStage.Pixel, "PSMain", "AtmosphereDepthFixturePixel.hlsl"))
            { TargetFormat = GraphicsFrameTargetFormat.Rgba16Float, DepthEnabled = true, CullMode = GraphicsCullMode.None });
            using var hud = graphics.CreateGraphicsPipeline(new(vertex,
                compiler.Compile("float4 PSMain() : SV_Target0 { return float4(0.1,0.8,0.3,1); }",
                    GraphicsShaderStage.Pixel, "PSMain", "AtmosphereOverlayFixturePixel.hlsl"))
            { CullMode = GraphicsCullMode.None });
            return graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
            {
                Assert.Throws<InvalidOperationException>(() => graphics.ConfigureAtmosphere(settings));
                if (view) context.SetAtmosphereView(new(Matrix4x4.Identity, new(0, 0, -100), 420));
                context.SetPipeline(world);
                context.SetScissor(context.Width / 4, 0, context.Width * 3 / 4, context.Height);
                context.Draw(3);
                Assert.Throws<InvalidOperationException>(() => context.SetAtmosphereView(new(Matrix4x4.Identity, Vector3.Zero, 420)));
                context.BeginPass(GraphicsFramePass.Overlay);
                context.SetPipeline(hud);
                context.SetScissor(context.Width * 3 / 4, 0, context.Width, context.Height / 2);
                context.Draw(3);
            });
        }
    }

    [Fact]
    public void FailedRecordingPublishesNoAppliedAtmosphere()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Atmosphere Fault Test", 320, 240));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableDebugLayer = false, EnableVSync = false });
        graphics.ConfigureSceneOutput(new(true, 1, true));
        graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality = AtmosphereQuality.Height });
        Assert.Throws<InvalidOperationException>(() => graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
        {
            context.SetAtmosphereView(new(Matrix4x4.Identity, Vector3.Zero, 420));
            context.BeginPass(GraphicsFramePass.Overlay);
            throw new InvalidOperationException("Controlled atmospheric recording failure.");
        }));
        Assert.False(graphics.Diagnostics.Atmosphere!.Value.Applied);
        Assert.Equal(0UL, graphics.Diagnostics.Atmosphere!.Value.CpuSubmission);
        Assert.Equal(0UL, graphics.Diagnostics.Surface.SubmittedFrameCount);
    }
}
