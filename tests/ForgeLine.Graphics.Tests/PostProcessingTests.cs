using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Graphics.Tests;

[CollectionDefinition("Native postprocessing", DisableParallelization = true)]
public sealed class NativePostProcessingGroup;

[Collection("Native postprocessing")]
public sealed class PostProcessingTests
{
    [Fact]
    public void InvalidGradeAndAntialiasingFailBeforeAllocation()
    {
        foreach (var settings in new[] {
            new GraphicsPostProcessingSettings { Contrast=float.NaN },
            new GraphicsPostProcessingSettings { WhiteBalance=.11f },
            new GraphicsPostProcessingSettings { Tint=-.11f },
            new GraphicsPostProcessingSettings { Saturation=.5f },
            new GraphicsPostProcessingSettings { Antialiasing=(SpatialAntialiasing)255 } })
            Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpatialEdgesRetainThinGeometryAndOverlayWithNoHistory(bool atmosphere)
    {
        using var platform=new WindowsPlatform();
        using IWindow window=platform.CreateWindow(new("FORGELINE Spatial Image Test",320,240));
        using var graphics=(D3D12GraphicsDevice)GraphicsDeviceFactory.CreateForWindow(window,
            new() { ForceSoftwareAdapter=true, EnableVSync=false, EnableDebugLayer=false });
        var high=new GraphicsPostProcessingSettings { Antialiasing=SpatialAntialiasing.High };
        Assert.Throws<InvalidOperationException>(()=>graphics.ConfigurePostProcessing(high));
        graphics.ConfigureSceneOutput(new(true,1,false));
        if(atmosphere) graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default with { Quality=AtmosphereQuality.Height });
        var compiler=new DxcShaderCompiler();
        var vertex=compiler.Compile("float4 VSMain(uint id:SV_VertexID):SV_Position { float2 uv=float2((id<<1)&2,id&2); return float4(uv*float2(2,-2)+float2(-1,1),.5,1); }",GraphicsShaderStage.Vertex,"VSMain","SpatialEdgeVertex.hlsl");
        var pixel=compiler.Compile("float4 PSMain(float4 p:SV_Position):SV_Target0 { int2 q=int2(p.xy); float3 color=q.x==q.y ? 1 : .02; if(q.y==60) color=float3(1,.15,0); return float4(color,1); }",GraphicsShaderStage.Pixel,"PSMain","SpatialEdgePixel.hlsl");
        using var world=graphics.CreateGraphicsPipeline(new(vertex,pixel) { TargetFormat=GraphicsFrameTargetFormat.Rgba16Float,CullMode=GraphicsCullMode.None });
        var ui=compiler.Compile("float4 PSMain():SV_Target0 { return float4(.1,.8,.3,1); }",GraphicsShaderStage.Pixel,"PSMain","SpatialUiPixel.hlsl");
        using var overlay=graphics.CreateGraphicsPipeline(new(vertex,ui) { CullMode=GraphicsCullMode.None });
        byte[] baseline=Capture();
        graphics.ConfigurePostProcessing(high);
        byte[] aa=Capture();
        string? captureRoot=Environment.GetEnvironmentVariable("FORGELINE_POST_REGRESSION_OUTPUT");
        if(!string.IsNullOrWhiteSpace(captureRoot))
        {
            Directory.CreateDirectory(captureRoot);
            Save("neutral",baseline);Save("aa",aa);
            void Save(string name,byte[] pixels)
            {
                using var file=File.Create(Path.Combine(captureRoot,$"post-edge-{atmosphere}-{name}.ppm"));
                file.Write(System.Text.Encoding.ASCII.GetBytes("P6\n320 240\n255\n"));
                for(int pixel=0;pixel<pixels.Length;pixel+=4) file.Write(pixels.AsSpan(pixel,3));
            }
        }
        Assert.Equal(aa,Capture());
        int thin=(120*320+120)*4;
        Assert.Equal(255,baseline[thin]);
        Assert.InRange(aa[thin],210,254);
        Assert.True(aa[(120*320+121)*4]>baseline[(120*320+121)*4]);
        int warning=(60*320+140)*4;
        Assert.InRange(aa[warning],210,255);
        Assert.True(aa[warning]>aa[warning+1] && aa[warning+1]>aa[warning+2]);
        for(int y=0;y<50;y++)
            Assert.Equal(baseline.AsSpan((y*320+240)*4,80*4).ToArray(),aa.AsSpan((y*320+240)*4,80*4).ToArray());
        graphics.ConfigurePostProcessing(high with { WhiteBalance=.05f, Tint=-.03f, Contrast=1.04f, Saturation=1.03f });
        byte[] graded=Capture();
        int background=(100*320+20)*4;
        Assert.True(graded[background]>graded[background+2]);
        Assert.Equal(aa.AsSpan(240*4,80*4).ToArray(),graded.AsSpan(240*4,80*4).ToArray());
        GraphicsPostProcessingDiagnostics diagnostics=graphics.Diagnostics.PostProcessing!.Value;
        Assert.Equal(5,diagnostics.MaximumSceneSamples);
        Assert.Equal(0,diagnostics.HistoryTargets);
        Assert.Equal(0,diagnostics.IntermediateTargets);
        Assert.Equal(1,diagnostics.ResolutionScale);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear,c=>Assert.Throws<InvalidOperationException>(()=>graphics.ConfigurePostProcessing(high)));
        Assert.Throws<InvalidOperationException>(()=>graphics.ConfigureSceneOutput(new(false,1,false)));
        graphics.Resize(0,0);
        ulong submitted=graphics.Diagnostics.Surface.SubmittedFrameCount;
        graphics.RenderFrame(GraphicsColor.ForgeLineClear, _=>throw new InvalidOperationException());
        Assert.Equal(submitted,graphics.Diagnostics.Surface.SubmittedFrameCount);
        graphics.Resize(400,300);
        graphics.RenderFrame(GraphicsColor.ForgeLineClear);
        byte[] resized=Capture();
        Assert.Equal(400*300*4,resized.Length);
        graphics.WaitForIdle();
        Assert.Equal(0,graphics.Diagnostics.Health.PendingRetirementCount);
        Assert.Equal(3+(atmosphere?1:0),graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);
        graphics.ConfigurePostProcessing(GraphicsPostProcessingSettings.Neutral);
        graphics.ConfigureAtmosphere(GraphicsAtmosphereSettings.Default);
        graphics.ConfigureSceneOutput(new(false,1,false));
        Assert.Equal(0,graphics.Diagnostics.Resources.ShaderResourceDescriptorsUsed);

        byte[] Capture()=>graphics.CaptureFrame(GraphicsColor.ForgeLineClear,c=> {
            c.SetPipeline(world); c.Draw(3);
            c.BeginPass(GraphicsFramePass.Overlay); c.SetPipeline(overlay);
            c.SetScissor(c.Width*3/4,0,c.Width,50); c.Draw(3);
        });
    }
}
