using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal enum ReferenceRunProfile { Warm, RendererCold }
internal enum ReferenceCameraPath { Fixed, Sweep }

// Only the explicit reference runner constructs this collector. It never submits simulation commands.
internal sealed class ReferencePerformanceSamples(int capacity, ulong minimumFence)
{
    private readonly List<ReferenceCpuSample> _cpu = new(capacity);
    private readonly List<ReferenceGpuSample> _gpu = new(capacity);
    private ulong _lastFence = minimumFence;

    internal void AddCpu(int index, double milliseconds, long allocatedBytes, GraphicsDiagnostics diagnostics, object? submission = null)
    {
        if (!double.IsFinite(milliseconds) || milliseconds <= 0 || allocatedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var frame = diagnostics.Frame;
        _cpu.Add(new(index, milliseconds, allocatedBytes, frame?.WorldCpuMilliseconds, frame?.OverlayCpuMilliseconds,
            frame?.CompositeCpuMilliseconds, diagnostics.DirectionalShadows?.CpuMilliseconds,
            diagnostics.Resources, diagnostics.Health, diagnostics.Memory, submission));
        AddGpu(diagnostics);
    }

    internal void AddGpu(GraphicsDiagnostics diagnostics)
    {
        if (diagnostics.Frame is not { } frame || frame.GpuSubmissionFence <= _lastFence ||
            frame.GpuSubmissionFence == ulong.MaxValue || diagnostics.GpuFrameMilliseconds is not { } milliseconds)
            return;
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
            throw new InvalidOperationException("Invalid completed GPU timestamp duration.");
        _lastFence = frame.GpuSubmissionFence;
        _gpu.Add(new(frame.GpuSubmissionFence, milliseconds, frame.WorldGpuMilliseconds,
            frame.OverlayGpuMilliseconds, frame.CompositeGpuMilliseconds,
            diagnostics.DirectionalShadows is { } shadow && shadow.GpuSubmissionFence == _lastFence ? shadow.GpuMilliseconds : null,
            diagnostics.Particles is { } particles && particles.GpuSubmissionFence == _lastFence ? particles.UpdateAndRenderGpuMilliseconds : null,
            diagnostics.Atmosphere is { } atmosphere && atmosphere.GpuSubmissionFence == _lastFence ? atmosphere.CompositionGpuMilliseconds : null));
    }

    internal object Report(GraphicsDiagnostics diagnostics) => new
    {
        cpuFrames = _cpu, gpuFrames = _gpu,
        allocations = VisualReferenceCapture.Distribution(_cpu.Select(x => (double)x.AllocatedBytes).ToArray()),
        cpuPasses = new
        {
            world = CpuValues(x => x.WorldMilliseconds), overlay = CpuValues(x => x.OverlayMilliseconds),
            composite = CpuValues(x => x.CompositeMilliseconds), shadows = CpuValues(x => x.ShadowMilliseconds)
        },
        gpu = VisualReferenceCapture.Distribution(_gpu.Select(x => x.FrameMilliseconds).ToArray()),
        gpuPasses = new
        {
            world = Values(x => x.WorldMilliseconds), overlay = Values(x => x.OverlayMilliseconds),
            composite = Values(x => x.CompositeMilliseconds), shadows = Values(x => x.ShadowMilliseconds),
            particles = Values(x => x.ParticleMilliseconds), atmosphere = Values(x => x.AtmosphereMilliseconds)
        },
        gpuTiming = new { available = _gpu.Count > 0, timestampFrequencyHz = diagnostics.TimestampFrequencyHz,
            unavailableReason = _gpu.Count > 0 ? null : diagnostics.Frame?.GpuTimingUnavailableReason ?? "No distinct completed GPU samples observed.",
            method = "Direct-queue ticks * 1000 / frequency; readback only after submission fence completion. Warmup fences excluded. Latest completed diagnostics may skip intermediate fences; sample counts are explicit. Passes can overlap and must not be summed." }
    };

    private object Values(Func<ReferenceGpuSample, double?> select) =>
        VisualReferenceCapture.Distribution(_gpu.Select(select).Where(x => x.HasValue).Select(x => x!.Value).ToArray());

    private object CpuValues(Func<ReferenceCpuSample, double?> select) =>
        VisualReferenceCapture.Distribution(_cpu.Select(select).Where(x => x.HasValue).Select(x => x!.Value).ToArray());

    internal static RtsCameraState CameraPose(RtsCameraState start, ReferenceCameraPath path, int frame, int samples)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 1);
        if (!Enum.IsDefined(path)) throw new ArgumentOutOfRangeException(nameof(path));
        if (path == ReferenceCameraPath.Fixed) return start;
        float phase = (frame % samples) / (float)samples * MathF.Tau;
        return start with { Target = start.Target + new Vector3(20 * MathF.Sin(phase), 0, 10 * (1 - MathF.Cos(phase))),
            YawRadians = start.YawRadians + .15f * MathF.Sin(phase) };
    }
}

internal sealed record ReferenceGpuSample(ulong Fence, double FrameMilliseconds, double? WorldMilliseconds,
    double? OverlayMilliseconds, double? CompositeMilliseconds, double? ShadowMilliseconds,
    double? ParticleMilliseconds, double? AtmosphereMilliseconds);

internal sealed record ReferenceCpuSample(int Index, double Milliseconds, long AllocatedBytes,
    double? WorldMilliseconds, double? OverlayMilliseconds, double? CompositeMilliseconds, double? ShadowMilliseconds,
    GraphicsResourceDiagnostics Resources, GraphicsHealthDiagnostics Health, GraphicsMemoryDiagnostics? Memory, object? Submission);
