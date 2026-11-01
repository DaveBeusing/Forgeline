using Vortice.Direct3D12;
using System.Security.Cryptography;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private ID3D12Resource? _captureTarget;
    private GraphicsResourceRetirement.Resource? _captureLifetime;
    private PlacedSubresourceFootPrint _captureFootprint;
    private IncrementalHash? _captureInputHash;
    private ID3D12Resource? _captureShadowTarget;
    private GraphicsResourceRetirement.Resource? _captureShadowLifetime;
    private PlacedSubresourceFootPrint _captureShadowFootprint;
    private bool _captureShadowRecorded;
    private bool _captureFrameRecorded;
    private Dictionary<GraphicsCaptureStages, CaptureStageTarget>? _captureStages;
    private GraphicsStageCapture? _lastStageCapture;
    private int _captureRecordedFrameIndex;
    private sealed record CaptureStageTarget(ID3D12Resource Resource, GraphicsResourceRetirement.Resource Lifetime,
        PlacedSubresourceFootPrint Footprint, GraphicsTextureReadbackLayout Layout, ulong Bytes)
    { internal bool Recorded { get; set; } }
    public string? CaptureInputFingerprint { get; private set; }
    public ReadOnlyMemory<byte> CaptureShadowDepth { get; private set; }
    public GraphicsTextureReadbackLayout? CaptureShadowLayout { get; private set; }
    internal void RecordCaptureInput(ReadOnlySpan<byte> values) => _captureInputHash?.AppendData(values);

    // Qualification only: a requested capture adds one copy and waits for completion.
    // Normal RenderFrame never allocates a readback resource or copies the output.
    public byte[] CaptureFrame(GraphicsColor clearColor, Action<IGraphicsCommandContext> commands, bool captureShadowDepth = false)
        => CaptureCore(clearColor, commands, captureShadowDepth, 0);

    public IReadOnlyList<GraphicsStageFootprint> CaptureStageFootprints(GraphicsCaptureStages stages = GraphicsCaptureStages.All)
    {
        ThrowIfDisposed(); ThrowIfRecording();
        if ((stages & ~GraphicsCaptureStages.All) != 0 || (stages & GraphicsCaptureStages.FinalColor) == 0)
            throw new ArgumentOutOfRangeException(nameof(stages));
        if (_surfaceLifecycle.IsSuspended || _surfaceLifecycle.HasPendingResize || _surfaceLifecycle.IsOccluded)
            throw new InvalidOperationException("Footprints require an active settled surface.");
        var result = new List<GraphicsStageFootprint>();
        foreach (var stage in new[] { GraphicsCaptureStages.ShadowDepth, GraphicsCaptureStages.WorldDepth,
            GraphicsCaptureStages.SceneLinear, GraphicsCaptureStages.ComposedColor, GraphicsCaptureStages.FinalColor })
        {
            if ((stages & stage) == 0) continue;
            ID3D12Resource? resource = stage switch
            {
                GraphicsCaptureStages.ShadowDepth => _directionalShadows.Enabled ? _shadowTargets![_frameIndex] : null,
                GraphicsCaptureStages.WorldDepth => _sceneOutput.Enabled ? _depthTarget : null,
                GraphicsCaptureStages.SceneLinear => _sceneOutput.Enabled ? _sceneTargets![_frameIndex] : null,
                GraphicsCaptureStages.ComposedColor => _sceneOutput.Enabled ? _renderTargets[_frameIndex] : null,
                _ => _renderTargets[_frameIndex]
            };
            if (resource is null) { result.Add(new(stage, null, 0, "Stage is unavailable in the effective graphics configuration.")); continue; }
            var footprints = new PlacedSubresourceFootPrint[1];
            _device.GetCopyableFootprints(resource.Description, 0, 1, 0, footprints, new uint[1], new ulong[1], out ulong bytes);
            result.Add(new(stage, Layout(resource.Description, footprints[0]), bytes, null));
        }
        return result.ToArray();
    }

    public GraphicsStageCapture CaptureFrameDiagnostics(GraphicsColor clearColor, Action<IGraphicsCommandContext> commands,
        GraphicsCaptureStages stages = GraphicsCaptureStages.All)
    {
        if ((stages & ~GraphicsCaptureStages.All) != 0 || (stages & GraphicsCaptureStages.FinalColor) == 0)
            throw new ArgumentOutOfRangeException(nameof(stages), "Diagnostic capture must include final color and known stages only.");
        _ = CaptureCore(clearColor, commands, (stages & GraphicsCaptureStages.ShadowDepth) != 0, stages);
        return _lastStageCapture ?? throw new InvalidOperationException("Diagnostic capture did not produce completed evidence.");
    }

    private byte[] CaptureCore(GraphicsColor clearColor, Action<IGraphicsCommandContext> commands,
        bool captureShadowDepth, GraphicsCaptureStages stages)
    {
        GraphicsWorldBindingAttempt? worldAttempt = null;
        bool boundCaptureArmed = false;
        bool captureBodySucceeded = false;
        Exception? capturePrimary = null;
        byte[]? captureOutput = null;
        ArgumentNullException.ThrowIfNull(commands);
        ThrowIfDisposed();
        ThrowIfRecording();
        InvalidatePostFrameStamp();
        if (_surfaceLifecycle.IsSuspended || _surfaceLifecycle.HasPendingResize || _surfaceLifecycle.IsOccluded)
            throw new InvalidOperationException("Frame capture requires an active, settled surface.");
        PlacedSubresourceFootPrint[] footprints = new PlacedSubresourceFootPrint[1];
        _device.GetCopyableFootprints(_renderTargets[_frameIndex].Description, 0, 1, 0,
            footprints, new uint[1], new ulong[1], out ulong bytes);
        _captureFootprint = footprints[0];
        ID3D12Resource readback = _device.CreateCommittedResource(HeapType.Readback,
            ResourceDescription.Buffer(bytes), ResourceStates.CopyDest);
        GraphicsResourceRetirement.Resource lifetime;
        try { lifetime = RegisterResource(readback.Dispose, checked((long)bytes)); }
        catch { readback.Dispose(); throw; }
        _captureTarget = readback;
        _captureLifetime = lifetime;
        CaptureInputFingerprint = null;
        CaptureShadowDepth = ReadOnlyMemory<byte>.Empty;
        CaptureShadowLayout = null;
        _captureShadowRecorded = false;
        _captureFrameRecorded = false;
        _captureRecordedFrameIndex = -1;
        _lastStageCapture = null;
        ulong presentedBefore = _presentedFrameCount;
        var submissionBefore = LastSubmission;
        try
        {
            _captureInputHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            ulong shadowBytes = 0;
            if (captureShadowDepth && _directionalShadows.Enabled)
            {
                _device.GetCopyableFootprints(_shadowTargets![_frameIndex].Description, 0, 1, 0,
                    footprints, new uint[1], new ulong[1], out shadowBytes);
                _captureShadowFootprint = footprints[0];
                var description = _shadowTargets![_frameIndex].Description;
                CaptureShadowLayout = new(checked((int)description.Width), checked((int)description.Height),
                    checked((int)_captureShadowFootprint.Footprint.RowPitch), _captureShadowFootprint.Offset, description.Format.ToString());
                ID3D12Resource shadowReadback = _device.CreateCommittedResource(HeapType.Readback,
                    ResourceDescription.Buffer(shadowBytes), ResourceStates.CopyDest);
                try { _captureShadowLifetime = RegisterResource(shadowReadback.Dispose, checked((long)shadowBytes)); }
                catch { shadowReadback.Dispose(); throw; }
                _captureShadowTarget = shadowReadback;
            }
            if (stages != 0)
            {
                _captureStages = new();
                if (_sceneOutput.Enabled)
                {
                    AddStageTarget(stages, GraphicsCaptureStages.SceneLinear, _sceneTargets![_frameIndex]);
                    AddStageTarget(stages, GraphicsCaptureStages.WorldDepth, _depthTarget!);
                    AddStageTarget(stages, GraphicsCaptureStages.ComposedColor, _renderTargets[_frameIndex]);
                }
            }
            if (stages != 0)
            {
                ArmWorldBindingJournal();
                worldAttempt = GraphicsWorldBindingAttempt.ClaimCurrent();
                if (_boundInputTracker is not null)
                {
                    _boundInputTracker.Arm(checked((ulong)_surfaceLifecycle.AppliedResizeGeneration + 1), _frameIndex, _presentedFrameCount);
                    boundCaptureArmed = true;
                }
            }
            RenderFrame(clearColor, commands);
            if (!_captureFrameRecorded) throw new InvalidOperationException("Native frame capture did not submit a backbuffer copy.");
            CaptureInputFingerprint = Convert.ToHexString(_captureInputHash.GetHashAndReset());
            WaitForIdle();
            if (_captureShadowRecorded)
            {
                byte[] shadow = new byte[checked((int)shadowBytes)];
                _captureShadowTarget!.GetData<byte>(shadow.AsSpan());
                CaptureShadowDepth = shadow;
            }
            byte[] padded = new byte[checked((int)bytes)];
            readback.GetData<byte>(padded.AsSpan());
            int rowBytes = checked(_surfaceLifecycle.Width * 4);
            byte[] output = new byte[checked(rowBytes * _surfaceLifecycle.Height)];
            for (int row = 0; row < _surfaceLifecycle.Height; row++)
                padded.AsSpan(checked((int)_captureFootprint.Offset + row * (int)_captureFootprint.Footprint.RowPitch), rowBytes)
                    .CopyTo(output.AsSpan(row * rowBytes, rowBytes));
            if (stages != 0)
            {
                var submission = LastSubmission ?? throw new InvalidOperationException("Actual capture submission unavailable.");
                ulong completed = _frameFence.CompletedValue;
                if (submission.SubmittedFence == 0 || submission.Generation == 0 || submission.ResourceGeneration == 0 ||
                    submissionBefore is not { } prior || submission.Submission != prior.Submission + 1 ||
                    submission.SubmittedFence <= prior.SubmittedFence || completed == ulong.MaxValue || completed < submission.SubmittedFence ||
                    _captureRecordedFrameIndex < 0 || _presentedFrameCount != presentedBefore + 1)
                    throw new InvalidOperationException("Diagnostic capture lacks one successful completed submission/presentation.");
                var observations = new List<GraphicsStageReadback>();
                foreach (var stage in new[] { GraphicsCaptureStages.ShadowDepth, GraphicsCaptureStages.WorldDepth,
                    GraphicsCaptureStages.SceneLinear, GraphicsCaptureStages.ComposedColor, GraphicsCaptureStages.FinalColor })
                {
                    if ((stages & stage) == 0) continue;
                    if (stage == GraphicsCaptureStages.FinalColor)
                        observations.Add(new(stage, Layout(_renderTargets[_captureRecordedFrameIndex].Description, _captureFootprint), padded, null));
                    else if (stage == GraphicsCaptureStages.ShadowDepth)
                        observations.Add(new(stage, CaptureShadowLayout, CaptureShadowDepth,
                            _captureShadowRecorded ? null : "Directional shadow depth was not recorded in this frame."));
                    else if (_captureStages!.TryGetValue(stage, out var target) && target.Recorded)
                    {
                        var raw = new byte[checked((int)target.Bytes)]; target.Resource.GetData<byte>(raw.AsSpan());
                        observations.Add(new(stage, target.Layout, raw, null));
                    }
                    else observations.Add(new(stage, null, ReadOnlyMemory<byte>.Empty,
                        "Linear scene composition stage was unavailable or not recorded."));
                }
                var worldJournal = _worldBindingRecorder!.Finish(submission, completed, _captureRecordedFrameIndex, presentedBefore, _presentedFrameCount);
                var provenance = _boundInputTracker?.Seal(worldJournal);
                _lastStageCapture = new(output, submission, completed, _captureRecordedFrameIndex, presentedBefore,
                    _presentedFrameCount, DeviceIdentity ?? throw new InvalidOperationException("Actual device identity unavailable."), observations.ToArray()) { BufferCount = _configuration.BufferCount,
                    WorldBindings = worldJournal, BoundInputProvenance = provenance };
            }
            captureBodySucceeded = true;
            captureOutput = output;
        }
        catch(Exception error)
        {
            capturePrimary = error;
            if(_worldBindingRecorder is { } journal)
                try { GraphicsWorldBindingJournal.AttachFailure(error,journal.Prefix(error),worldAttempt); }
                catch { }
            if (boundCaptureArmed && _boundInputTracker is { } tracker)
                try { GraphicsBoundInputProvenance.AttachFailure(error, tracker.Prefix(error), worldAttempt); } catch { }
        }
        finally
        {
            bool cleanupSucceeded = false;
            try
            {
                if (boundCaptureArmed) _boundInputTracker?.EndCapture();
                _worldBindingRecorder=null;
                if (_captureStages is { } stageTargets)
                    foreach (var stage in stageTargets.Values) RetireResource(stage.Lifetime);
                _captureStages = null;
                _captureTarget = null;
                _captureLifetime = null;
                _captureInputHash?.Dispose();
                _captureInputHash = null;
                _captureShadowTarget = null;
                if (_captureShadowLifetime is { } shadowLifetime) RetireResource(shadowLifetime);
                _captureShadowLifetime = null;
                RetireResource(lifetime);
                cleanupSucceeded = true;
            }
            catch (Exception cleanup)
            {
                if (capturePrimary is null) capturePrimary = cleanup;
                else GraphicsPostFrameFailure.TrySecondary(capturePrimary, cleanup);
            }
            if (captureBodySucceeded && cleanupSucceeded)
                PublishPostFrameStamp(stages == 0 && !captureShadowDepth && _boundInputTracker is null, presentedBefore);
        }
        if (capturePrimary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(capturePrimary).Throw();
        return captureOutput!;
    }

    private static GraphicsTextureReadbackLayout Layout(ResourceDescription description, PlacedSubresourceFootPrint footprint) =>
        new(checked((int)description.Width), checked((int)description.Height), checked((int)footprint.Footprint.RowPitch),
            footprint.Offset, description.Format.ToString());

    private void AddStageTarget(GraphicsCaptureStages requested, GraphicsCaptureStages stage, ID3D12Resource source)
    {
        if ((requested & stage) == 0) return;
        var footprints = new PlacedSubresourceFootPrint[1];
        _device.GetCopyableFootprints(source.Description, 0, 1, 0, footprints, new uint[1], new ulong[1], out ulong bytes);
        var resource = _device.CreateCommittedResource(HeapType.Readback, ResourceDescription.Buffer(bytes), ResourceStates.CopyDest);
        GraphicsResourceRetirement.Resource lifetime;
        try { lifetime = RegisterResource(resource.Dispose, checked((long)bytes)); }
        catch { resource.Dispose(); throw; }
        _captureStages!.Add(stage, new(resource, lifetime, footprints[0], Layout(source.Description, footprints[0]), bytes));
    }

    private void RecordStageCapture(GraphicsCaptureStages stage, ID3D12Resource source, ResourceStates state)
    {
        if (_captureStages is null || !_captureStages.TryGetValue(stage, out var target)) return;
        if (target.Recorded) throw new InvalidOperationException("A diagnostic stage cannot be copied twice in one frame.");
        UseResource(target.Lifetime);
        _commandList.ResourceBarrierTransition(source, state, ResourceStates.CopySource);
        _commandList.CopyTextureRegion(new TextureCopyLocation(target.Resource, target.Footprint), 0, 0, 0, new TextureCopyLocation(source, 0));
        _commandList.ResourceBarrierTransition(source, ResourceStates.CopySource, state);
        target.Recorded = true;
    }

    private void RecordPreCompositionCapture()
    {
        if (_captureStages is null) return;
        RecordStageCapture(GraphicsCaptureStages.SceneLinear, _sceneTargets![_frameIndex], ResourceStates.PixelShaderResource);
        RecordStageCapture(GraphicsCaptureStages.WorldDepth, _depthTarget!, ResourceStates.DepthWrite);
    }
    private void RecordPreOverlayCapture()
    {
        if (_captureStages is null) return;
        RecordStageCapture(GraphicsCaptureStages.ComposedColor, _renderTargets[_frameIndex], ResourceStates.RenderTarget);
    }

    private void RecordShadowCapture()
    {
        if (_captureShadowTarget is null) return;
        UseResource(_captureShadowLifetime!);
        ID3D12Resource depth = _shadowTargets![_frameIndex];
        _commandList.ResourceBarrierTransition(depth, ResourceStates.PixelShaderResource, ResourceStates.CopySource);
        _commandList.CopyTextureRegion(new TextureCopyLocation(_captureShadowTarget, _captureShadowFootprint), 0, 0, 0,
            new TextureCopyLocation(depth, 0));
        _commandList.ResourceBarrierTransition(depth, ResourceStates.CopySource, ResourceStates.PixelShaderResource);
        _captureShadowRecorded = true;
    }

    private void RecordFrameCapture(ID3D12Resource backBuffer)
    {
        if (_captureTarget is null)
            return;
        UseResource(_captureLifetime!);
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.RenderTarget, ResourceStates.CopySource);
        _commandList.CopyTextureRegion(new TextureCopyLocation(_captureTarget, _captureFootprint), 0, 0, 0,
            new TextureCopyLocation(backBuffer, 0));
        _captureFrameRecorded = true;
        _captureRecordedFrameIndex = _frameIndex;
        _commandList.ResourceBarrierTransition(backBuffer, ResourceStates.CopySource, ResourceStates.RenderTarget);
    }
}
