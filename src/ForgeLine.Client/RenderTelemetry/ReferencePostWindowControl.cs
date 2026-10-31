using System.Diagnostics;
using ForgeLine.Graphics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ForgeLine.Client;

internal sealed record PostWindowCaseIdentity(string Kind,string CaseKey,bool RecipeAvailable,
    string? RecipeFingerprint,string ProfileFingerprint,ulong? SimulationSession=null,string? SessionOriginFingerprint=null,string? ProfileInputBytesBase64=null);

// Ordinary control is distinct from the trace hold. Its deadline is inherited
// once from the actual launcher command context, never restarted at window end.
internal sealed class ReferencePostWindowControl
{
    private readonly string _directory,_checkpointSha,_run,_source,_runtime,_providerUuid,_providerPci;
    private readonly int _pid;
    private readonly long _hwnd,_deadline;
    private byte[]? _ready;
    private JsonObject? _readyObject;
    internal bool Completed { get; private set; }
    internal bool ReadyPublished=>_ready is not null;
    internal object Evidence()=>new {schemaVersion=1,kind="ordinary-post-window-control",readyPublished=ReadyPublished,completed=Completed,receipt=Receipt,qualified=false};
    internal object? Receipt { get; private set; }
    private ReferencePostWindowControl(string directory,JsonObject checkpoint,byte[] bytes,JsonObject binding)
    {
        _directory=directory;_checkpointSha=Hash(bytes);_providerUuid=binding["uuid"]!.GetValue<string>();_providerPci=binding["pciBusId"]!.GetValue<string>();_run=checkpoint["runId"]!.GetValue<string>();
        _source=checkpoint["sourceSha"]!.GetValue<string>();_runtime=checkpoint["runtimeFingerprint"]!.GetValue<string>();
        _pid=checkpoint["adapterIdentity"]!["processId"]!.GetValue<int>();_hwnd=checkpoint["nativeWindowHandle"]!.GetValue<long>();
        if(binding["exactPhysicalMappingAvailable"]?.GetValue<bool>()!=true || _pid!=Environment.ProcessId || _source!=ClientBuildIdentity.SourceRevision ||
            !string.Equals(binding["nativeManifestSha256"]?.GetValue<string>(),_checkpointSha,StringComparison.OrdinalIgnoreCase) ||
            !JsonNode.DeepEquals(binding["rendererIdentity"],checkpoint["adapterIdentity"]))throw new InvalidDataException("Post-window control lacks the actual pre-window producer checkpoint binding.");
        var context=binding["commandContext"]!.AsObject();
        if(context["processId"]?.GetValue<int>()!=_pid || context["sourceSha"]?.GetValue<string>()!=_source)throw new InvalidDataException("Post-window command context belongs to another process/source.");
        int seconds=context["timeoutSeconds"]!.GetValue<int>();
        if(seconds is < 1 or > 600)throw new InvalidDataException("Original post-window command timeout is unsupported.");
        DateTimeOffset started=context["startedAtUtc"]!.GetValue<DateTimeOffset>();
        double remaining=(started.AddSeconds(seconds)-DateTimeOffset.UtcNow).TotalSeconds;
        if(remaining<=0 || remaining>seconds)throw new TimeoutException("Original command budget no longer permits post-window observation.");
        _deadline=checked(Stopwatch.GetTimestamp()+(long)(remaining*Stopwatch.Frequency));
    }
    internal static ReferencePostWindowControl? Create(string? directory,string? checkpointDirectory)
    {
        if(string.IsNullOrWhiteSpace(directory))return null;
        if(string.IsNullOrWhiteSpace(checkpointDirectory))throw new InvalidDataException("Ordinary post-window control requires actual pre-window checkpoint evidence.");
        string output=Path.GetFullPath(directory),payload=Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        if(output.Equals(payload,StringComparison.OrdinalIgnoreCase) || output.StartsWith(payload+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Post-window evidence cannot mutate the producer payload.");
        byte[] checkpoint=File.ReadAllBytes(Path.Combine(checkpointDirectory,"checkpoint.json"));
        var result=new ReferencePostWindowControl(output,JsonNode.Parse(checkpoint)!.AsObject(),checkpoint,
            JsonNode.Parse(File.ReadAllBytes(Path.Combine(checkpointDirectory,"binding.json")))!.AsObject());
        Directory.CreateDirectory(output);return result;
    }
    internal void PublishReady(PostWindowCaseIdentity identity,int samples,long lastMeasuredEndTick,long? lastCycleEndTick,GraphicsFrameSubmission lastSubmission)
    {
        if(_ready is not null)throw new InvalidOperationException("A sealed window cannot publish a different ready record.");
        if(samples<1 || lastMeasuredEndTick<=0 || lastCycleEndTick<lastMeasuredEndTick)throw new InvalidDataException("Actual sealed observation endpoints are missing.");
        if(lastSubmission.Submission==0 || lastSubmission.SubmittedFence==0 || lastSubmission.Generation==0 || lastSubmission.ResourceGeneration==0)throw new InvalidDataException("Actual final submission identity is missing.");
        byte[] profile=Convert.FromBase64String(identity.ProfileInputBytesBase64 ?? throw new InvalidDataException("Actual composition bytes missing."));
        if(!string.Equals(Hash(profile),identity.ProfileFingerprint,StringComparison.OrdinalIgnoreCase) || JsonNode.Parse(profile) is not JsonObject)throw new InvalidDataException("Actual composition bytes/hash differ.");
        long sealedTick=Stopwatch.GetTimestamp();
        _ready=JsonSerializer.SerializeToUtf8Bytes(new {schemaVersion=1,kind="ordinary-post-window-control-ready",qualified=false,
            sourceSha=_source,runtimeFingerprint=_runtime,runId=_run,checkpointSha256=_checkpointSha,processId=_pid,nativeWindowHandle=_hwnd,
            caseIdentity=identity,lastSubmission,sampleCount=samples,lastMeasuredEndTick,lastCycleEndTick,ownerCycleAvailable=lastCycleEndTick.HasValue,
            endpointKind=lastCycleEndTick.HasValue ? "owner-cycle-v1" : "native-legacy-frame",sealedEndTick=sealedTick,sealedAtUtc=DateTimeOffset.UtcNow,frequencyHz=Stopwatch.Frequency,
            scope=lastCycleEndTick.HasValue ? "After shared final owner-cycle edge and ACK tail, before readbacks" : "Native legacy measured endpoint; host-cycle/process totals unavailable"},VisualReferenceCapture.JsonOptions);
        _readyObject=JsonNode.Parse(_ready)!.AsObject();Publish("ready.json",_ready);
    }
    internal bool TryComplete()
    {
        if(Completed)return true;
        if(_ready is null)return false;
        if(Stopwatch.GetTimestamp()>=_deadline)throw new TimeoutException("Original command deadline expired before durable post-window ACK.");
        string path=Path.Combine(_directory,"ack.json");if(!File.Exists(path))return false;
        byte[] raw;
        try { raw=ReadBounded(path,1024*1024); }
        catch(IOException error) when((error.HResult&0xffff) is 32 or 33){return false;}
        var ack=JsonNode.Parse(raw)!.AsObject();
        if(ack["qualified"]?.GetValue<bool>()!=false || ack["schemaVersion"]?.GetValue<int>()!=1 || ack["kind"]?.GetValue<string>()!="ordinary-post-window-control-ack" ||
            !string.Equals(ack["readySha256"]?.GetValue<string>(),Hash(_ready),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Ordinary post-window ACK does not bind the original sealed ready bytes.");
        foreach(string key in new[]{"sourceSha","runtimeFingerprint","runId","checkpointSha256","processId","nativeWindowHandle","caseIdentity","lastSubmission"})
            if(!JsonNode.DeepEquals(ack[key],_readyObject![key]))throw new InvalidDataException("Ordinary post-window ACK identity/case differs: "+key);
        byte[] row=Convert.FromBase64String(ack["rowBytesBase64"]!.GetValue<string>()),prefix=Convert.FromBase64String(ack["prefixReceiptBytesBase64"]!.GetValue<string>());
        if(row.Length>65536 || prefix.Length>65536 || !string.Equals(Hash(row),ack["rowSha256"]?.GetValue<string>(),StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Hash(prefix),ack["prefixReceiptSha256"]?.GetValue<string>(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Post-window row/receipt bytes changed.");
        var rowNode=JsonNode.Parse(row)!.AsObject();var receipt=JsonNode.Parse(prefix)!.AsObject();var sample=rowNode["sample"]!.AsObject();
        if(receipt["schemaVersion"]?.GetValue<int>()!=1 || receipt["kind"]?.GetValue<string>()!="physical-sensor-journal-progress" || receipt["qualified"]?.GetValue<bool>()!=false || receipt["journal"]?.GetValue<string>()!="observations.jsonl")throw new InvalidDataException("Ordinary journal receipt schema differs.");
        if(ack["providerUuid"]?.GetValue<string>()!=_providerUuid || !string.Equals(ack["providerPciBusId"]?.GetValue<string>(),_providerPci,StringComparison.OrdinalIgnoreCase) ||
            sample["devices"] is not JsonArray devices || devices.Count(v=>v?["uuid"]?.GetValue<string>()==_providerUuid && string.Equals(v?["pciBusId"]?.GetValue<string>(),_providerPci,StringComparison.OrdinalIgnoreCase))!=1)throw new InvalidDataException("Post-window actual bound provider is unavailable or ambiguous.");
        int index=rowNode["index"]!.GetValue<int>();
        if(index<0 || ack["sampleIndex"]?.GetValue<int>()!=index || receipt["rows"]?.GetValue<int>()!=index+1 || receipt["failure"] is not null ||
            !JsonNode.DeepEquals(receipt["lastObservation"],sample) || !string.Equals(sample["postWindowReadySha256"]?.GetValue<string>(),Hash(_ready),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Durable receipt does not bind this freshly requested observation.");
        DateTimeOffset start=sample["acquisitionStartedAtUtc"]!.GetValue<DateTimeOffset>(),observed=sample["observedAtUtc"]!.GetValue<DateTimeOffset>();
        var window=sample["window"]!.AsObject();
        if(start<_readyObject!["sealedAtUtc"]!.GetValue<DateTimeOffset>() || observed<start || window["status"]?.GetValue<string>()!="controlled" ||
            window["ownerPid"]?.GetValue<int>()!=_pid || window["hwnd"]?.GetValue<long>()!=_hwnd || window["requestedPid"]?.GetValue<int>()!=_pid ||
            window["foregroundHwnd"]?.GetValue<long>()!=_hwnd || window["foregroundOwnerPid"]?.GetValue<int>()!=_pid || window["visible"]?.GetValue<bool>()!=true || window["iconic"]?.GetValue<bool>()!=false ||
            window["reasons"] is not JsonArray {Count:0} || window["candidates"] is not JsonArray candidates || !candidates.Any(v=>v?.GetValue<long>()==_hwnd) || sample["failure"] is not null ||
            sample["command"]?["exitCode"]?.GetValue<int>()!=0 || sample["command"]?["timedOut"]?.GetValue<bool>()!=false)
            throw new InvalidDataException("Post-window actual query start/foreground/process/control failed.");
        // The source-owned writer fsyncs these bytes before publishing the receipt.
        // Verify its declared prefix against the actual still-growing journal.
        long bytes=receipt["prefixBytes"]!.GetValue<long>();
        if(bytes<row.Length || bytes>64L*1024*1024)throw new InvalidDataException("Post-window journal prefix exceeds its declared bound.");
        string journal=Path.Combine(Path.GetDirectoryName(_directory)!,"sensors","observations.jsonl");
        using(var stream=new FileStream(journal,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        using(var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            byte[] buffer=new byte[65536];long remaining=bytes;
            while(remaining>0){int read=stream.Read(buffer,0,(int)Math.Min(remaining,buffer.Length));if(read==0)throw new InvalidDataException("Durable journal prefix truncated.");digest.AppendData(buffer,0,read);remaining-=read;}
            if(!string.Equals(Convert.ToHexString(digest.GetHashAndReset()),receipt["prefixSha256"]?.GetValue<string>(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Durable journal prefix hash differs.");
            stream.Position=bytes-row.Length;byte[] lastRow=new byte[row.Length];stream.ReadExactly(lastRow);if(!lastRow.AsSpan().SequenceEqual(row))throw new InvalidDataException("Durable prefix does not end with this ACK row.");
        }
        long endpointTick=_readyObject!["lastCycleEndTick"]?.GetValue<long>() ?? _readyObject["lastMeasuredEndTick"]!.GetValue<long>();
        DateTimeOffset endpoint=_readyObject["sealedAtUtc"]!.GetValue<DateTimeOffset>().AddSeconds(-(double)(_readyObject["sealedEndTick"]!.GetValue<long>()-endpointTick)/_readyObject["frequencyHz"]!.GetValue<long>());
        if((observed-endpoint).TotalSeconds is < 0 or > 5 || (DateTimeOffset.UtcNow-endpoint).TotalSeconds is < 0 or > 5 || observed>DateTimeOffset.UtcNow)throw new InvalidDataException("Existing five-second post-window coverage/freshness exceeded.");
        if(Stopwatch.GetTimestamp()>=_deadline)throw new TimeoutException("Original command budget expired during post-window validation.");
        Completed=true;Receipt=new {schemaVersion=1,kind="ordinary-post-window-control-receipt",readySha256=Hash(_ready),ackSha256=Hash(raw),sampleIndex=index,
            prefixBytes=bytes,prefixSha256=receipt["prefixSha256"]!.GetValue<string>(),queryStartedAtUtc=start,observedAtUtc=observed,scope="Fresh-query durable control point; no stability/policy approval"};
        return true;
    }
    internal void WaitForAck(Func<bool> continueWaiting)
    {
        while(!TryComplete()){if(!continueWaiting())throw new OperationCanceledException("Post-window control cancelled before actual ACK.");Thread.Sleep(10);}
    }
    private void Publish(string name,byte[] bytes)
    {
        string target=Path.Combine(_directory,name),pending=target+".pending";
        using(var file=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(flushToDisk:true);}
        File.Move(pending,target);
    }
    private static byte[] ReadBounded(string path,int limit)
    {using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);if(file.Length>limit)throw new InvalidDataException("Post-window ACK exceeds bound.");byte[] result=new byte[checked((int)file.Length)];file.ReadExactly(result);return result;}
    private static string Hash(ReadOnlySpan<byte> value)=>Convert.ToHexString(SHA256.HashData(value));
}
