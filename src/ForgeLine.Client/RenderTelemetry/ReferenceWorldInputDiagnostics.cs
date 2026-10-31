using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using ForgeLine.Presentation.RenderContracts;
using ForgeLine.Presentation.RenderSubmission;
using ForgeLine.Presentation.TerrainResidency;

namespace ForgeLine.Client;

internal enum WorldInputSite { NativeFixedPoseRestore, CaptureEnsurePrepared, HostReadbackPrepare }
internal enum WorldInputPhase { BeforeSite, AfterSite, AfterCaptureAndClientRetire }
internal readonly record struct TerrainInputFacts(bool IsPrepared,TerrainWorkMetrics Work,TerrainRenderDiagnostics Last,RuntimeMaterialDiagnostics Material);
internal readonly record struct InstanceInputFacts(bool AssetsPrepared,SubmissionWorkMetrics Work,InstanceRenderDiagnostics Main,InstanceRenderDiagnostics Shadow,
    InstanceSubmissionMetrics Submission,RenderPreparationReuseMetrics Reuse,RenderAssetFrameMetrics? Assets,RuntimeMaterialDiagnostics Material);
internal sealed record WorldInputFacts(TerrainInputFacts Terrain,InstanceInputFacts Instances,ParticleVfxDiagnostics Particles,
    GraphicsDiagnostics Graphics,GraphicsResourceMemoryTelemetry? LogicalMemory,GraphicsCpuFrameIntervals? CpuIntervals,
    GraphicsFrameSubmission? LastSubmission,GraphicsDeviceTelemetryIdentity? DeviceIdentity,string? InputFingerprint);
internal sealed record WorldInputObservation(int Index,int Repetition,int SiteOrdinal,WorldInputSite? Site,WorldInputPhase Phase,bool? PreparationInvoked,
    long ReadStartTick,long ReadEndTick,int ManagedOwnerThread,uint NativeOwnerThread,RtsCameraState Camera,WorldInputFacts Facts);

