using System.Globalization;
using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class PbrMaterialShaderTests
{
    [Theory]
    [InlineData(0, .25)] [InlineData(0, .7)] [InlineData(0, 1)]
    [InlineData(1, .25)] [InlineData(1, .7)] [InlineData(1, 1)]
    public void WhiteMaterialHemisphericalDirectEnergyDoesNotExceedIncidentEnergy(float metallic, float roughness)
    {
        string pixel = $$"""
            float4 PSMain(float4 position:SV_Position):SV_Target {
                float3 energy=0;
                for(int i=0;i<32;i++) for(int j=0;j<64;j++) {
                    float cosine=(i+0.5)/32, phi=(j+0.5)*6.28318530718/64;
                    float radius=sqrt(1-cosine*cosine);
                    float3 l=float3(radius*cos(phi),radius*sin(phi),cosine);
                    energy+=PbrDirect(1.xxx,{{F(metallic)}},{{F(roughness)}},float3(0,0,1),float3(0,0,1),l);
                }
                return float4(energy*(6.28318530718/2048)*0.5,1);
            }
            """;
        byte[] rgba = Capture(pixel);
        Assert.InRange(rgba[0], 1, 131); // Half scale; midpoint quadrature and RGBA8 quantization allowance.
        Assert.Equal(rgba[0], rgba[1]); Assert.Equal(rgba[0], rgba[2]);
    }

    [Fact]
    public void ReciprocalDirectionsKeepTheSameBrdfAndBacklightingHasNoDirectCost()
    {
        byte[] rgba = Capture("""
            float4 PSMain(float4 p:SV_Position):SV_Target {
                float3 a=normalize(float3(.6,0,.8)), b=normalize(float3(-.3,0,.95));
                float3 v=p.x<8 ? a:b, l=p.x<8 ? b:a;
                float3 value=PbrDirect(float3(.6,.3,.1),.4,.65,float3(0,0,1),v,l)/l.z;
                return float4(value,1);
            }
            """);
        for (int channel=0; channel<3; channel++) Assert.InRange(Math.Abs(rgba[channel]-rgba[12*4+channel]),0,1);
        rgba = Capture("""
            float4 PSMain(float4 p:SV_Position):SV_Target {
                return float4(PbrDirect(1.xxx,0,.5,float3(0,0,1),float3(0,0,1),float3(0,0,-1)),1);
            }
            """);
        Assert.Equal(new byte[] {0,0,0,255},rgba[..4]);
    }

    [Theory]
    [InlineData(2)] [InlineData(-2)]
    public void NonuniformAndMirroredNormalsAgreeWithIndependentInverseTranspose(float scaleX)
    {
        Matrix4x4 basis = Matrix4x4.CreateScale(scaleX,.5f,1.5f)*Matrix4x4.CreateRotationY(.65f)*Matrix4x4.CreateRotationZ(.25f);
        Vector3 normal = Vector3.Normalize(new(.3f,.8f,.5f));
        Assert.True(Matrix4x4.Invert(basis,out Matrix4x4 inverse));
        Vector3 expected = Vector3.Normalize(Vector3.TransformNormal(normal,Matrix4x4.Transpose(inverse)));
        byte[] rgba = Capture($$"""
            float4 PSMain(float4 p:SV_Position):SV_Target {
                float orientation;
                float3 n=PbrTransformNormal(float3({{F(normal.X)}},{{F(normal.Y)}},{{F(normal.Z)}}),
                    float3x3({{F(basis.M11)}},{{F(basis.M12)}},{{F(basis.M13)}},{{F(basis.M21)}},{{F(basis.M22)}},{{F(basis.M23)}},{{F(basis.M31)}},{{F(basis.M32)}},{{F(basis.M33)}}),orientation);
                return float4(n*.5+.5,orientation>0 ? 1:0);
            }
            """);
        float[] channels = [expected.X,expected.Y,expected.Z];
        for(int i=0;i<3;i++) Assert.InRange(Math.Abs(rgba[i]-(int)MathF.Round((channels[i]*.5f+.5f)*255)),0,1);
        Assert.Equal(scaleX>0 ? 255:0,rgba[3]);
    }

    private static string F(float value) => value.ToString("R",CultureInfo.InvariantCulture);

    private static byte[] Capture(string pixel)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new("FORGELINE Material Contract",256,256));
        using var graphics = GraphicsDeviceFactory.CreateForWindow(window,new() { ForceSoftwareAdapter=true,EnableVSync=false,EnableDebugLayer=false });
        graphics.Resize(16,16); graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        var compiler = new DxcShaderCompiler();
        using var pipeline = graphics.CreateGraphicsPipeline(new(
            compiler.Compile("""
                float4 VSMain(uint id:SV_VertexID):SV_Position {
                    float2 uv=float2((id<<1)&2,id&2); return float4(uv*float2(2,-2)+float2(-1,1),0,1);
                }
                """,GraphicsShaderStage.Vertex,"VSMain","PbrContractVertex.hlsl"),
            compiler.Compile("#define LINEAR_SCENE 1\n"+DirectionalShadowShader.Disabled+PbrMaterialShader.Lighting+pixel,
                GraphicsShaderStage.Pixel,"PSMain","PbrContractPixel.hlsl")) { CullMode=GraphicsCullMode.None });
        return graphics.CaptureFrame(GraphicsColor.ForgeLineClear,context => { context.SetPipeline(pipeline); context.Draw(3); });
    }
}
