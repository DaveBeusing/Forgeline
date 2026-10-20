using System.Numerics;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests.SceneComposition;

[Collection("Native postprocessing")]
public sealed class CompositionProductNativeTests
{
    [Theory]
    [InlineData(.5f)]
    [InlineData(.75f)]
    [InlineData(1f)]
    public void AuthoredSentinelAndFullResolutionOverlayBypassFogToneGradeAndWorldScaling(float scale)
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Composition Sentinel Contracts", 321, 241));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        graphics.ConfigureSceneOutput(new(true, 1, false, scale));
        var compiler = new DxcShaderCompiler();
        var worldVertex = compiler.Compile(CompositionShaderNativeTests.ProbeVertex.Replace(".5,1", ".2,1", StringComparison.Ordinal),
            GraphicsShaderStage.Vertex, "VSMain", "SentinelWorldVertex.hlsl");
        var overlayVertex = compiler.Compile(CompositionShaderNativeTests.ProbeVertex,
            GraphicsShaderStage.Vertex, "VSMain", "SentinelOverlayVertex.hlsl");
        var sentinelPixel = compiler.Compile("float4 PSMain():SV_Target0 { return float4(.125,.25,.625,-1.25); }",
            GraphicsShaderStage.Pixel, "PSMain", "SentinelWorldPixel.hlsl");
        var coveredPixel = compiler.Compile("float4 PSMain():SV_Target0 { return float4(0,1,0,1); }",
            GraphicsShaderStage.Pixel, "PSMain", "SentinelCoveredPixel.hlsl");
        var markerPixel = compiler.Compile("float4 PSMain(float4 p:SV_Position):SV_Target0 { if(int(p.x)!=9 && int(p.y)!=20) discard; return float4(1,0,0,1); }",
            GraphicsShaderStage.Pixel, "PSMain", "SentinelMarkerPixel.hlsl");
        using var world = graphics.CreateGraphicsPipeline(new(worldVertex, sentinelPixel)
        {
            CullMode = GraphicsCullMode.None,
            DepthEnabled = true,
            TargetFormat = GraphicsFrameTargetFormat.Rgba16Float
        });
        using var covered = graphics.CreateGraphicsPipeline(new(overlayVertex, coveredPixel)
        {
            CullMode = GraphicsCullMode.None,
            DepthEnabled = true
        });
        using var marker = graphics.CreateGraphicsPipeline(new(overlayVertex, markerPixel)
        {
            CullMode = GraphicsCullMode.None
        });

