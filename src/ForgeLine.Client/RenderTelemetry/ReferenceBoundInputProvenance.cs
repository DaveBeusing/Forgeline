using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Graphics;

namespace ForgeLine.Client;

// Selected native-only CPU-source companion. Never aliases the five-stage exporter.
internal static class ReferenceBoundInputProvenance
{
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static object WriteNew(string root, string file, byte[] bytes, long capacity)
    {
        if (bytes.LongLength > capacity) throw new InvalidDataException("Bound provenance serialized payload exceeds its structural refusal limit.");
        using var stream = new FileStream(Path.Combine(root, file), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        return new { file, sha256 = Hash(bytes), bytes = bytes.Length, schemaVersion = 1 };
    }
    internal static object Write(GraphicsStageCapture capture, string root, string key, int repetition,
        string runtimeFingerprint, string recipeFingerprint, string recipeInputBytesBase64, object camera,
        object effectiveProfile, string? legacyFingerprint)
    {
        try { return WriteCore(capture, root, key, repetition, runtimeFingerprint, recipeFingerprint, recipeInputBytesBase64, camera, effectiveProfile, legacyFingerprint); }
        catch (Exception error)
        {
            RetainCaptureFailure(error, root, key + "-readback-" + repetition.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                () => new { runtimeFingerprint, recipeFingerprint, repetition }, capture, null);
            throw;
        }
    }
    private static object WriteCore(GraphicsStageCapture capture, string root, string key, int repetition,
        string runtimeFingerprint, string recipeFingerprint, string recipeInputBytesBase64, object camera,
        object effectiveProfile, string? legacyFingerprint)
    {
        var journal = capture.WorldBindings ?? throw new InvalidDataException("Selected provenance requires current binding journal.");
        var provenance = capture.BoundInputProvenance ?? throw new InvalidDataException("Selected CPU-source companion unavailable.");
        if (!journal.Complete || !provenance.Complete || capture.Stages.Count != 1 || capture.Stages[0].Stage != GraphicsCaptureStages.FinalColor ||
            provenance.Submission != capture.Submission || journal.Submission != capture.Submission ||
            provenance.CompletedFence != capture.CompletedFence || journal.CompletedFence != capture.CompletedFence ||
            provenance.FrameIndex != capture.FrameIndex || journal.FrameIndex != capture.FrameIndex ||
            provenance.DeviceGeneration != capture.DeviceIdentity.Generation || provenance.DeviceSession != capture.DeviceIdentity.SessionId ||
            provenance.ResourceGeneration != capture.Submission.ResourceGeneration || journal.ResourceGeneration != capture.Submission.ResourceGeneration ||
            journal.DeviceGeneration != capture.DeviceIdentity.Generation || journal.DeviceSession != capture.DeviceIdentity.SessionId ||
            journal.PresentedBefore != capture.PresentedBefore || journal.PresentedAfter != capture.PresentedAfter ||
            provenance.PresentedBefore != capture.PresentedBefore || provenance.PresentedAfter != capture.PresentedAfter)
            throw new InvalidDataException("CPU-source/journal/FinalColor capture provenance differs.");
        if (GraphicsWorldBindingJournal.SerializeSidecar(journal).LongLength > GraphicsWorldBindingJournal.JournalCapacity ||
            GraphicsWorldBindingJournal.SerializeSidecar(provenance).LongLength > GraphicsBoundInputProvenance.WireCapacity - GraphicsBoundInputProvenance.EnvelopeCapacity)
            throw new InvalidDataException("Actual compact journal/provenance body exceeds reserved structural capacity.");
        var final = capture.Stages[0];
        if (final.UnavailableReason is not null || final.Layout is not { } layout ||
            !GraphicsReadbackRows.LogicalBytes(layout, final.RawBytes.Span).AsSpan().SequenceEqual(capture.FinalRgba))
            throw new InvalidDataException("Actual FinalColor layout/pixels unavailable or different.");
        byte[] profile = JsonSerializer.SerializeToUtf8Bytes(effectiveProfile, VisualReferenceCapture.JsonOptions);
        string pixelSha256 = Hash(capture.FinalRgba);
        var metadata = new
        {
            sourceSha = ClientBuildIdentity.SourceRevision, producer = "native-reference", runtimeFingerprint,
            recipeFingerprint, recipeInputBytesBase64, repetition, camera,
            profileDomain = "effective-quality-profile", profileInputBytesBase64 = Convert.ToBase64String(profile), profileFingerprint = Hash(profile),
            capture.DeviceIdentity, capture.BufferCount, capture.Submission, capture.CompletedFence, capture.FrameIndex,
            capture.PresentedBefore, capture.PresentedAfter, finalColorLayout = layout, pixelSha256, legacyFingerprint
        };
        string prefix = key + "-readback-" + repetition.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        var journalValue = new { schemaVersion = 1, kind = "final-color-world-binding-journal", qualified = false, metadata, journal = (GraphicsWorldBindingJournal?)journal };
        byte[] journalRaw = GraphicsWorldBindingJournal.SerializeSidecar(journalValue);
        byte[] journalEnvelope = GraphicsWorldBindingJournal.SerializeSidecar(journalValue with { journal = (GraphicsWorldBindingJournal?)null });
        if (journalEnvelope.Length > GraphicsWorldBindingJournal.EnvelopeCapacity)
            throw new InvalidDataException("FinalColor journal context exceeds envelope capacity.");
        var journalRef = WriteNew(root, prefix + "-world-bindings.json", journalRaw, GraphicsWorldBindingJournal.PayloadCapacity);
        var value = new
        {
            schemaVersion = 1, kind = "reference-bound-input-cpu-provenance", qualified = false,
            metadata, journalReference = journalRef, provenance = (GraphicsBoundInputProvenance?)provenance,
            scope = "Private CPU source bytes and observed destination/copy/write intent only; no GPU-fetched content or root-cause proof. Unknown ranges are not zero."
        };
        byte[] envelope = GraphicsWorldBindingJournal.SerializeSidecar(value with { provenance = null });
        if (envelope.Length > GraphicsBoundInputProvenance.EnvelopeCapacity)
            throw new InvalidDataException("CPU-source context exceeds envelope capacity.");
        return WriteNew(root, prefix + "-bound-input-provenance.json", GraphicsWorldBindingJournal.SerializeSidecar(value), GraphicsBoundInputProvenance.WireCapacity);
    }
    internal static void Preflight(string root, string key, int width, int height, int repeats)
    {
        if (width <= 0 || height <= 0 || repeats is < 2 or > 128) throw new ArgumentOutOfRangeException(nameof(repeats));
        ulong records = checked((ulong)repeats * ((ulong)GraphicsBoundInputProvenance.WireCapacity + (ulong)GraphicsWorldBindingJournal.PayloadCapacity));
        ulong images = checked((ulong)width * (ulong)height * 4 * (ulong)repeats * 2);
        ulong estimate = checked(records + images + 128UL * 1024 * 1024);
        ulong required = checked((estimate * 3 + 1) / 2);
        string absolute = Path.GetFullPath(root);
        long free = new DriveInfo(Path.GetPathRoot(absolute)!).AvailableFreeSpace;
        var value = new { kind = "bound-input-cpu-provenance-storage", qualified = false, repeats, recordBytes = records, imagePlanningBytes = images,
            estimatedBytes = estimate, requiredBytes = required, freeBytes = free,
            meaning = "Structural output planning; not codec-hardbound, managed heap or allocation-policy acceptance." };
        WriteNew(root, key + "-bound-input-storage.json", GraphicsWorldBindingJournal.SerializeSidecar(value), 1024 * 1024);
        if (free < 0 || (ulong)free < required) throw new IOException("Insufficient output storage for full provenance/journal refusal capacities.");
    }
    internal static void RetainCaptureFailure(Exception original, string root, string key, Func<object?> context,
        GraphicsStageCapture? current, GraphicsWorldBindingAttempt? attempt)
    {
        try
        {
            var provenance = current?.BoundInputProvenance is { } known
                ? known with { Complete = false, Failure = "post-capture-export-failure", Submission = null, CompletedFence = null, PresentedAfter = null }
                : GraphicsBoundInputProvenance.FailurePrefix(original, attempt);
            if (provenance is null) return;
            ReferenceStageDiagnostics.RetainWorldBindingFailure(original, root, key, context, current, attempt);
            object? scoped = null;
            try
            {
                scoped = context();
                if (scoped is not null && GraphicsWorldBindingJournal.SerializeSidecar(scoped).Length > GraphicsBoundInputProvenance.EnvelopeCapacity / 2) scoped = null;
            }
            catch { scoped = null; }
            var value = new { schemaVersion = 1, kind = "incomplete-bound-input-capture", qualified = false,
                sourceSha = ClientBuildIdentity.SourceRevision, context = scoped,
                contextUnavailable = scoped is null ? "Context unavailable; accepted current prefix retained." : null, provenance };
            WriteNew(root, key + "-bound-input-capture-failure.json", GraphicsWorldBindingJournal.SerializeSidecar(value), GraphicsBoundInputProvenance.WireCapacity);
        }
        catch { try { Console.Error.WriteLine("[bound-input:prefix-unavailable] Original exception preserved."); } catch { } }
    }
    internal static void RetainScopeFailure(Exception original, GraphicsBoundInputProvenanceScope? scope, string root)
    {
        if (scope is null) return;
        try
        {
            var value = new { schemaVersion = 1, kind = "bound-input-supplementary-scope-failure", qualified = false,
                sourceSha = ClientBuildIdentity.SourceRevision, scope = scope.ScopeFailureFacts(),
                failureType = original.GetType().Name, complete = false, captureTokenIncluded = false };
            WriteNew(root, "bound-input-scope-failure.json", GraphicsWorldBindingJournal.SerializeSidecar(value), 1024 * 1024);
        }
        catch { try { Console.Error.WriteLine("[bound-input:scope-prefix-unavailable] Original exception preserved."); } catch { } }
    }
}
