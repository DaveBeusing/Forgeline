using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using ForgeLine.Client;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ReferenceWorldInputDiagnosticsTests
{
    [Fact]
    public void NativeTwoSitesThenSingleSitePreserveUnknownSkippedPredicateAndTypedNulls()
    {
        using var data=new Fixture();var inspector=data.Create("native-reference");
        using(inspector.Arm(0)){NativeZero(inspector);data.Complete(inspector);var reference=inspector.Persist(data.Stage());Assert.NotNull(reference);}
        using var json=JsonDocument.Parse(File.ReadAllBytes(data.Output(0)));
        var rows=json.RootElement.GetProperty("observations");Assert.Equal(5,rows.GetArrayLength());
        Assert.Equal(JsonValueKind.Null,rows[2].GetProperty("preparationInvoked").ValueKind);Assert.False(rows[3].GetProperty("preparationInvoked").GetBoolean());
        var facts=rows[4].GetProperty("stateAndActivityFacts");Assert.Equal(JsonValueKind.Null,facts.GetProperty("logicalMemory").ValueKind);Assert.Equal(JsonValueKind.Null,facts.GetProperty("instances").GetProperty("assets").ValueKind);
        Assert.False(facts.GetProperty("terrain").GetProperty("last").TryGetProperty("cpuSubmissionMilliseconds",out _));Assert.True(rows[4].GetProperty("timingFacts").TryGetProperty("terrain.last.cpuSubmissionMilliseconds",out _));
        Assert.Equal(0,json.RootElement.GetProperty("captured").GetProperty("frameIndex").GetInt32());Assert.Equal(1,facts.GetProperty("graphics").GetProperty("surface").GetProperty("frameIndex").GetInt32());
        data.Advance();
        using(inspector.Arm(1)){inspector.BeforeSite(WorldInputSite.CaptureEnsurePrepared);inspector.AfterSite(WorldInputSite.CaptureEnsurePrepared,true);data.Complete(inspector);inspector.Persist(data.Stage());}
        Assert.Equal(8,inspector.Count);Assert.False(inspector.Armed);
    }
    [Fact]
    public void StageEnabledButUnarmedSharedHelperCallsDoNotConsumeFixedSlotsOrReadFacts()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");
        for(int i=0;i<8;i++){inspector.BeforeSite(WorldInputSite.CaptureEnsurePrepared);inspector.AfterSite(WorldInputSite.CaptureEnsurePrepared,true);inspector.AfterCapture(null);Assert.Null(inspector.Persist(null));}
        Assert.Equal(0,data.Reads);Assert.Equal(0,inspector.Count);Assert.Empty(Directory.GetFiles(data.Root,"*world-inputs.json"));
        using(inspector.Arm(0)){Host(inspector);data.Complete(inspector);inspector.Persist(data.Stage());}
        inspector.BeforeSite(WorldInputSite.CaptureEnsurePrepared);Assert.Equal(3,inspector.Count);
    }
    [Theory]
    [InlineData(0)][InlineData(129)][InlineData(int.MaxValue)]
    public void UnsupportedStorageRejectedBeforeAllocation(int repeats)
    {using var data=new Fixture();Assert.Throws<ArgumentOutOfRangeException>(()=>data.Create("native-reference",repeats));}
    [Fact]
    public void MissingDuplicateRegressingOwnerAndPredicatePhasesCannotPublishComplete()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");
        Assert.Throws<InvalidOperationException>(()=>inspector.Arm(1));
        using(inspector.Arm(0))
        {
            inspector.BeforeSite(WorldInputSite.HostReadbackPrepare);
            Assert.Throws<InvalidDataException>(()=>inspector.BeforeSite(WorldInputSite.HostReadbackPrepare));
            Assert.Throws<InvalidDataException>(()=>inspector.AfterSite(WorldInputSite.HostReadbackPrepare,false));
            Exception? foreign=null;var thread=new Thread(()=>{try{inspector.AfterSite(WorldInputSite.HostReadbackPrepare,true);}catch(Exception e){foreign=e;}});thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(5)));Assert.IsType<InvalidOperationException>(foreign);
            Assert.Throws<InvalidDataException>(()=>inspector.Persist(data.Stage()));
        }
        using var json=JsonDocument.Parse(File.ReadAllBytes(data.Output(0)));Assert.False(json.RootElement.GetProperty("complete").GetBoolean());Assert.Single(json.RootElement.GetProperty("observations").EnumerateArray());
    }
    [Theory]
    [InlineData("fence")][InlineData("slot")][InlineData("slot-in-range")][InlineData("resource")][InlineData("owner")][InlineData("present")]
    public void ActualCaptureTupleMustMatchPublicAfterFacts(string mutation)
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");using var arm=inspector.Arm(0);Host(inspector);
        var capture=mutation switch {"fence"=>data.Capture with {CompletedFence=1},"slot"=>data.Capture with {FrameIndex=3},"slot-in-range"=>data.Capture with {FrameIndex=2},"resource"=>data.Capture with {Submission=data.Capture.Submission with {ResourceGeneration=2}},"owner"=>data.Capture with {DeviceIdentity=data.Capture.DeviceIdentity with {ProcessId=1}},_=>data.Capture with {PresentedAfter=3}};
        Assert.Throws<InvalidDataException>(()=>data.Complete(inspector,capture));
    }
    [Fact]
    public void PartialPrefixRetainsOriginalExceptionAndCreateNewNeverOverwrites()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");var original=new InvalidOperationException("prepare failed");
        Action fail=()=>{using var arm=inspector.Arm(0);inspector.BeforeSite(WorldInputSite.HostReadbackPrepare);throw original;};
        var caught=Assert.Throws<InvalidOperationException>(fail);Assert.Same(original,caught);
        byte[] prefix=File.ReadAllBytes(data.Output(0));
        using var json=JsonDocument.Parse(prefix);Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
        var second=data.Create("actual-render-host");using(var arm=second.Arm(0)){Host(second);data.Complete(second);Assert.Throws<IOException>(()=>second.Persist(data.Stage()));}
        Assert.Equal(prefix,File.ReadAllBytes(data.Output(0)));
    }
    [Fact]
    public void StageHashTamperCannotPublishACompleteInspector()
    {using var data=new Fixture();var inspector=data.Create("actual-render-host");using var arm=inspector.Arm(0);Host(inspector);data.Complete(inspector);Assert.Throws<InvalidDataException>(()=>inspector.Persist(new {file="stage.json",sha256=new string('0',64)}));}
    [Fact]
    public void RejectedOverlappingArmCannotResumeOriginalScopeIntoCompleteEvidence()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");
        using(inspector.Arm(0))
        {
            inspector.BeforeSite(WorldInputSite.HostReadbackPrepare);
            Assert.Throws<InvalidOperationException>(()=>inspector.Arm(0));
            Assert.Throws<InvalidDataException>(()=>inspector.AfterSite(WorldInputSite.HostReadbackPrepare,true));
            Assert.Throws<InvalidDataException>(()=>inspector.Persist(data.Stage()));
        }
        using var json=JsonDocument.Parse(File.ReadAllBytes(data.Output(0)));Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
    }
    [Fact]
    public void FailedArmedPhaseCannotBeCorrectedIntoACompleteRecord()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");
        using(inspector.Arm(0))
        {
            inspector.BeforeSite(WorldInputSite.HostReadbackPrepare);
            Assert.Throws<InvalidDataException>(()=>inspector.AfterSite(WorldInputSite.HostReadbackPrepare,false));
            Assert.Throws<InvalidDataException>(()=>inspector.AfterSite(WorldInputSite.HostReadbackPrepare,true));
            Assert.Throws<InvalidDataException>(()=>data.Complete(inspector));
            Assert.Throws<InvalidDataException>(()=>inspector.Persist(data.Stage()));
        }
        using var json=JsonDocument.Parse(File.ReadAllBytes(data.Output(0)));Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
        Assert.Throws<InvalidOperationException>(()=>inspector.Arm(0));
    }
    [Fact]
    public void ReplayedCompletedTupleCannotBePublishedForNextRepetition()
    {
        using var data=new Fixture();var inspector=data.Create("actual-render-host");
        using(inspector.Arm(0)){Host(inspector);data.Complete(inspector);inspector.Persist(data.Stage());}
        using(inspector.Arm(1)){Host(inspector);Assert.Throws<InvalidDataException>(()=>data.Complete(inspector));Assert.Throws<InvalidDataException>(()=>inspector.Persist(data.Stage()));}
    }
    [Fact]
    public void CompiledSampleAndPreparationHelpersHaveNoInspectorHooks()
    {
        var methods=AllTypes(typeof(VisualReferenceCapture)).SelectMany(t=>t.GetMethods(BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)).ToArray();
        foreach(string name in new[]{"g__PrepareTerrain","g__RecordFrame"})
        {var method=Assert.Single(methods,m=>m.Name.Contains(name,StringComparison.Ordinal));Assert.DoesNotContain(Calls(method),m=>m.DeclaringType==typeof(ReferenceWorldInputDiagnostics));}
        var capture=Assert.Single(methods,m=>m.Name.Contains("g__CaptureFrame",StringComparison.Ordinal));Assert.Single(Calls(capture),m=>m.Name.Contains("g__PrepareTerrain",StringComparison.Ordinal));
    }
    private static void NativeZero(ReferenceWorldInputDiagnostics inspector)
    {inspector.BeforeSite(WorldInputSite.NativeFixedPoseRestore);inspector.AfterSite(WorldInputSite.NativeFixedPoseRestore,true);inspector.BeforeSite(WorldInputSite.CaptureEnsurePrepared);inspector.AfterSite(WorldInputSite.CaptureEnsurePrepared,false);}
    private static void Host(ReferenceWorldInputDiagnostics inspector){inspector.BeforeSite(WorldInputSite.HostReadbackPrepare);inspector.AfterSite(WorldInputSite.HostReadbackPrepare,true);}
    private static IEnumerable<Type> AllTypes(Type type)=>new[]{type}.Concat(type.GetNestedTypes(BindingFlags.NonPublic|BindingFlags.Public).SelectMany(AllTypes));
    private static readonly Dictionary<short,OpCode> s_codes=typeof(OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public).Select(f=>(OpCode)f.GetValue(null)!).GroupBy(c=>c.Value).ToDictionary(g=>g.Key,g=>g.First());
    private static IEnumerable<MethodBase> Calls(MethodInfo method)
    {
        byte[] il=method.GetMethodBody()?.GetILAsByteArray() ?? [];int p=0;
        while(p<il.Length){short code=il[p++];if(code==0xfe)code=(short)(0xfe00|il[p++]);var op=s_codes[code];if(op.OperandType==OperandType.InlineMethod && method.Module.ResolveMethod(BitConverter.ToInt32(il,p)) is { } called)yield return called;
            p+=op.OperandType switch{OperandType.InlineNone=>0,OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,OperandType.InlineVar=>2,OperandType.InlineI8 or OperandType.InlineR=>8,OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(il,p),_=>4};}
    }
    private sealed class Fixture:IDisposable
    {
        private readonly DirectoryInfo _directory=Directory.CreateTempSubdirectory("forgeline-world-input-");
        internal string Root=>_directory.FullName;internal int Reads;private bool _afterCapture;
        internal GraphicsStageCapture Capture {get;private set;}
        internal Fixture()
        {var identity=new GraphicsDeviceTelemetryIdentity("0000000000000001",1,0,1,1,Environment.ProcessId,1,new string('e',32));Capture=new([],new(1,5,1,1),5,0,0,1,identity,[]){BufferCount=3};File.WriteAllText(Path.Combine(Root,"stage.json"),"{}");}
        internal ReferenceWorldInputDiagnostics Create(string producer,int repeats=2)=>new(Root,"case",producer,new string('b',64),new string('c',64),"e30=",new {quality="cpu-fixture"},repeats,Facts,static()=>default);
        internal void Complete(ReferenceWorldInputDiagnostics inspector,GraphicsStageCapture? capture=null){_afterCapture=true;try{inspector.AfterCapture(capture ?? Capture);}finally{_afterCapture=false;}}
        internal void Advance()=>Capture=Capture with {Submission=new(Capture.Submission.Submission+1,Capture.Submission.SubmittedFence+3,1,1),CompletedFence=Capture.Submission.SubmittedFence+3,FrameIndex=(Capture.FrameIndex+1)%3,PresentedBefore=Capture.PresentedAfter,PresentedAfter=Capture.PresentedAfter+1};
        internal object Stage()=>new {file="stage.json",sha256=VisualReferenceCapture.FileHash(Path.Combine(Root,"stage.json"))};
        internal string Output(int repeat)=>Path.Combine(Root,"case-readback-"+repeat.ToString("D2",System.Globalization.CultureInfo.InvariantCulture)+"-world-inputs.json");
        private WorldInputFacts Facts(){Reads++;var g=new GraphicsDiagnostics(new("CPU-only",0,true,"fixture",false),new(2,2,3,_afterCapture ? (Capture.FrameIndex+1)%3 : Capture.FrameIndex,false,"fixture"){PresentedFrameCount=Capture.PresentedAfter});return new(new(true,default,default,default),new(true,default,default,default,default,default,null,default),default,g,null,null,Capture.Submission,Capture.DeviceIdentity,null);}
        public void Dispose()=>_directory.Delete(recursive:true);
    }
}
