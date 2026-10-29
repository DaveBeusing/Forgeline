using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal enum ReferenceRunProfile { Warm, RendererCold }
internal enum ReferenceCameraPath { Fixed, Sweep, Traverse }

// Only the explicit reference runner constructs this collector. It never submits simulation commands.
internal sealed class ReferencePerformanceSamples(int capacity, ulong minimumFence)
{
    private readonly List<ReferenceCpuSample> _cpu = new(ValidateCapacity(capacity));
    private readonly List<ReferenceGpuSample> _gpu = new(capacity);
    private readonly int _capacity = capacity;
    private ulong _lastFence = minimumFence;
    private readonly ulong _minimumFence = minimumFence;
    private ulong _frequency;
    private long _observations, _excluded, _duplicate, _missing, _zero, _missingFrequency;
    private ulong _unobservedFenceValues;

    internal void AddCpu(int index, double milliseconds, long allocatedBytes, GraphicsDiagnostics diagnostics, object? submission = null)
    {
        if (!double.IsFinite(milliseconds) || milliseconds <= 0 || allocatedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (index != _cpu.Count || _cpu.Count == _capacity)
            throw new ArgumentOutOfRangeException(nameof(index), "CPU samples must be contiguous, unique and within capacity.");
        var frame = diagnostics.Frame;
        _cpu.Add(new(index, milliseconds, allocatedBytes, frame?.WorldCpuMilliseconds, frame?.OverlayCpuMilliseconds,
            frame?.CompositeCpuMilliseconds, diagnostics.DirectionalShadows?.CpuMilliseconds,
            diagnostics.Resources, diagnostics.Health, diagnostics.Memory, submission)
        { CpuSubmission = frame?.CpuSubmission, CompletedGpuFence = frame?.GpuSubmissionFence,
            PresentMilliseconds = frame?.PresentMilliseconds, FrameWaitMilliseconds = frame?.FrameWaitMilliseconds });
        AddGpu(diagnostics);
    }

    internal void AddGpu(GraphicsDiagnostics diagnostics)
    {
        _observations++;
        if (diagnostics.Frame is not { } frame || frame.GpuSubmissionFence == ulong.MaxValue ||
            frame.GpuSubmissionFence <= _minimumFence) { _excluded++; return; }
        if (frame.GpuSubmissionFence <= _lastFence) { _duplicate++; return; }
        if (diagnostics.GpuFrameMilliseconds is not { } milliseconds) { _missing++; return; }
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
            throw new InvalidOperationException("Invalid completed GPU timestamp duration.");
        if (milliseconds == 0) { _zero++; return; }
        if (diagnostics.TimestampFrequencyHz == 0) { _missingFrequency++; return; }
        if (_frequency != 0 && _frequency != diagnostics.TimestampFrequencyHz)
            throw new InvalidOperationException("Queue timestamp frequency changed within a sample window.");
        if (_gpu.Count == _capacity)
            throw new InvalidOperationException("GPU sample capacity exceeded.");
        _frequency = diagnostics.TimestampFrequencyHz;
        _unobservedFenceValues = checked(_unobservedFenceValues + frame.GpuSubmissionFence - _lastFence - 1);
        _lastFence = frame.GpuSubmissionFence;
        _gpu.Add(new(frame.GpuSubmissionFence, milliseconds, frame.WorldGpuMilliseconds,
            frame.OverlayGpuMilliseconds, frame.CompositeGpuMilliseconds,
            diagnostics.DirectionalShadows is { } shadow && shadow.GpuSubmissionFence == _lastFence ? shadow.GpuMilliseconds : null,
            diagnostics.Particles is { } particles && particles.GpuSubmissionFence == _lastFence ? particles.UpdateAndRenderGpuMilliseconds : null,
            diagnostics.Atmosphere is { } atmosphere && atmosphere.GpuSubmissionFence == _lastFence ? atmosphere.CompositionGpuMilliseconds : null)
            { TimestampFrequencyHz = _frequency, ObservedAtCpuSubmission = frame.CpuSubmission });
    }

    internal object Report(GraphicsDiagnostics diagnostics) => new
    {
        collectorSchemaVersion = 2, cpuFrames = _cpu, gpuFrames = _gpu,
        controls = new { staticBufferUploadFallback = Environment.GetEnvironmentVariable("FORGELINE_STATIC_BUFFER_UPLOAD_FALLBACK") == "1" },
        allocations = VisualReferenceCapture.Distribution(_cpu.Select(x => (double)x.AllocatedBytes).ToArray()),
        cpuPasses = new
        {
            world = CpuValues(x => x.WorldMilliseconds), overlay = CpuValues(x => x.OverlayMilliseconds),
            composite = CpuValues(x => x.CompositeMilliseconds), shadows = CpuValues(x => x.ShadowMilliseconds),
            present = CpuValues(x => x.PresentMilliseconds), frameReuseWait = CpuValues(x => x.FrameWaitMilliseconds),
            preparation = VisualReferenceCapture.Distribution([])
        },
        gpu = VisualReferenceCapture.Distribution(_gpu.Select(x => x.FrameMilliseconds).ToArray()),
        gpuPasses = new
        {
            world = Values(x => x.WorldMilliseconds), overlay = Values(x => x.OverlayMilliseconds),
            composite = Values(x => x.CompositeMilliseconds), shadows = Values(x => x.ShadowMilliseconds),
            particles = Values(x => x.ParticleMilliseconds), atmosphere = Values(x => x.AtmosphereMilliseconds)
        },
        gpuTiming = new { available = _gpu.Count > 0, timestampFrequencyHz = _frequency,
            observations = _observations, acceptedCompletions = _gpu.Count, excludedWarmupOrInvalidFence = _excluded,
            duplicateOrOlderObservations = _duplicate, missingDurationObservations = _missing,
            zeroDurationObservations = _zero, missingFrequencyObservations = _missingFrequency,
            unobservedFenceValues = _unobservedFenceValues,
            unavailableReason = _gpu.Count > 0 ? null : diagnostics.Frame?.GpuTimingUnavailableReason ?? "No distinct completed GPU samples observed.",
            method = "Direct-queue ticks * 1000 / frequency; readback only after submission fence completion. Warmup fences excluded. Latest completed diagnostics may skip intermediate fences; sample counts are explicit. Fence-value gaps are not a count of lost rendered frames: other queue submissions may occupy values. Passes can overlap and must not be summed. Present/reuse waits are subsets of RenderFrame. Preparation is unavailable until separately instrumented." }
    };

    private static int ValidateCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 8192);
        return capacity;
    }

    private object Values(Func<ReferenceGpuSample, double?> select) =>
        VisualReferenceCapture.Distribution(_gpu.Select(select).Select(ValidPass).Where(x => x.HasValue).Select(x => x!.Value).ToArray());

    private static double? ValidPass(double? value)
    {
        if (value is null or 0) return null;
        if (!double.IsFinite(value.Value) || value < 0) throw new InvalidOperationException("Invalid GPU pass duration.");
        return value;
    }

    private object CpuValues(Func<ReferenceCpuSample, double?> select) =>
        VisualReferenceCapture.Distribution(_cpu.Select(select).Where(x => x.HasValue).Select(x => x!.Value).ToArray());

    internal static RtsCameraState CameraPose(RtsCameraState start, ReferenceCameraPath path, int frame, int samples)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 1);
        if (!Enum.IsDefined(path)) throw new ArgumentOutOfRangeException(nameof(path));
        if (path == ReferenceCameraPath.Fixed) return start;
        float phase = (frame % samples) / (float)samples * MathF.Tau;
        if (path == ReferenceCameraPath.Traverse)
            return start with { Target = start.Target + new Vector3(700 * MathF.Sin(phase), 0, 500 * (1 - MathF.Cos(phase))),
                YawRadians = start.YawRadians + .6f * MathF.Sin(phase),
                Distance = Math.Max(120, start.Distance * (1 + .3f * MathF.Sin(2 * phase))) };
        return start with { Target = start.Target + new Vector3(20 * MathF.Sin(phase), 0, 10 * (1 - MathF.Cos(phase))),
            YawRadians = start.YawRadians + .15f * MathF.Sin(phase) };
    }
}

internal sealed record ReferenceGpuSample(ulong Fence, double FrameMilliseconds, double? WorldMilliseconds,
    double? OverlayMilliseconds, double? CompositeMilliseconds, double? ShadowMilliseconds,
    double? ParticleMilliseconds, double? AtmosphereMilliseconds)
{
    public ulong TimestampFrequencyHz { get; init; }
    public ulong ObservedAtCpuSubmission { get; init; }
}

internal sealed record ReferenceCpuSample(int Index, double Milliseconds, long AllocatedBytes,
    double? WorldMilliseconds, double? OverlayMilliseconds, double? CompositeMilliseconds, double? ShadowMilliseconds,
    GraphicsResourceDiagnostics Resources, GraphicsHealthDiagnostics Health, GraphicsMemoryDiagnostics? Memory, object? Submission)
{
    public ulong? CpuSubmission { get; init; }
    public ulong? CompletedGpuFence { get; init; }
    public double? PresentMilliseconds { get; init; }
    public double? FrameWaitMilliseconds { get; init; }
}
