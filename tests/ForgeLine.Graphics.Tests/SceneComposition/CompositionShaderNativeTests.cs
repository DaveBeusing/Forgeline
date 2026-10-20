using System.Globalization;
using System.Numerics;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests.SceneComposition;

[Collection("Native postprocessing")]
public sealed class CompositionShaderNativeTests
{
    internal const string ProbeVertex = """
        float4 VSMain(uint id:SV_VertexID):SV_Position {
            float2 uv=float2((id<<1)&2,id&2);
            return float4(uv*float2(2,-2)+float2(-1,1),.5,1);
        }
        """;

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void BothCompositionVariantsCompileWithEveryExistingAtmosphereAndScaleProfile(bool scaled, bool atmosphere, bool localHaze)
    {
        var compiler = new DxcShaderCompiler();
        var vertex = compiler.Compile(SceneCompositeShader.Vertex, GraphicsShaderStage.Vertex,
            "VSMain", "CompositionContractVertex.hlsl");
        Assert.False(vertex.Data.IsEmpty);
        Assert.Equal(40, SceneCompositeShader.ConstantCount);
        foreach (bool candidate in new[] { false, true })
        {
            var pixel = compiler.Compile(Header(candidate, scaled, atmosphere, localHaze) + SceneCompositeShader.Pixel,
                GraphicsShaderStage.Pixel, "PSMain", "CompositionContractPixel.hlsl");
            Assert.False(pixel.Data.IsEmpty);
            new GraphicsPipelineDescription(vertex, pixel)
            {
                CullMode = GraphicsCullMode.None,
                PixelTextureCount = scaled || atmosphere ? 2 : 1,
                DepthEnabled = scaled,
                DepthAlwaysPass = scaled,
                VertexRootConstantCount = SceneCompositeShader.ConstantCount
            }.Validate();
        }
    }

    [Theory]
    [InlineData(1, 1, 3840, 2160)]
    [InlineData(2, 7, 17, 19)]
    [InlineData(7, 1, 17, 3)]
    [InlineData(7, 5, 19, 13)]
    public void SharedLinearCompositionMatchesIndependentSamplingAtVirtualDisplayCornersAndInterior(
        int sourceWidth, int sourceHeight, int displayWidth, int displayHeight)
    {
        using var platform = new WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Composition Sample Contracts", 320, 240));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableVSync = false, EnableDebugLayer = false });
        var compiler = new DxcShaderCompiler();
        var vertex = compiler.Compile(ProbeVertex, GraphicsShaderStage.Vertex, "VSMain", "CompositionProbeVertex.hlsl");
        byte[] color = new byte[sourceWidth * sourceHeight * 4];
        byte[] depth = new byte[sourceWidth * sourceHeight];
        for (int y = 0; y < sourceHeight; y++)
        {
            for (int x = 0; x < sourceWidth; x++)
            {
                int index = y * sourceWidth + x;
                color[index * 4] = (byte)((x * 37 + y * 19 + 41) % 256);
                color[index * 4 + 1] = (byte)((x * 11 + y * 73 + 67) % 256);
                color[index * 4 + 2] = (byte)((x * 53 + y * 7 + 113) % 256);
                color[index * 4 + 3] = (byte)(64 + ((x + y) % 192));
                depth[index] = y == sourceHeight - 1 ? (byte)255 : x < sourceWidth / 2 ? (byte)51 : (byte)204;
            }
        }
        using var scene = graphics.CreateTexture(new(new(sourceWidth, sourceHeight,
            GraphicsTextureFormat.Rgba8Unorm, GraphicsTextureColorSpace.Linear, 1),
            [new(sourceWidth, sourceHeight, sourceWidth * 4, color)]));
        using var depths = graphics.CreateTexture(new(new(sourceWidth, sourceHeight,
            GraphicsTextureFormat.R8Unorm, GraphicsTextureColorSpace.Linear, 1),
            [new(sourceWidth, sourceHeight, sourceWidth, depth)]));

        foreach (float edgeBlend in new[] { 0f, .3f })
        {
            for (int atmosphere = 0; atmosphere < 3; atmosphere++)
            {
                byte[] independent = Capture(false, edgeBlend, atmosphere);
                byte[] shared = Capture(true, edgeBlend, atmosphere);
                Assert.Equal(independent.Length, shared.Length);
                for (int index = 0; index < shared.Length; index++)
                {
                    Assert.InRange(Math.Abs(independent[index] - shared[index]), 0, 1);
                }
            }
        }
        Assert.Equal(0, graphics.Diagnostics.Health.SubmissionFaultCount);
        Assert.Equal(0, graphics.Diagnostics.Resources.TextureBindingFailureCount);

        byte[] Capture(bool candidate, float edgeBlend, int atmosphere)
        {
            string probe = string.Create(CultureInfo.InvariantCulture, $$"""
                float4 ProbeMain(float4 position:SV_Position):SV_Target0 {
                    uint column=uint(position.x)%5;
                    uint row=uint(position.y)%5;
                    float2 display=float2({{displayWidth}},{{displayHeight}});
                    float x=column==0 ? .5 : column==1 ? 1.25 : column==2 ? display.x*.5+.25 :
                        column==3 ? display.x-1.25 : display.x-.5;
                    float y=row==0 ? .5 : row==1 ? 1.25 : row==2 ? display.y*.5+.25 :
                        row==3 ? display.y-1.25 : display.y-.5;
                    Output input=(Output)0;
                    input.Position=float4(x,y,.5,1);
                    input.Settings=float4(1.15,1,{{(atmosphere == 0 ? 0 : 1)}},2);
                    input.R0=float4(1,0,0,0); input.R1=float4(0,1,0,0);
                    input.R2=float4(0,0,100,0); input.R3=float4(0,0,0,1);
                    input.Camera=float4(0,0,10,0);
                    input.ColorDensity=float4(.3,.4,.6,.001);
                    input.Fog=float4(0,0,.02,.65);
                    input.Grade=float4(.05,-.03,1.04,1.03);
                    input.Post=float4({{edgeBlend}},display,0);
                    return PSMain(input).Color;
                }
                """);
            var pixel = compiler.Compile(Header(candidate, true, atmosphere > 0, atmosphere > 1) +
                SceneCompositeShader.Pixel + "\n" + probe,
                GraphicsShaderStage.Pixel, "ProbeMain", "CompositionSamplingPixel.hlsl");
            using var pipeline = graphics.CreateGraphicsPipeline(new(vertex, pixel)
            {
                CullMode = GraphicsCullMode.None,
                PixelTextureCount = 2
            });
            return graphics.CaptureFrame(new(0, 0, 0, 1), context =>
            {
                context.SetPipeline(pipeline);
                context.SetPixelTexture(0, scene);
                context.SetPixelTexture(1, depths);
                context.Draw(3);
            });
        }
    }

    private static string Header(bool candidate, bool scaled, bool atmosphere, bool localHaze) =>
        string.Create(CultureInfo.InvariantCulture,
            $"#define COMPOSITION_SHARED_LINEAR {(candidate ? 1 : 0)}\n#define UPSAMPLE {(scaled ? 1 : 0)}\n#define ATMOSPHERE {(atmosphere ? 1 : 0)}\n") +
        (localHaze ? new GraphicsLocalHazeSettings(new Vector3(0, 0, 25), 50, .005f).ShaderConstants() : "");
}