// Diagnostic-only copied public facts. No callback is installed in Prepare/Render or the sample loop.
internal sealed class ReferenceWorldInputDiagnostics
{
    private readonly string _root,_key,_producer,_runtime,_recipe,_recipeBytes,_profileHash,_profileBytes;
    private readonly Func<WorldInputFacts> _facts;
    private readonly Func<RtsCameraState> _camera;
    private readonly int _owner=Environment.CurrentManagedThreadId,_pid=Environment.ProcessId,_repeats;
    private readonly uint _nativeOwner=RenderOwnerThreadIdentity.GetCurrentThreadId();
    private readonly WorldInputObservation?[] _rows;
    private int _count,_start,_repetition=-1,_nextRepeat,_site;
    private bool _armed,_afterCapture,_persisted,_invalid;
    private WorldInputSite? _pending;
    private GraphicsStageCapture? _capture;
    private GraphicsFrameSubmission? _previousSubmission;
    private ulong _previousPresented;
    private GraphicsDeviceTelemetryIdentity? _previousDevice;
    internal int Count=>_count;
    internal bool Armed=>_armed;
    internal ReferenceWorldInputDiagnostics(string root,string key,string producer,string runtime,string recipe,string recipeBytes,object profile,int repeats,
        Func<WorldInputFacts> facts,Func<RtsCameraState> camera)
    {
        if(repeats is < 2 or > 128)throw new ArgumentOutOfRangeException(nameof(repeats));
        if(producer is not ("native-reference" or "actual-render-host"))throw new ArgumentException("Unknown inspector producer.",nameof(producer));
        if(Path.GetFileName(key)!=key || string.IsNullOrWhiteSpace(key))throw new ArgumentException("Inspector key must be a basename.",nameof(key));
        _root=root;_key=key;_producer=producer;_runtime=runtime;_recipe=recipe;_recipeBytes=recipeBytes;_repeats=repeats;
        byte[] profileBytes=ReferenceFixtureRecipe.SerializeIdentityInputs(profile);_profileBytes=Convert.ToBase64String(profileBytes);_profileHash=Hash(profileBytes);
        _facts=facts;_camera=camera;_rows=new WorldInputObservation[checked(5*repeats)];
    }
    internal static ReferenceWorldInputDiagnostics Create(string root,string key,string producer,string runtime,string recipe,string recipeBytes,object profile,int repeats,
        TerrainRenderer terrain,SimpleInstanceRenderer instances,ParticleVfxRenderer particles,IGraphicsDevice graphics,RtsCamera camera) =>
        new(root,key,producer,runtime,recipe,recipeBytes,profile,repeats,()=>Read(terrain,instances,particles,graphics),camera.CaptureState);
    private static WorldInputFacts Read(TerrainRenderer terrain,SimpleInstanceRenderer instances,ParticleVfxRenderer particles,IGraphicsDevice graphics)
    {
        // Exactly one Diagnostics read; it may harvest timing/debug state. No drain/idle/mutator.
        GraphicsDiagnostics diagnostic=graphics.Diagnostics;var telemetry=graphics as IGraphicsRenderTelemetry;
        return new(new(terrain.IsPrepared,terrain.WorkMetrics,terrain.LastDiagnostics,terrain.MaterialDiagnostics),
            new(instances.AssetsPrepared,instances.WorkMetrics,instances.LastDiagnostics,instances.LastShadowDiagnostics,instances.SubmissionMetrics,
                instances.PreparationReuse,instances.AssetFrameMetrics,instances.MaterialDiagnostics),particles.Diagnostics,diagnostic,
            telemetry?.ResourceMemory,telemetry?.CpuIntervals,telemetry?.LastSubmission,telemetry?.DeviceIdentity,graphics.CaptureInputFingerprint);
    }
    private void VerifyOwner()
    {if(_owner!=Environment.CurrentManagedThreadId || _nativeOwner!=RenderOwnerThreadIdentity.GetCurrentThreadId()){if(Volatile.Read(ref _armed))Volatile.Write(ref _invalid,true);throw new InvalidOperationException("Inspector is render-owner-only.");}}
    private InvalidDataException Invalid(string message){Volatile.Write(ref _invalid,true);return new(message);}
    internal IDisposable Arm(int repetition)
    {
        VerifyOwner();if(_armed){Volatile.Write(ref _invalid,true);throw new InvalidOperationException("An armed fixed repetition cannot overlap another scope.");}
        if(Volatile.Read(ref _invalid) || repetition!=_nextRepeat || repetition>=_repeats)throw new InvalidOperationException("Fixed inspector repetition order differs.");
        _armed=true;_persisted=_afterCapture=false;_pending=null;_capture=null;_site=0;_start=_count;_repetition=repetition;
        return new Scope(this,repetition);
    }
    internal void BeforeSite(WorldInputSite site){if(!_armed)return;Observe(site,WorldInputPhase.BeforeSite,null);}
    internal void AfterSite(WorldInputSite site,bool invoked){if(!_armed)return;Observe(site,WorldInputPhase.AfterSite,invoked);}
    private void Observe(WorldInputSite? site,WorldInputPhase phase,bool? invoked)
    {
        try
        {
            VerifyOwner();long start=Stopwatch.GetTimestamp();var camera=_camera();var facts=_facts();long end=Stopwatch.GetTimestamp();
            Append(new(_count,_repetition,_site,site,phase,invoked,start,end,_owner,_nativeOwner,camera,facts));
        }
        catch(Exception error) when(error is not OutOfMemoryException and not StackOverflowException){Volatile.Write(ref _invalid,true);throw;}
    }
    // Pure sequence seam: no renderer interfaces or fake graphics implementation needed by tests.
    internal void Append(in WorldInputObservation row)
    {
        VerifyOwner();if(!_armed)throw new InvalidOperationException("Inspector append requires an explicitly armed fixed repetition.");
        if(Volatile.Read(ref _invalid))throw Invalid("A failed armed observation cannot be corrected into complete evidence.");
        if(_count==_rows.Length)throw Invalid("Inspector capacity exhausted.");
        if(row.Index!=_count || row.Repetition!=_repetition || row.SiteOrdinal!=_site || row.ManagedOwnerThread!=_owner || row.NativeOwnerThread!=_nativeOwner ||
            row.ReadStartTick<0 || row.ReadEndTick<row.ReadStartTick || _count>0 && row.ReadStartTick<_rows[_count-1]!.ReadEndTick)
            throw Invalid("Foreign/regressing inspector observation.");
        WorldInputSite expected=_producer=="actual-render-host" ? WorldInputSite.HostReadbackPrepare : _repetition==0 && _site==0 ? WorldInputSite.NativeFixedPoseRestore : WorldInputSite.CaptureEnsurePrepared;
        int sites=_producer=="native-reference" && _repetition==0 ? 2 : 1;
        if(row.Phase==WorldInputPhase.BeforeSite)
        {if(_pending.HasValue || _site>=sites || row.Site!=expected || row.PreparationInvoked is not null)throw Invalid("Invalid before-site/predicate phase.");_pending=row.Site;}
        else if(row.Phase==WorldInputPhase.AfterSite)
        {if(_pending!=row.Site || !_pending.HasValue || row.PreparationInvoked is null || row.Site!=WorldInputSite.CaptureEnsurePrepared && row.PreparationInvoked!=true)throw Invalid("Missing/mismatched preparation invocation.");_pending=null;_site++;}
        else if(row.Phase==WorldInputPhase.AfterCaptureAndClientRetire)
        {if(_afterCapture || _pending.HasValue || _site!=sites || row.Site is not null || row.PreparationInvoked is not null)throw Invalid("Incomplete capture phase.");_afterCapture=true;}
        else throw Invalid("Unknown inspector phase.");
        _rows[_count++]=row;
    }
    internal void AfterCapture(GraphicsStageCapture? capture)
    {
        if(!_armed)return;
        if(capture is null)throw Invalid("Selected inspector requires actual stage capture.");
        _capture=capture;Observe(null,WorldInputPhase.AfterCaptureAndClientRetire,null);
        var actual=_rows[_count-1]!.Facts;
        if(capture.Submission.Submission==0 || capture.Submission.Generation==0 || capture.Submission.ResourceGeneration==0 || capture.DeviceIdentity.ProcessId!=_pid || capture.Submission.SubmittedFence==0 || capture.CompletedFence==ulong.MaxValue || capture.CompletedFence<capture.Submission.SubmittedFence || capture.BufferCount!=3 || capture.FrameIndex<0 || capture.FrameIndex>=capture.BufferCount || capture.FrameIndex!=_rows[_count-2]!.Facts.Graphics.Surface.FrameIndex ||
            capture.PresentedBefore==ulong.MaxValue || capture.PresentedAfter!=capture.PresentedBefore+1 || actual.LastSubmission!=capture.Submission || actual.DeviceIdentity!=capture.DeviceIdentity ||
            capture.DeviceIdentity.Generation!=capture.Submission.Generation || actual.Graphics.Surface.BufferCount!=capture.BufferCount || actual.Graphics.Surface.PresentedFrameCount!=capture.PresentedAfter)
        { _invalid=true;throw Invalid("Inspector capture/submission/device/presentation join failed."); }
        if(_previousSubmission is { } previous && (previous.Submission==ulong.MaxValue || capture.Submission.Submission!=previous.Submission+1 ||
            capture.Submission.SubmittedFence<=previous.SubmittedFence || capture.Submission.Generation!=previous.Generation || capture.Submission.ResourceGeneration!=previous.ResourceGeneration ||
            capture.PresentedBefore!=_previousPresented || capture.DeviceIdentity!=_previousDevice))
        { _invalid=true;throw Invalid("Inspector fixed repetition replays/changes its prior submission identity."); }
    }
    internal object? Persist(object? stages)
    {
        if(!_armed)return null;
        try{return PersistCore(stages);}
        catch(Exception error) when(error is not OutOfMemoryException and not StackOverflowException){Volatile.Write(ref _invalid,true);throw;}
    }
    private object PersistCore(object? stages)
    {
        VerifyOwner();if(Volatile.Read(ref _invalid) || !_afterCapture || _capture is null || stages is null)throw Invalid("Inspector cannot publish complete evidence without actual stage metadata.");
        JsonNode stageRef=JsonSerializer.SerializeToNode(stages,VisualReferenceCapture.JsonOptions)!;
        string stageFile=stageRef["file"]!.GetValue<string>();
        if(Path.GetFileName(stageFile)!=stageFile)throw Invalid("Stage metadata must be beside the inspector.");
        string stagePath=Path.Combine(_root,stageFile);
        if(new FileInfo(stagePath).Length>1024*1024 || !string.Equals(VisualReferenceCapture.FileHash(stagePath),stageRef["sha256"]!.GetValue<string>(),StringComparison.OrdinalIgnoreCase))throw Invalid("Original stage metadata hash differs.");
        object reference=Write(complete:true,stages,failure:null);_previousSubmission=_capture.Submission;_previousPresented=_capture.PresentedAfter;_previousDevice=_capture.DeviceIdentity;_persisted=true;_armed=false;_nextRepeat++;return reference;
    }
    private object Write(bool complete,object? stages,string? failure)
    {
        var observations=new List<object>();
        for(int i=_start;i<_count;i++)
        {
            var row=_rows[i]!;JsonNode state=JsonSerializer.SerializeToNode(row.Facts,VisualReferenceCapture.JsonOptions)!;var timings=new JsonObject();ExtractTimings(state,"",timings);
            observations.Add(new {row.Index,row.Repetition,row.SiteOrdinal,row.Site,row.Phase,row.PreparationInvoked,row.ReadStartTick,row.ReadEndTick,row.ManagedOwnerThread,row.NativeOwnerThread,row.Camera,
                stateAndActivityFacts=state,timingFacts=timings});
        }
        var capture=_capture;
        var value=new {schemaVersion=1,kind="reference-world-input-inspector",qualified=false,complete,failure,
            sourceSha=ClientBuildIdentity.SourceRevision,producer=_producer,runtimeFingerprint=_runtime,recipeFingerprint=_recipe,recipeInputBytesBase64=_recipeBytes,
            profileDomain="effective-quality-profile",profileFingerprint=_profileHash,profileInputBytesBase64=_profileBytes,processId=_pid,managedOwnerThread=_owner,nativeOwnerThread=_nativeOwner,
            repetition=_repetition,declaredRepeats=_repeats,capacity=_rows.Length,stageDiagnostics=stages,
            captured=capture is null ? null : new {capture.Submission,capture.CompletedFence,capture.FrameIndex,capture.BufferCount,capture.PresentedBefore,capture.PresentedAfter,capture.DeviceIdentity},
            observations,scope="Copied public aggregates only; equal values do not prove equal bindings/GPU contents. Surface.FrameIndex is next slot after capture; captured slot/completion come only from stage capture.",
            observationEffects="One Graphics.Diagnostics read per phase may harvest completed timing/debug records and allocate; DXGI memory is cached250ms. No extra waits/drains/ObserveFrameDiagnostics. BeforeSite predicate is unknown, false AfterSite means reused preparation.",
            classification="stateAndActivityFacts includes expected cumulative/per-draw counters, not an invariant claim; *Milliseconds leaves are separately labelled timingFacts. SkinUploadBytes is resident-slot accounting, UploadBytes logical submitted bytes; no arena internals or complete GPU binding trace."};
        string file=$"{_key}-readback-{_repetition:D2}-world-inputs.json";
        using(var stream=new FileStream(Path.Combine(_root,file),FileMode.CreateNew,FileAccess.Write,FileShare.Read))JsonSerializer.Serialize(stream,value,VisualReferenceCapture.JsonOptions);
        return new {file,sha256=VisualReferenceCapture.FileHash(Path.Combine(_root,file)),schemaVersion=1};
    }
    private static void ExtractTimings(JsonNode value,string prefix,JsonObject timings)
    {
        if(value is JsonObject obj)
        {
            foreach(var item in obj.ToArray())
            {
                string path=prefix.Length==0 ? item.Key : prefix+"."+item.Key;
                if(item.Key.EndsWith("Milliseconds",StringComparison.Ordinal)){timings[path]=item.Value?.DeepClone();obj.Remove(item.Key);}
                else if(item.Value is not null)ExtractTimings(item.Value,path,timings);
            }
        }
        else if(value is JsonArray array)for(int i=0;i<array.Count;i++)if(array[i] is { } item)ExtractTimings(item,prefix+"["+i.ToString(System.Globalization.CultureInfo.InvariantCulture)+"]",timings);
    }
    private void Disarm(int repetition)
    {
        if(!_armed || _repetition!=repetition)return;
        try {if(!_persisted)_=Write(false,null,"Fixed capture scope exited before complete persistence; original producer exception remains authoritative.");}
        catch(Exception error) when(error is not OutOfMemoryException and not StackOverflowException)
        {
            try {Console.Error.WriteLine("[reference:world-input-prefix-unavailable] "+error);}
            catch(Exception loggingError) when(loggingError is not OutOfMemoryException and not StackOverflowException)
            { /* Best-effort diagnostic logging must not replace the original producer exception. */ }
        }
        finally {_armed=false;}
    }
    private sealed class Scope(ReferenceWorldInputDiagnostics owner,int repetition):IDisposable
    {public void Dispose()=>owner.Disarm(repetition);}
    private static string Hash(ReadOnlySpan<byte> bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
}
