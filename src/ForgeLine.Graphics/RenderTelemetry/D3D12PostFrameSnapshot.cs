using System.Diagnostics;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private GraphicsPostFrameCompletionStamp _postFrameStamp;
    private bool _postFrameReceiptIssued;
    private ulong _postFrameIdleObserved;
    internal Action<GraphicsPostFrameOperation>? PostFrameFailureInjection { get; set; }

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
            _surfaceLifecycle.AppliedResizeGeneration, _constantOffset, _shadowConstants?[_captureRecordedFrameIndex]);
    }
    private void ValidatePostFrameStamp(GraphicsPostFrameCompletionStamp stamp)
    {
        ThrowIfDisposed(); ThrowIfRecording();
        object? currentArena = _shadowConstants is not null && stamp.FrameIndex >= 0 && stamp.FrameIndex < _shadowConstants.Length ? _shadowConstants[stamp.FrameIndex] : null;
        if (!stamp.Valid || !_postFrameStamp.Valid || stamp != _postFrameStamp || !ReferenceEquals(currentArena, stamp.ConstantArenaWitness) ||
            stamp.Submission.Generation != _telemetryGeneration || stamp.Submission.Submission != _submittedFrameCount ||
            stamp.Submission.SubmittedFence != _lastSubmittedFrameFence || stamp.PresentedAfter != _presentedFrameCount ||
            stamp.ResizeGeneration != _surfaceLifecycle.ResizeGeneration || stamp.AppliedResizeGeneration != _surfaceLifecycle.AppliedResizeGeneration ||
            stamp.Width != _surfaceLifecycle.Width || stamp.Height != _surfaceLifecycle.Height ||
            _surfaceLifecycle.IsSuspended || _surfaceLifecycle.HasPendingResize || _surfaceLifecycle.IsOccluded)
            throw new InvalidOperationException("Post-frame receipt has no current successfully cleaned capture.");
    }
    internal GraphicsPostFrameReceipt IssuePostFrameReceipt(int recordedSlot)
    {
        var stamp = _postFrameStamp;
        ValidatePostFrameStamp(stamp);
        if (_postFrameReceiptIssued || recordedSlot != stamp.FrameIndex)
            throw new InvalidOperationException("Post-frame receipt already issued or actual callback slot differs.");
        var identity = DeviceIdentity ?? throw new InvalidDataException("Actual post-frame device identity unavailable.");
        var original = new GraphicsPostFrameOriginalReceipt(1, "original-device-frame-fence", identity, _ownerThreadId,
            stamp.FrameIndex, stamp.Submission, stamp.CompletedFence, stamp.PresentedBefore, stamp.PresentedAfter,
            stamp.Width, stamp.Height, stamp.ResizeGeneration, stamp.AppliedResizeGeneration);
        var receipt = new GraphicsPostFrameReceipt(this, stamp.Serial, original);
        _postFrameReceiptIssued = true;
        return receipt;
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
        PlacedSubresourceFootPrint[] Footprints, bool Texture);

    internal GraphicsPostFrameSnapshotResult CapturePostFrameSnapshot(GraphicsPostFrameReceipt receipt,
        GraphicsPostFrameRoster roster, GraphicsPostFrameIntent intent)
    {
        ArgumentNullException.ThrowIfNull(receipt); ArgumentNullException.ThrowIfNull(roster);
        var stamp = _postFrameStamp;
        ValidatePostFrameStamp(stamp);
        if (receipt.Serial != stamp.Serial || receipt.Original.Submission != stamp.Submission || receipt.Original.FrameIndex != stamp.FrameIndex)
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
                bool isTexture;
                if (request.Resource is D3D12GraphicsBuffer b && ReferenceEquals(b.Owner, this))
                {
                    ValidatePostFrameLifetime(b.Lifetime, stamp);
                    source = Own(b.Resource.QueryInterface<ID3D12Resource>()); buffer = b.Description; isTexture = false;
                }
                else if (request.Resource is D3D12GraphicsTexture t && ReferenceEquals(t.Owner, this))
                {
                    ValidatePostFrameLifetime(t.Lifetime, stamp);
                    source = Own(t.Resource.QueryInterface<ID3D12Resource>()); texture = t.Description; isTexture = true;
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
                    buffer, texture, null, request.Offset, observed, "Declared resident subset; not a prior shader-fetch or bound-range assertion.", mips.ToArray());
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
                plans.Add(new(source, description, [], false)); cursor = checked(cursor + bytes); canonical = checked(canonical + bytes);
            }
            GraphicsPostFrameLimits.Totals(cursor, canonical);
            GraphicsPostFrameLimits.ValidateIntent(intent, plans.Count);
            GraphicsPostFrameLimits.MetadataBody(new { resources = plans.Select(x => x.Description).ToArray(), intent },
                plans.Sum(x => x.Description.Mips.Length));
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
                    commands.ResourceBarrierTransition(plan.Resource, ResourceStates.PixelShaderResource, ResourceStates.CopySource);
                    for (int mip = 0; mip < plan.Footprints.Length; mip++)
                        commands.CopyTextureRegion(new TextureCopyLocation(readback, plan.Footprints[mip]), 0, 0, 0, new TextureCopyLocation(plan.Resource, checked((uint)mip)));
                    commands.ResourceBarrierTransition(plan.Resource, ResourceStates.CopySource, ResourceStates.PixelShaderResource);
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
            return new(receipt.Original, copyReceipt, retained.ToArray(), intent, cursor, canonical, lease.Errors);
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
