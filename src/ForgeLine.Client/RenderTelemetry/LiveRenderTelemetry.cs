using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ForgeLine.Graphics;
using ForgeLine.Assets;

namespace ForgeLine.Client;

// Enabled only for an explicitly requested evidence stream. Storage is fixed before frames.
internal sealed class LiveRenderTelemetry : IDisposable
{
    private readonly IGraphicsRenderTelemetry _source;
    private readonly string? _output;
    private readonly JsonObject? _context;
    private readonly string? _runtimeFingerprint;
    private readonly GraphicsDeviceInfo? _actualDevice;
    private readonly LiveTelemetryFrame[] _frames;
    private readonly GraphicsRawGpuTiming[] _gpu;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly uint _nativeOwner = RenderOwnerThreadIdentity.GetCurrentThreadId();
    private readonly object _clockAnchor = RenderOwnerThreadIdentity.ClockAnchor();
    private readonly string _streamId = Guid.NewGuid().ToString("N");
    private int _count, _gpuCount;
    private const int StartupObservationBudget = 4096;
    private const int NativeCompletionHeadroom = 8;
    private const int MaximumCompletions = 65536;
    private readonly LiveTelemetryObservation[] _beforeWindow;
    private readonly int _warmups;
    private int _beforeCount, _warmupsRemaining;
    private bool _armed, _finished;
    private ulong _firstMeasuredFence;
    private bool _overflow, _disposed;
    internal bool ObserveAbsoluteReadBrackets { get; set; }
    internal LiveTelemetryCounterEnd? LastAbsoluteRead { get; private set; }
    internal OwnerCycleObservations? OwnerCycles { get; set; }
    private object? _failedFixtureAttempt;
    private LiveSessionOrigin? _sessionOrigin;
    private object? _producerAdapterBinding, _producerRuntimeSeal;
    internal void BindProducerEvidence(object? adapterBinding, object runtimeSeal)
    {
        VerifyOwner();
        if (_armed || _count != 0) throw new InvalidOperationException("Actual producer identity must bind before sampling.");
        _producerAdapterBinding = adapterBinding; _producerRuntimeSeal = runtimeSeal;
    }
    internal void BindSessionOrigin(LiveSessionOrigin? origin)
    {
        VerifyOwner();
        if (_armed || _count != 0) throw new InvalidOperationException("Actual session origin must be bound before capture starts.");
        _sessionOrigin = origin;
    }
    internal int Count => _count;
    internal LiveTelemetryFrame? LastObservedFrame => _count>0 ? _frames[_count-1] : null;
    internal bool WindowCompleted => _finished;
    internal ReferencePostWindowControl? PostWindowControl { get; set; }
    internal ClientRenderConfiguration? ObservedComposition { get; set; }
    internal bool Overflowed => _overflow || _source.TelemetryOverflowed;
    internal LiveRenderTelemetry(IGraphicsRenderTelemetry source, int capacity, string? output = null, JsonObject? context = null, int warmups = 0, string? runtimeFingerprint = null, GraphicsDeviceInfo? actualDevice = null)
    {
        int completionCapacity = CompletionCapacityFor(capacity, warmups);
        _source = source; _frames = new LiveTelemetryFrame[capacity]; _gpu = new GraphicsRawGpuTiming[capacity];
        _beforeWindow = new LiveTelemetryObservation[checked(StartupObservationBudget + warmups)];
        _warmups = _warmupsRemaining = warmups;
        _output = output; _context = context; _runtimeFingerprint = runtimeFingerprint; _actualDevice = actualDevice; source.EnableRenderTelemetry(completionCapacity);
    }
    internal static int CompletionCapacityFor(int samples, int warmups)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(warmups);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(warmups, 4096);
        int reserved = checked(StartupObservationBudget + warmups + NativeCompletionHeadroom);
        if (samples > MaximumCompletions - reserved)
            throw new ArgumentOutOfRangeException(nameof(samples), "Samples, warmups, startup observations and native completion headroom must fit the 65,536-observation budget.");
        return checked(samples + reserved);
    }
    internal static LiveRenderTelemetry? FromEnvironment(IGraphicsDevice graphics, RuntimeAssetCatalog? runtimeAssets)
    {
        string? output = Environment.GetEnvironmentVariable("FORGELINE_RENDER_TELEMETRY_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return null;
        if (graphics is not IGraphicsRenderTelemetry source)
            throw new NotSupportedException("The device cannot produce requested live telemetry.");
        if (File.Exists(output)) throw new IOException("Telemetry evidence must use a new output file.");
        string? runtimeFingerprint = runtimeAssets is null ? null : VisualReferenceCapture.RuntimeFingerprint(runtimeAssets.RuntimeRoot);
        JsonObject? context = ReadOptionalContextFromEnvironment(ClientBuildIdentity.SourceRevision, runtimeFingerprint);
        int capacity = int.TryParse(Environment.GetEnvironmentVariable("FORGELINE_RENDER_TELEMETRY_SAMPLES"), out int parsed) ? parsed : 512;
        int warmups = int.TryParse(Environment.GetEnvironmentVariable("FORGELINE_RENDER_TELEMETRY_WARMUPS"), out int discarded) ? discarded : 64;
        return new(source, capacity, output, context, warmups, runtimeFingerprint, graphics.Diagnostics.Device);
    }
    internal static JsonObject? ReadOptionalContextFromEnvironment(string expectedSource, string? runtimeFingerprint)
    {
        string? path = Environment.GetEnvironmentVariable("FORGELINE_RENDER_TELEMETRY_CONTEXT");
        if (string.IsNullOrWhiteSpace(path)) return null;
        JsonObject context = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("Invalid telemetry identity context.");
        if (context["sourceSha"]?.GetValue<string>() != expectedSource)
            throw new InvalidDataException("Telemetry context source does not match the executing assembly.");
        if (!string.Equals(context["runtimeFingerprint"]?.GetValue<string>(), runtimeFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Telemetry context runtime does not match the executing sealed payload.");
        return context;
    }
    internal LiveTelemetryStart Begin(GraphicsTimestampInterval signalWait = default)
    {
        VerifyOwner();
        if (_failedFixtureAttempt is not null) throw new InvalidOperationException("A failed fixture window cannot resume.");
        if (_finished) return default;
        long serviceStart = Stopwatch.GetTimestamp();
        long counterStart = Stopwatch.GetTimestamp();
        long process = GC.GetTotalAllocatedBytes(precise: true);
        long counterEnd = Stopwatch.GetTimestamp();
        long threadStart=ObserveAbsoluteReadBrackets ? Stopwatch.GetTimestamp() : 0;
        long thread = GC.GetAllocatedBytesForCurrentThread();
        long threadEnd=ObserveAbsoluteReadBrackets ? Stopwatch.GetTimestamp() : 0;
        return new(Stopwatch.GetTimestamp(), process, thread)
        { ServiceStartTick = serviceStart, BeginProcessCounter = new(counterStart, counterEnd), BeginThreadCounter=new(threadStart,threadEnd), SignalWait = signalWait };
    }
    internal void Complete(in LiveTelemetryStart start, string state, long prepareStart = 0, long prepareEnd = 0, LiveSimulationProvenance? simulation = null)
    {
        VerifyOwner();
        if (_failedFixtureAttempt is not null) throw new InvalidOperationException("A failed fixture window cannot consume another observation.");
        if (_finished) { DrainGpu(); return; }
        long end = Stopwatch.GetTimestamp();
        long threadStart=ObserveAbsoluteReadBrackets ? Stopwatch.GetTimestamp() : 0;
        long threadAbsolute=GC.GetAllocatedBytesForCurrentThread();
        long threadEnd=ObserveAbsoluteReadBrackets ? Stopwatch.GetTimestamp() : 0;
        long thread = threadAbsolute - start.ThreadBytes;
        long endCounterStart = Stopwatch.GetTimestamp();
        long processAbsolute=GC.GetTotalAllocatedBytes(precise: true);
        long process = processAbsolute - start.ProcessBytes;
        long endCounterEnd = Stopwatch.GetTimestamp();
        if(ObserveAbsoluteReadBrackets)LastAbsoluteRead=new(processAbsolute,threadAbsolute,new(endCounterStart,endCounterEnd),new(threadStart,threadEnd),end);
        var observer = new LiveObserverIntervals(start.BeginProcessCounter, new(endCounterStart, endCounterEnd),
            new(endCounterEnd, endCounterEnd), new(endCounterEnd, endCounterEnd));
        if (!_armed && state == "gameplay") _armed = true;
        if (!_armed || _warmupsRemaining > 0)
        {
            if (!_armed && _beforeCount >= StartupObservationBudget) _overflow = true;
            int prefixIndex = _beforeCount;
            if (_beforeCount == _beforeWindow.Length) { _overflow = true; prefixIndex = -1; }
            else _beforeWindow[_beforeCount++] = new(start.Tick, end, state, process, thread)
                { ServiceStartTick = start.ServiceStartTick, SignalWait = start.SignalWait, Observer = observer, Simulation = simulation };
            if (_armed) _warmupsRemaining--;
            long drainStart = Stopwatch.GetTimestamp(); DrainGpu(); long drainEnd = Stopwatch.GetTimestamp();
            if (prefixIndex >= 0) _beforeWindow[prefixIndex] = _beforeWindow[prefixIndex] with
            { ServiceEndTick = Stopwatch.GetTimestamp(), Observer = observer with { GpuDrain = new(drainStart, drainEnd) } };
            return;
        }
        int frameIndex = _count;
        {
            var cpu = _source.CpuIntervals;
            // Loading/suspended observations retain their real classification; they cannot be
            // relabelled as complete gameplay frames by the supplemental exporter.
            long renderStart = cpu.RenderFrame.StartTick >= start.Tick && cpu.RenderFrame.EndTick <= end
                ? cpu.RenderFrame.StartTick : end;
            GraphicsTimestampInterval render = renderStart == end ? new(end, end) : cpu.RenderFrame;
            GraphicsTimestampInterval present = InFrame(cpu.Present, start.Tick, end) ? cpu.Present : new(end, end);
            GraphicsTimestampInterval wait = InFrame(cpu.FenceWait, start.Tick, end) ? cpu.FenceWait : new(end, end);
            var prepare = prepareStart >= start.Tick && prepareEnd >= prepareStart && prepareEnd <= end
                ? new GraphicsTimestampInterval(prepareStart, prepareEnd) : new GraphicsTimestampInterval(end, end);
            long memoryStart = Stopwatch.GetTimestamp();
            GraphicsResourceMemoryTelemetry memory = _source.ResourceMemory;
            long memoryEnd = Stopwatch.GetTimestamp();
            observer = observer with { MemorySnapshot = new(memoryStart, memoryEnd) };
            _frames[_count] = new(_count, start.Tick, end, process, thread, state,
                new(start.Tick, prepareStart >= start.Tick ? prepareStart : renderStart), prepare,
                render, present, wait, cpu.Submission, render.StartTick < render.EndTick && cpu.Submitted ? cpu.SubmittedFence : 0, cpu.Generation, cpu.ResourceGeneration, memory)
                { ServiceStartTick = start.ServiceStartTick, SignalWait = start.SignalWait, Observer = observer, Simulation = simulation };
            if (_firstMeasuredFence == 0) _firstMeasuredFence = cpu.SubmittedFence;
            _count++;
            if (_count == _frames.Length) { _finished = true; _source.StopRenderTelemetry(); }
        }
        long gpuDrainStart = Stopwatch.GetTimestamp(); DrainGpu(); long gpuDrainEnd = Stopwatch.GetTimestamp();
        _frames[frameIndex] = _frames[frameIndex] with
        { ServiceEndTick = Stopwatch.GetTimestamp(), Observer = observer with { GpuDrain = new(gpuDrainStart, gpuDrainEnd) } };
    }
    internal void RecordFixtureFailure(in LiveTelemetryStart start, string state)
    {
        VerifyOwner();
        if (_failedFixtureAttempt is not null) throw new InvalidOperationException("The first failed fixture attempt cannot be replaced.");
        long end = Stopwatch.GetTimestamp();
        long? thread = _finished ? null : GC.GetAllocatedBytesForCurrentThread() - start.ThreadBytes;
        long? process = _finished ? null : GC.GetTotalAllocatedBytes(precise: true) - start.ProcessBytes;
        _failedFixtureAttempt = new { state, nextSampleIndex = _count, warmupsRemaining = _warmupsRemaining,
            startTick = start.Tick, endTick = end, processAllocatedBytes = process, threadAllocatedBytes = thread,
            cpu = _source.CpuIntervals, scope = _finished ? "post-window" : "failed attempt; does not advance warmups or sample index" };
        _source.StopRenderTelemetry();
    }
    internal void ValidateFixtureCorrespondence(ReadOnlySpan<ReferenceFixtureFrameObservation> acknowledgements)
    {
        VerifyOwner(); DrainGpu();
        if (!_finished || _failedFixtureAttempt is not null || Overflowed || _count != _frames.Length || acknowledgements.Length != _count || _gpuCount != _count)
            throw new InvalidDataException("Fixture owner/ACK/GPU window is failed or incomplete.");
        var completed = new HashSet<(ulong, ulong, ulong)>();
        foreach (var sample in _gpu.AsSpan(0, _gpuCount))
            if (!completed.Add((sample.Generation, sample.ResourceGeneration, sample.SubmittedFence)) || sample.CompletedFence < sample.SubmittedFence)
                throw new InvalidDataException("Fixture GPU completion is duplicated or uncompleted.");
        for (int i = 0; i < _count; i++)
        {
            var frame = _frames[i]; var ack = acknowledgements[i];
            if (frame.Index != i || ack.Index != i || ack.Ordinal != _warmups + i || frame.State != "gameplay" ||
                ack.SubmittedFence != frame.SubmittedFence || ack.Generation != frame.Generation || ack.ResourceGeneration != frame.ResourceGeneration ||
                !completed.Remove((frame.Generation, frame.ResourceGeneration, frame.SubmittedFence)))
                throw new InvalidDataException("Actual owner CPU, successful camera ACK and completed GPU tuple/index differ.");
        }
        if (completed.Count != 0) throw new InvalidDataException("Completed GPU tuples remain unreferenced by the fixture owner window.");
    }
    internal void DrainGpu()
    {
        VerifyOwner();
        while (_source.TryReadGpuCompletion(out var sample))
        {
            if (_firstMeasuredFence == 0 || sample.SubmittedFence < _firstMeasuredFence) continue;
            if (_gpuCount == _gpu.Length) _overflow = true;
            else _gpu[_gpuCount++] = sample;
        }
    }
    private static bool InFrame(GraphicsTimestampInterval interval, long start, long end) =>
        start <= interval.StartTick && interval.StartTick <= interval.EndTick && interval.EndTick <= end;
    private void VerifyOwner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Live telemetry is render-owner-only.");
    }
    internal JsonObject Export()
    {
        VerifyOwner();
        JsonObject document = _context?.DeepClone().AsObject() ?? new JsonObject();
        document["schemaVersion"] = 1;
        document["kind"] = "live-render-owner-telemetry";
        document["nativeFixtureBindingAvailable"] = false;
        document["sessionOrigin"] = JsonSerializer.SerializeToNode(_sessionOrigin, JsonOptions);
        bool realSession = _sessionOrigin is not null && _finished && _count > 1 && _frames.AsSpan(0, _count).ToArray().All(f => f.Simulation is { Running: true } p &&
            p.ExpectedSession > 0 && p.ExpectedSession == p.SnapshotSession && p.SnapshotSession == p.CallbackSession) &&
            _frames[0].Simulation!.Value.CompletedTicks < _frames[_count - 1].Simulation!.Value.CompletedTicks &&
            _frames[0].Simulation!.Value.SnapshotTick < _frames[_count - 1].Simulation!.Value.SnapshotTick;
        document["producerMode"] = realSession ? "actual-live-gameplay" : "unattested-live-or-authored-reference-fixture";
        document["modeAttestationAvailable"] = realSession;
        document["sourceSha"] = ClientBuildIdentity.SourceRevision;
        document["runtimeFingerprint"] = _runtimeFingerprint;
        document["actualDevice"] = JsonSerializer.SerializeToNode(_actualDevice, JsonOptions);
        document["adapterIdentity"] = JsonSerializer.SerializeToNode(_source.DeviceIdentity, JsonOptions);
        document["adapterBinding"] = JsonSerializer.SerializeToNode(_producerAdapterBinding, JsonOptions);
        document["runtimeSeal"] = JsonSerializer.SerializeToNode(_producerRuntimeSeal, JsonOptions);
        document["scope"] = "live-client";
        document["gpuStreamScope"] = "independent-live-client";
        if(OwnerCycles is not null)document["ownerCycles"]=JsonSerializer.SerializeToNode(OwnerCycles.Export(),JsonOptions);
        if(ObservedComposition is not null)document["observedComposition"]=JsonSerializer.SerializeToNode(ObservedComposition,VisualReferenceCapture.JsonOptions);
        if(PostWindowControl is not null)document["postWindowControl"]=JsonSerializer.SerializeToNode(PostWindowControl.Evidence(),JsonOptions);
        document["streamId"] = _streamId;
        document["frequencyHz"] = Stopwatch.Frequency;
        document["clockAnchor"] = JsonSerializer.SerializeToNode(_clockAnchor, JsonOptions);
        document["ownerThread"] = JsonSerializer.SerializeToNode(new { managedThreadId = _owner, nativeThreadId = _nativeOwner,
            processId = Environment.ProcessId, timestampClock = "Stopwatch/QPC", highResolution = Stopwatch.IsHighResolution }, JsonOptions);
        document["overflowed"] = Overflowed;
        document["failedFixtureAttempt"] = JsonSerializer.SerializeToNode(_failedFixtureAttempt, JsonOptions);
        document["serviceIntervalsAvailable"] = true;
        document["window"] = JsonSerializer.SerializeToNode(new { arm = "first-gameplay-iteration", warmups = _warmups,
            samples = _frames.Length, completed = _finished, contiguous = true, idleWaitIncluded = false,
            scope = "legacy active payload/service brackets; excludes compiler lead-in, ACK/backedge tail, idle wait and independent owners; owner-cycle-v1 is separate" }, JsonOptions);
        document["beforeWindow"] = JsonSerializer.SerializeToNode(_beforeWindow.AsSpan(0, _beforeCount).ToArray(), JsonOptions);
        document["includes"] = new JsonArray("prepare", "host", "renderFrame", "present", "fenceWait");
        document["gpuCompletions"] = JsonSerializer.SerializeToNode(_gpu.AsSpan(0, _gpuCount).ToArray(), JsonOptions);
        var completed = _gpu.AsSpan(0, _gpuCount).ToArray().ToDictionary(x => (x.Generation, x.ResourceGeneration, x.SubmittedFence));
        document["frames"] = JsonSerializer.SerializeToNode(_frames.AsSpan(0, _count).ToArray().Select(f => new
        {
            f.Index, f.StartTick, f.EndTick, f.ServiceStartTick, f.ServiceEndTick, f.SignalWait, f.Observer,
            f.ProcessAllocatedBytes, f.ThreadAllocatedBytes, f.State, f.Simulation, f.Submission, f.SubmittedFence, f.Generation, f.ResourceGeneration,
            phases = new { host = f.Host, prepare = f.Prepare, renderFrame = f.RenderFrame, present = f.Present, fenceWait = f.FenceWait },
            memory = f.Memory, gpu = completed.TryGetValue((f.Generation, f.ResourceGeneration, f.SubmittedFence), out var timing) ? new
            { queue = timing.Queue, generation = timing.Generation, resourceGeneration = timing.ResourceGeneration,
                submittedFence = timing.SubmittedFence, completedFence = timing.CompletedFence, frequencyHz = timing.FrequencyHz,
                startTick = timing.StartTick, endTick = timing.EndTick } : null
        }), JsonOptions);
        return document;
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public void Dispose()
    {
        if (_disposed) return;
        VerifyOwner(); DrainGpu();
        if (_output is not null)
        {
            string path = Path.GetFullPath(_output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, Export(), JsonOptions);
        }
        _disposed = true;
    }
}
internal readonly record struct LiveTelemetryStart(long Tick, long ProcessBytes, long ThreadBytes)
{
    internal long ServiceStartTick { get; init; }
    internal GraphicsTimestampInterval BeginProcessCounter { get; init; }
    internal GraphicsTimestampInterval BeginThreadCounter { get; init; }
    internal GraphicsTimestampInterval SignalWait { get; init; }
}
internal readonly record struct LiveObserverIntervals(GraphicsTimestampInterval BeginProcessCounter,
    GraphicsTimestampInterval EndProcessCounter, GraphicsTimestampInterval MemorySnapshot, GraphicsTimestampInterval GpuDrain);
internal readonly record struct LiveTelemetryFrame(int Index, long StartTick, long EndTick,
    long ProcessAllocatedBytes, long ThreadAllocatedBytes, string State, GraphicsTimestampInterval Host,
    GraphicsTimestampInterval Prepare, GraphicsTimestampInterval RenderFrame, GraphicsTimestampInterval Present,
    GraphicsTimestampInterval FenceWait, ulong Submission, ulong SubmittedFence, ulong Generation,
    ulong ResourceGeneration, GraphicsResourceMemoryTelemetry Memory)
{
    public long ServiceStartTick { get; init; }
    public long ServiceEndTick { get; init; }
    public GraphicsTimestampInterval SignalWait { get; init; }
    public LiveObserverIntervals Observer { get; init; }
    public LiveSimulationProvenance? Simulation { get; init; }
}

internal readonly record struct LiveTelemetryObservation(long StartTick, long EndTick, string State, long ProcessAllocatedBytes, long ThreadAllocatedBytes)
{
    public long ServiceStartTick { get; init; }
    public long ServiceEndTick { get; init; }
    public GraphicsTimestampInterval SignalWait { get; init; }
    public LiveObserverIntervals Observer { get; init; }
    public LiveSimulationProvenance? Simulation { get; init; }
}

internal readonly record struct LiveSimulationProvenance(ulong ExpectedSession, ulong SnapshotSession, ulong SnapshotTick,
    ulong CallbackSession, ulong CompletedTicks, bool Running);