        byte[] neutral = Capture();
        graphics.ConfigureSceneOutput(new(true, 4, true, scale));
        graphics.ConfigurePostProcessing(new()
        {
            Antialiasing = SpatialAntialiasing.High,
            WhiteBalance = .1f,
            Tint = -.1f,
            Contrast = 1.08f,
            Saturation = 1.05f
        });
        graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with
        {
            Quality = AtmosphereQuality.Height,
            Color = Vector3.UnitY,
            Density = .01f,
            StartDistance = 0,
            MaximumOpacity = .65f,
            ZoomCompensation = 0,
            LocalHaze = new(Vector3.UnitZ * 5, 10, .01f)
        });
        byte[] transformed = Capture();
        Assert.Equal(neutral, transformed);
        Assert.Equal(transformed, Capture());
        int width = graphics.Diagnostics.Surface.Width;
        int height = graphics.Diagnostics.Surface.Height;
        for (int y = 0; y < height; y++)
        {
            AssertPixel(transformed, width, 9, y, 255, 0, 0, 255);
        }
        for (int x = 0; x < width; x++)
        {
            AssertPixel(transformed, width, x, 20, 255, 0, 0, 255);
        }
        AssertPixel(transformed, width, 8, 21, 32, 64, 159, 64);
        AssertPixel(transformed, width, 10, 21, 32, 64, 159, 64);
        AssertPixel(transformed, width, width - 1, height - 1, 32, 64, 159, 64);

        byte[] clear = graphics.CaptureFrame(new(.125f, .25f, .625f, .25f), context =>
            context.SetAtmosphereView(new(Matrix4x4.Identity, Vector3.UnitZ * 10, 420)));
        AssertPixel(clear, width, width / 2, height / 2, 32, 64, 159, 64);
        AssertHealthy(graphics);

        byte[] Capture() => graphics.CaptureFrame(new(0, 0, 0, 1), context =>
        {
            if (graphics.Atmosphere.Enabled)
                context.SetAtmosphereView(new(Matrix4x4.Identity, Vector3.UnitZ * 10, 420));
            context.SetPipeline(world);
            context.Draw(3);
            context.BeginPass(GraphicsFramePass.Overlay);
            Assert.Equal(graphics.Diagnostics.Surface.Width, context.Width);
            Assert.Equal(graphics.Diagnostics.Surface.Height, context.Height);
            context.SetPipeline(covered);
            context.Draw(3);
            context.SetPipeline(marker);
            context.Draw(3);
        });
    }

    [Theory]
    [InlineData(.5f, false)]
    [InlineData(.5f, true)]
    [InlineData(.75f, false)]
    [InlineData(.75f, true)]
    public void ScaledDiagonalThinSurfacesDoNotAcquireColorsFromDifferentDepths(float scale, bool atmosphere)
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Composition Depth Contracts", 321, 241));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        graphics.ConfigureSceneOutput(new(true, 1, false, scale));
        graphics.ConfigurePostProcessing(new() { Antialiasing = SpatialAntialiasing.High });
        if (atmosphere)
        {
            graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with
            {
                Quality = AtmosphereQuality.Distance,
                Color = Vector3.UnitY,
                Density = .001f,
                StartDistance = 0,
                MaximumOpacity = .65f,
                ZoomCompensation = 0
            });
        }
        var compiler = new DxcShaderCompiler();
        var vertex = compiler.Compile(CompositionShaderNativeTests.ProbeVertex,
            GraphicsShaderStage.Vertex, "VSMain", "ThinSurfaceVertex.hlsl");
        var pixel = compiler.Compile("""
            struct Result { float4 Color:SV_Target0; float DepthValue:SV_Depth; };
            Result PSMain(float4 position:SV_Position) {
                bool nearSurface=int(position.x)==int(position.y);
                Result result;
                result.Color=float4(nearSurface ? float3(1,0,0) : float3(0,0,1),1);
                result.DepthValue=nearSurface ? .2 : .8;
                return result;
            }
            """, GraphicsShaderStage.Pixel, "PSMain", "ThinSurfacePixel.hlsl");
        using var world = graphics.CreateGraphicsPipeline(new(vertex, pixel)
        {
            CullMode = GraphicsCullMode.None,
            DepthEnabled = true,
            TargetFormat = GraphicsFrameTargetFormat.Rgba16Float
        });
        byte[] image = Capture();
        Assert.Equal(image, Capture());
        int redSurfaces = 0;
        int blueSurfaces = 0;
        for (int offset = 0; offset < image.Length; offset += 4)
        {
            bool red = image[offset] > 0;
            bool blue = image[offset + 2] > 0;
            Assert.True(red != blue, $"Cross-depth color at pixel {offset / 4}: {image[offset]},{image[offset + 1]},{image[offset + 2]}.");
            if (red) redSurfaces++;
            else blueSurfaces++;
            Assert.Equal(255, image[offset + 3]);
            if (!atmosphere) Assert.Equal(0, image[offset + 1]);
        }
        Assert.True(redSurfaces > 0);
        Assert.True(blueSurfaces > redSurfaces);
        AssertHealthy(graphics);

        byte[] Capture() => graphics.CaptureFrame(new(0, 0, 0, 1), context =>
        {
            if (graphics.Atmosphere.Enabled)
                context.SetAtmosphereView(new(Matrix4x4.CreateScale(1, 1, 1000), Vector3.Zero, 420));
            context.SetPipeline(world);
            context.Draw(3);
        });
    }

    private static void AssertPixel(byte[] image, int width, int x, int y, byte r, byte g, byte b, byte a) =>
        Assert.Equal(new[] { r, g, b, a }, image.AsSpan((y * width + x) * 4, 4).ToArray());

    private static void AssertHealthy(IGraphicsDevice graphics)
    {
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        Assert.Equal(0, graphics.Diagnostics.Resources.TextureBindingFailureCount);
        Assert.Equal(0, graphics.Diagnostics.PostProcessing!.Value.HistoryTargets);
        Assert.Equal(0, graphics.Diagnostics.PostProcessing!.Value.IntermediateTargets);
    }
}
