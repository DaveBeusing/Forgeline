using System.Diagnostics;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private GraphicsPostFrameCompletionStamp _postFrameStamp;
    private bool _postFrameReceiptIssued;
    private ulong _postFrameIdleObserved;
    private string? _postFrameCoverageMode;
    private bool _postFrameAttachmentClosed, _postFrameCapturePending, _postFrameInvocationOpen, _postFrameSessionEnded;
    private ulong _postFrameInvocation;
    private int _postFrameRepetition = -1;
    private GraphicsPostFrameCaptureContext _postFrameContext;
    private GraphicsPostFrameCommandTape? _postFrameTape;
    private Guid _postFrameTapeSession;
    private PostFrameTapeInvocation _postFrameTapeInvocation;
    private PostFrameTapeCompletion _postFrameTapeCompletion;
    private bool _postFrameTapeClaimed;
    private PostFrameTapeWire? _postFrameMaterializedWitness;
    private GraphicsPostFrameSnapshotResult? _postFrameCompletedCopy;
    internal GraphicsPostFrameCommandTape? ActivePostFrameTape => _postFrameTape is { Armed: true } ? _postFrameTape : null;
    internal PostFrameTapeInvocation ActivePostFrameTapeInvocation => _postFrameTapeInvocation;
    internal Action<GraphicsPostFrameOperation>? PostFrameFailureInjection { get; set; }

    internal void ConfigurePostFrameSnapshots(string mode)
    {
        ThrowIfDisposed(); ThrowIfRecording();
        if (_postFrameSessionEnded || !GraphicsPostFrameSnapshotModes.IsSupported(mode) || _postFrameAttachmentClosed || _postFrameCoverageMode is not null)
            throw new InvalidOperationException("Snapshot v2 selection requires one exact mode before any pipeline/frame.");
        if (mode == GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments)
        {
            _postFrameTapeSession = Guid.ParseExact(_telemetryIdentitySession, "N");
            _postFrameTape = new(this, _postFrameTapeSession, _ownerThreadId);
        }
        // Attachment-only stores this selection value and allocates no catalog/tape/observer arrays.
        _postFrameCoverageMode = mode;
    }

    internal GraphicsPostFrameCaptureContext BeginPostFrameCapture(string mode, int repetition)
    {
        ThrowIfDisposed(); ThrowIfRecording();
        if (_postFrameSessionEnded || !GraphicsPostFrameSnapshotModes.IsSupported(mode) || !string.Equals(mode, _postFrameCoverageMode, StringComparison.Ordinal) ||
            _postFrameCapturePending || _postFrameInvocationOpen || repetition != _postFrameRepetition + 1 || repetition is < 0 or > 7 ||
            _postFrameInvocation == ulong.MaxValue)
            throw new InvalidOperationException("Snapshot v2 invocation is foreign, unfinished or out of order.");
        _postFrameContext = new(this, _postFrameInvocation + 1, repetition, mode);
        _postFrameCapturePending = true;
        return _postFrameContext;
    }

    private void ClaimPostFrameCapture()
    {
        if (_postFrameCoverageMode is null) return;
        if (_postFrameSessionEnded) throw new InvalidOperationException("Selected post-frame session has terminated.");
        if (!_postFrameCapturePending || !_postFrameContext.Matches(this, _postFrameInvocation + 1, _postFrameCoverageMode))
            throw new InvalidOperationException("Selected capture lacks its current declared invocation.");
        _postFrameInvocation = _postFrameContext.Invocation;
        _postFrameRepetition = _postFrameContext.Repetition;
        _postFrameCapturePending = false;
        _postFrameInvocationOpen = true;
        _postFrameTapeClaimed = false;
        _postFrameTapeInvocation = default;
        _postFrameTapeCompletion = default;
        _postFrameMaterializedWitness = null;
        _postFrameCompletedCopy = null;
    }

    private void ArmPostFrameOpaqueTape()
    {
        if (_postFrameTape is null) return;
        _postFrameTapeInvocation = _postFrameTape.Arm(this, _postFrameTapeSession, _ownerThreadId, _postFrameRepetition);
        _postFrameTapeClaimed = _postFrameTapeInvocation.BelongsTo(_postFrameTape, _postFrameInvocation);
    }

    private void SealPostFrameOpaqueTape(bool bodySucceeded, bool cleanupSucceeded)
    {
        if (_postFrameTape is null || !_postFrameTapeClaimed) return;
        GraphicsPostFrameCompletionStamp stamp = _postFrameStamp;
        PostFrameTapeFrame frame = stamp.Valid ? new((uint)stamp.FrameIndex, stamp.Submission.Submission,
            stamp.Submission.SubmittedFence, stamp.CompletedFence, stamp.Submission.Generation,
            stamp.Submission.ResourceGeneration, stamp.PresentedBefore, stamp.PresentedAfter) : default;
        _postFrameTapeCompletion = _postFrameTape.ObserveOriginalCompletion(_postFrameTapeInvocation,
            frame, bodySucceeded, cleanupSucceeded && stamp.Valid);
        _postFrameTape.Seal(_postFrameTapeInvocation, this, _postFrameTapeSession, _ownerThreadId, _postFrameTapeCompletion);
        _postFrameTape.ClearSourceScratch();
    }

    internal uint TapeBufferIdentity(D3D12GraphicsBuffer buffer) => ActivePostFrameTape is { } tape ?
        tape.ObserveIdentity(_postFrameTapeInvocation, buffer, buffer.Lifetime, PostFrameTapeIdentityKind.Buffer,
            _telemetryGeneration, 0, (ulong)buffer.Description.Memory, 0, buffer.Description.SizeInBytes, 0) : 0;

    internal uint TapeTextureIdentity(D3D12GraphicsTexture texture) => ActivePostFrameTape is { } tape ?
        tape.ObserveIdentity(_postFrameTapeInvocation, texture, texture.Lifetime, PostFrameTapeIdentityKind.Texture,
            _telemetryGeneration, 0, (ulong)texture.DescriptorIndex,
            (uint)texture.Description.Width | ((ulong)(uint)texture.Description.Height << 32), 0,
            (uint)texture.Description.MipCount | ((ulong)(uint)texture.Description.Format << 8) | ((ulong)(uint)texture.Description.ColorSpace << 16)) : 0;

    internal uint TapePipelineIdentity(D3D12GraphicsPipeline pipeline, bool root = false) => ActivePostFrameTape is { } tape ?
        tape.ObserveIdentity(_postFrameTapeInvocation, root ? pipeline.RootSignature : pipeline, pipeline.Lifetime,
            root ? PostFrameTapeIdentityKind.RootSignature : PostFrameTapeIdentityKind.Pipeline, 0, 0, 0, 0, 0, 0) : 0;

    private uint TapeRawIdentity(ID3D12Resource resource, ulong slot, ulong descriptor = 0)
    {
        if (ActivePostFrameTape is not { } tape) return 0;
        if (_surfaceLifecycle.AppliedResizeGeneration == ulong.MaxValue) { tape.Refuse(PostFrameTapeRefusal.InvalidTransition); return 0; }
        return tape.ObserveIdentity(_postFrameTapeInvocation, resource, null, PostFrameTapeIdentityKind.RetainedTarget,
            _surfaceLifecycle.AppliedResizeGeneration + 1, slot, descriptor, 0, 0, 0);
    }

    private uint TapeHeapIdentity() => ActivePostFrameTape!.ObserveIdentity(_postFrameTapeInvocation, _shaderResourceHeap, null,
        PostFrameTapeIdentityKind.DescriptorHeap, _telemetryGeneration, 0, 0, 0, 0, 0);
    internal uint TapeShaderHeapIdentity() => ActivePostFrameTape is null ? 0 : TapeHeapIdentity();

    internal void TapeOwnerTargets(PostFrameTapeOwnerPostcondition source)
    {
        if (ActivePostFrameTape is not { } tape) return;
        bool shadow = source == PostFrameTapeOwnerPostcondition.ShadowTargets;
        bool particle = source == PostFrameTapeOwnerPostcondition.ParticleTargets;
        bool overlay = _passState.Current == GraphicsFramePass.Overlay;
        tape.Pass(_postFrameTapeInvocation, shadow ? PostFrameTapePass.Shadow : particle ? PostFrameTapePass.Particle :
            overlay ? PostFrameTapePass.Overlay : PostFrameTapePass.OpaqueWorld, true);
        uint color = shadow ? 0 : TapeRawIdentity(overlay || !_sceneOutput.Enabled ? _renderTargets[_frameIndex] : _sceneTargets![_frameIndex], (ulong)_frameIndex);
        uint depth = particle ? 0 : shadow ? TapeRawIdentity(_shadowTargets![_frameIndex], (ulong)_frameIndex, (ulong)_shadowDescriptors![_frameIndex]) :
            TapeRawIdentity(overlay && _overlayDepthTarget is not null ? _overlayDepthTarget : _depthTarget!, ulong.MaxValue);
        tape.OwnerPostcondition(_postFrameTapeInvocation, source, PostFrameTapeBinding.RenderTarget, 0, color);
        tape.OwnerPostcondition(_postFrameTapeInvocation, source, PostFrameTapeBinding.DepthTarget, 0, depth);
        if (source is PostFrameTapeOwnerPostcondition.ShadowTargets or PostFrameTapeOwnerPostcondition.ShadowReturnTargets)
        {
            int width = shadow ? _directionalShadows.Resolution : WorldWidth, height = shadow ? _directionalShadows.Resolution : WorldHeight;
            tape.Viewport(_postFrameTapeInvocation, 0, 0, BitConverter.SingleToUInt32Bits(width), BitConverter.SingleToUInt32Bits(height),
                0, BitConverter.SingleToUInt32Bits(1), source);
            tape.OwnerPostcondition(_postFrameTapeInvocation, source, PostFrameTapeBinding.Scissor, 0, 0, 0, 0, (uint)width, (uint)height);
        }
    }

    internal void TapeShadowReceiver(D3D12GraphicsPipeline pipeline)
    {
        if (ActivePostFrameTape is not { } tape) return;
        if (_surfaceLifecycle.AppliedResizeGeneration == ulong.MaxValue) { tape.Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        ulong epoch = _surfaceLifecycle.AppliedResizeGeneration + 1;
        uint arena = tape.ObserveIdentity(_postFrameTapeInvocation, _shadowConstants![_frameIndex], null,
            PostFrameTapeIdentityKind.ImplicitArena, epoch, (ulong)_frameIndex, 0, 0, GraphicsDirectionalShadowSettings.ConstantArenaBytesPerFrame, 4);
        uint root = (uint)(1 + pipeline.Description.PixelTextureCount);
        tape.OwnerPostcondition(_postFrameTapeInvocation, PostFrameTapeOwnerPostcondition.ShadowReceiverImplicit,
            PostFrameTapeBinding.RootParameter, root, arena, 0, 256, epoch, (ulong)_frameIndex);
        tape.RootWords(_postFrameTapeInvocation, root, tape.ShadowFrameWords, true, (uint)_frameIndex, epoch, 0, arena,
            PostFrameTapeOwnerPostcondition.ShadowReceiverImplicit);
        uint shadow = TapeRawIdentity(_shadowTargets![_frameIndex], (ulong)_frameIndex, (ulong)_shadowDescriptors![_frameIndex]);
        tape.OwnerPostcondition(_postFrameTapeInvocation, PostFrameTapeOwnerPostcondition.ShadowReceiverImplicit,
            PostFrameTapeBinding.DescriptorHeap, 0, TapeHeapIdentity());
        tape.OwnerPostcondition(_postFrameTapeInvocation, PostFrameTapeOwnerPostcondition.ShadowReceiverImplicit,
            PostFrameTapeBinding.TextureTable, 15, shadow, (ulong)_shadowDescriptors![_frameIndex], root + 1, TapeHeapIdentity(), 1);
    }

    internal void TapeVertexConstants(ReadOnlySpan<float> values, bool buffered)
    {
        if (ActivePostFrameTape is not { } tape) return;
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<float, uint>(values);
        if (!buffered) { tape.RootWords(_postFrameTapeInvocation, 0, words, false); return; }
        if (_surfaceLifecycle.AppliedResizeGeneration == ulong.MaxValue || _constantOffset < 256)
        { tape.Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        ulong epoch = _surfaceLifecycle.AppliedResizeGeneration + 1, offset = (ulong)(_constantOffset - 256);
        uint arena = tape.ObserveIdentity(_postFrameTapeInvocation, _shadowConstants![_frameIndex], null,
            PostFrameTapeIdentityKind.ImplicitArena, epoch, (ulong)_frameIndex, 0, 0, GraphicsDirectionalShadowSettings.ConstantArenaBytesPerFrame, 4);
        tape.OwnerPostcondition(_postFrameTapeInvocation, PostFrameTapeOwnerPostcondition.BufferedVertexConstant,
            PostFrameTapeBinding.RootParameter, 0, arena, offset, 256, epoch, (ulong)_frameIndex);
        tape.RootWords(_postFrameTapeInvocation, 0, words, true, (uint)_frameIndex, epoch, offset, arena);
    }

    internal void TapeVertexConstantsRequest(int words, bool buffered)
    {
        if (ActivePostFrameTape is not { } tape) return;
        if (!buffered) { tape.Request(_postFrameTapeInvocation, PostFrameTapeBinding.RootParameter, 0, 0, 0, (uint)words); return; }
        if (_shadowConstants is null || _surfaceLifecycle.AppliedResizeGeneration == ulong.MaxValue || _constantOffset < 256)
        { tape.Refuse(PostFrameTapeRefusal.InvalidTransition); return; }
        ulong epoch = _surfaceLifecycle.AppliedResizeGeneration + 1;
        uint arena = tape.ObserveIdentity(_postFrameTapeInvocation, _shadowConstants[_frameIndex], null,
            PostFrameTapeIdentityKind.ImplicitArena, epoch, (ulong)_frameIndex, 0, 0, GraphicsDirectionalShadowSettings.ConstantArenaBytesPerFrame, 4);
        tape.Request(_postFrameTapeInvocation, PostFrameTapeBinding.RootParameter, 0, arena, (uint)_constantOffset, 256, epoch, (ulong)_frameIndex);
    }

    internal void TapeDescriptorHeapReturned()
    {
        if (ActivePostFrameTape is { } tape)
            tape.NativeReturned(_postFrameTapeInvocation, PostFrameTapeBinding.DescriptorHeap, 0, TapeHeapIdentity());
    }

    private void InvalidatePostFrameStamp()
    {
        _postFrameStamp = default;
        _postFrameReceiptIssued = false;
    }
    private void PublishPostFrameStamp(bool ordinaryOnly, ulong before)
    {
        // Value facts only. DeviceIdentity queries and receipt allocation are strictly late.
        if (!ordinaryOnly || _failure is not null || _captureRecordedFrameIndex < 0 ||
            _captureRecordedFrameIndex >= _configuration.BufferCount || !_captureFrameRecorded ||
            _postFrameIdleObserved is 0 or ulong.MaxValue || _lastSubmittedFrameFence == 0 ||
            _postFrameIdleObserved < _lastSubmittedFrameFence || _presentedFrameCount != before + 1 ||
            _surfaceLifecycle.IsSuspended || _surfaceLifecycle.HasPendingResize || _surfaceLifecycle.IsOccluded ||
            _surfaceLifecycle.AppliedResizeGeneration == ulong.MaxValue ||
            (_shadowConstants is not null && _captureRecordedFrameIndex >= _shadowConstants.Length))
            return;
        _postFrameStamp = new(true, _submittedFrameCount,
            new(_submittedFrameCount, _lastSubmittedFrameFence, _telemetryGeneration, _surfaceLifecycle.AppliedResizeGeneration + 1),
            _postFrameIdleObserved, _captureRecordedFrameIndex, before, _presentedFrameCount,
            _surfaceLifecycle.Width, _surfaceLifecycle.Height, _surfaceLifecycle.ResizeGeneration,
            _surfaceLifecycle.AppliedResizeGeneration, _constantOffset, _shadowConstants?[_captureRecordedFrameIndex])
        {
            CoverageMode = _postFrameCoverageMode,
            Invocation = _postFrameCoverageMode is null ? 0 : _postFrameInvocation,
            SceneWitness = _postFrameCoverageMode is null ? null : _sceneTargets?[_captureRecordedFrameIndex],
            ShadowWitness = _postFrameCoverageMode is null ? null : _shadowTargets?[_captureRecordedFrameIndex],
            FinalDepthWitness = _postFrameCoverageMode is null ? null : _overlayDepthTarget ?? _depthTarget,
            WorldDepthWitness = _postFrameCoverageMode is null ? null : _depthTarget,
            WorldWidth = _postFrameCoverageMode is null ? 0 : WorldWidth,
            WorldHeight = _postFrameCoverageMode is null ? 0 : WorldHeight,
            ShadowResolution = _postFrameCoverageMode is null ? 0 : _directionalShadows.Resolution,
            ShadowRecorded = _postFrameCoverageMode is not null && _shadowRecorded
        };
    }
    private void ValidatePostFrameStamp(GraphicsPostFrameCompletionStamp stamp)
    {
        ThrowIfDisposed(); ThrowIfRecording();
        if (_postFrameSessionEnded) throw new InvalidOperationException("Selected post-frame session has terminated.");
        object? currentArena = _shadowConstants is not null && stamp.FrameIndex >= 0 && stamp.FrameIndex < _shadowConstants.Length ? _shadowConstants[stamp.FrameIndex] : null;
        if (!stamp.Valid || !_postFrameStamp.Valid || stamp != _postFrameStamp || !ReferenceEquals(currentArena, stamp.ConstantArenaWitness) ||
            stamp.Submission.Generation != _telemetryGeneration || stamp.Submission.Submission != _submittedFrameCount ||
            stamp.Submission.SubmittedFence != _lastSubmittedFrameFence || stamp.PresentedAfter != _presentedFrameCount ||
            stamp.ResizeGeneration != _surfaceLifecycle.ResizeGeneration || stamp.AppliedResizeGeneration != _surfaceLifecycle.AppliedResizeGeneration ||
            stamp.Width != _surfaceLifecycle.Width || stamp.Height != _surfaceLifecycle.Height ||
            _surfaceLifecycle.IsSuspended || _surfaceLifecycle.HasPendingResize || _surfaceLifecycle.IsOccluded)
            throw new InvalidOperationException("Post-frame receipt has no current successfully cleaned capture.");
        if (stamp.CoverageMode is not null && (!GraphicsPostFrameSnapshotModes.IsSupported(stamp.CoverageMode) ||
            !_postFrameInvocationOpen || !string.Equals(stamp.CoverageMode, _postFrameCoverageMode, StringComparison.Ordinal) ||
            stamp.Invocation != _postFrameInvocation || !stamp.ShadowRecorded || stamp.SceneWitness is null || stamp.ShadowWitness is null || stamp.FinalDepthWitness is null ||
            _sceneTargets is null || _shadowTargets is null || stamp.FrameIndex >= _sceneTargets.Length || stamp.FrameIndex >= _shadowTargets.Length ||
            !ReferenceEquals(stamp.SceneWitness, _sceneTargets[stamp.FrameIndex]) || !ReferenceEquals(stamp.ShadowWitness, _shadowTargets[stamp.FrameIndex]) ||
            !ReferenceEquals(stamp.FinalDepthWitness, _overlayDepthTarget ?? _depthTarget) || !ReferenceEquals(stamp.WorldDepthWitness, _depthTarget)))
            throw new InvalidOperationException("Snapshot v2 required retained attachment witnesses are missing or stale.");
        if (stamp.CoverageMode == GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments &&
            !ReferenceEquals(stamp.WorldDepthWitness, stamp.FinalDepthWitness))
            throw new NotSupportedException("Opaque World depth is not covered by the declared retained-final-depth attachment; no substitute copy is permitted.");
    }
    internal GraphicsPostFrameReceipt IssuePostFrameReceipt(int recordedSlot, GraphicsPostFrameCaptureContext? context = null)
    {
        var stamp = _postFrameStamp;
        ValidatePostFrameStamp(stamp);
        if (_postFrameReceiptIssued || recordedSlot != stamp.FrameIndex)
            throw new InvalidOperationException("Post-frame receipt already issued or actual callback slot differs.");
        if (stamp.CoverageMode is null ? context is not null : context is not { } selected ||
            !selected.Matches(this, stamp.Invocation, stamp.CoverageMode) || selected.Repetition != _postFrameRepetition)
            throw new InvalidOperationException("Snapshot mode/invocation context differs from the successful capture.");
        if (stamp.CoverageMode == GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments)
        {
            if (!_postFrameTapeClaimed || _postFrameTape is null || !_postFrameTape.PngClosed(_postFrameTapeInvocation))
                throw new InvalidOperationException("Opaque capture lacks its actual current claimed invocation and closed primary PNG source edge.");
            _postFrameMaterializedWitness = _postFrameTape.Materialize(_postFrameTapeInvocation);
            if (_postFrameTape.Refusal != PostFrameTapeRefusal.None)
                throw new InvalidDataException("Opaque observer refused the original capture: " + _postFrameTape.Refusal);
            _ = GraphicsPostFrameJson.Measure(_postFrameMaterializedWitness, GraphicsPostFrameLimits.BodyBytes);
        }
        var identity = DeviceIdentity ?? throw new InvalidDataException("Actual post-frame device identity unavailable.");
        var original = new GraphicsPostFrameOriginalReceipt(stamp.CoverageMode is null ? 1 : 2, "original-device-frame-fence", identity, _ownerThreadId,
            stamp.FrameIndex, stamp.Submission, stamp.CompletedFence, stamp.PresentedBefore, stamp.PresentedAfter,
            stamp.Width, stamp.Height, stamp.ResizeGeneration, stamp.AppliedResizeGeneration)
        { CoverageMode = stamp.CoverageMode, Invocation = stamp.CoverageMode is null ? null : stamp.Invocation,
            WorldDepthAliasesRetainedFinalDepth = stamp.CoverageMode is null ? null : ReferenceEquals(stamp.WorldDepthWitness, stamp.FinalDepthWitness) };
        var receipt = new GraphicsPostFrameReceipt(this, stamp.Serial, original, context);
        _postFrameReceiptIssued = true;
        return receipt;
    }

    internal object? CurrentPostFrameFailureFacts(GraphicsPostFrameCaptureContext context)
    {
        VerifyOwner();
        if (_postFrameSessionEnded || !context.Matches(this, _postFrameInvocation, _postFrameCoverageMode) || context.Repetition != _postFrameRepetition) return null;
        return new { kind = "current-post-frame-v2-incomplete-context", mode = context.Mode, invocation = context.Invocation,
            repetition = context.Repetition, opaqueTapeCaptureClaimed = _postFrameTapeClaimed,
            originalReceiptIssued = _postFrameReceiptIssued, originalStampValid = _postFrameStamp.Valid,
            observerRefusal = _postFrameTape?.Refusal, opaqueWitness = _postFrameMaterializedWitness,
            copyCompletionAvailable = _postFrameCompletedCopy is not null, invocationOpen = _postFrameInvocationOpen,
            complete = false, qualified = false };
    }

    internal void CompletePostFrameExport(GraphicsPostFrameReceipt receipt, GraphicsPostFrameSnapshotResult result)
    {
        ThrowIfDisposed(); ThrowIfRecording(); ValidatePostFrameStamp(_postFrameStamp);
        if (!_postFrameInvocationOpen || !receipt.Consumed || !ReferenceEquals(receipt.Owner, this) ||
            !ReferenceEquals(result, _postFrameCompletedCopy) || !ReferenceEquals(result.CompletionOwner, this) ||
            !ReferenceEquals(result.OriginalFrameReceipt, receipt.Original) || receipt.CaptureContext is not { } context ||
            !context.Matches(this, _postFrameInvocation, _postFrameCoverageMode) ||
            !result.CopyReceipt.CompletionProved || !result.CopyReceipt.MayHaveExecuted || result.CopyReceipt.CompletedFence == ulong.MaxValue ||
            result.CopyReceipt.SubmittedFence == 0 || result.CopyReceipt.CompletedFence < result.CopyReceipt.SubmittedFence)
            throw new InvalidOperationException("Snapshot publication lacks its current actual native copy/export source proof.");
        if (_postFrameCoverageMode == GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments)
        {
            if (_postFrameTape is null || result.OpaqueWitness is not PostFrameTapeWire witness ||
                !_postFrameTape.ConfirmEvidenceExported(_postFrameTapeInvocation) ||
                !_postFrameTape.FinalizeNativeExport(_postFrameTapeInvocation, _postFrameTapeCompletion, receipt, result, witness))
                throw new InvalidOperationException("Opaque native publication was not finalized by its current owner.");
        }
        else if (result.OpaqueWitness is not null) throw new InvalidOperationException("Attachment-only cannot publish an opaque witness.");
        _postFrameInvocationOpen = false;
    }

    internal void EndPostFrameSnapshotSession()
    {
        // Terminal managed cleanup remains available on a failed/disposed owner, but must not
        // destroy an observer while the original command recording is still in progress.
        VerifyOwner(); ThrowIfRecording();
        if (_postFrameSessionEnded || _postFrameCoverageMode is null) return;
        _postFrameSessionEnded = true;
        _postFrameAttachmentClosed = true;
        _postFrameCapturePending = _postFrameInvocationOpen = _postFrameTapeClaimed = false;
        _postFrameContext = default;
        _postFrameTapeInvocation = default;
        _postFrameTapeCompletion = default;
        _postFrameTapeSession = default;
        _postFrameIdleObserved = 0;
        InvalidatePostFrameStamp();
        _postFrameTape?.Dispose();
        _postFrameTape = null;
        _postFrameMaterializedWitness = null;
        _postFrameCompletedCopy = null;
        // Keep the selected mode and the native resource-retirement registry. This is not
        // a new selection window, nor proof that uncertain native copy work has completed.
    }
    private void VerifyPostFrameInitializerAbsence(GraphicsPostFrameCompletionStamp stamp)
    {
        // No tracker is required and no late flush is performed. All values are existing upload bookkeeping.
        if (_activeUploadBatch >= 0)
            throw new InvalidOperationException("Post-frame snapshot refuses a pending initializer batch.");
        foreach (var batch in _uploadBatches)
            if (batch is not null && (batch.Destinations.Count != 0 || batch.Fence == ulong.MaxValue || batch.Fence > stamp.CompletedFence))
                throw new InvalidOperationException("Post-frame snapshot lacks proof of completed initializer work.");
    }
    private static void ValidatePostFrameLifetime(GraphicsResourceRetirement.Resource lifetime, GraphicsPostFrameCompletionStamp stamp)
    {
        lifetime.ThrowIfDisposed();
        if (lifetime.Recording || lifetime.LastUseFence > stamp.CompletedFence)
            throw new InvalidOperationException("Declared post-frame resource has unresolved original-frame use.");
    }

    private sealed record PostFrameNativeSource(ID3D12Resource Resource, GraphicsPostFrameResourceDescription Description,
        PlacedSubresourceFootPrint[] Footprints, bool Texture, ResourceStates RestoreState = ResourceStates.PixelShaderResource,
        uint TransitionSubresource = uint.MaxValue);

    internal GraphicsPostFrameSnapshotResult CapturePostFrameSnapshot(GraphicsPostFrameReceipt receipt,
        GraphicsPostFrameRoster roster, GraphicsPostFrameIntent intent)
    {
        ArgumentNullException.ThrowIfNull(receipt); ArgumentNullException.ThrowIfNull(roster);
        var stamp = _postFrameStamp;
        ValidatePostFrameStamp(stamp);
        if (receipt.Serial != stamp.Serial || receipt.Original.Submission != stamp.Submission || receipt.Original.FrameIndex != stamp.FrameIndex ||
            !string.Equals(receipt.Original.CoverageMode, stamp.CoverageMode, StringComparison.Ordinal) ||
            (stamp.CoverageMode is not null && (receipt.CaptureContext is not { } selected || !selected.Matches(this, stamp.Invocation, stamp.CoverageMode))))
            throw new InvalidOperationException("Post-frame receipt belongs to a stale capture.");
        receipt.Consume(this);
        VerifyPostFrameInitializerAbsence(stamp);
        var parts = new List<IDisposable>(GraphicsPostFrameLimits.Resources + 8);
        var plans = new List<PostFrameNativeSource>(GraphicsPostFrameLimits.Resources);
        var retained = new List<GraphicsPostFrameResourceBytes>();
        GraphicsPostFrameCompositeLease? lease = null;
        GraphicsResourceRetirement.Resource? leaseLifetime = null;
        GraphicsPostFrameCopyReceipt? copyReceipt = null;
        string phase = "declare";
        T Own<T>(T part) where T : IDisposable
        {
            try { parts.Add(part); return part; }
            catch { try { part.Dispose(); } catch { } throw; }
        }
        try
        {
            ulong cursor = 0, canonical = 0;
            int mipEntries = 0;
            foreach (var request in roster.Sources)
            {
                ID3D12Resource source;
                GraphicsBufferDescription? buffer = null;
                GraphicsTextureDescription? texture = null;
                uint? causalIdentity = null;
                bool isTexture;
                if (request.Resource is D3D12GraphicsBuffer b && ReferenceEquals(b.Owner, this))
                {
                    ValidatePostFrameLifetime(b.Lifetime, stamp);
                    source = Own(b.Resource.QueryInterface<ID3D12Resource>()); buffer = b.Description; isTexture = false;
                    uint found = _postFrameTape?.FindIdentity(b, b.Lifetime, PostFrameTapeIdentityKind.Buffer) ?? 0;
                    if (found != 0) causalIdentity = found;
                }
                else if (request.Resource is D3D12GraphicsTexture t && ReferenceEquals(t.Owner, this))
                {
                    ValidatePostFrameLifetime(t.Lifetime, stamp);
                    source = Own(t.Resource.QueryInterface<ID3D12Resource>()); texture = t.Description; isTexture = true;
                    uint found = _postFrameTape?.FindIdentity(t, t.Lifetime, PostFrameTapeIdentityKind.Texture) ?? 0;
                    if (found != 0) causalIdentity = found;
                }
                else throw new NotSupportedException("A declared post-frame resource is unsupported or belongs to another device.");
                var native = source.Description;
                phase = "footprints"; PostFrameFailureInjection?.Invoke(GraphicsPostFrameOperation.Footprints);
                var mips = new List<GraphicsPostFrameMip>();
                PlacedSubresourceFootPrint[] footprints;
                ulong observed;
                if (!isTexture)
                {
                    observed = request.Length ?? checked(buffer!.Value.SizeInBytes - request.Offset);
                    if (native.Dimension != ResourceDimension.Buffer || native.Width != buffer!.Value.SizeInBytes || observed == 0 ||
                        request.Offset > native.Width || observed > native.Width - request.Offset)
                        throw new NotSupportedException("Declared buffer range/native description differs.");
                    cursor = GraphicsPostFrameLimits.Align(cursor, 512);
                    mips.Add(new(-1, 0, 0, 1, observed, observed, cursor, observed, observed, "Buffer"));
                    cursor = checked(cursor + observed); canonical = checked(canonical + observed); footprints = [];
                }
                else
                {
                    texture!.Validate();
                    if (native.Dimension != ResourceDimension.Texture2D || native.DepthOrArraySize != 1 || native.SampleDescription.Count != 1 ||
                        native.Width != (ulong)texture.Width || native.Height != texture.Height || native.MipLevels != texture.MipCount)
                        throw new NotSupportedException("Only declared immutable single-layer single-sample2D textures are supported.");
                    footprints = new PlacedSubresourceFootPrint[texture.MipCount];
                    var rows = new uint[texture.MipCount]; var rowSizes = new ulong[texture.MipCount];
                    cursor = GraphicsPostFrameLimits.Align(cursor, 512);
                    _device.GetCopyableFootprints(native, 0, checked((uint)texture.MipCount), cursor, footprints, rows, rowSizes, out ulong queriedBytes);
                    if (queriedBytes is 0 or ulong.MaxValue) throw new InvalidDataException("Invalid native snapshot footprints.");
                    Format expectedFormat = texture.Format == GraphicsTextureFormat.R8Unorm ? Format.R8_UNorm :
                        texture.Format == GraphicsTextureFormat.Bc7Unorm ? (texture.ColorSpace == GraphicsTextureColorSpace.Srgb ? Format.BC7_UNorm_SRgb : Format.BC7_UNorm) :
                        (texture.ColorSpace == GraphicsTextureColorSpace.Srgb ? Format.R8G8B8A8_UNorm_SRgb : Format.R8G8B8A8_UNorm);
                    if (native.Format != expectedFormat) throw new NotSupportedException("Declared texture/native encoded format differs.");
                    observed = 0;
                    for (int mip = 0; mip < texture.MipCount; mip++)
                    {
                        var f = footprints[mip];
                        var logical = GraphicsPostFrameLimits.CanonicalMip(texture, mip);
                        int width = logical.Width, height = logical.Height, count = logical.Rows;
                        var footprintSize = GraphicsPostFrameLimits.NativeFootprintDimensions(texture, mip);
                        ulong logicalRow = logical.RowBytes;
                        if (f.Footprint.Width != footprintSize.Width || f.Footprint.Height != footprintSize.Height || f.Footprint.Depth != 1 || rows[mip] != count ||
                            rowSizes[mip] != logicalRow || f.Footprint.RowPitch < logicalRow || f.Footprint.RowPitch % 256 != 0 || f.Offset % 512 != 0)
                            throw new InvalidDataException("Actual native mip footprint differs from supported encoded shape.");
                        ulong padded = checked((ulong)f.Footprint.RowPitch * (ulong)count), raw = checked(logicalRow * (ulong)count);
                        mips.Add(new(mip, width, height, count, logicalRow, f.Footprint.RowPitch, f.Offset, padded, raw, f.Footprint.Format.ToString()));
                        cursor = Math.Max(cursor, checked(f.Offset + padded)); observed = checked(observed + raw); canonical = checked(canonical + raw);
                    }
                }
                mipEntries = checked(mipEntries + mips.Count);
                if (mipEntries > GraphicsPostFrameLimits.MipEntries) throw new InvalidDataException("Snapshot mip-entry capacity exceeded.");
                GraphicsPostFrameLimits.Totals(cursor, canonical);
                var description = new GraphicsPostFrameResourceDescription(request.Id, request.Name, isTexture ? "texture" : "buffer", true,
                    buffer, texture, null, request.Offset, observed, "Declared resident subset; not a prior shader-fetch or bound-range assertion.", mips.ToArray())
                { CausalIdentityId = causalIdentity };
                plans.Add(new(source, description, footprints, isTexture));
            }
            // Include only the actual captured-slot implicit arena prefix, never the next swapchain slot.
            if (_shadowConstants is not null)
            {
                if (plans.Count == GraphicsPostFrameLimits.Resources || stamp.ConstantUsedPrefix is < 256 or > 4096 * 256)
                    throw new InvalidDataException("Implicit arena prefix is unavailable or exceeds the declared resource roster.");
                ID3D12Resource source = Own(_shadowConstants[stamp.FrameIndex].QueryInterface<ID3D12Resource>());
                ulong bytes = checked((ulong)stamp.ConstantUsedPrefix); cursor = GraphicsPostFrameLimits.Align(cursor, 512);
                var mip = new GraphicsPostFrameMip(-1, 0, 0, 1, bytes, bytes, cursor, bytes, bytes, "Buffer");
                var implicitIdentity = new GraphicsWorldImplicitBinding("captured-slot-shadow-vertex-constant-prefix", stamp.Submission.ResourceGeneration,
                    stamp.FrameIndex, null, null, null, 0);
                var description = new GraphicsPostFrameResourceDescription(plans.Count + 1, "captured-slot-constant-prefix", "implicit-buffer", false,
                    new(source.Description.Width, GraphicsBufferMemory.Upload), null, implicitIdentity, 0, bytes,
                    "Existing allocation cursor prefix including reserved/padding bytes; shader relevance is unknown.", [mip]);
                uint arenaId = _postFrameTape?.FindIdentity(_shadowConstants[stamp.FrameIndex], null, PostFrameTapeIdentityKind.ImplicitArena) ?? 0;
                if (arenaId != 0) description = description with { CausalIdentityId = arenaId };
                plans.Add(new(source, description, [], false)); cursor = checked(cursor + bytes); canonical = checked(canonical + bytes);
            }
            if (stamp.CoverageMode is not null)
            {
                AddAttachment("retained-scene-linear", (ID3D12Resource)stamp.SceneWitness!, Format.R16G16B16A16_Float,
                    stamp.WorldWidth, stamp.WorldHeight, ResourceStates.PixelShaderResource);
                AddAttachment("retained-shadow-depth", (ID3D12Resource)stamp.ShadowWitness!, Format.R32_Typeless,
                    stamp.ShadowResolution, stamp.ShadowResolution, ShadowReadState);
                var finalDepth = (ID3D12Resource)stamp.FinalDepthWitness!;
                AddAttachment("retained-final-depth", finalDepth, finalDepth.Description.Format,
                    _overlayDepthTarget is null ? stamp.WorldWidth : stamp.Width,
                    _overlayDepthTarget is null ? stamp.WorldHeight : stamp.Height, ResourceStates.DepthWrite);
            }
            void AddAttachment(string role, ID3D12Resource actual, Format expectedFormat, int width, int height, ResourceStates state)
            {
                if (plans.Count >= GraphicsPostFrameLimits.Resources || mipEntries >= GraphicsPostFrameLimits.MipEntries)
                    throw new InvalidDataException("Required attachment exceeds existing aggregate structural capacity.");
                ID3D12Resource retainedSource = Own(actual.QueryInterface<ID3D12Resource>());
                ResourceDescription native = retainedSource.Description;
                bool scene = role == "retained-scene-linear";
                if (width <= 0 || height <= 0 || native.Dimension != ResourceDimension.Texture2D || native.Width != (ulong)width ||
                    native.Height != height || native.DepthOrArraySize != 1 || native.SampleDescription.Count != 1 ||
                    !GraphicsPostFrameLimits.SupportedAttachmentMips(role, width, height, native.MipLevels, 0, 1, 0) ||
                    native.Format != expectedFormat || (scene ? expectedFormat != Format.R16G16B16A16_Float : expectedFormat is not (Format.R32_Typeless or Format.D32_Float)))
                    throw new NotSupportedException($"Required retained attachment lacks its exact supported native format/view/layout: role={role}; dimension={native.Dimension}; width={native.Width}; height={native.Height}; array={native.DepthOrArraySize}; mipLevels={native.MipLevels}; sampleCount={native.SampleDescription.Count}; sampleQuality={native.SampleDescription.Quality}; format={native.Format}.");
                phase = "attachment-footprints"; PostFrameFailureInjection?.Invoke(GraphicsPostFrameOperation.Footprints);
                cursor = GraphicsPostFrameLimits.Align(cursor, 512);
                var footprints = new PlacedSubresourceFootPrint[1]; var rows = new uint[1]; var rowBytes = new ulong[1];
                _device.GetCopyableFootprints(native, 0, 1, cursor, footprints, rows, rowBytes, out ulong queried);
                var footprint = footprints[0]; ulong logicalRow = checked((ulong)width * (scene ? 8UL : 4UL));
                if (queried is 0 or ulong.MaxValue || footprint.Offset % 512 != 0 || footprint.Footprint.Width != width ||
                    footprint.Footprint.Height != height || footprint.Footprint.Depth != 1 || footprint.Footprint.Format != expectedFormat ||
                    rows[0] != height || rowBytes[0] != logicalRow || footprint.Footprint.RowPitch < logicalRow || footprint.Footprint.RowPitch % 256 != 0)
                    throw new InvalidDataException("Required retained attachment native footprint differs.");
                ulong padded = checked((ulong)footprint.Footprint.RowPitch * (ulong)height), raw = checked(logicalRow * (ulong)height);
                cursor = checked(footprint.Offset + padded); canonical = checked(canonical + raw); mipEntries++;
                GraphicsPostFrameLimits.Totals(cursor, canonical);
                var mip = new GraphicsPostFrameMip(0, width, height, height, logicalRow, footprint.Footprint.RowPitch,
                    footprint.Offset, padded, raw, expectedFormat.ToString());
                var description = new GraphicsPostFrameResourceDescription(plans.Count + 1, role, "retained-attachment", false,
                    null, null, null, 0, raw, "Actual captured-slot retained target after PNG; not earlier shader fetches or pre-overlay depth.", [mip])
                {
                    Attachment = new(role, expectedFormat.ToString(), scene ? "R16G16B16A16_Float" : "R32_Float",
                        scene ? null : "D32_Float", role == "retained-shadow-depth" ? "R32_Float" : scene ? "R16G16B16A16_Float" : null,
                        state.ToString(), width, height, stamp.FrameIndex, stamp.Submission.ResourceGeneration,
                        "Same DIRECT queue, mip0 array0 plane0 only after original capture and PNG; other backing mip contents unobserved; final depth can include overlay writes.")
                    { NativeMipLevels = native.MipLevels, ObservedMipSlice = 0, ObservedMipLevels = 1, ObservedSubresource = 0 }
                };
                uint targetId = _postFrameTape?.FindIdentity(actual, null, PostFrameTapeIdentityKind.RetainedTarget) ?? 0;
                if (targetId != 0) description = description with { CausalIdentityId = targetId };
                plans.Add(new(retainedSource, description, footprints, true, state, 0));
            }
            GraphicsPostFrameLimits.Totals(cursor, canonical);
            GraphicsPostFrameLimits.ValidateIntent(intent, plans.Count);
            if (stamp.CoverageMode is null)
                GraphicsPostFrameLimits.MetadataBody(new { resources = plans.Select(x => x.Description).ToArray(), intent },
                    plans.Sum(x => x.Description.Mips.Length));
            else GraphicsPostFrameLimits.MetadataBody(new { resources = plans.Select(x => x.Description).ToArray(), intent,
                    opaqueWitness = _postFrameMaterializedWitness }, plans.Sum(x => x.Description.Mips.Length));
            phase = "allocate"; PostFrameFailureInjection?.Invoke(GraphicsPostFrameOperation.AllocateReadback);
            ID3D12Resource readback = Own(_device.CreateCommittedResource(HeapType.Readback, ResourceDescription.Buffer(cursor), ResourceStates.CopyDest));
            phase = "copy-objects"; PostFrameFailureInjection?.Invoke(GraphicsPostFrameOperation.CreateCopyObjects);
            var allocator = Own(_device.CreateCommandAllocator(CommandListType.Direct));
            var commands = Own(_device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, allocator));
            var fence = Own(_device.CreateFence(0)); var completion = Own(new AutoResetEvent(false));
            string domain = "late-copy-fence-" + Guid.NewGuid().ToString("N");
            lease = new(parts.ToArray()); leaseLifetime = RegisterResource(lease.Dispose, checked((long)cursor));
            phase = "record";
            foreach (var plan in plans)
            {
                if (plan.Texture)
                {
                    commands.ResourceBarrierTransition(plan.Resource, plan.RestoreState, ResourceStates.CopySource, plan.TransitionSubresource);
                    for (int mip = 0; mip < plan.Footprints.Length; mip++)
                        commands.CopyTextureRegion(new TextureCopyLocation(readback, plan.Footprints[mip]), 0, 0, 0, new TextureCopyLocation(plan.Resource, checked((uint)mip)));
                    commands.ResourceBarrierTransition(plan.Resource, ResourceStates.CopySource, plan.RestoreState, plan.TransitionSubresource);
                }
                else commands.CopyBufferRegion(readback, plan.Description.Mips[0].PackedOffset, plan.Resource, plan.Description.SourceOffset, plan.Description.ObservedBytes);
            }
            commands.Close();
            phase = "before-execute"; PostFrameFailureInjection?.Invoke(GraphicsPostFrameOperation.BeforeExecute);
            lease.MayHaveExecuted = true; // BEFORE the native call: a throwing Execute is not proof that no work ran.
            phase = "execute"; _commandQueue.ExecuteCommandList(commands);
            PostFrameFailureInjection?.Invoke(GraphicsPostFrameOperation.AfterExecute);
            phase = "signal"; PostFrameFailureInjection?.Invoke(GraphicsPostFrameOperation.Signal);
            CheckResult(_commandQueue.Signal(fence, 1), "post-frame-copy-signal-failed");
            phase = "wait"; PostFrameFailureInjection?.Invoke(GraphicsPostFrameOperation.Wait);
            CheckResult(fence.SetEventOnCompletion(1, completion), "post-frame-copy-event-failed");
            long began = Stopwatch.GetTimestamp(); ulong completed;
            while ((completed = fence.CompletedValue) < 1)
            {
                completion.WaitOne(100);
                completed = fence.CompletedValue;
                if (completed == ulong.MaxValue) throw new GraphicsDeviceException("Device removed during late copy.", "device-removed");
                if (completed >= 1) break;
                if (Stopwatch.GetElapsedTime(began) >= TimeSpan.FromSeconds(10)) throw new GraphicsDeviceException("Late copy fence timeout.", "fence-timeout");
            }
            if (completed == ulong.MaxValue) throw new GraphicsDeviceException("Device removed during late copy.", "device-removed");
            lease.CompletionProved = true;
            copyReceipt = new(1, domain, "Same DIRECT queue, issued after original completed color capture; fence values belong to separate domains.", 1, completed, true, true);
            phase = "map"; PostFrameFailureInjection?.Invoke(GraphicsPostFrameOperation.Map);
            var paddedBytes = new byte[checked((int)cursor)]; readback.GetData<byte>(paddedBytes);
            foreach (var plan in plans)
            {
                var raw = new byte[plan.Description.Mips.Length][];
                for (int mip = 0; mip < raw.Length; mip++)
                {
                    var layout = plan.Description.Mips[mip]; var bytes = new byte[checked((int)layout.CanonicalBytes)];
                    for (int row = 0; row < layout.RowCount; row++)
                        paddedBytes.AsSpan(checked((int)(layout.PackedOffset + (ulong)row * layout.RowPitch)), checked((int)layout.RowBytes))
                            .CopyTo(bytes.AsSpan(checked(row * (int)layout.RowBytes), checked((int)layout.RowBytes)));
                    raw[mip] = bytes;
                }
                retained.Add(new(plan.Description, raw));
            }
            phase = "retire"; RetireResource(leaseLifetime);
            if (lease.ErrorCount != 0) throw new InvalidOperationException("Late native lease cleanup failed; diagnostic unavailable.");
            var result = new GraphicsPostFrameSnapshotResult(receipt.Original, copyReceipt, retained.ToArray(), intent, cursor, canonical, lease.Errors)
            { CompletionOwner = stamp.CoverageMode is null ? null : this };
            if (stamp.CoverageMode == GraphicsPostFrameSnapshotModes.OpaqueStateAndRetainedAttachments)
                result = result with { OpaqueWitness = _postFrameTape!.CompleteNativeWitness(_postFrameTapeInvocation,
                    _postFrameTapeCompletion, receipt, result, _postFrameMaterializedWitness ?? throw new InvalidDataException("Current opaque materialization unavailable.")) };
            if (stamp.CoverageMode is not null) _postFrameCompletedCopy = result;
            return result;
        }
        catch (Exception error)
        {
            // Uncertain executed work stays ACTIVE in the existing registry until device drain/removal.
            if (leaseLifetime is not null && (lease is { MayHaveExecuted: false } or { CompletionProved: true }))
                try { RetireResource(leaseLifetime); } catch (Exception cleanup) { GraphicsPostFrameFailure.TrySecondary(error, cleanup); }
            else if (leaseLifetime is null)
            {
                if (lease is not null) lease.Dispose();
                else
                    for (int index = 0; index < parts.Count; index++)
                        try { parts[index].Dispose(); } catch (Exception cleanup) { GraphicsPostFrameFailure.TrySecondary(error, cleanup); }
            }
            try { GraphicsPostFrameFailure.Attach(error, receipt, phase, copyReceipt, retained.ToArray(), lease?.MayHaveExecuted ?? false, lease?.Errors ?? []); }
            catch { }
            throw;
        }
    }
}

