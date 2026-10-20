using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Presentation.Tests.MaterialQuality;

[Collection("Native scene output")]
public sealed class MaterialNumericsTests
{
    [Theory]
    [InlineData("float3 n=PbrNormalize(0.xxx,float3(0,1,0)); return float4(n.y,all(isfinite(n)),1,1);")]
    [InlineData("float3 n=PbrNormalize(float3(asfloat(0x7fc00000),0,0),float3(0,1,0)); return float4(n.y,all(isfinite(n)),1,1);")]
    [InlineData("float3 n=PbrNormalize(float3(asfloat(0x7f800000),0,0),float3(0,1,0)); return float4(n.y,all(isfinite(n)),1,1);")]
    [InlineData("float o; float3 n=PbrTransformNormal(0.xxx,(float3x3)0,o); return float4(n.y,all(isfinite(n)),o,1);")]
    [InlineData("float3 a=PbrDirect(1.xxx,1,0,float3(0,0,1),float3(0,0,1),float3(0,0,1)); return float4(all(isfinite(a)),all(a>=0),a.x>0,1);")]
    [InlineData("float3 a=PbrDirect(1.xxx,0,1,float3(0,0,1),float3(0,0,-1),float3(0,0,1)); return float4(all(a==0),all(isfinite(a)),1,1);")]
    public void DegenerateAndBoundaryInputsStayFinite(string body)
    {
        byte[] pixels = MaterialCapture.Capture("float4 PSMain(float4 p:SV_Position):SV_Target { " + body + " }");
        Assert.Equal(new byte[] { 255,255,255,255 }, pixels[..4]);
    }

    [Theory]
    [InlineData(0f)] [InlineData(.045f)] [InlineData(.25f)] [InlineData(.85f)] [InlineData(1f)]
    public void FlatFootprintKeepsTheAuthoredRoughness(float roughness)
    {
        string r=roughness.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        byte[] pixels=MaterialCapture.Capture($$"""
            float4 PSMain(float4 p:SV_Position):SV_Target {
                float r=PbrSpecularRoughness({{r}},0.xxx,0.xxx);
                return float4(abs(r-{{r}})<1e-5,all(isfinite(r)),r>=0 && r<=1,1);
            }
            """);
        Assert.Equal(new byte[] {255,255,255,255},pixels[..4]);
    }

    [Fact]
    public void TwoAxisVarianceKeepsTheRoughnessDomainAndCannotWashOutASeam()
    {
        byte[] pixels=MaterialCapture.Capture("""
            float4 PSMain(float4 p:SV_Position):SV_Target {
                float a=PbrSpecularRoughness(.2,float3(.2,0,0),float3(0,.2,0));
                float b=PbrSpecularRoughness(.2,float3(20,0,0),float3(0,20,0));
                float expected=sqrt(.2*.2+.02);
                return float4(abs(a-expected)<1e-5,b<.29,b>a,1);
            }
            """);
        Assert.Equal(new byte[] {255,255,255,255},pixels[..4]);
    }

    [Fact]
    public void EmissiveRemainsLinearAndIndependentOfSurfaceOcclusion()
    {
        byte[] pixels=MaterialCapture.Capture("""
            float4 PSMain(float4 p:SV_Position):SV_Target {
                return float4(ApplySceneLighting(.5.xxx,float3(0,1,0),float3(0,1,0),0,.5,1,
                    float3(.25,.5,.75),float4(0,1,0,0),0.xxxx,float4(0,0,0,8),float2(.4,1),0.xxx,float3(0,1,0)),1);
            }
            """);
        Assert.Equal(new byte[] {64,128,191,255},pixels[..4]);
    }

    [Fact]
    public void ReceiverBiasUsesSurfaceNormalWhileDirectLightingKeepsNormalDetail()
    {
        byte[] pixels=MaterialCapture.Capture("""
            float4 PSMain(float4 p:SV_Position):SV_Target {
                float3 n=normalize(float3(.3,.4,.85)), g=float3(0,0,1), l=normalize(float3(.2,.3,.9));
                float3 expected=PbrDirect(.5.xxx,0,.6,n,float3(0,0,1),l)*dot(g,l);
                float3 actual=ApplySceneLighting(.5.xxx,n,g,1,.6,0,0.xxx,float4(l,1),float4(1,1,1,0),
                    float4(0,0,0,1),float2(.4,0),0.xxx,float3(0,0,1));
                return float4(all(abs(expected-actual)<1e-5),1,1,1);
            }
            """, "float2 SampleDirectionalShadow(float3 p,float3 n,float3 l) {return float2(saturate(dot(n,l)),1);}");
        Assert.Equal(new byte[] {255,255,255,255},pixels[..4]);
    }

    [Fact]
    public void ReceiverPlaneIsScaleIndependentAndDegenerateQuadsStayBounded()
    {
        byte[] pixels=MaterialCapture.Capture("""
            float4 PSMain(float4 p:SV_Position):SV_Target {
                float2 a=ShadowReceiverPlane(float2(.1,0),float2(0,.1),.2,.3);
                float2 b=ShadowReceiverPlane(float2(.00001,0),float2(0,.00001),.00002,.00003);
                float2 degenerate=ShadowReceiverPlane(float2(.1,.1),float2(.2,.2),.7,.8);
                float corrected=ShadowReceiverDepth(.5,float2(1e10,1e10),float2(.1,.1),.01);
                return float4(all(abs(a-float2(2,3))<1e-5) && all(abs(a-b)<1e-5),
                    all(degenerate==0),abs(corrected-.51)<1e-5,1);
            }
            """, ShadowReceiverShader.Source+DirectionalShadowShader.Disabled);
        Assert.Equal(new byte[] {255,255,255,255},pixels[..4]);
    }

