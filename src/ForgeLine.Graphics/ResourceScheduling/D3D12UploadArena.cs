using ForgeLine.Graphics.RenderContracts;
using Vortice.Direct3D12;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private readonly UploadBatch?[] _uploadBatches = new UploadBatch[UploadArenaLimits.SlotCount];
    private readonly PlacedSubresourceFootPrint[] _uploadLayouts = new PlacedSubresourceFootPrint[32];
    private readonly uint[] _uploadRows = new uint[32];
    private readonly ulong[] _uploadRowSizes = new ulong[32];
    private int _activeUploadBatch = -1;
    private long _uploadAllocations, _uploadTransfers, _uploadSubmissions, _uploadBackpressureWaits, _uploadBytes;
    private ulong _uploadResidentBytes, _uploadPeakResidentBytes;
    private readonly bool _staticUploadFallback = Environment.GetEnvironmentVariable("FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK") == "1";
    internal Action<SchedulingOperation>? SchedulingFailureInjection { get; set; }
    private void InjectSchedulingFailure(SchedulingOperation operation) => SchedulingFailureInjection?.Invoke(operation);

    internal UploadArenaDiagnostics UploadDiagnostics
    {
        get
        {
            VerifyOwner();
            int slots = 0;
            foreach (UploadBatch? batch in _uploadBatches) if (batch is not null) slots++;
            return new(slots, _uploadResidentBytes, _uploadPeakResidentBytes, _uploadAllocations,
                _uploadTransfers, _uploadSubmissions, _uploadBackpressureWaits, _uploadBytes,
                _memoryBudgetSampled ? System.Diagnostics.Stopwatch.GetElapsedTime(_memoryBudgetSampleTimestamp).TotalMilliseconds : null);
        }
    }

    public IGraphicsBuffer CreateInitializedStaticBuffer(InitializedStaticBufferDescription description, ReadOnlySpan<byte> initialData)
    {
        ThrowIfDisposed();
        description.Validate(initialData.Length);
        if (_staticUploadFallback)
        {
            IGraphicsBuffer fallback = CreateBuffer(new(description.SizeInBytes, GraphicsBufferMemory.Upload));
            try
            {
                fallback.SetData(initialData);
                if (_boundInputTracker is { } tracker && fallback is D3D12GraphicsBuffer actual)
                    tracker.Commit(actual.Lifetime, path: "initialized-static-upload-fallback", usage: description.Usage.ToString());
                return fallback;
            }
            catch { fallback.Dispose(); throw; }
        }
        using var frozenSource = _boundInputTracker is null ? null : new GraphicsBoundFrozenSource(initialData);
        ReadOnlySpan<byte> consumed = frozenSource is null ? initialData : frozenSource.Bytes;
        _ = UploadArenaLimits.Capacity(description.SizeInBytes);
        // Buffers start in COMMON and promote to COPY_DEST on their first copy.
        ID3D12Resource resource = _device.CreateCommittedResource(HeapType.Default,
            ResourceDescription.Buffer(description.SizeInBytes), ResourceStates.Common);
        var buffer = new D3D12GraphicsBuffer(this, new(description.SizeInBytes, GraphicsBufferMemory.GpuLocal), resource);
        try
        {
            UploadBatch batch = AcquireUploadBatch(description.SizeInBytes);
            ulong offset = UploadArenaLimits.Align(batch.Used);
            InjectSchedulingFailure(SchedulingOperation.MapUpload);
            Span<byte> mapped = batch.Upload.Map<byte>(0, checked((int)batch.Capacity));
            try { consumed.CopyTo(mapped.Slice(checked((int)offset), consumed.Length)); }
            finally { batch.Upload.Unmap(0); }
            batch.Commands.CopyBufferRegion(resource, 0, batch.Upload, offset, description.SizeInBytes);
            batch.Commands.ResourceBarrierTransition(resource, ResourceStates.CopyDest,
                description.Usage == StaticGraphicsBufferUsage.Index ? ResourceStates.IndexBuffer : ResourceStates.VertexAndConstantBuffer);
            FinishUploadTransfer(batch, buffer.Lifetime, offset + description.SizeInBytes, description.SizeInBytes);
            RecordCaptureInput(consumed);
            if (_boundInputTracker is { } tracker)
            {
                tracker.Write(buffer.Lifetime, "initialized-static", 0, (ulong)consumed.Length, 1,
                    frozenSource!.Sha256, (ulong)consumed.Length, tracker.Pending(buffer.Lifetime));
                tracker.Commit(buffer.Lifetime, path: "initialized-static-gpu-local", usage: description.Usage.ToString());
            }
            return buffer;
        }
        catch { buffer.Dispose(); throw; }
    }

    private UploadBatch AcquireUploadBatch(ulong bytes)
    {
        ulong capacity = UploadArenaLimits.Capacity(bytes);
        if (_activeUploadBatch >= 0)
        {
            UploadBatch active = _uploadBatches[_activeUploadBatch]!;
            if (UploadArenaLimits.CanAppend(active.Used, active.Capacity, bytes, active.Destinations.Count)) return active;
            FlushUploads();
        }
        ulong completed = GetCompletedFence();
        int first = bytes > UploadArenaLimits.NormalBytes ? 3 : 0;
        int end = first == 3 ? 4 : 3;
        int selected = -1;
        for (int i = first; i < end; i++)
        {
            if (_uploadBatches[i] is not { } existing || UploadArenaLimits.CanReset(existing.Fence, completed)) { selected = i; break; }
        }
        if (selected < 0)
        {
            selected = first;
            for (int i = first + 1; i < end; i++)
                if (_uploadBatches[i]!.Fence < _uploadBatches[selected]!.Fence) selected = i;
            _uploadBackpressureWaits++;
            WaitForFence(_uploadBatches[selected]!.Fence);
            completed = GetCompletedFence();
            _resources.Collect(completed);
        }
        UploadBatch? batch = _uploadBatches[selected];
        if (batch is not null && !UploadArenaLimits.CanReset(batch.Fence, completed))
            throw new GraphicsDeviceException("Upload slot has no completed retirement fence.", "upload-not-retired");
        if (batch is null)
        {
            InjectSchedulingFailure(SchedulingOperation.AllocateUpload);
            batch = new UploadBatch(_device, capacity);
            _uploadBatches[selected] = batch;
            _uploadResidentBytes += capacity;
            _uploadAllocations++;
        }
        else if (batch.Capacity < capacity)
        {
            InjectSchedulingFailure(SchedulingOperation.AllocateUpload);
            ID3D12Resource replacement = _device.CreateCommittedResource(HeapType.Upload,
                ResourceDescription.Buffer(capacity), ResourceStates.GenericRead);
            _uploadResidentBytes += capacity - batch.Capacity;
            batch.Upload.Dispose();
            batch.Upload = replacement;
            batch.Capacity = capacity;
            _uploadAllocations++;
        }
        _uploadPeakResidentBytes = Math.Max(_uploadPeakResidentBytes, _uploadResidentBytes);
        batch.Allocator.Reset();
        batch.Commands.Reset(batch.Allocator);
        batch.Used = 0;
        batch.Destinations.Clear();
        batch.ProvenanceGeneration = _boundInputTracker?.NewBatch() ?? 0;
        batch.ProvenanceSlot = selected;
        _activeUploadBatch = selected;
        return batch;
    }

    private void FinishUploadTransfer(UploadBatch batch, GraphicsResourceRetirement.Resource lifetime, ulong used, ulong bytes)
    {
        lifetime.Recording = true;
        batch.Destinations.Add(lifetime);
        batch.Used = used;
        _uploadTransfers++;
        _uploadBytes = checked(_uploadBytes + (long)bytes);
        _boundInputTracker?.Queue(lifetime, batch.ProvenanceSlot, batch.ProvenanceGeneration, used - bytes, bytes);
    }

    private void FlushUploads(bool provenanceCleanup = false)
    {
        if (_activeUploadBatch < 0)
        {
            if (!provenanceCleanup) _boundInputTracker?.VerifyAvailable();
            return;
        }
        UploadBatch batch = _uploadBatches[_activeUploadBatch]!;
        _activeUploadBatch = -1;
        string provenanceStage = "close";
        Exception? provenanceFailure = _boundInputTracker is { IsInvalid: true }
            ? new InvalidOperationException("Bound input observer is unavailable; native upload bookkeeping is retained.") : null;
        bool observeProvenance = _boundInputTracker is { IsInvalid: false };
        try
        {
            InjectSchedulingFailure(SchedulingOperation.CloseUpload);
            batch.Commands.Close();
            if (batch.Destinations.Count == 0) return;
            // Unknown completion remains unresettable if Execute succeeds but Signal fails.
            batch.Fence = ulong.MaxValue;
            provenanceStage = "execute";
            InjectSchedulingFailure(SchedulingOperation.ExecuteUpload);
            _commandQueue.ExecuteCommandList(batch.Commands);
            if (observeProvenance)
                try { _boundInputTracker!.UploadState(batch.ProvenanceSlot, batch.ProvenanceGeneration, "execute-returned"); }
                catch (Exception error) { _boundInputTracker!.Invalidate(); provenanceFailure = error; }
            provenanceStage = "signal";
            InjectSchedulingFailure(SchedulingOperation.SignalUpload);
            ulong fence = _nextFenceValue++;
            CheckResult(_commandQueue.Signal(_frameFence, fence), "upload-signal-failed");
            batch.Fence = fence;
            if (observeProvenance)
                try { _boundInputTracker!.UploadState(batch.ProvenanceSlot, batch.ProvenanceGeneration, "signaled", fence); }
                catch (Exception error) { _boundInputTracker!.Invalidate(); provenanceFailure ??= error; }
            foreach (GraphicsResourceRetirement.Resource destination in batch.Destinations)
            {
                destination.LastUseFence = Math.Max(destination.LastUseFence, fence);
                destination.Recording = false;
            }
            batch.Destinations.Clear();
            _uploadSubmissions++;
        }
        catch (Exception exception)
        {
            if (observeProvenance)
                try { _boundInputTracker!.UploadState(batch.ProvenanceSlot, batch.ProvenanceGeneration, "failed", failure: provenanceStage); } catch { _boundInputTracker!.Invalidate(); }
            _submissionFaultCount++;
            _failure = exception as GraphicsDeviceException ?? new GraphicsDeviceException("Upload submission failed.", exception);
            throw;
        }
        // Observer refusal cannot suppress the existing native Signal/bookkeeping.
        if (provenanceFailure is not null && !provenanceCleanup)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(provenanceFailure).Throw();
    }

    private void ReleaseUploadArena()
    {
        foreach (UploadBatch? batch in _uploadBatches) batch?.Dispose();
        Array.Clear(_uploadBatches);
        _uploadResidentBytes = 0;
    }

    private sealed class UploadBatch : IDisposable
    {
        internal ID3D12Resource Upload;
        internal readonly ID3D12CommandAllocator Allocator;
        internal readonly ID3D12GraphicsCommandList Commands;
        internal readonly List<GraphicsResourceRetirement.Resource> Destinations = new(UploadArenaLimits.MaximumTransfersPerBatch);
        internal ulong Capacity, Used, Fence;
        internal int ProvenanceSlot;
        internal ulong ProvenanceGeneration;
        internal UploadBatch(ID3D12Device device, ulong capacity)
        {
            Upload = device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer(capacity), ResourceStates.GenericRead);
            try
            {
                Allocator = device.CreateCommandAllocator(CommandListType.Direct);
                try
                {
                    Commands = device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, Allocator);
                    try { Commands.Close(); }
                    catch { Commands.Dispose(); throw; }
                }
                catch { Allocator.Dispose(); throw; }
            }
            catch { Upload.Dispose(); throw; }
            Capacity = capacity;
        }
        public void Dispose() { Commands.Dispose(); Allocator.Dispose(); Upload.Dispose(); }
    }
}
