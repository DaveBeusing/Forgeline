using System.Security.Cryptography;
using System.Threading;

namespace ForgeLine.Client;

internal sealed partial class ClientRenderHost
{
    private ReferencePostWindowControl? _postWindowControl;
    private PostWindowCaseIdentity? _postWindowCase;
    private int _postWindowPending;
    internal bool PostWindowControlCompleted=>Volatile.Read(ref _postWindowPending)==0;
    private void ConfigurePostWindowControl(LiveRenderTelemetry? collector,ClientRenderConfiguration composition)
    {
        string? directory=_referenceFixture?.PostWindowControlDirectory ?? Environment.GetEnvironmentVariable("FORGELINE_RENDER_POST_WINDOW_CONTROL");
        if(string.IsNullOrWhiteSpace(directory))return;
        if(_ownerObservationVersion!=1)throw new InvalidDataException("Ordinary post-window control requires owner observation v1.");
        _postWindowControl=ReferencePostWindowControl.Create(directory,_referenceFixture?.AdapterBindingDirectory ?? Environment.GetEnvironmentVariable("FORGELINE_RENDER_TELEMETRY_ADAPTER_BINDING"));
        byte[] profileBytes=ReferenceFixtureRecipe.SerializeIdentityInputs(composition);
        string profile=Convert.ToHexString(SHA256.HashData(profileBytes));string profileBase64=Convert.ToBase64String(profileBytes);
        _postWindowCase=_referenceFixture is { } fixture
            ? new("authored-world-fixture",fixture.CaseKey,true,fixture.Fingerprint,profile,ProfileInputBytesBase64:profileBase64)
            : new("actual-live-session","session-"+(_expectedSession?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable"),false,null,profile,_expectedSession?.Value,_sessionOrigin?.Fingerprint,profileBase64);
        if(_referenceFixture is null && (_expectedSession is null || _sessionOrigin is null))throw new InvalidDataException("Actual live post-window identity lacks its composed session origin.");
        Volatile.Write(ref _postWindowPending,1);
        if(collector is not null){collector.PostWindowControl=_postWindowControl;collector.ObservedComposition=composition;}
    }
    private void ObservePostWindowControl(OwnerCycleObservations? cycles)
    {
        if(_postWindowControl is null || _postWindowControl.Completed || cycles?.Sealed!=true)return;
        if(cycles.Failed)throw new InvalidDataException("Failed/overflowed owner cycles cannot seal an ordinary post-window control point.");
        int samples=_referenceFixture?.Samples ?? cycles.MeasuredCount;
        _postWindowControl.PublishReady(_postWindowCase!,samples,cycles.LastMeasuredEndTick,cycles.LastSealedEdge.ReadEndTick,cycles.LastMeasuredSubmission);
        _postWindowControl.WaitForAck(()=>Volatile.Read(ref _stopping)==0);
        Volatile.Write(ref _postWindowPending,0);
    }
}