    [Theory]
    [InlineData(false,false,false)] [InlineData(false,false,true)]
    [InlineData(false,true,false)] [InlineData(false,true,true)]
    [InlineData(true,false,false)] [InlineData(true,false,true)]
    [InlineData(true,true,false)] [InlineData(true,true,true)]
    public void ProductionVariantsCompileWithoutChangingGpuContracts(bool shadows,bool linear,bool environment)
    {
        using var platform=new WindowsPlatform();
        using IWindow window=platform.CreateWindow(new("FORGELINE Material Variants",256,256));
        using var graphics=GraphicsDeviceFactory.CreateForWindow(window,new() {ForceSoftwareAdapter=true,EnableVSync=false,EnableDebugLayer=false});
        graphics.ConfigureDirectionalShadows(new(shadows ? DirectionalShadowQuality.Low:DirectionalShadowQuality.Disabled));
        graphics.ConfigureSceneOutput(new(linear,1.15f,true));
        var world=environment ? new WorldEnvironmentSettings {Wetness=.75f,CloudCover=.5f}:new();
        using var plain=InstanceMaterialPipeline.CreatePipeline(graphics,world);
        using var textured=InstanceMaterialPipeline.CreateTexturedPipeline(graphics,world);
        using var terrain=TerrainMaterialPipeline.CreateTerrainPipeline(graphics,TerrainSurfaceDetailSettings.Default,world);
        using var terrainPlain=TerrainMaterialPipeline.CreateTerrainPipeline(graphics,TerrainSurfaceDetailSettings.Disabled,world);
        using var caster=DirectionalShadowCasterPipeline.Create(graphics,true);
        Assert.Equal(35,plain.Description.VertexRootConstantCount);
        Assert.Equal(35,textured.Description.VertexRootConstantCount);
        Assert.Equal(51,terrain.Description.VertexRootConstantCount);
        Assert.Equal(4,textured.Description.PixelTextureCount);
        Assert.Equal(13,terrain.Description.PixelTextureCount);
        Assert.Equal(shadows,textured.Description.DirectionalShadowReceiver);
        Assert.Equal(shadows,terrain.Description.VertexConstantsInBuffer);
        Assert.Equal(linear ? GraphicsFrameTargetFormat.Rgba16Float:GraphicsFrameTargetFormat.Rgba8Unorm,textured.Description.TargetFormat);
        Assert.Equal(GraphicsFrameTargetFormat.Depth32Float,caster.Description.TargetFormat);
        Assert.Equal(16,caster.Description.VertexRootConstantCount);
    }

    [Theory]
    [InlineData(0f,1f,0f,-1200f)] [InlineData(.0001f,1f,.0001f,-1200f)]
    [InlineData(0f,-1f,0f,-1200f)] [InlineData(.35f,.85f,-.25f,-1200f)]
    [InlineData(.35f,.85f,-.25f,1200f)]
    public void ProjectionKeepsNegativeCoordinatesAndNearlyVerticalLightStable(float x,float y,float z,float coordinate)
    {
        var camera=new RtsCameraState(new(coordinate,7,coordinate),0,-1,420,false,default);
        var lighting=SceneLightingSettings.Default with {DirectionToLight=Vector3.Normalize(new(x,y,z))};
        var settings=new GraphicsDirectionalShadowSettings(DirectionalShadowQuality.High);
        var first=DirectionalShadowProjection.Create(camera,lighting,settings);
        first.Validate();
        Assert.Equal(first,DirectionalShadowProjection.Create(camera with {Distance=120},lighting,settings));
        Vector3 clip=Vector3.Transform(first.Center,first.ViewProjection);
        Assert.InRange(Math.Abs(clip.X),0,.00001f);
        Assert.InRange(Math.Abs(clip.Y),0,.00001f);
    }
}

internal static class MaterialCapture
{
    internal static readonly string Vertex="""
        float4 VSMain(uint id:SV_VertexID):SV_Position {
            float2 uv=float2((id<<1)&2,id&2);
            return float4(uv*float2(2,-2)+float2(-1,1),0,1);
        }
        """;

    internal static byte[] Capture(string pixel,string? shadow=null)
    {
        using var platform=new WindowsPlatform();
        using IWindow window=platform.CreateWindow(new("FORGELINE Material Numerics",256,256));
        using var graphics=GraphicsDeviceFactory.CreateForWindow(window,new() {ForceSoftwareAdapter=true,EnableVSync=false,EnableDebugLayer=false});
        graphics.Resize(16,16);graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        var compiler=new DxcShaderCompiler();
        using var pipeline=graphics.CreateGraphicsPipeline(new(
            compiler.Compile(Vertex,GraphicsShaderStage.Vertex,"VSMain","MaterialNumericsVertex.hlsl"),
            compiler.Compile("#define LINEAR_SCENE 1\n"+(shadow ?? DirectionalShadowShader.Disabled)+PbrMaterialShader.Lighting+pixel,
                GraphicsShaderStage.Pixel,"PSMain","MaterialNumericsPixel.hlsl")) {CullMode=GraphicsCullMode.None});
        return graphics.CaptureFrame(GraphicsColor.ForgeLineClear,c=> {c.SetPipeline(pipeline);c.Draw(3);});
    }
}
