using System.Diagnostics;
using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.Presentation.RenderContracts;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

// A render-owner facade over the existing caches. Native resources still retire
// through their device's last-use fences; this class does not create a second cache.
internal sealed class RenderAssetSession : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly IGraphicsDevice _graphics;
    private readonly RuntimeWorldAssetResources _resources;
    private enum Phase { Idle, Preparing, Prepared, Recording, Submitted }
    private Phase _phase;
    private bool _explicitFrames;
    private bool _disposed;
    private ulong _sequence;
    private SimulationSessionId _session;
    private ulong _tick;
    private long _started;
    private long _recordStarted;
    private double? _prepareMs, _recordMs, _submitMs, _retireMs, _waitMs, _presentMs;
    private ulong? _fence;
    private long? _uploadsBefore, _observedUploads;
    private bool _submitted;
    private long? _uploadCount, _logicalBytes;
    private ulong? _dxgiBytes;
    private string? _failure;

    internal RenderAssetSession(IGraphicsDevice graphics, RuntimeAssetCatalog catalog, TextureDetailQuality quality)
    {
        _graphics = graphics;
        _resources = new(graphics, catalog, quality);
        try { _resources.PrepareRequiredAssets(CancellationToken.None); }
        catch { _resources.Dispose(); throw; }
    }
    internal AssetResidencyProgress Residency => _resources.Residency;
    internal bool MetricsEnabled { get; set; }
    internal bool IsPrepared => _phase == Phase.Prepared;
    internal bool FrameActive => _phase != Phase.Idle;
    internal RenderAssetFrameMetrics? Metrics => MetricsEnabled
        ? new(_sequence, _session, _tick, _prepareMs, _recordMs, _submitMs, _retireMs, _fence,
            PresentMilliseconds: _presentMs, WaitMilliseconds: _waitMs,
            UploadedTextureCount: _uploadCount, LogicalTextureBytes: _logicalBytes,
            ObservedDxgiLocalUsageBytes: _dxgiBytes, FailureStatus: _failure) : null;
    internal RuntimeMaterialDiagnostics MaterialDiagnostics { get { CheckOwner(); return _resources.MaterialDiagnostics; } }

    internal void BeginPrepare(SimulationSessionId session, ulong tick)
    {
        CheckOwner();
        if (_graphics.IsRecording || _phase != Phase.Idle)
            throw new InvalidOperationException("Prepare requires an idle render owner before recording.");
        _explicitFrames = true; _phase = Phase.Preparing; _sequence++; _session = session; _tick = tick;
        _prepareMs = _recordMs = _submitMs = _retireMs = _waitMs = _presentMs = null; _fence = null;
        _uploadCount = _logicalBytes = null; _dxgiBytes = null; _failure = null;
        if (!MetricsEnabled) _observedUploads = null;
        _uploadsBefore = MetricsEnabled ? _observedUploads : null;
        _submitted = false;
        _started = MetricsEnabled ? Stopwatch.GetTimestamp() : 0;
    }
    internal void EndPrepare()
    {
        CheckOwner();
        if (_phase != Phase.Preparing) throw new InvalidOperationException("Prepare is not active.");
        if (MetricsEnabled) _prepareMs = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
        _phase = Phase.Prepared;
    }
    internal void AbortPrepare() { CheckOwner(); _phase = Phase.Idle; }
    internal void BeginRecord()
    {
        CheckOwner();
        if (!_explicitFrames) return; // Unchanged legacy Render callers retain synchronous cache resolution.
        if (_phase is not (Phase.Prepared or Phase.Recording))
            throw new InvalidOperationException("Recording requires a prepared asset frame.");
        _phase = Phase.Recording;
        _recordStarted = MetricsEnabled ? Stopwatch.GetTimestamp() : 0;
    }
    internal void EndRecord()
    {
        CheckOwner();
        if (_explicitFrames && MetricsEnabled)
            _recordMs = (_recordMs ?? 0) + Stopwatch.GetElapsedTime(_recordStarted).TotalMilliseconds;
    }
    internal void Submit()
    {
        CheckOwner();
        if (_graphics.IsRecording || _phase != Phase.Recording)
            throw new InvalidOperationException("Submit follows completed command recording.");
        long start = MetricsEnabled ? Stopwatch.GetTimestamp() : 0;
        _submitted = true;
        _phase = Phase.Submitted;
        if (MetricsEnabled) _submitMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    // Consume the caller's existing observation outside the measured frame. Never
    // poll the adapter or allocate diagnostic snapshots from Prepare/Submit.
    internal void ObserveFrameDiagnostics(GraphicsDiagnostics diagnostics)
    {
        CheckOwner();
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (_graphics.IsRecording || _phase is not (Phase.Idle or Phase.Submitted))
            throw new InvalidOperationException("Observe diagnostics after CPU submission.");
        if (!MetricsEnabled) return;
        long uploads = diagnostics.Resources.TextureUploadCount;
        if (_submitted)
        {
            _fence = diagnostics.Frame?.GpuSubmissionFence;
            _waitMs = diagnostics.Frame?.FrameWaitMilliseconds;
            _presentMs = diagnostics.Frame?.PresentMilliseconds;
            _uploadCount = _uploadsBefore is { } before && uploads >= before ? uploads - before : null;
            _logicalBytes = diagnostics.Resources.ResidentTextureBytes;
            _dxgiBytes = diagnostics.Memory is { Available: true } memory ? memory.LocalUsageBytes : null;
            _failure = diagnostics.Health.FailureReasonCode;
        }
        _observedUploads = uploads;
    }
    internal void Retire()
    {
        CheckOwner();
        if (_graphics.IsRecording || _phase != Phase.Submitted)
            throw new InvalidOperationException("Retire requires a completed CPU submission.");
        long start = MetricsEnabled ? Stopwatch.GetTimestamp() : 0;
        // Release only the CPU frame boundary. Cache resources remain owned; actual
        // native disposal continues to use existing device retirement, never CPU timing.
        _phase = Phase.Idle;
        if (MetricsEnabled) _retireMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    internal bool TryGetMesh(string id, out RuntimeMeshBuffers mesh, out AssetId resolved)
    { CheckOwner(); return _resources.TryGetMesh(id, out mesh, out resolved); }
    internal bool TryGetMesh(string id, out RuntimeMeshBuffers mesh)
    { CheckOwner(); return _resources.TryGetMesh(id, out mesh); }
    internal bool TryGetMesh(in WorldFeaturePresentationMetadata feature, WorldAssetLod lod, out RuntimeMeshBuffers mesh, out AssetId resolved)
    { CheckOwner(); return _resources.TryGetMesh(feature, lod, out mesh, out resolved); }
    internal RuntimeMaterialResources ResolveMaterial(string id)
    { CheckOwner(); return _resources.ResolveMaterial(id); }
    internal bool TryGetMaterialTint(string id, out Vector4 tint)
    { CheckOwner(); return _resources.TryGetMaterialTint(id, out tint); }
    internal bool TryGetMaterialTint(in WorldFeaturePresentationMetadata feature, out Vector4 tint)
    { CheckOwner(); return _resources.TryGetMaterialTint(feature, out tint); }
    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _owner)
            throw new InvalidOperationException("Asset sessions belong to their creating render owner.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Dispose requires the render owner.");
        if (_disposed) return;
        _resources.Dispose(); _disposed = true;
    }
}