internal static class GraphicsPostFrameFailure
{
    private const string Key = "ForgeLine.PostFrameSnapshot.v1";
    private sealed record Attachment(GraphicsPostFrameReceipt Receipt, string Phase, GraphicsPostFrameCopyReceipt? CopyReceipt,
        GraphicsPostFrameResourceBytes[] Resources, bool MayHaveExecuted, string[] CleanupErrors);
    internal static GraphicsPostFrameSnapshotFailure? Read(Exception error, GraphicsPostFrameReceipt? receipt)
    {
        if (receipt is null || !receipt.Consumed || error.Data[Key] is not Attachment attachment || !ReferenceEquals(attachment.Receipt, receipt)) return null;
        return new(attachment.Phase, receipt.Original, attachment.CopyReceipt, attachment.Resources, attachment.MayHaveExecuted, attachment.CleanupErrors);
    }
    internal static void TrySecondary(Exception primary, Exception secondary)
    {
        try { primary.Data["ForgeLine.PostFrameSecondary." + primary.Data.Count] = secondary.GetType().Name + ": " + secondary.Message; } catch { }
    }
    internal static void Attach(Exception error, GraphicsPostFrameReceipt receipt, string phase, GraphicsPostFrameCopyReceipt? copy,
        GraphicsPostFrameResourceBytes[] resources, bool mayHaveExecuted, string[] cleanup)
    {
        try { error.Data[Key] = new Attachment(receipt, phase, copy, resources, mayHaveExecuted, cleanup); } catch { }
    }
}
