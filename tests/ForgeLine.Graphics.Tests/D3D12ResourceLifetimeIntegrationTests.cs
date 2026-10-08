using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class D3D12ResourceLifetimeIntegrationTests
{
    public static bool GpuTestsEnabled =>
        Environment.GetEnvironmentVariable("FORGELINE_GPU_LIFETIME_TESTS") == "1";

    [Theory(Skip = "Set FORGELINE_GPU_LIFETIME_TESTS=1 for native lifetime qualification.", SkipUnless = nameof(GpuTestsEnabled))]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordedResourcesSurviveDisposalAndFrameGrowth(bool requireDebugLayer)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Lifetime Test", 320, 240));
        using IGraphicsDevice graphics = GraphicsDeviceFactory.CreateForWindow(window, new GraphicsConfiguration
        {
            EnableDebugLayer = requireDebugLayer,
            EnableVSync = false,
            AllowSoftwareAdapterFallback = true
        });
        if (requireDebugLayer)
        {
            Assert.True(graphics.Diagnostics.Device.DebugLayerEnabled);
        }
        var compiler = new DxcShaderCompiler();
        GraphicsShaderBytecode vertex = compiler.Compile(
            "float4 VSMain(float3 position : POSITION) : SV_Position { return float4(position, 1); }",
            GraphicsShaderStage.Vertex, "VSMain", "LifetimeVertex.hlsl");
        GraphicsShaderBytecode pixel = compiler.Compile(
            "Texture2D Color : register(t0); SamplerState MaterialSampler : register(s0); float4 PSMain() : SV_Target0 { return Color.Sample(MaterialSampler, float2(0.5, 0.5)); }",
            GraphicsShaderStage.Pixel, "PSMain", "LifetimePixel.hlsl");
        IGraphicsCommandContext? escaped = null;
        for (int frame = 0; frame < 24; frame++)
        {
            using IGraphicsPipeline pipeline = graphics.CreateGraphicsPipeline(new GraphicsPipelineDescription(vertex, pixel)
            {
                VertexElements = [new GraphicsVertexElement("POSITION", 0, GraphicsVertexElementFormat.Float3, 0)],
                CullMode = GraphicsCullMode.None,
                PixelTextureCount = 1
            });
            using IGraphicsBuffer buffer = graphics.CreateBuffer(new GraphicsBufferDescription((ulong)(36 + frame * 256), GraphicsBufferMemory.Upload));
            buffer.SetData<float>([-0.5f, -0.5f, 0, 0, 0.5f, 0, 0.5f, -0.5f, 0]);
            graphics.RenderFrame(GraphicsColor.ForgeLineClear, context =>
            {
                using IGraphicsTexture texture = graphics.CreateTexture(new GraphicsTextureData(
                    new GraphicsTextureDescription(1, 1, GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1),
                    [new GraphicsTextureMipData(1, 1, 4, [255, 255, 255, 255])]));
                escaped = context;
                context.SetPipeline(pipeline);
                context.SetVertexBuffer(buffer, 12);
                context.SetPixelTexture(0, texture);
                context.Draw(3);
                Assert.Throws<InvalidOperationException>(() => buffer.SetData<float>([0]));
                pipeline.Dispose();
                buffer.Dispose();
                texture.Dispose();
                Assert.True(graphics.Diagnostics.Health.PendingRetirementCount >= 3);
                Assert.True(graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed >= 1);
                Assert.Throws<ObjectDisposedException>(() => context.SetVertexBuffer(buffer, 12));
            });
        }
        Assert.NotNull(escaped);
        Assert.Throws<ObjectDisposedException>(() => escaped.Draw(3));
        graphics.WaitForIdle();
        Assert.Equal(0, graphics.Diagnostics.Health.PendingRetirementCount);
        Assert.Equal(0, graphics.Diagnostics.Health.LiveResourceCount);
        Assert.Equal(0, graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        Assert.Equal(0, graphics.Diagnostics.Debug.ErrorCount);
    }
}
