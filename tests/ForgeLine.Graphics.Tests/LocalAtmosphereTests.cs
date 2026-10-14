using System.Numerics;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

[Collection("Native postprocessing")]
public sealed class LocalAtmosphereTests
{
    [Fact]
    public void LocalDensityUsesExistingDepthAndPreservesNativeOverlayAcrossDisableResize()
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Local Atmosphere", 320, 240));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        graphics.ConfigureSceneOutput(new(true, 1, false));
        var compiler = new DxcShaderCompiler();
        var vertex = compiler.Compile("float4 VSMain(uint id:SV_VertexID):SV_Position { float2 uv=float2((id<<1)&2,id&2); return float4(uv*float2(2,-2)+float2(-1,1),.5,1); }", GraphicsShaderStage.Vertex, "VSMain", "HazeWorld.hlsl");
        var pixel = compiler.Compile("float4 PSMain():SV_Target0 { return float4(.4,0,0,1); }", GraphicsShaderStage.Pixel, "PSMain", "HazeColor.hlsl");
        var marker = compiler.Compile("float4 PSMain(float4 p:SV_Position):SV_Target0 { if(int(p.x)!=9) discard; return float4(1,0,0,1); }", GraphicsShaderStage.Pixel, "PSMain", "HazeMarker.hlsl");
        using var world = graphics.CreateGraphicsPipeline(new(vertex, pixel) { DepthEnabled = true, CullMode = GraphicsCullMode.None, TargetFormat = GraphicsFrameTargetFormat.Rgba16Float });
        using var overlay = graphics.CreateGraphicsPipeline(new(vertex, marker) { CullMode = GraphicsCullMode.None });
        var atmosphere = GraphicsAtmosphereSettings.Default with { Quality = AtmosphereQuality.Height, Density = 0,
            StartDistance = 0, ZoomCompensation = 0, Color = Vector3.UnitY };
        graphics.ConfigureAtmosphere(atmosphere);
        byte[] baseline = Capture();
        graphics.ConfigureAtmosphere(atmosphere with { LocalHaze = new(Vector3.UnitZ * 5, 10, .01f) });
        byte[] hazed = Capture();
        Assert.Equal(hazed, Capture());
        int center = (120 * 320 + 160) * 4;
        Assert.InRange(hazed[center + 1], 75, 85);
        Assert.True(hazed[center] < baseline[center]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, hazed.AsSpan((120 * 320 + 9) * 4, 4).ToArray());
        var diagnostics = graphics.Diagnostics.Atmosphere!.Value;
        Assert.Equal(0, diagnostics.AdditionalTargetBytes);
        Assert.Equal(1, diagnostics.DepthDescriptors);
        Assert.Equal(3, diagnostics.MaximumLocalDensityEvaluations);
        graphics.ConfigureAtmosphere(atmosphere with { LocalHaze = new(new(100, 0, 0), 10, .01f) });
        Assert.Equal(baseline, Capture());
        graphics.ConfigureAtmosphere(atmosphere);
        Assert.Equal(baseline, Capture());
        graphics.Resize(400, 300);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        Assert.Equal(400 * 300 * 4, Capture().Length);
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsLocalHazeSettings(new(float.NaN, 0, 0), 10, .01f).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphicsLocalHazeSettings(Vector3.Zero, 9, .01f).Validate());

        byte[] Capture() => graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
        {
            context.SetAtmosphereView(new(Matrix4x4.Identity, new(0, 0, 10), 420));
            context.SetPipeline(world); context.Draw(3);
            context.BeginPass(GraphicsFramePass.Overlay);
            context.SetPipeline(overlay); context.Draw(3);
        });
    }
}
