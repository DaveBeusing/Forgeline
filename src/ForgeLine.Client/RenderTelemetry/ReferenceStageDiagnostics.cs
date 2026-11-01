using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Graphics;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal static class ReferenceStageDiagnostics
{
    internal static object Preflight(IGraphicsDevice graphics, string root, string key, int repeats)
    {
        var footprints = graphics.CaptureStageFootprints();
        if (repeats is < 2 or > 128 || footprints.Count != 5 || footprints.Any(f => f.UnavailableReason is not null || f.Layout is null))
            throw new InvalidDataException("All five actual stage footprints are required for the declared diagnostic.");
        var storage = EstimateStorage(footprints, repeats);
        ulong estimate = storage.EstimatedBytes, required = storage.RequiredBytes;
        long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;
        var value = new { schemaVersion = 1, kind = "stage-readback-capacity-preflight", qualified = false,
            sourceSha = ClientBuildIdentity.SourceRevision, key, repeats, footprints, estimatedBytes = estimate,
            journalRecordsBytes = storage.JournalBytes, journalRecordByteCap = GraphicsWorldBindingJournal.PayloadCapacity,
            reserveNumerator = 3, reserveDenominator = 2, requiredBytes = required, freeBytes = free,
            sufficient = free >= 0 && (ulong)free >= required };
        ReferenceCaptureManifest.Write(Path.Combine(root, key + "-stage-capacity.json"), JsonSerializer.Serialize(value, VisualReferenceCapture.JsonOptions));
        if (free < 0 || (ulong)free < required) throw new IOException("Insufficient1.5x actual-footprint capacity for all declared stage readbacks.");
        return value;
    }

    internal static (ulong EstimatedBytes, ulong RequiredBytes, ulong JournalBytes) EstimateStorage(
        IReadOnlyList<GraphicsStageFootprint> footprints, int repeats)
    {
        if (repeats is < 2 or > 128 || footprints.Count != 5 ||
            footprints.Any(f => f.UnavailableReason is not null || f.Layout is null || f.RawBytes == 0))
            throw new InvalidDataException("Five actual positive footprints and bounded repeats are required.");
        ulong raw = 0;
        foreach (var footprint in footprints) raw = checked(raw + footprint.RawBytes);
        var final = footprints.Single(f => f.Stage == GraphicsCaptureStages.FinalColor).Layout!.Value;
        ulong journals = checked((ulong)repeats * (ulong)GraphicsWorldBindingJournal.PayloadCapacity);
        ulong estimate = checked(raw * (ulong)repeats + (ulong)final.Width * (ulong)final.Height * 4 * (ulong)repeats * 2 +
            journals + 64 * 1024 * 1024);
        return (estimate, checked((estimate * 3 + 1) / 2), journals);
    }


    internal static object Write(GraphicsStageCapture capture, string root, string key, int repetition,
        string producer, string runtimeFingerprint, string recipeFingerprint, string recipeInputBytesBase64, RtsCameraState actualCamera,
        object effectiveProfile, string? inputFingerprint)
    {
        try
        {
        if (capture.Stages.Count != 5 || capture.Stages.Select(s => s.Stage).Distinct().Count() != 5)
            throw new InvalidDataException("Diagnostic capture must retain every distinct declared stage.");
        var records = new List<object>();
        foreach (var stage in capture.Stages)
        {
            if (stage.UnavailableReason is not null || stage.Layout is not { } layout)
                throw new InvalidDataException("Missing requested stage: " + stage.Stage + ": " + stage.UnavailableReason);
            byte[] logical = GraphicsReadbackRows.LogicalBytes(layout, stage.RawBytes.Span);
            string file = $"{key}-readback-{repetition:D2}-{stage.Stage}.raw";
            using (var stream = new FileStream(Path.Combine(root, file), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                stream.Write(stage.RawBytes.Span);
            byte[] layoutBytes = JsonSerializer.SerializeToUtf8Bytes(layout, VisualReferenceCapture.JsonOptions);
            records.Add(new { stage = stage.Stage, file, layout, rawBytes = stage.RawBytes.Length,
                bytesPerTexel = GraphicsReadbackRows.BytesPerTexel(layout.Format), logicalBytes = logical.Length,
                rawSha256 = Hash(stage.RawBytes.Span), logicalSha256 = Hash(logical),
                layoutBytesBase64 = Convert.ToBase64String(layoutBytes), layoutSha256 = Hash(layoutBytes) });
        }
        var worldBindingJournal=WriteWorldBindings(capture,root,key,repetition,producer,runtimeFingerprint,recipeFingerprint,recipeInputBytesBase64,actualCamera,effectiveProfile);
        var finalLayout = capture.Stages.Single(s => s.Stage == GraphicsCaptureStages.FinalColor).Layout!.Value;
        var value = new { schemaVersion = 1, kind = "renderer-stage-readback", qualified = false,
            producer, sourceSha = ClientBuildIdentity.SourceRevision, runtimeFingerprint, recipeFingerprint, recipeInputBytesBase64,
            outputRaster = new { width = finalLayout.Width, height = finalLayout.Height, capture.BufferCount },
            repetition, camera = actualCamera, effectiveProfile, inputFingerprint, capture.Submission,
            capture.CompletedFence, capture.FrameIndex, capture.PresentedBefore, capture.PresentedAfter,
            capture.DeviceIdentity,worldBindingJournalVersion=1,worldBindingJournal,stages = records.ToArray() };
        string metadataFile = $"{key}-readback-{repetition:D2}-stages.json";
        using (var stream = new FileStream(Path.Combine(root, metadataFile), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            JsonSerializer.Serialize(stream, value, VisualReferenceCapture.JsonOptions);
        return new { file = metadataFile, sha256 = VisualReferenceCapture.FileHash(Path.Combine(root, metadataFile)) };
        }
        catch(Exception error)
        {
            RetainWorldBindingFailure(error,root,key,()=>new {repetition,producer,runtimeFingerprint,recipeFingerprint,recipeInputBytesBase64,actualCamera,effectiveProfile},capture);
            throw;
        }
    }
    private static object WriteWorldBindings(GraphicsStageCapture capture, string root, string key, int repetition,
        string producer, string runtimeFingerprint, string recipeFingerprint, string recipeBytes, RtsCameraState camera, object profile)
    {
        var journal = capture.WorldBindings ?? throw new InvalidDataException("Selected stage capture has no World binding journal.");
        if (!journal.Complete || journal.Failure is not null || journal.Submission != capture.Submission ||
            journal.CompletedFence != capture.CompletedFence || journal.FrameIndex != capture.FrameIndex ||
            journal.PresentedBefore != capture.PresentedBefore || journal.PresentedAfter != capture.PresentedAfter ||
            journal.DeviceGeneration != capture.DeviceIdentity.Generation || journal.DeviceSession != capture.DeviceIdentity.SessionId)
            throw new InvalidDataException("World binding journal differs from its actual completed stage capture.");
        byte[] profileBytes = JsonSerializer.SerializeToUtf8Bytes(profile, VisualReferenceCapture.JsonOptions);
        var value = new { schemaVersion = 1, kind = "reference-world-binding-journal", qualified = false,
            sourceSha = ClientBuildIdentity.SourceRevision, producer, runtimeFingerprint, recipeFingerprint, recipeInputBytesBase64 = recipeBytes,
            repetition, camera, profileDomain = "effective-quality-profile", profileInputBytesBase64 = Convert.ToBase64String(profileBytes),
            profileFingerprint = Hash(profileBytes), capture.DeviceIdentity, capture.BufferCount, journal = (GraphicsWorldBindingJournal?)journal,
            scope = "Ordered opaque World command/binding observations only; no GPU fetched/prearmed contents or cross-process resource identity proof." };
        byte[] envelope = GraphicsWorldBindingJournal.SerializeSidecar(value with { journal = null });
        if (envelope.Length > GraphicsWorldBindingJournal.EnvelopeCapacity)
            throw new InvalidDataException("World binding success context exceeds its reserved envelope.");
        byte[] raw = GraphicsWorldBindingJournal.SerializeSidecar(value);
        if (raw.LongLength > GraphicsWorldBindingJournal.PayloadCapacity)
            throw new InvalidDataException("World binding sidecar exceeds32MiB actual UTF8 bytes.");
        string file = $"{key}-readback-{repetition:D2}-world-bindings.json";
        using (var stream = new FileStream(Path.Combine(root, file), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            stream.Write(raw);
        return new { file, sha256 = Hash(raw), bytes = raw.Length, schemaVersion = 1 };
    }

    internal static void RetainWorldBindingFailure(Exception original, string root, string key, Func<object?> context,
        GraphicsStageCapture? currentCapture = null, GraphicsWorldBindingAttempt? attempt = null)
    {
        try
        {
            var prefix = currentCapture?.WorldBindings is { } current
                ? current with { Complete = false, Failure = "export-or-retirement-failure", Submission = null, CompletedFence = null, PresentedAfter = null }
                : GraphicsWorldBindingJournal.FailurePrefix(original, attempt);
            if (prefix is null) return; // Before arming is unavailable, never borrowed from a previous capture.

            object? scoped = null;
            string? contextUnavailable = null;
            try
            {
                scoped = context();
                byte[] envelope = GraphicsWorldBindingJournal.SerializeSidecar(new
                {
                    schemaVersion = 1, kind = "incomplete-world-binding-journal", qualified = false,
                    sourceSha = ClientBuildIdentity.SourceRevision, context = scoped, contextUnavailable = (string?)null,
                    journal = (GraphicsWorldBindingJournal?)null, scope = "Incomplete current-capture prefix; no successful tuple or content proof."
                });
                if (envelope.Length > GraphicsWorldBindingJournal.EnvelopeCapacity)
                    throw new InvalidDataException("World failure context exceeds reserved envelope.");
            }
            catch
            {
                scoped = null;
                contextUnavailable = "Original context serialization failed or exceeded its reserved bound; journal prefix is retained intact.";
            }
            byte[] raw = GraphicsWorldBindingJournal.SerializeSidecar(new
            {
                schemaVersion = 1, kind = "incomplete-world-binding-journal", qualified = false,
                sourceSha = ClientBuildIdentity.SourceRevision, context = scoped, contextUnavailable,
                journal = prefix, scope = "Incomplete current-capture prefix; no successful tuple or content proof."
            });
            if (raw.LongLength > GraphicsWorldBindingJournal.PayloadCapacity)
                throw new IOException("Incomplete World binding sidecar exceeds32MiB.");
            using var file = new FileStream(Path.Combine(root, key + "-world-bindings-failure.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            file.Write(raw);
        }
        catch
        {
            try { Console.Error.WriteLine("[world-bindings:prefix-unavailable] Original exception remains authoritative."); }
            catch { }
        }
    }


    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
