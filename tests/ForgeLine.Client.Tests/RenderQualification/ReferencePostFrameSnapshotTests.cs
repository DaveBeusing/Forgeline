using System.Numerics;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ReferencePostFrameSnapshotTests
{
    private sealed class Output : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("forgeline-post-frame-");
        internal string Path => _root.FullName;
        public void Dispose() => _root.Delete(true);
    }
    private static VisualReferenceCaptureRequest Request() => new()
    {
        OutputRoot="cpu-only", RuntimeRoot="cpu-only", Commit=new string('a',40),
        Scenes=[VisualReferenceSceneKind.IndustrialBase],Profiles=[RenderQuality.Medium],Zooms=[RtsReferenceZoom.NormalGameplay],Displays=[new(1920,1080)],
        RunProfile=ReferenceRunProfile.Warm,CameraPath=ReferenceCameraPath.Fixed,CollectorMode=ReferenceCollectorMode.Full,
        Warmups=64,Samples=512,RepeatCaptures=8,ContinueAfterMismatch=true,OwnerObservationVersion=1,OwnerCycleCapacity=8192,RasterDpi=96,
        CapturePostFrameResourceSnapshotVersion=1
    };
    [Fact]
    public void DefaultWireAndSharedValidationRemainUnselected()
    {
        var request=Request(); Assert.Throws<NotSupportedException>(()=>request.Validate());
        request.Validate(false,true);
        using var wire=JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(request with {CapturePostFrameResourceSnapshotVersion=null},VisualReferenceCapture.JsonOptions));
        Assert.False(wire.RootElement.TryGetProperty("capturePostFrameResourceSnapshotVersion",out _));
        VisualReferenceCaptureRequest[] invalid=[request with {CapturePostFrameResourceSnapshotVersion=0},request with {CaptureStageDiagnostics=true},
            request with {CaptureShadowDiagnostics=true},request with {CaptureBoundInputProvenanceVersion=1},request with {OwnerTraceControlDirectory="trace"},
            request with {Warmups=63},request with {Samples=511},request with {RasterDpi=0},request with {ContinueAfterMismatch=false}];
        foreach(var value in invalid)Assert.Throws<NotSupportedException>(()=>value.Validate(true,true));
    }
    [Fact]
    public void CleanupAttemptsBothAndPreservesFirstFailure()
    {
        var first=new IOException("submit");var second=new IOException("retire");int calls=0;
        Assert.Same(first,Assert.Throws<IOException>(()=>ReferencePostFrameSnapshot.CleanupAssets(()=>{calls++;throw first;},()=>{calls++;throw second;})));
        Assert.Equal(2,calls);Assert.Contains("retire",first.Data["postFrameRetireCleanup"]!.ToString());
        Assert.Same(first,ReferencePostFrameSnapshot.SelectPrimary(second,first));
        Assert.Contains("retire",JsonSerializer.Serialize(ReferencePostFrameSnapshot.FailureFacts(first,"cleanup",null,[]),VisualReferenceCapture.JsonOptions));
        Assert.Same(second,ReferencePostFrameSnapshot.SelectPrimary(second,null));
    }
    private static (GraphicsPostFrameSnapshotResult Result, ReferencePostFrameSnapshot.Metadata Context) Fixture(string root)
    {
        byte[] pixels=new byte[16];ReferenceCaptureImage.WritePng(System.IO.Path.Combine(root,"synthetic.png"),2,2,pixels);
        var identity=new GraphicsDeviceTelemetryIdentity("0000000000000001",1,0,1,1,Environment.ProcessId,1,new string('e',32));
        var receipt=new GraphicsPostFrameOriginalReceipt(1,"original-device-frame-fence",identity,Environment.CurrentManagedThreadId,0,new(578,700,1,2),700,10,11,2,2,1,1);
        var buffer=new GraphicsPostFrameResourceDescription(1,"terrain-resident-vertex","buffer",true,new(144,GraphicsBufferMemory.Upload),null,null,0,144,"Retained only",[new(-1,0,0,1,144,144,0,144,144,"Buffer")]);
        var texture=new GraphicsPostFrameResourceDescription(2,"terrain-control","texture",true,null,new(4,4,GraphicsTextureFormat.Bc7Unorm,GraphicsTextureColorSpace.Srgb,2),null,0,32,"Encoded blocks",[new(0,4,4,1,16,256,512,256,16,"BC7_UNorm_SRgb"),new(1,2,2,1,16,256,1024,256,16,"BC7_UNorm_SRgb")]);
        var instances=new GraphicsPostFrameResourceDescription(3,"main-captured-slot-instances","buffer",true,new(112,GraphicsBufferMemory.Upload),null,null,0,112,"Actual-slot used instance prefix",[new(-1,0,0,1,112,112,1536,112,112,"Buffer")]);
        var vertices=new GraphicsPostFrameResourceDescription(4,"retained-batch-vertices","buffer",true,new(144,GraphicsBufferMemory.GpuLocal),null,null,0,144,"Three retained vertices",[new(-1,0,0,1,144,144,2048,144,144,"Buffer")]);
        var indices=new GraphicsPostFrameResourceDescription(5,"retained-batch-indices","buffer",true,new(12,GraphicsBufferMemory.GpuLocal),null,null,0,12,"Three retained32bit indices",[new(-1,0,0,1,12,12,2560,12,12,"Buffer")]);
        var terrainIndices=new GraphicsPostFrameResourceDescription(6,"terrain-shared-indices","buffer",true,new(12,GraphicsBufferMemory.GpuLocal),null,null,0,12,"Shared terrain32bit indices",[new(-1,0,0,1,12,12,3072,12,12,"Buffer")]);
        var intent=new GraphicsPostFrameIntent([new(0,4,1,1,Vector3.Zero,Vector3.One,0,"Stored history",1,["synthetic-material"],[new(0,3,1)],[new(1,"vertex"),new(6,"shared-indices"),new(2,"control"),new(2,"layer-0-base-color"),new(2,"layer-0-normal"),new(2,"layer-0-orm"),new(2,"layer-0-retained-emissive")])],
            [new(0,"synthetic-mesh",null,-1,1,0,112,48,0,3,"ThirtyTwoBit","Final retained scratch only",[new(3,"captured-slot-instance-prefix"),new(4,"mesh-vertex"),new(5,"mesh-index")])],[]);
        var result=new GraphicsPostFrameSnapshotResult(receipt,new(1,"late-copy-fence-"+new string('f',32),"Same DIRECT queue; source ordered after original frame completion",1,1,true,true),
            [new(buffer,[new byte[144]]),new(texture,[new byte[16],Enumerable.Repeat((byte)255,16).ToArray()]),
                new(instances,[new byte[112]]),new(vertices,[new byte[144]]),new(indices,[new byte[12]]),new(terrainIndices,[new byte[12]])],intent,3084,456,[]);
        var profile=new RenderQualitySettings().Resolve(true,false,2,2,3);
        byte[] recipe=JsonSerializer.SerializeToUtf8Bytes(new {display=new {width=2,height=2},settings=profile.RequestedSettings,runtimeFingerprint=new string('b',64)},VisualReferenceCapture.JsonOptions);
        var context=ReferencePostFrameSnapshot.Context(new string('b',64),ReferencePostFrameSnapshot.Hash(recipe),Convert.ToBase64String(recipe),0,
            new RtsCameraState(Vector3.Zero,.5f,.6f,50,false,Vector2.Zero),profile,new { profile,lighting=new {},atmosphere=new {} },pixels,new string('c',64),root,"synthetic.png");
        return(result,context);
    }
    [Fact]
    public void NonemptyActualTypedExporterRetainsRawHashGraphAndSyntheticFixture()
    {
        using var root=new Output();var fixture=Fixture(root.Path);
        object reference=ReferencePostFrameSnapshot.Write(fixture.Result,fixture.Context,root.Path,"synthetic",0);
        byte[] refRaw=JsonSerializer.SerializeToUtf8Bytes(reference,VisualReferenceCapture.JsonOptions);using var refs=JsonDocument.Parse(refRaw);
        byte[] raw=File.ReadAllBytes(System.IO.Path.Combine(root.Path,refs.RootElement.GetProperty("file").GetString()!));
        Assert.Equal(ReferencePostFrameSnapshot.Hash(raw),refs.RootElement.GetProperty("sha256").GetString());
        using var value=JsonDocument.Parse(raw);Assert.True(value.RootElement.GetProperty("complete").GetBoolean());Assert.Equal(6,value.RootElement.GetProperty("resources").GetArrayLength());
        var batch=Assert.Single(value.RootElement.GetProperty("intent").GetProperty("mainBatches").EnumerateArray());
        Assert.Equal(-1,batch.GetProperty("partIndex").GetInt32());Assert.Equal(112,batch.GetProperty("instanceStride").GetInt32());
        foreach(var resource in value.RootElement.GetProperty("resources").EnumerateArray())foreach(var mip in resource.GetProperty("rawMips").EnumerateArray())
            Assert.Equal(mip.GetProperty("sha256").GetString(),ReferencePostFrameSnapshot.Hash(File.ReadAllBytes(System.IO.Path.Combine(root.Path,mip.GetProperty("file").GetString()!))));
        string? output=Environment.GetEnvironmentVariable("FORGELINE_POST_FRAME_CPU_FIXTURE_OUTPUT");
        if(output is not null)
        {
            Assert.False(Directory.Exists(output));Directory.CreateDirectory(output);
            foreach(string file in Directory.GetFiles(root.Path))File.Copy(file,System.IO.Path.Combine(output,System.IO.Path.GetFileName(file)));
            File.WriteAllBytes(System.IO.Path.Combine(output,"producer-reference.json"),refRaw);
            File.WriteAllText(System.IO.Path.Combine(output,"synthetic-provenance.json"),JsonSerializer.Serialize(new {synthetic=true,qualified=false,rendererExecuted=false,nativeDeviceCreated=false,sourceTreeCleanVerified=false},VisualReferenceCapture.JsonOptions));
        }
    }
    [Fact]
    public void CreateNewFailureKeepsPngAndOriginalWithActualPrefix()
    {
        using var root=new Output();var fixture=Fixture(root.Path);
        string file=System.IO.Path.Combine(root.Path,"synthetic-readback-00-post-frame-resource-0002-mip-01.raw");File.WriteAllText(file,"old");
        Assert.Throws<IOException>(()=>ReferencePostFrameSnapshot.Write(fixture.Result,fixture.Context,root.Path,"synthetic",0));
        Assert.Equal("old",File.ReadAllText(file));Assert.True(File.Exists(System.IO.Path.Combine(root.Path,"synthetic.png")));
        using var prefix=JsonDocument.Parse(File.ReadAllBytes(System.IO.Path.Combine(root.Path,"synthetic-readback-00-post-frame-export.partial.json")));
        Assert.False(prefix.RootElement.GetProperty("complete").GetBoolean());Assert.Equal(1,prefix.RootElement.GetProperty("resources")[1].GetProperty("rawMips").GetArrayLength());
    }
    [Fact]
    public void CompletionTamperAndEnvelopeOverflowNeverProduceSuccess()
    {
        using var root=new Output();var fixture=Fixture(root.Path);
        Assert.Throws<InvalidDataException>(()=>ReferencePostFrameSnapshot.Write(fixture.Result with {CopyReceipt=fixture.Result.CopyReceipt with {CompletionProved=false}},fixture.Context,root.Path,"synthetic",0));
        Assert.Throws<InvalidDataException>(()=>ReferencePostFrameSnapshot.Write(fixture.Result,fixture.Context with {RecipeInputBytesBase64=new string('x',1024*1024+1)},root.Path,"synthetic",0));
        Assert.False(File.Exists(System.IO.Path.Combine(root.Path,"synthetic-readback-00-post-frame.json")));
        using var prefix=JsonDocument.Parse(File.ReadAllBytes(System.IO.Path.Combine(root.Path,"synthetic-readback-00-post-frame-export.partial.json")));
        Assert.True(prefix.RootElement.GetProperty("metadata").GetProperty("contextUnavailable").GetBoolean());
        Assert.Equal("export-incomplete",prefix.RootElement.GetProperty("metadata").GetProperty("failure").GetProperty("phase").GetString());
    }
    private sealed class CleanupFailure(Exception error) : IDisposable
    {
        public void Dispose()=>throw error;
    }
    private static CleanupFailure ConstructorFailure(Exception error)=>throw error;
    [Theory]
    [InlineData("row")]
    [InlineData("manifest")]
    [InlineData("next-constructor")]
    public void ActualUsingUnwindKeepsBodyOrConstructorErrorBeforeTwoCleanupErrors(string phase)
    {
        var body=new IOException(phase);var inner=new IOException("asset-dispose");var outer=new IOException("device-dispose");Exception? primary=null;
        try
        {
            using var device=new CleanupFailure(outer);
            try
            {
                using var asset=phase=="next-constructor" ? ConstructorFailure(body) : new CleanupFailure(inner);
                try { throw body; }
                catch(Exception error){primary=ReferencePostFrameSnapshot.Remember(primary,error);throw;}
            }
            catch(Exception error){primary=ReferencePostFrameSnapshot.Remember(primary,error);throw;}
        }
        catch(Exception caught)
        {
            Assert.Same(body,ReferencePostFrameSnapshot.SelectPrimary(caught,primary));
            using var root=new Output();ReferencePostFrameSnapshot.RetainReportingFailure(body,root.Path);
            string wire=File.ReadAllText(System.IO.Path.Combine(root.Path,"post-frame-error-reporting.partial.json"));
            Assert.Contains(phase,wire);Assert.Contains("device-dispose",wire);
            if(phase!="next-constructor")Assert.Contains("asset-dispose",wire);
            return;
        }
    }
    [Fact]
    public void BeforeReceiptFailureKeepsImagePhaseOnlyAndLaterCleanupSupplementNeverReplacesPrefix()
    {
        using var root=new Output();_ = Fixture(root.Path);var original=new IOException("receipt refusal");
        ReferencePostFrameSnapshot.RetainFailure(original,root.Path,"early",null,null,"receipt-issue","synthetic.png",null);
        string path=System.IO.Path.Combine(root.Path,"early-post-frame-failure.partial.json");byte[] before=File.ReadAllBytes(path);
        using var prefix=JsonDocument.Parse(before);Assert.False(prefix.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null,prefix.RootElement.GetProperty("originalFrameReceipt").ValueKind);
        Assert.Equal(JsonValueKind.Null,prefix.RootElement.GetProperty("copyReceipt").ValueKind);
        var metadata=prefix.RootElement.GetProperty("metadata");Assert.Equal("receipt-issue",metadata.GetProperty("failure").GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null,metadata.GetProperty("failure").GetProperty("mayHaveExecuted").ValueKind);
        Assert.False(metadata.GetProperty("context").GetProperty("captureReceiptAvailable").GetBoolean());Assert.NotEqual(JsonValueKind.Null,metadata.GetProperty("context").GetProperty("image").ValueKind);
        ReferencePostFrameSnapshot.Remember(original,new IOException("late-device-dispose"));ReferencePostFrameSnapshot.RetainReportingFailure(original,root.Path);
        Assert.Equal(before,File.ReadAllBytes(path));Assert.Contains("late-device-dispose",File.ReadAllText(System.IO.Path.Combine(root.Path,"post-frame-error-reporting.partial.json")));
    }
}
