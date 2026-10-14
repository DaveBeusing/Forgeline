using System.Numerics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

public sealed class D3D12ParticleTests
{
    [Fact]
    public void PerspectiveDepthUsesPositiveAsymptoteAndRetainsVisibleForegroundParticles()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Perspective Particle Depth", 320, 240));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableDebugLayer = false, EnableVSync = false });
        using var renderer = new GpuParticleRenderer(graphics, 512);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 320f / 240, .25f, 1000);
        Matrix4x4 view = Matrix4x4.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.UnitY);
        Vector4 plane = Vector4.Transform(new Vector4(0, 0, -20, 1), projection);
        float depth = plane.Z / plane.W;
        var compiler = new DxcShaderCompiler();
        var vertex = compiler.Compile("""
            float4 VSMain(uint id : SV_VertexID) : SV_Position {
                float2 uv = float2((id<<1)&2,id&2);
                return float4(uv*float2(2,-2)+float2(-1,1),PLANE_DEPTH,1);
            }
            """.Replace("PLANE_DEPTH", depth.ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
            GraphicsShaderStage.Vertex, "VSMain", "PerspectiveParticlePlane.hlsl");
        var pixel = compiler.Compile("float4 PSMain() : SV_Target0 { return float4(.1,.2,.3,1); }",
            GraphicsShaderStage.Pixel, "PSMain", "PerspectiveParticlePlanePixel.hlsl");
        using var opaque = graphics.CreateGraphicsPipeline(new(vertex, pixel) { DepthEnabled = true, CullMode = GraphicsCullMode.None });
        byte[] Capture(float projectionA)
        {
            GraphicsParticle[] particles = [new(new(0, 0, 0, .25f), new(0, 0, 0, 1), new(0, 1, 0, 1), new(1, 0, 2, 0))];
            return graphics.CaptureFrame(GraphicsColor.ForgeLineClear, context =>
            {
                context.SetPipeline(opaque); context.Draw(3);
                renderer.Render(context, particles, view * projection, Vector3.UnitX, Vector3.UnitY,
                    projectionA, projection.M43, 1, false, false);
                context.BeginPass(GraphicsFramePass.Overlay);
            });
        }
        byte[] incorrect = Capture(projection.M33);
        byte[] correct = Capture(-projection.M33);
        var surface = graphics.Diagnostics.Surface;
        int center = (surface.Height / 2 * surface.Width + surface.Width / 2) * 4;
        Assert.True(correct[center + 1] > incorrect[center + 1] + 50);
        Assert.Equal(correct, Capture(-projection.M33));
        Assert.Equal(0, graphics.Diagnostics.Resources.TextureBindingFailureCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpritesUseOpaqueDepthWithoutWritingItAndSurviveResize(bool linear)
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowConfiguration("FORGELINE Particle Depth", 320, 240));
        using var graphics = (D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter = true, EnableDebugLayer = false, EnableVSync = false });
        graphics.ConfigureSceneOutput(new(linear,1,false));
        using var particles = new GpuParticleRenderer(graphics,512);
        var compiler = new DxcShaderCompiler();
        var vs = compiler.Compile("""
            float4 VSMain(uint id : SV_VertexID) : SV_Position {
                float2 uv = float2((id<<1)&2,id&2);
                return float4(uv*float2(2,-2)+float2(-1,1),.6,1);
            }
            """,GraphicsShaderStage.Vertex,"VSMain","ParticleDepthPlaneVertex.hlsl");
        var ps = compiler.Compile("float4 PSMain() : SV_Target0 { return float4(.1,.2,.3,1); }",GraphicsShaderStage.Pixel,"PSMain","ParticleDepthPlanePixel.hlsl");
        using var ground = graphics.CreateGraphicsPipeline(new(vs,ps) { DepthEnabled=true,CullMode=GraphicsCullMode.None,
            TargetFormat=linear ? GraphicsFrameTargetFormat.Rgba16Float : GraphicsFrameTargetFormat.Rgba8Unorm });
        byte[] Capture(float depth)
        {
            GraphicsParticle[] data = [new(new(0,0,depth,.25f),new(0,0,0,1),new(0,1,0,1),new(.2f,0,2,0))];
            return graphics.CaptureFrame(GraphicsColor.ForgeLineClear,context =>
            {
                context.SetPipeline(ground); context.Draw(3);
                particles.Render(context,data,Matrix4x4.Identity,Vector3.UnitX,Vector3.UnitY,1,-1,1,false,linear);
                context.SetPipeline(ground);
                Assert.Throws<InvalidOperationException>(()=>context.Draw(3));
                context.BeginPass(GraphicsFramePass.Overlay);
            });
        }
        byte[] behind=Capture(.7f), front=Capture(.3f), intersection=Capture(.59f);
        Assert.Equal(behind,Capture(.7f));
        int center=(120*320+160)*4;
        Assert.True(front[center+1]>behind[center+1]);
        Assert.InRange(intersection[center+1],behind[center+1],front[center+1]);
        Assert.Equal(1,graphics.Diagnostics.Particles!.Value.DepthDescriptors);
        Assert.NotNull(graphics.Diagnostics.Particles.Value.UpdateAndRenderGpuMilliseconds);
        Assert.Equal(0,graphics.Diagnostics.Resources.TextureBindingFailureCount);
        graphics.Resize(0,0); graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        Assert.True(graphics.Diagnostics.Surface.IsSuspended);
        graphics.Resize(640,400); graphics.RenderFrame(GraphicsColor.ForgeLineClear); byte[] resized=Capture(.3f);
        Assert.Equal(640*400*4,resized.Length);
        particles.Dispose(); ground.Dispose(); graphics.WaitForIdle();
        graphics.ConfigureParticleDepth(false);
        Assert.Equal(0,graphics.Diagnostics.Particles.Value.DepthDescriptors);
        Assert.Equal(0,graphics.Diagnostics.Health.PendingRetirementCount);
    }

    [Fact]
    public void PipelineBudgetAndParticleCapacityRejectInvalidPaths()
    {
        Assert.Throws<ArgumentException>(()=>new GraphicsParticle(new(0,0,0,float.NaN),new(0,0,0,1),Vector4.One,new(1,0,0,0)).Validate());
        var compiler=new DxcShaderCompiler();
        var vs=compiler.Compile("float4 VSMain() : SV_Position { return float4(0,0,0,1); }",GraphicsShaderStage.Vertex,"VSMain","ParticleContractVertex.hlsl");
        var ps=compiler.Compile("float4 PSMain() : SV_Target0 { return 1; }",GraphicsShaderStage.Pixel,"PSMain","ParticleContractPixel.hlsl");
        Assert.Throws<ArgumentException>(()=>new GraphicsPipelineDescription(vs,ps) { SceneDepthReceiver=true,AlphaBlendEnabled=true,DepthEnabled=true }.Validate());
        Assert.Throws<ArgumentException>(()=>new GraphicsPipelineDescription(vs,ps) { SceneDepthReceiver=true,AlphaBlendEnabled=true,VertexRootConstantCount=64 }.Validate());
        Assert.Throws<NotSupportedException>(()=>new GraphicsPipelineDescription(vs,ps) { AlphaBlendEnabled=true,TargetFormat=GraphicsFrameTargetFormat.Rgba16Float }.Validate());
    }
}
