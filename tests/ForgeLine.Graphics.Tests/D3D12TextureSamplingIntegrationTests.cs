using System.Runtime.InteropServices;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class D3D12TextureSamplingIntegrationTests
{
    [Fact]
    public void MaterialPrimitiveSamplesGpuTextureSetWithMipCapableSampler()
    {
        using var platform = new WindowsPlatform();
        using IWindow window =
            platform.CreateWindow(
                new WindowConfiguration(
                    "FORGELINE Material Sampling Test",
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

        GraphicsTextureData baseColorData =
            CreateBaseColorTextureData();
        GraphicsTextureData normalData =
            CreateSinglePixelTexture(
                GraphicsTextureColorSpace.Linear,
                128,
                128,
                255,
                255);
        GraphicsTextureData ormData =
            CreateSinglePixelTexture(
                GraphicsTextureColorSpace.Linear,
                255,
                192,
                0,
                255);
        GraphicsTextureData emissiveData =
            CreateSinglePixelTexture(
                GraphicsTextureColorSpace.Srgb,
                8,
                4,
                0,
                255);

        using IGraphicsTexture baseColor =
            graphics.CreateTexture(
                baseColorData);
        using IGraphicsTexture normal =
            graphics.CreateTexture(
                normalData);
        using IGraphicsTexture orm =
            graphics.CreateTexture(
                ormData);
        using IGraphicsTexture emissive =
            graphics.CreateTexture(
                emissiveData);

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
                "MaterialTextureSmokeVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                """
                Texture2D BaseColorTexture : register(t0);
                Texture2D NormalTexture : register(t1);
                Texture2D OrmTexture : register(t2);
                Texture2D EmissiveTexture : register(t3);
                SamplerState WorldMaterialSampler : register(s0);

                struct PixelInput
                {
                    float4 Position : SV_Position;
                    float2 Uv : TEXCOORD0;
                };

                float4 PSMain(PixelInput input) : SV_Target0
                {
                    float4 baseColor =
                        BaseColorTexture.Sample(
                            WorldMaterialSampler,
                            input.Uv);
                    float3 normal =
                        NormalTexture.Sample(
                            WorldMaterialSampler,
                            input.Uv).xyz;
                    float3 orm =
                        OrmTexture.Sample(
                            WorldMaterialSampler,
                            input.Uv).rgb;
                    float3 emissive =
                        EmissiveTexture.Sample(
                            WorldMaterialSampler,
                            input.Uv).rgb;

                    float response =
                        0.98f +
                        0.01f * saturate(normal.z) +
                        0.01f * saturate(orm.r);

                    return float4(
                        baseColor.rgb * response +
                        emissive * 0.1f,
                        baseColor.a);
                }
                """,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "MaterialTextureSmokePixel.hlsl");

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
                    PixelTextureCount = 4
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
        vertexBuffer.SetData<TextureVertex>(
            vertices);

        graphics.RenderFrame(
            GraphicsColor.ForgeLineClear,
            context =>
            {
                context.SetPipeline(
                    pipeline);
                context.SetVertexBuffer(
                    vertexBuffer,
                    TextureVertex.SizeInBytes);
                context.SetPixelTexture(
                    0,
                    baseColor);
                context.SetPixelTexture(
                    1,
                    normal);
                context.SetPixelTexture(
                    2,
                    orm);
                context.SetPixelTexture(
                    3,
                    emissive);
                context.Draw(
                    vertices.Length);
            });

        GraphicsResourceDiagnostics resources =
            graphics.Diagnostics.Resources;
        long expectedResidentBytes =
            baseColorData.ResidentByteCount +
            normalData.ResidentByteCount +
            ormData.ResidentByteCount +
            emissiveData.ResidentByteCount;

        Assert.Equal(
            4,
            resources.LoadedTextureCount);
        Assert.Equal(
            expectedResidentBytes,
            resources.ResidentTextureBytes);
        Assert.Equal(
            4,
            resources.ShaderResourceDescriptorsUsed);
        Assert.Equal(
            0,
            resources.TextureBindingFailureCount);
    }

    private static GraphicsTextureData CreateBaseColorTextureData() =>
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

    private static GraphicsTextureData CreateSinglePixelTexture(
        GraphicsTextureColorSpace colorSpace,
        byte red,
        byte green,
        byte blue,
        byte alpha) =>
        new(
            new GraphicsTextureDescription(
                1,
                1,
                GraphicsTextureFormat.Rgba8Unorm,
                colorSpace,
                1),
            [
                new GraphicsTextureMipData(
                    1,
                    1,
                    4,
                    [
                        red,
                        green,
                        blue,
                        alpha
                    ])
            ]);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct TextureVertex(
        float X,
        float Y,
        float Z,
        float U,
        float V)
    {
        public const int SizeInBytes =
            sizeof(float) *
            5;
    }
}
