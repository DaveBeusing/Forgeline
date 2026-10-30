using System.Diagnostics;
using ForgeLine.Graphics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

// The SAME lightweight preallocated probe runs in both supplemental arms, outside idle/publication waits.
// OFF does not create a LiveRenderTelemetry or request its precise counters/memory/completion stream.
internal sealed class ReferenceCollectorControl(int warmups, int samples, ReferenceCollectorMode mode)
{
    internal static LiveRenderTelemetry? CreateOptionalCollector(ReferenceCollectorMode mode, IGraphicsRenderTelemetry source,
        int samples, string? output, int warmups, string runtime, GraphicsDeviceInfo? device) =>
        mode == ReferenceCollectorMode.CollectorOff ? null : new(source, samples, output, warmups: warmups, runtimeFingerprint: runtime, actualDevice: device);
    private readonly ReferenceCollectorControlFrame[] _frames = new ReferenceCollectorControlFrame[checked(warmups + samples)];
    private int _next;
    private readonly object _clockAnchor = RenderOwnerThreadIdentity.ClockAnchor();
    internal void Record(int ordinal, long start, long end, GraphicsFrameSubmission submission, RtsCameraState camera, int width, int height)
    {
        if (ordinal != _next || ordinal >= _frames.Length || start >= end || submission.SubmittedFence == 0 ||
            (_next > 0 && (_frames[_next - 1].EndTick > start || _frames[_next - 1].Submission.SubmittedFence >= submission.SubmittedFence)))
            throw new InvalidDataException("Collector control must preserve every contiguous successfully presented attempt.");
        _frames[_next++] = new(ordinal, ordinal - warmups, start, end, submission, camera, width, height);
    }
    internal object Export()
    {
        if (_next != _frames.Length) throw new InvalidDataException("Collector-control window incomplete; cannot select a winning subset.");
        return new { schemaVersion = 1, kind = "actual-host-collector-control", producerMode = "observer-on-off", modeAttestationAvailable = true,
            collectorEnabled = mode == ReferenceCollectorMode.CollectorOn, preciseProcessCountersEnabled = mode == ReferenceCollectorMode.CollectorOn,
            optionalMemoryAndCompletionCollectorEnabled = mode == ReferenceCollectorMode.CollectorOn,
            commonProbe = "identical preallocated two-boundary-QPC and successful-presentation/actual-submission probe in both arms",
            frequencyHz = Stopwatch.Frequency, clockAnchor = _clockAnchor, warmups, samples, frames = _frames,
            scope = "Wave0 optional collector overhead only; ordinary native GPU timing, production diagnostics, frame publication and ACK probe are retained in both arms; idle/publication wait excluded",
            allocations = "OFF has no fabricated process/thread totals or GPU/memory stream; full allocation budget uses separate Full mode" };
    }
}
internal readonly record struct ReferenceCollectorControlFrame(int Ordinal, int Index, long StartTick, long EndTick,
    GraphicsFrameSubmission Submission, RtsCameraState Camera, int Width, int Height);
