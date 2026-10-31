using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ForgeLine.Client;
using Xunit;

namespace ForgeLine.Client.Tests.RenderQualification;

public sealed class PostWindowControlTests
{
    [Fact]
    public void ActualFilesBindReadyFreshQueryDurablePrefixAndPumpUntilAck()
    {
        using var fixture=new Fixture();int pumped=0;
        fixture.Control.WaitForAck(()=>{pumped++;fixture.WriteAck();return true;});
        Assert.Equal(1,pumped);Assert.True(fixture.Control.Completed);Assert.NotNull(fixture.Control.Receipt);
    }
    [Theory]
    [InlineData("foreign-window")][InlineData("query-before-ready")][InlineData("prefix-changed")]
    [InlineData("last-row-changed")][InlineData("provider-changed")][InlineData("receipt-kind")]
    [InlineData("ready-hash")][InlineData("bool-index")]
    public void MalformedForeignOrUndurableAckCannotComplete(string mutation)
    {
        using var fixture=new Fixture();fixture.WriteAck(mutation);
        Assert.ThrowsAny<Exception>(()=>fixture.Control.TryComplete());Assert.False(fixture.Control.Completed);
    }
    [Fact]
    public void MissingAckRemainsPendingAndCancellationDoesNotFabricateCompletion()
    {
        using var fixture=new Fixture();Assert.False(fixture.Control.TryComplete());
        Assert.Throws<OperationCanceledException>(()=>fixture.Control.WaitForAck(()=>false));Assert.False(fixture.Control.Completed);
    }
    [Fact]
    public void OriginalExpiredCommandBudgetCannotBeRestartedAtSeal()
    {
        using var fixture=new Fixture(createControl:false);
        var binding=JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixture.Identity,"binding.json")))!;
        binding["commandContext"]!["startedAtUtc"]=JsonValue.Create(DateTimeOffset.UtcNow.AddSeconds(-601));
        File.WriteAllText(Path.Combine(fixture.Identity,"binding.json"),binding.ToJsonString());
        Assert.Throws<TimeoutException>(()=>ReferencePostWindowControl.Create(fixture.Post,fixture.Identity));
    }
    private sealed class Fixture:IDisposable
    {
        private static readonly long[] s_candidates=[43L];
        private readonly DirectoryInfo _root=Directory.CreateTempSubdirectory("forgeline-post-control-");
        internal string Identity { get; }
        internal string Post { get; }
        internal ReferencePostWindowControl Control { get; }
        private JsonObject? _ready;
        internal Fixture(bool createControl=true)
        {
            Identity=Path.Combine(_root.FullName,"identity");Post=Path.Combine(_root.FullName,"post-window");Directory.CreateDirectory(Identity);
            var checkpoint=new {sourceSha=ClientBuildIdentity.SourceRevision,runtimeFingerprint=new string('b',64),runId="synthetic-cpu-regression",adapterIdentity=new {processId=Environment.ProcessId},nativeWindowHandle=43L};
            byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(checkpoint,VisualReferenceCapture.JsonOptions);
            File.WriteAllBytes(Path.Combine(Identity,"checkpoint.json"),bytes);
            File.WriteAllBytes(Path.Combine(Identity,"binding.json"),JsonSerializer.SerializeToUtf8Bytes(new {
                exactPhysicalMappingAvailable=true,nativeManifestSha256=Hash(bytes),rendererIdentity=checkpoint.adapterIdentity,uuid="GPU-aaaa-bbbb",pciBusId="00000000:30:00.0",
                commandContext=new {processId=Environment.ProcessId,sourceSha=ClientBuildIdentity.SourceRevision,timeoutSeconds=600,startedAtUtc=DateTimeOffset.UtcNow}},VisualReferenceCapture.JsonOptions));
            Control=null!;if(!createControl)return;
            Control=ReferencePostWindowControl.Create(Post,Identity)!;long end=Stopwatch.GetTimestamp();
            Control.PublishReady(new("actual-live-session","actual-session",false,null,Hash(Encoding.UTF8.GetBytes("{}")),1,new string('d',64),Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))),512,end-1000,end,new(512,600,1,1));
            _ready=JsonNode.Parse(File.ReadAllBytes(Path.Combine(Post,"ready.json")))!.AsObject();
        }
        internal void WriteAck(string? mutation=null)
        {
            byte[] ready=File.ReadAllBytes(Path.Combine(Post,"ready.json"));
            DateTimeOffset at=DateTimeOffset.UtcNow;
            var sample=JsonSerializer.SerializeToNode(new {acquisitionStartedAtUtc=at,observedAtUtc=at,postWindowReadySha256=Hash(ready).ToLowerInvariant(),
                command=new {exitCode=0,timedOut=false},devices=new[]{new {uuid="GPU-aaaa-bbbb",pciBusId="00000000:30:00.0"}},
                window=new {status="controlled",requestedPid=Environment.ProcessId,ownerPid=Environment.ProcessId,hwnd=43L,foregroundHwnd=43L,foregroundOwnerPid=Environment.ProcessId,visible=true,iconic=false,reasons=Array.Empty<string>(),candidates=s_candidates}},VisualReferenceCapture.JsonOptions)!.AsObject();
            if(mutation=="foreign-window")sample["window"]!["foregroundHwnd"]=99;
            if(mutation=="query-before-ready")sample["acquisitionStartedAtUtc"]=JsonValue.Create(at.AddSeconds(-10));
            var row=new JsonObject{["index"]=0,["sample"]=sample.DeepClone()};
            if(mutation=="bool-index")row["index"]=true;
            byte[] rowBytes=Encoding.UTF8.GetBytes(row.ToJsonString()+"\n");
            string sensors=Path.Combine(_root.FullName,"sensors");Directory.CreateDirectory(sensors);
            byte[] journal=mutation=="last-row-changed" ? Encoding.UTF8.GetBytes(new string('x',rowBytes.Length)) : rowBytes;
            File.WriteAllBytes(Path.Combine(sensors,"observations.jsonl"),journal);
            var prefix=new {schemaVersion=1,kind=mutation=="receipt-kind" ? "foreign" : "physical-sensor-journal-progress",qualified=false,rows=1,journal="observations.jsonl",prefixBytes=journal.Length,prefixSha256=mutation=="prefix-changed" ? new string('0',64) : Hash(journal),lastObservation=sample,failure=(string?)null};
            byte[] prefixBytes=JsonSerializer.SerializeToUtf8Bytes(prefix,VisualReferenceCapture.JsonOptions);
            var ack=new JsonObject{["schemaVersion"]=1,["kind"]="ordinary-post-window-control-ack",["qualified"]=false,["readySha256"]=mutation=="ready-hash" ? new string('0',64) : Hash(ready),
                ["sampleIndex"]=0,["providerUuid"]=mutation=="provider-changed" ? "GPU-ffff" : "GPU-aaaa-bbbb",["providerPciBusId"]="00000000:30:00.0",
                ["rowBytesBase64"]=Convert.ToBase64String(rowBytes),["rowSha256"]=Hash(rowBytes),["prefixReceiptBytesBase64"]=Convert.ToBase64String(prefixBytes),["prefixReceiptSha256"]=Hash(prefixBytes)};
            foreach(string key in new[]{"sourceSha","runtimeFingerprint","runId","checkpointSha256","processId","nativeWindowHandle","caseIdentity","lastSubmission"})ack[key]=_ready![key]!.DeepClone();
            File.WriteAllText(Path.Combine(Post,"ack.json"),ack.ToJsonString());
        }
        private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
        public void Dispose()=>_root.Delete(recursive:true);
    }
}
