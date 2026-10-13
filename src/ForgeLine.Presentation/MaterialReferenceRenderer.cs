using System.Numerics;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public sealed record MaterialReferenceSample(string Name, Vector3 LinearAlbedo, float Roughness, float Metallic);

// Explicit parameter controls share the production BRDF; they are not replacement game assets.
public sealed class MaterialReferenceRenderer : IDisposable
{
    public static IReadOnlyList<MaterialReferenceSample> Materials { get; } = Array.AsReadOnly<MaterialReferenceSample>(
    [new("Steel",new(.56f,.57f,.58f),.35f,1), new("Painted armor",new(.12f,.18f,.08f),.45f,0),
     new("Rubber",new(.025f,.025f,.025f),.85f,0), new("Concrete",new(.5f,.5f,.47f),.9f,0),
     new("Rough terrain",new(.14f,.1f,.065f),1,0)]);

    private readonly IGraphicsPipeline _pipeline;
    private readonly SceneLightingSettings _lighting;
    private const string Interface = """
        struct Output {
            float4 Position:SV_Position;
            nointerpolation float4 Settings:TEXCOORD0;
            nointerpolation float4 Light:TEXCOORD1;
            nointerpolation float4 DirectionalAmbient:TEXCOORD2;
            nointerpolation float4 AmbientExposure:TEXCOORD3;
            nointerpolation float2 GroundTone:TEXCOORD4;
            nointerpolation float3 Right:TEXCOORD5;
            nointerpolation float3 Up:TEXCOORD6;
            nointerpolation float3 View:TEXCOORD7;
        };
        """;

    public MaterialReferenceRenderer(IGraphicsDevice graphics, SceneLightingSettings? lighting = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        _lighting=lighting ?? SceneLightingSettings.Default;
        _lighting.Validate();
        var compiler=new DxcShaderCompiler();
        string surfaces=string.Join(",",Materials.Select(m=>FormattableString.Invariant($"float4({m.LinearAlbedo.X},{m.LinearAlbedo.Y},{m.LinearAlbedo.Z},{m.Roughness})")));
        string metals=string.Join(",",Materials.Select(m=>m.Metallic.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        string vertex=Interface+"""
            cbuffer Frame:register(b0) {
                float4 Settings,Light,DirectionalAmbient,AmbientExposure;
                float2 GroundTone,Padding; float4 Right,Up,View;
            };
            Output VSMain(uint id:SV_VertexID) {
                Output o; float2 uv=float2((id<<1)&2,id&2);
                o.Position=float4(uv*float2(2,-2)+float2(-1,1),0,1);
                o.Settings=Settings; o.Light=Light; o.DirectionalAmbient=DirectionalAmbient;
                o.AmbientExposure=AmbientExposure; o.GroundTone=GroundTone;
                o.Right=Right.xyz; o.Up=Up.xyz; o.View=View.xyz; return o;
            }
            """;
        string pixel=(graphics.SceneOutput.Enabled ? "#define LINEAR_SCENE 1\n":"#define LINEAR_SCENE 0\n")+
            DirectionalShadowShader.Disabled+PbrMaterialShader.Lighting+Interface+
            $"static const float4 Surfaces[5]={{{surfaces}}};\nstatic const float Metals[5]={{{metals}}};\n"+"""
            float4 PSMain(Output input):SV_Target {
                float2 grid=input.Position.xy/input.Settings.xy*float2(5,3);
                int column=min(4,(int)floor(grid.x)), row=min(2,(int)floor(grid.y));
                float2 uv=frac(grid)*2-1; uv.y=-uv.y;
                uv.x*=input.Settings.x*3/(input.Settings.y*5);
                uv/=0.8*input.Settings.z;
                // Form normals and derivatives for the entire quad before a coverage return.
                float3 n=uv.x*input.Right+uv.y*input.Up+sqrt(max(0,1-dot(uv,uv)))*input.View;
                float roughness=saturate(Surfaces[column].w*(row==0 ? .5:row==1 ? 1:1.5));
                float3 color=ApplySceneLighting(Surfaces[column].rgb,n,1,roughness,Metals[column],0,
                    input.Light,input.DirectionalAmbient,input.AmbientExposure,input.GroundTone,n,input.View);
                if(dot(uv,uv)>=1) {
                #if LINEAR_SCENE
                    return float4(.08,.1,.12,-2);
                #else
                    return float4(.08,.1,.12,1);
                #endif
                }
                return float4(color,1);
            }
            """;
        _pipeline=graphics.CreateGraphicsPipeline(new(compiler.Compile(vertex,GraphicsShaderStage.Vertex,"VSMain","MaterialReferenceVertex.hlsl"),
            compiler.Compile(pixel,GraphicsShaderStage.Pixel,"PSMain","MaterialReferencePixel.hlsl"))
            {VertexRootConstantCount=32,CullMode=GraphicsCullMode.None,TargetFormat=graphics.SceneOutput.Enabled ? GraphicsFrameTargetFormat.Rgba16Float:GraphicsFrameTargetFormat.Rgba8Unorm});
    }

    public void Render(IGraphicsCommandContext context, RtsCamera camera)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(camera);
        Span<float> constants=stackalloc float[32]; constants.Clear();
        constants[0]=context.Width; constants[1]=context.Height; constants[2]=Math.Clamp(120/camera.Distance,.02f,1);
        _lighting.WriteShaderConstants(constants[4..]);
        Matrix4x4.Invert(camera.GetMatrices(context.Width,context.Height).View,out Matrix4x4 inverse);
        constants[20]=inverse.M11;constants[21]=inverse.M12;constants[22]=inverse.M13;
        constants[24]=inverse.M21;constants[25]=inverse.M22;constants[26]=inverse.M23;
        constants[28]=inverse.M31;constants[29]=inverse.M32;constants[30]=inverse.M33;
        context.SetPipeline(_pipeline); context.SetVertexConstants(constants); context.Draw(3);
    }

    public void Dispose() => _pipeline.Dispose();
}
