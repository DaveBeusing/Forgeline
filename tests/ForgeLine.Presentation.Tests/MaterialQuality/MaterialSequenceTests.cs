using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using Xunit;

namespace ForgeLine.Presentation.Tests.MaterialQuality;

[Collection("Native scene output")]
public sealed class MaterialSequenceTests
{
    private static readonly JsonSerializerOptions ReportOptions=new() {WriteIndented=true};
    private static readonly string[] Panels=["paint","bare-steel","rubber","concrete","sensor-emissive","normal-detail","uv-seam","nonuniform-scale","mirrored-scale","thin-barrel","fence-coverage","sloped-surface"];
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_MATERIAL_SEQUENCE_OUTPUT"));

    [Fact(Skip="Set FORGELINE_MATERIAL_SEQUENCE_OUTPUT and FORGELINE_MATERIAL_BASELINE_ASSEMBLY for isolated hardware measurement.",SkipUnless=nameof(Enabled))]
    public void CaptureControlledMaterialPanAndZoomPairs()
    {
        string root=Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_MATERIAL_SEQUENCE_OUTPUT")!);
        Assert.False(Directory.Exists(root));Directory.CreateDirectory(root);
        string baselinePath=Path.GetFullPath(Environment.GetEnvironmentVariable("FORGELINE_MATERIAL_BASELINE_ASSEMBLY")!);
        var baseline=Assembly.LoadFile(baselinePath);
        string oldLighting=ReadConstant(baseline,"PbrMaterialShader","Lighting");
        string oldShadow=ReadConstant(baseline,"DirectionalShadowShader","Disabled");
        using var platform=new WindowsPlatform();
        using IWindow window=platform.CreateWindow(new("FORGELINE Material Sequences",768,576));
        using var graphics=GraphicsDeviceFactory.CreateForWindow(window,new() {EnableVSync=false,EnableDebugLayer=false,AllowSoftwareAdapterFallback=false});
        Assert.False(graphics.Diagnostics.Device.IsSoftwareAdapter);
        var compiler=new DxcShaderCompiler();
        const string vertex="""
            cbuffer Parameters:register(b0) {float4 Settings;};
            struct Output {float4 Position:SV_Position; nointerpolation float4 Settings:TEXCOORD0;};
            Output VSMain(uint id:SV_VertexID) {
                Output o;float2 uv=float2((id<<1)&2,id&2);
                o.Position=float4(uv*float2(2,-2)+float2(-1,1),0,1);o.Settings=Settings;return o;
            }
            """;
        using var before=Pipeline(oldShadow+oldLighting);
        using var after=Pipeline(DirectionalShadowShader.Disabled+PbrMaterialShader.Lighting);
        var results=new List<object>();
        foreach(float frequency in new[] {8f,24f,56f})
        foreach(string member in new[] {"before-1","after-1","after-2","before-2"})
        {
            IGraphicsPipeline pipeline=member.StartsWith("before",StringComparison.Ordinal) ? before:after;
            float phase=0;
            Action<IGraphicsCommandContext> record=c=> {
                c.SetPipeline(pipeline);c.SetVertexConstants([c.Width,c.Height,phase,frequency]);c.Draw(3);
                c.BeginPass(GraphicsFramePass.Overlay);
            };
            for(int i=0;i<64;i++) graphics.RenderFrame(GraphicsColor.ForgeLineClear,record);
            var worldGpu=new List<double>();var cpu=new List<double>();ulong fence=0;
            for(int i=0;i<256;i++)
            {
                phase=i*.003f;
                long start=System.Diagnostics.Stopwatch.GetTimestamp();graphics.RenderFrame(GraphicsColor.ForgeLineClear,record);
                cpu.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                var d=graphics.Diagnostics;
                if(d.Frame is {} f && f.GpuSubmissionFence>fence)
                {fence=f.GpuSubmissionFence;if(f.WorldGpuMilliseconds is double ms)worldGpu.Add(ms);}
            }
            string directory=Path.Combine(root,$"frequency-{frequency}-{member}");Directory.CreateDirectory(directory);
            var captures=new List<object>();var motion=new double[12];byte[]? previous=null;
            for(int i=0;i<32;i++)
            {
                phase=i*.003f;
                byte[] pixels=graphics.CaptureFrame(GraphicsColor.ForgeLineClear,record);
                if(previous is not null)
                    for(int y=0;y<576;y++) for(int x=0;x<768;x++)
                    {
                        int tile=x/192+y/192*4;
                        // Keep panel silhouettes/coverage out of the shimmer diagnostic.
                        if(x%192<32 || x%192>=160 || y%192<32 || y%192>=160) continue;
                        int offset=(y*768+x)*4;
                        for(int channel=0;channel<3;channel++)motion[tile]+=Math.Abs(pixels[offset+channel]-previous[offset+channel]);
                    }
                string hash=Convert.ToHexString(SHA256.HashData(pixels));
                using(var stream=File.Create(Path.Combine(directory,$"{i:00}.rgba.gz")))
                using(var gzip=new GZipStream(stream,CompressionLevel.SmallestSize))gzip.Write(pixels);
                if(i==0 || i==16 || i==31)WritePpm(Path.Combine(directory,$"{i:00}.ppm"),pixels);
                captures.Add(new {frame=i,phase,sha256=hash});previous=pixels;
            }
            graphics.WaitForIdle();
            results.Add(new {frequency,member,width=768,height=576,warmups=64,samples=256,
                cpuMilliseconds=cpu,worldGpuMilliseconds=worldGpu,shadowGpuMilliseconds=(double?)null,
                motionMeanAbsoluteChannelDelta=motion.Select(v=>v/(31*128*128*3)).ToArray(),captures,
                resources=graphics.Diagnostics.Resources,health=graphics.Diagnostics.Health});
        }
        File.WriteAllText(Path.Combine(root,"report.json"),JsonSerializer.Serialize(new {
            baseline=baseline.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            current=typeof(RtsCamera).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            baselineAssemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baselinePath))),
            currentAssemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(RtsCamera).Assembly.Location))),
            graphics.Diagnostics.Device,graphics.Diagnostics.Surface,driver=Environment.GetEnvironmentVariable("FORGELINE_BENCHMARK_DRIVER"),
            panels=Panels,
            method="Controlled diagnostic uses production BRDF with analytic surfaces and normal detail, unchanged static light and 32-step pan; frequency is a sampling stress proxy, not a production camera distance. Fixed B-A-A-B cohorts; readbacks outside timing. It does not replace real asset, world-shadow or human material qualification.",results},ReportOptions));

        IGraphicsPipeline Pipeline(string lighting)=>graphics.CreateGraphicsPipeline(new(
            compiler.Compile(vertex,GraphicsShaderStage.Vertex,"VSMain","MaterialSequenceVertex.hlsl"),
            compiler.Compile("#define LINEAR_SCENE 0\n"+lighting+Pixel,GraphicsShaderStage.Pixel,"PSMain","MaterialSequencePixel.hlsl"))
            {VertexRootConstantCount=4,CullMode=GraphicsCullMode.None});
    }

    private static string ReadConstant(Assembly assembly,string type,string field)=>(string)assembly.GetType("ForgeLine.Presentation."+type,true)!
        .GetField(field,BindingFlags.NonPublic|BindingFlags.Static)!.GetRawConstantValue()!;

    private static void WritePpm(string path,byte[] rgba)
    {
        using var output=File.Create(path);output.Write(Encoding.ASCII.GetBytes("P6\n768 576\n255\n"));
        for(int i=0;i<rgba.Length;i+=4)output.Write(rgba.AsSpan(i,3));
    }

    private const string Pixel="""
        struct Output {float4 Position:SV_Position;nointerpolation float4 Settings:TEXCOORD0;};
        float4 PSMain(Output input):SV_Target {
            float2 grid=input.Position.xy/input.Settings.xy*float2(4,3);
            int panel=min(3,(int)grid.x)+min(2,(int)grid.y)*4;
            float2 uv=frac(grid)*2-1;uv.y=-uv.y;
            float radius=dot(uv,uv);
            float3 n=PbrNormalize(float3(uv*.8,sqrt(max(.01,1-radius*.64))),float3(0,0,1));
            float3 tangent=PbrNormalize(cross(float3(0,1,0),n),float3(1,0,0));
            float3 bitangent=cross(n,tangent);
            float2 detailUv=uv+float2(input.Settings.z,input.Settings.z*.37);
            float amplitude=panel==2 || panel==3 || panel==4 ? 0:.22;
            float2 detail=amplitude*sin(detailUv*input.Settings.w*3.14159265);
            if(panel==6) detail.x*=uv.x<0 ? -1:1;
            float orientation=1;
            if(panel==7 || panel==8) {
                float3x3 basis=float3x3(panel==8 ? -2:2,0,0,0,.5,0,0,0,1.5);
                n=PbrTransformNormal(n,basis,orientation);
                tangent=PbrNormalize(mul(tangent,basis)-n*dot(n,mul(tangent,basis)),float3(1,0,0));
                bitangent=cross(n,tangent)*orientation;
            }
            n=PbrNormalize(n+detail.x*tangent+detail.y*bitangent,n);
            float roughness=panel==1 ? .075:panel==2 ? .85:panel==3 ? .9:.35;
            float metallic=panel==1 ? 1:0;
            float3 albedo=panel==0 ? float3(.12,.18,.08):panel==1 ? .57.xxx:panel==2 ? .025.xxx:panel==3 ? .5.xxx:float3(.22,.25,.28);
            float3 emissive=panel==4 ? float3(.02,.8,.5):0;
            if(panel==11)n=PbrNormalize(n+float3(0,.7,0),float3(0,1,0));
            float3 color=ApplySceneLighting(albedo,n,1,roughness,metallic,emissive,
                float4(.2,.4,.89442719,1.1),float4(1,.96,.9,.52),float4(.58,.66,.76,1.15),float2(.42,1),float3(uv,0),float3(0,0,1));
            if(radius>.85 || (panel==9 && abs(uv.y)>.07) || (panel==10 && frac((uv.x+input.Settings.z)*12)>.2))return float4(.08,.1,.12,1);
            return float4(color,1);
        }
        """;
}
