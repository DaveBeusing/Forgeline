using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using ForgeLine.Presentation.RenderSubmission;

namespace ForgeLine.Client;

internal sealed partial class ClientRenderHost
{
    private readonly ReferenceFixtureRecipe? _referenceFixture;
    private readonly ReferenceFixtureSequence? _fixtureSequence;
    private readonly ReferenceFixtureFrameObservation[]? _fixtureFrames;
    private int _fixtureCaptureRequested, _fixtureCaptureCompleted;
    private object? _fixtureReadbackEvidence;
    private ReferenceWorldInputDiagnostics? _fixtureWorldInputInspector;
    private object? _fixtureAdapterBinding;
    private readonly ReferenceCollectorControl? _fixtureCollectorControl;
    private void RejectFixtureEarlyExit(string state) { if (_referenceFixture is not null) throw new InvalidDataException("Fixture attempt failed before successful presentation: " + state); }
    private ClientRenderConfiguration? _fixtureComposition;
    private GraphicsDeviceInfo? _fixtureDevice;
    internal int FixtureAcknowledged => _fixtureSequence?.Acknowledged ?? -1;
    internal bool FixtureReadbackCompleted => Volatile.Read(ref _fixtureCaptureCompleted) != 0;
    internal void CheckFixtureFailure() => ThrowIfFaulted();
    internal void PublishFixtureFrame(int ordinal)
    {
        var recipe = _referenceFixture ?? throw new InvalidOperationException("No reference fixture configured.");
        _fixtureSequence!.Request(ordinal);
        if (!Publish(recipe.Frame(ordinal))) throw new InvalidOperationException("Fixture host stopped before publication.");
    }
    internal void RequestFixtureReadback()
    {
        if (_referenceFixture is null || FixtureAcknowledged != _referenceFixture.FrameCount - 1)
            throw new InvalidOperationException("Readbacks require all contiguous successful frame ACKs.");
        Volatile.Write(ref _fixtureCaptureRequested, 1); _signal.Set();
    }
    private void AcknowledgeFixtureFrame(int ordinal, RtsCameraState camera, int width, int height,
        PresentationSnapshot snapshot, IGraphicsRenderTelemetry source, SimpleInstanceRenderer meshes, int bufferCount, int hudVertices, ParticleVfxDiagnostics particles)
    {
        long observedFrom = Stopwatch.GetTimestamp();
        var recipe = _referenceFixture!;
        var expectedCamera = recipe.CameraAt(ordinal);
        if (!ReferenceEquals(snapshot, recipe.Snapshot) || camera != expectedCamera || width != recipe.Display.Width || height != recipe.Display.Height)
            throw new InvalidOperationException("Actual fixture state differs from the shared immutable recipe.");
        int index = ordinal - recipe.Warmups;
        if (index >= 0 && index < recipe.Samples)
        {
            var cpu = source.LastSubmission ?? throw new InvalidDataException("Fixture actual submission missing.");
            if (cpu.SubmittedFence == 0) throw new InvalidOperationException("Fixture ACK requires an actual submitted frame.");
            _fixtureFrames![index] = new(ordinal, index, camera, width, height, cpu.SubmittedFence, cpu.Generation, cpu.ResourceGeneration,
                meshes.WorkMetrics, meshes.SubmissionMetrics, bufferCount, hudVertices, observedFrom, Stopwatch.GetTimestamp())
                { Content = meshes.CaptureContentEvidence(), Particles = particles };
        }
        _fixtureSequence!.Acknowledge(ordinal);
    }
    private void CaptureFixtureReadback(IGraphicsDevice graphics, Action<IGraphicsCommandContext> commands, Action prepare, SimpleInstanceRenderer meshes,
        Func<RtsCameraState> actualCamera)
    {
        var recipe = _referenceFixture!;
        byte[]? original = null;
        var records = new List<object>();
        bool allEqual = true;
        if (recipe.CaptureStageDiagnostics) ReferenceStageDiagnostics.Preflight(graphics, recipe.OutputRoot, "host-fixed", recipe.RepeatCaptures);
        for (int repeat = 0; repeat < recipe.RepeatCaptures; repeat++)
        {
            using var inputScope=_fixtureWorldInputInspector?.Arm(repeat);
            _fixtureWorldInputInspector?.BeforeSite(WorldInputSite.HostReadbackPrepare);
            prepare();
            _fixtureWorldInputInspector?.AfterSite(WorldInputSite.HostReadbackPrepare,true);
            ulong beforePresent = graphics.PresentedFrameCount;
            var beforeSubmission = ((IGraphicsRenderTelemetry)graphics).LastSubmission;
            GraphicsStageCapture? stageCapture = recipe.CaptureStageDiagnostics ? graphics.CaptureFrameDiagnostics(GraphicsColor.ForgeLineClear, commands) : null;
            byte[] pixels = stageCapture?.FinalRgba ?? graphics.CaptureFrame(GraphicsColor.ForgeLineClear, commands, recipe.CaptureShadowDiagnostics);
            meshes.Submit(); meshes.Retire();
            ReferenceFixturePresentation.RequireSuccessful(beforePresent, graphics.PresentedFrameCount, beforeSubmission, ((IGraphicsRenderTelemetry)graphics).LastSubmission);
            _fixtureWorldInputInspector?.AfterCapture(stageCapture);
            object? stages=stageCapture is null ? null : ReferenceStageDiagnostics.Write(stageCapture, recipe.OutputRoot, "host-fixed", repeat,
                "actual-render-host", recipe.RuntimeFingerprint, recipe.Fingerprint, recipe.InputBytesBase64, actualCamera(),
                _fixtureComposition?.Profile ?? throw new InvalidDataException("Actual effective host profile unavailable."), graphics.CaptureInputFingerprint);
            object? worldInputs=_fixtureWorldInputInspector?.Persist(stages);
            string hash = Convert.ToHexString(SHA256.HashData(pixels));
            string? shadowFile = null;
            if (recipe.CaptureShadowDiagnostics && !graphics.CaptureShadowDepth.IsEmpty)
            {
                shadowFile = $"host-fixed-readback-{repeat:D2}.depth";
                using var stream = new FileStream(Path.Combine(recipe.OutputRoot, shadowFile), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                stream.Write(graphics.CaptureShadowDepth.Span);
            }
            records.Add(new { repeat, worldInputDiagnostics=worldInputs, pixelSha256 = hash, inputFingerprint = graphics.CaptureInputFingerprint,
                actualCamera = actualCamera(), actualSubmission = ((IGraphicsRenderTelemetry)graphics).LastSubmission,
                shadowFile, shadowLayout = ((IGraphicsRenderTelemetry)graphics).CaptureShadowLayout,
                shadowFingerprint = graphics.CaptureShadowDepth.IsEmpty ? null : Convert.ToHexString(SHA256.HashData(graphics.CaptureShadowDepth.Span)),
                stageDiagnostics = stages });
            if (original is null)
            {
                original = pixels;
                ReferenceCaptureImage.WritePng(Path.Combine(recipe.OutputRoot, "host-fixed.png"), recipe.Display.Width, recipe.Display.Height, pixels);
            }
            bool equal = original.AsSpan().SequenceEqual(pixels);
            allEqual &= equal;
            _fixtureReadbackEvidence = new { adapterIdentity = ((IGraphicsRenderTelemetry)graphics).DeviceIdentity,
                actualSurface = graphics.Diagnostics.Surface, records = records.ToArray(), repeatPixelsEqual = allEqual,
                pixelSha256 = Convert.ToHexString(SHA256.HashData(original)), image = "host-fixed.png",
                imageSha256 = VisualReferenceCapture.FileHash(Path.Combine(recipe.OutputRoot, "host-fixed.png")),
                worldInputInspectorVersion=recipe.CaptureStageDiagnostics ? 1 : (int?)null,timingScope = "All readbacks/PNG/hash serialization occur after the completed fixed sample window" };
            ReferenceCaptureManifest.Write(Path.Combine(recipe.OutputRoot, "host-readback.json"), JsonSerializer.Serialize(_fixtureReadbackEvidence, VisualReferenceCapture.JsonOptions));
            if (!equal)
            {
                ReferenceCaptureImage.WritePng(Path.Combine(recipe.OutputRoot, $"failed-host-repeat-{repeat:D2}.png"), recipe.Display.Width, recipe.Display.Height, pixels);
                if (!recipe.ContinueAfterMismatch) throw new InvalidDataException("Repeated actual host fixture readback pixels differ; failed cohort retained.");
            }
        }
        if (!allEqual) throw new InvalidDataException("Repeated actual host fixture pixels differ; all declared diagnostic records retained as failed evidence.");
        Volatile.Write(ref _fixtureCaptureRequested, 0);
        Volatile.Write(ref _fixtureCaptureCompleted, 1);
    }
    internal object FixtureEvidence()
    {
        var recipe = _referenceFixture ?? throw new InvalidOperationException("No fixture configured.");
        return new { schemaVersion = 1, kind = "actual-client-render-host-reference-fixture", sourceSha = ClientBuildIdentity.SourceRevision,
            runtimeFingerprint = recipe.RuntimeFingerprint, key = recipe.CaseKey, recipeFingerprint = recipe.Fingerprint, recipeInputs = recipe.IdentityInputs, recipeInputBytesBase64 = recipe.InputBytesBase64,
            device = _fixtureDevice, profile = _fixtureComposition?.Profile, lighting = _fixtureComposition?.Lighting, atmosphere = _fixtureComposition?.Atmosphere,
            snapshotFingerprint = recipe.SnapshotFingerprint, adapterBinding = _fixtureAdapterBinding, frames = _fixtureFrames, readback = _fixtureReadbackEvidence,
            collectorControl = _fixtureCollectorControl?.Export(), ownerCycles = _ownerCycles?.Export(), postWindowControl = _postWindowControl?.Evidence(), collectorMode = recipe.CollectorMode,
            window = new { recipe.Warmups, recipe.Samples, contiguous = true, acknowledged = FixtureAcknowledged + 1 },
            nativePixelComparison = "Requires separate actual native capture with identical computed recipe and pixels; not granted by CLI context" };
    }
}
internal readonly record struct ReferenceFixtureFrameObservation(int Ordinal, int Index, RtsCameraState Camera,
    int Width, int Height, ulong SubmittedFence, ulong Generation, ulong ResourceGeneration, SubmissionWorkMetrics Work,
    InstanceSubmissionMetrics Submission, int BufferCount, int HudVertices, long ObserverStartTick, long ObserverEndTick)
{
    public RenderContentEvidence? Content { get; init; }
    public ParticleVfxDiagnostics Particles { get; init; }
}
