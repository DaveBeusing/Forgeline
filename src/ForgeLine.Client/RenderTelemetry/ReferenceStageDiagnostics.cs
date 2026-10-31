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
        ulong raw = 0;
        foreach (var footprint in footprints) raw = checked(raw + footprint.RawBytes);
        var final = footprints.Single(f => f.Stage == GraphicsCaptureStages.FinalColor).Layout!.Value;
        // Reserve all raw footprints plus two lossless-image payloads per repeat and64MiB records.
        ulong estimate = checked(raw * (ulong)repeats + (ulong)final.Width * (ulong)final.Height * 4 * (ulong)repeats * 2 + 64 * 1024 * 1024);
        ulong required = checked((estimate * 3 + 1) / 2);
        long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;
        var value = new { schemaVersion = 1, kind = "stage-readback-capacity-preflight", qualified = false,
            sourceSha = ClientBuildIdentity.SourceRevision, key, repeats, footprints, estimatedBytes = estimate,
            reserveNumerator = 3, reserveDenominator = 2, requiredBytes = required, freeBytes = free,
            sufficient = free >= 0 && (ulong)free >= required };
        ReferenceCaptureManifest.Write(Path.Combine(root, key + "-stage-capacity.json"), JsonSerializer.Serialize(value, VisualReferenceCapture.JsonOptions));
        if (free < 0 || (ulong)free < required) throw new IOException("Insufficient1.5x actual-footprint capacity for all declared stage readbacks.");
        return value;
    }

    internal static object Write(GraphicsStageCapture capture, string root, string key, int repetition,
        string producer, string runtimeFingerprint, string recipeFingerprint, string recipeInputBytesBase64, RtsCameraState actualCamera,
        object effectiveProfile, string? inputFingerprint)
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
        var finalLayout = capture.Stages.Single(s => s.Stage == GraphicsCaptureStages.FinalColor).Layout!.Value;
        var value = new { schemaVersion = 1, kind = "renderer-stage-readback", qualified = false,
            producer, sourceSha = ClientBuildIdentity.SourceRevision, runtimeFingerprint, recipeFingerprint, recipeInputBytesBase64,
            outputRaster = new { width = finalLayout.Width, height = finalLayout.Height, capture.BufferCount },
            repetition, camera = actualCamera, effectiveProfile, inputFingerprint, capture.Submission,
            capture.CompletedFence, capture.FrameIndex, capture.PresentedBefore, capture.PresentedAfter,
            capture.DeviceIdentity, stages = records.ToArray() };
        string metadataFile = $"{key}-readback-{repetition:D2}-stages.json";
        using (var stream = new FileStream(Path.Combine(root, metadataFile), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            JsonSerializer.Serialize(stream, value, VisualReferenceCapture.JsonOptions);
        return new { file = metadataFile, sha256 = VisualReferenceCapture.FileHash(Path.Combine(root, metadataFile)) };
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
