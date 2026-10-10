using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class D3D12SceneCompositionTests
{
    [Fact]
    public void SceneConfigurationAndPipelineFormatsAreGuardedDuringRecording()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Scene Guard Test", 320, 240));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        {
            EnableDebugLayer = false, EnableVSync = false, ForceSoftwareAdapter = true
        });
        graphics.ConfigureSceneOutput(new(true, 1.15f, true));
        var compiler = new DxcShaderCompiler();
        using IGraphicsPipeline overlay = graphics.CreateGraphicsPipeline(new(
            compiler.Compile("float4 VSMain(uint id : SV_VertexID) : SV_Position { return float4(0,0,0,1); }", GraphicsShaderStage.Vertex, "VSMain", "SceneGuardVertex.hlsl"),
            compiler.Compile("float4 PSMain() : SV_Target0 { return float4(1,1,1,1); }", GraphicsShaderStage.Pixel, "PSMain", "SceneGuardPixel.hlsl")));
        Assert.Throws<NotSupportedException>(() => graphics.CreateGraphicsPipeline(overlay.Description with
        {
            TargetFormat = GraphicsFrameTargetFormat.Rgba16Float, AlphaBlendEnabled = true
        }));
        graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
        {
            Assert.Throws<ArgumentException>(() => context.SetPipeline(overlay));
            Assert.Throws<InvalidOperationException>(() => graphics.ConfigureSceneOutput(new(false, 1, true)));
            context.BeginPass(GraphicsFramePass.Overlay);
            context.SetPipeline(overlay);
        });
        graphics.WaitForIdle();
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => graphics.ConfigureSceneOutput(new(true, float.NaN, true)));
        Assert.True(graphics.SceneOutput.Enabled);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void SceneCompositionMatchesLegacyTextureTransferAndPreservesOverlay(bool warp, bool aces)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Scene Color Test", 320, 240));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        {
            ForceSoftwareAdapter = warp, EnableDebugLayer = false, EnableVSync = false
        });
        Assert.Equal(warp, graphics.Diagnostics.Device.IsSoftwareAdapter);
        using IGraphicsTexture albedo = graphics.CreateTexture(new GraphicsTextureData(
            new(1, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Srgb, 1),
            [new(1, 1, 4, [128, 128, 128, 255])]));
        using IGraphicsTexture orm = graphics.CreateTexture(new GraphicsTextureData(
            new(1, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1),
            [new(1, 1, 4, [128, 128, 128, 255])]));
        foreach (float exposure in new[] { 0.25f, 1.15f, 4.0f })
        {
            graphics.ConfigureSceneOutput(new(false, exposure, aces));
            byte[] legacy = Capture(graphics, albedo, orm, exposure, aces);
            graphics.ConfigureSceneOutput(new(true, exposure, aces));
            byte[] composed = Capture(graphics, albedo, orm, exposure, aces);
            Assert.Equal(legacy.Length, composed.Length);
            for (int pixel = 0; pixel < legacy.Length; pixel++)
                Assert.InRange(Math.Abs(legacy[pixel] - composed[pixel]), 0, 1);
            GraphicsSurfaceInfo surface = graphics.Diagnostics.Surface;
            int overlayPixel = ((surface.Height / 4) * surface.Width + surface.Width * 7 / 8) * 4;
            Assert.Equal(legacy.AsSpan(overlayPixel, 4).ToArray(), composed.AsSpan(overlayPixel, 4).ToArray());
            int worldPixel = ((surface.Height / 2) * surface.Width + surface.Width * 5 / 8) * 4;
            float linear = MathF.Pow((128 / 255.0f + 0.055f) / 1.055f, 2.4f) * (128 / 255.0f) * 4;
            Assert.InRange(Math.Abs(composed[worldPixel] - OutputByte(linear, exposure, aces)), 0, 1);
            GraphicsFrameDiagnostics frame = graphics.Diagnostics.Frame!.Value;
            Assert.NotNull(frame.Plan.Scene);
            Assert.Equal((long)surface.Width * surface.Height * 8 * surface.BufferCount, frame.Plan.TransientPayloadBytes);
            Assert.NotNull(frame.CompositeCpuMilliseconds);
            Assert.Equal(1, frame.CompositeDrawCalls);
            if (graphics.Diagnostics.GpuTimingAvailable)
                Assert.NotNull(frame.CompositeGpuMilliseconds);
            Assert.Null(frame.IntermediateUnavailableReason);
        }

        graphics.Resize(0, 0);
        ulong submitted = graphics.Diagnostics.Surface.SubmittedFrameCount;
        graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        Assert.Equal(submitted, graphics.Diagnostics.Surface.SubmittedFrameCount);
        graphics.Resize(400, 300);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear, context => context.BeginPass(GraphicsFramePass.Overlay));
        graphics.WaitForIdle();
        Assert.Equal(400, graphics.Diagnostics.Frame!.Value.Plan.Scene!.Value.Width);
        Assert.Equal(graphics.Diagnostics.Surface.BufferCount + 2, graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        graphics.ConfigureSceneOutput(new(false, 1, true));
        Assert.Equal(2, graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        Assert.Null(graphics.Diagnostics.Frame!.Value.Plan.Scene);
        Assert.Equal(0, graphics.Diagnostics.Debug.ErrorCount);
    }

    private static byte[] Capture(D3D12GraphicsDevice graphics, IGraphicsTexture albedo, IGraphicsTexture orm, float exposure, bool aces)
    {
        var compiler = new DxcShaderCompiler();
        GraphicsShaderBytecode vertex = compiler.Compile("""
            cbuffer Constants : register(b0) { float Exposure; float Aces; };
            struct Output { float4 Position : SV_Position; nointerpolation float2 Settings : TEXCOORD0; };
            Output VSMain(uint id : SV_VertexID) {
                Output o; float2 uv = float2((id << 1) & 2, id & 2);
                o.Position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
                o.Settings = float2(Exposure, Aces); return o;
            }
            """, GraphicsShaderStage.Vertex, "VSMain", "ColorContractVertex.hlsl");
        string prefix = graphics.SceneOutput.Enabled ? "#define LINEAR_SCENE 1\n" : "#define LINEAR_SCENE 0\n";
        GraphicsShaderBytecode pixel = compiler.Compile(prefix + """
            Texture2D<float4> Albedo : register(t0); Texture2D<float4> Orm : register(t1);
            float4 PSMain(float4 position : SV_Position, nointerpolation float2 settings : TEXCOORD0) : SV_Target0 {
                float3 value = Albedo.Load(int3(0,0,0)).rgb * Orm.Load(int3(0,0,0)).r * 4 + float3(0,0.15,2);
            #if !LINEAR_SCENE
                value *= settings.x;
                if (settings.y >= 0.5) value = saturate((value * (2.51 * value + 0.03)) / (value * (2.43 * value + 0.59) + 0.14));
                else value = saturate(value);
                value = lerp(1.055 * pow(max(value,0),1.0/2.4) - 0.055, value * 12.92, step(value,0.0031308));
            #endif
                return float4(value,0.5);
            }
            """, GraphicsShaderStage.Pixel, "PSMain", "ColorContractPixel.hlsl");
        using IGraphicsPipeline world = graphics.CreateGraphicsPipeline(new(vertex, pixel)
        {
            TargetFormat = graphics.SceneOutput.Enabled ? GraphicsFrameTargetFormat.Rgba16Float : GraphicsFrameTargetFormat.Rgba8Unorm,
            CullMode = GraphicsCullMode.None, PixelTextureCount = 2, VertexRootConstantCount = 2
        });
        GraphicsShaderBytecode overlayPixel = compiler.Compile(
            "float4 PSMain() : SV_Target0 { return float4(0.1,0.8,0.3,1); }", GraphicsShaderStage.Pixel, "PSMain", "OverlayColorPixel.hlsl");
        using IGraphicsPipeline overlay = graphics.CreateGraphicsPipeline(new(vertex, overlayPixel)
        {
            CullMode = GraphicsCullMode.None, VertexRootConstantCount = 2
        });
        return graphics.CaptureFrame(new GraphicsColor(0.015f, 0.025f, 0.055f, 0.5f), context =>
        {
            context.SetPipeline(world);
            context.SetVertexConstants([exposure, aces ? 1 : 0]);
            context.SetPixelTexture(0, albedo);
            context.SetPixelTexture(1, orm);
            context.SetScissor(context.Width / 2, 0, context.Width * 3 / 4, context.Height);
            context.Draw(3);
            context.BeginPass(GraphicsFramePass.Overlay);
            context.SetPipeline(overlay);
            context.SetVertexConstants([exposure, aces ? 1 : 0]);
            context.SetScissor(context.Width * 3 / 4, 0, context.Width, context.Height / 2);
            context.Draw(3);
        });
    }

    private static int OutputByte(float linear, float exposure, bool aces)
    {
        float value = linear * exposure;
        value = aces ? Math.Clamp((value * (2.51f * value + 0.03f)) / (value * (2.43f * value + 0.59f) + 0.14f), 0, 1)
            : Math.Clamp(value, 0, 1);
        float srgb = value <= 0.0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - 0.055f;
        return (int)MathF.Round(srgb * 255);
    }
}
