using System.Runtime.InteropServices;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class D3D12TextureSamplingIntegrationTests
{
    [Fact]
    public void TexturedPrimitiveUsesRealGpuTextureAndMipCapableSampler()
    {
        using var platform = new WindowsPlatform();
        using IWindow window =
            platform.CreateWindow(
                new WindowConfiguration(
                    "FORGELINE Texture Sampling Test",
                    320,
                    240,
                    resizable: false,
                    WindowMode.Windowed));

        using IGraphicsDevice graphics =
            GraphicsDeviceFactory.CreateForWindow(
                window,
                new GraphicsConfiguration
                {
                    AllowSoftwareAdapterFallback = true,
                    EnableDebugLayer = false,
                    EnableVSync = false
                });

        GraphicsTextureData textureData = CreateTextureData();
        using IGraphicsTexture texture =
            graphics.CreateTexture(textureData);

        var compiler = new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader =
            compiler.Compile(
                """
                struct VertexInput
                {
                    float3 Position : POSITION;
                    float2 Uv : TEXCOORD0;
                };

                struct VertexOutput
                {
                    float4 Position : SV_Position;
                    float2 Uv : TEXCOORD0;
                };

                VertexOutput VSMain(VertexInput input)
                {
                    VertexOutput output;
                    output.Position = float4(input.Position, 1.0f);
                    output.Uv = input.Uv;
                    return output;
                }
                """,
                GraphicsShaderStage.Vertex,
                "VSMain",
                "TextureSmokeVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                """
                Texture2D BaseColorTexture : register(t0);
                SamplerState WorldMaterialSampler : register(s0);

                struct PixelInput
                {
                    float4 Position : SV_Position;
                    float2 Uv : TEXCOORD0;
                };

                float4 PSMain(PixelInput input) : SV_Target0
                {
                    return BaseColorTexture.Sample(
                        WorldMaterialSampler,
                        input.Uv);
                }
                """,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "TextureSmokePixel.hlsl");

        using IGraphicsPipeline pipeline =
            graphics.CreateGraphicsPipeline(
                new GraphicsPipelineDescription(
                    vertexShader,
                    pixelShader)
                {
                    VertexElements =
                    [
                        new GraphicsVertexElement(
                            "POSITION",
                            0,
                            GraphicsVertexElementFormat.Float3,
                            0),
                        new GraphicsVertexElement(
                            "TEXCOORD",
                            0,
                            GraphicsVertexElementFormat.Float2,
                            12)
                    ],
                    PixelTextureCount = 1
                });

        TextureVertex[] vertices =
        [
            new(-0.8f, -0.8f, 0.0f, 0.0f, 1.0f),
            new( 0.0f,  0.8f, 0.0f, 0.5f, 0.0f),
            new( 0.8f, -0.8f, 0.0f, 1.0f, 1.0f)
        ];

        using IGraphicsBuffer vertexBuffer =
            graphics.CreateBuffer(
                new GraphicsBufferDescription(
                    checked(
                        (ulong)vertices.Length *
                        TextureVertex.SizeInBytes),
                    GraphicsBufferMemory.Upload));
        vertexBuffer.SetData<TextureVertex>(vertices);

        graphics.RenderFrame(
            GraphicsColor.ForgeLineClear,
            context =>
            {
                context.SetPipeline(pipeline);
                context.SetVertexBuffer(
                    vertexBuffer,
                    TextureVertex.SizeInBytes);
                context.SetPixelTexture(0, texture);
                context.Draw(vertices.Length);
            });

        GraphicsResourceDiagnostics resources =
            graphics.Diagnostics.Resources;

        Assert.Equal(1, resources.LoadedTextureCount);
        Assert.Equal(
            textureData.ResidentByteCount,
            resources.ResidentTextureBytes);
        Assert.Equal(1, resources.ShaderResourceDescriptorsUsed);
        Assert.Equal(0, resources.TextureBindingFailureCount);
    }

    private static GraphicsTextureData CreateTextureData() =>
        new(
            new GraphicsTextureDescription(
                2,
                2,
                GraphicsTextureFormat.Rgba8Unorm,
                GraphicsTextureColorSpace.Srgb,
                2),
            [
                new GraphicsTextureMipData(
                    2,
                    2,
                    8,
                    [
                        255, 0, 0, 255,
                        0, 255, 0, 255,
                        0, 0, 255, 255,
                        255, 255, 255, 255
                    ]),
                new GraphicsTextureMipData(
                    1,
                    1,
                    4,
                    [128, 128, 128, 255])
            ]);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct TextureVertex(
        float X,
        float Y,
        float Z,
        float U,
        float V)
    {
        public const int SizeInBytes = sizeof(float) * 5;
    }
}
