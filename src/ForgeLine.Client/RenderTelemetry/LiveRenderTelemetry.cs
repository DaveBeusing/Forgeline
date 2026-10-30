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
    internal int Count => _count;
    internal bool WindowCompleted => _finished;
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
    internal LiveTelemetryStart Begin()
    {
        VerifyOwner();
        if (_finished) return default;
        long process = GC.GetTotalAllocatedBytes(precise: true);
        long thread = GC.GetAllocatedBytesForCurrentThread();
        return new(Stopwatch.GetTimestamp(), process, thread);
    }
    internal void Complete(in LiveTelemetryStart start, string state, long prepareStart = 0, long prepareEnd = 0)
    {
        VerifyOwner();
        if (_finished) { DrainGpu(); return; }
        long end = Stopwatch.GetTimestamp();
        long thread = GC.GetAllocatedBytesForCurrentThread() - start.ThreadBytes;
        long process = GC.GetTotalAllocatedBytes(precise: true) - start.ProcessBytes;
        if (!_armed && state == "gameplay") _armed = true;
        if (!_armed || _warmupsRemaining > 0)
        {
            if (!_armed && _beforeCount >= StartupObservationBudget) _overflow = true;
            if (_beforeCount == _beforeWindow.Length) _overflow = true;
            else _beforeWindow[_beforeCount++] = new(start.Tick, end, state, process, thread);
            if (_armed) _warmupsRemaining--;
            DrainGpu(); return;
        }
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
            _frames[_count] = new(_count, start.Tick, end, process, thread, state,
                new(start.Tick, prepareStart >= start.Tick ? prepareStart : renderStart), prepare,
                render, present, wait, cpu.Submission, render.StartTick < render.EndTick && cpu.Submitted ? cpu.SubmittedFence : 0, cpu.Generation, cpu.ResourceGeneration, _source.ResourceMemory);
            if (_firstMeasuredFence == 0) _firstMeasuredFence = cpu.SubmittedFence;
            _count++;
            if (_count == _frames.Length) { _finished = true; _source.StopRenderTelemetry(); }
        }
        DrainGpu();
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
        document["sourceSha"] = ClientBuildIdentity.SourceRevision;
        document["runtimeFingerprint"] = _runtimeFingerprint;
        document["actualDevice"] = JsonSerializer.SerializeToNode(_actualDevice, JsonOptions);
        document["scope"] = "live-client";
        document["gpuStreamScope"] = "independent-live-client";
        document["streamId"] = _streamId;
        document["frequencyHz"] = Stopwatch.Frequency;
        document["overflowed"] = Overflowed;
        document["window"] = JsonSerializer.SerializeToNode(new { arm = "first-gameplay-iteration", warmups = _warmups,
            samples = _frames.Length, completed = _finished, contiguous = true, idleWaitIncluded = false,
            scope = "complete-active-render-owner-iteration; independent platform and simulation owners excluded" }, JsonOptions);
        document["beforeWindow"] = JsonSerializer.SerializeToNode(_beforeWindow.AsSpan(0, _beforeCount).ToArray(), JsonOptions);
        document["includes"] = new JsonArray("prepare", "host", "renderFrame", "present", "fenceWait");
        document["gpuCompletions"] = JsonSerializer.SerializeToNode(_gpu.AsSpan(0, _gpuCount).ToArray(), JsonOptions);
        var completed = _gpu.AsSpan(0, _gpuCount).ToArray().ToDictionary(x => (x.Generation, x.ResourceGeneration, x.SubmittedFence));
        document["frames"] = JsonSerializer.SerializeToNode(_frames.AsSpan(0, _count).ToArray().Select(f => new
        {
            f.Index, f.StartTick, f.EndTick, f.ProcessAllocatedBytes, f.ThreadAllocatedBytes, f.State, f.Submission, f.SubmittedFence, f.Generation, f.ResourceGeneration,
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
internal readonly record struct LiveTelemetryStart(long Tick, long ProcessBytes, long ThreadBytes);
internal readonly record struct LiveTelemetryFrame(int Index, long StartTick, long EndTick,
    long ProcessAllocatedBytes, long ThreadAllocatedBytes, string State, GraphicsTimestampInterval Host,
    GraphicsTimestampInterval Prepare, GraphicsTimestampInterval RenderFrame, GraphicsTimestampInterval Present,
    GraphicsTimestampInterval FenceWait, ulong Submission, ulong SubmittedFence, ulong Generation,
    ulong ResourceGeneration, GraphicsResourceMemoryTelemetry Memory);

internal readonly record struct LiveTelemetryObservation(long StartTick, long EndTick, string State, long ProcessAllocatedBytes, long ThreadAllocatedBytes);
