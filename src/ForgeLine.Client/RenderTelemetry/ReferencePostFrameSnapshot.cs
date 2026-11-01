using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Graphics;

namespace ForgeLine.Client;

// Late selected diagnostic only. No graphics work is performed by the filesystem exporter.
internal static class ReferencePostFrameSnapshot
{
    internal sealed record Metadata(string SourceSha, string Producer, string RuntimeFingerprint, string RecipeFingerprint,
        string RecipeInputBytesBase64, int Repetition, object Camera, string ProfileDomain, string ProfileInputBytesBase64,
        string ProfileFingerprint, object ObservedComposition, string PixelSha256, string? LegacyFingerprint, object Image,
        bool Qualified = false);
    internal sealed record RawReference(string File, string Sha256, long Bytes, int MipIndex);
    internal sealed record ResourceRow(GraphicsPostFrameResourceDescription Description, RawReference[] RawMips);
    internal sealed record Coverage(string Meaning, string[] Included, string[] Excluded, string Unknown);
    internal sealed record Wire(int SchemaVersion, string Kind, bool Complete, object Metadata,
        GraphicsPostFrameOriginalReceipt? OriginalFrameReceipt, GraphicsPostFrameCopyReceipt? CopyReceipt,
        Coverage Coverage, ResourceRow[] Resources, GraphicsPostFrameIntent? Intent);
    private static readonly Coverage Scope = new(
        "Late resident-resource superset after primary PNG; not GPU-fetched bytes or historical draw bindings.",
        ["resident-terrain", "final-retained-main-scratch", "final-retained-decoration-scratch", "captured-slot-constant-arena-used-prefix-if-present"],
        ["render-targets", "depth-shadow-targets", "root-constants", "particles", "HUD", "skin-caches", "inactive-private-caches", "earlier-pass-scratch", "entity-root-associations"],
        "Stored terrain LOD includes culled history; scratch lacks an independent pass marker; raw arena padding has unknown shader relevance; subsequent repetitions are perturbed by these late copies.");
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static object WriteNew(string root, string file, byte[] bytes)
    {
        using (var stream = new FileStream(Path.Combine(root, file), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) stream.Write(bytes);
        return new { file, sha256 = Hash(bytes), bytes = bytes.LongLength, schemaVersion = 1 };
    }
    internal static Metadata Context(string runtime, string recipe, string recipeBytes, int repetition, object camera,
        object quality, object composition, byte[] pixels, string? fingerprint, string root, string image)
    {
        byte[] profile = JsonSerializer.SerializeToUtf8Bytes(quality, VisualReferenceCapture.JsonOptions);
        string path = Path.Combine(root, image);
        if (!File.Exists(path)) throw new InvalidDataException("Primary image must be closed before snapshot context.");
        return new(ClientBuildIdentity.SourceRevision, "native-reference", runtime, recipe, recipeBytes, repetition, camera,
            "effective-quality-profile", Convert.ToBase64String(profile), Hash(profile), composition, Hash(pixels), fingerprint,
            new { file = image, sha256 = VisualReferenceCapture.FileHash(path), bytes = new FileInfo(path).Length });
    }
    private static void Validate(GraphicsPostFrameSnapshotResult result)
    {
        var original = result.OriginalFrameReceipt; var copy = result.CopyReceipt;
        if (original.SchemaVersion != 1 || original.FenceDomain != "original-device-frame-fence" || original.Submission.Submission == 0 ||
            original.CompletedFence is 0 or ulong.MaxValue || original.CompletedFence < original.Submission.SubmittedFence ||
            original.Submission.Generation != original.DeviceIdentity.Generation || original.PresentedAfter != original.PresentedBefore + 1 ||
            original.Submission.ResourceGeneration != original.AppliedResizeGeneration + 1 || original.Width <= 0 || original.Height <= 0 ||
            original.ManagedOwnerThreadId <= 0 || original.FrameIndex < 0 || copy.SchemaVersion != 1 || !copy.MayHaveExecuted || !copy.CompletionProved ||
            copy.FenceDomain == original.FenceDomain || !copy.FenceDomain.StartsWith("late-copy-fence-", StringComparison.Ordinal) ||
            copy.SubmittedFence == 0 || copy.CompletedFence is 0 or ulong.MaxValue || copy.CompletedFence < copy.SubmittedFence ||
            result.SecondaryCleanupErrors.Length != 0 || result.Resources.Length is < 1 or > GraphicsPostFrameLimits.Resources)
            throw new InvalidDataException("Late snapshot receipt/completion/cleanup facts are inconsistent.");
        GraphicsPostFrameLimits.Totals(result.PaddedStagingBytes, result.CanonicalBytes);
        GraphicsPostFrameLimits.ValidateIntent(result.Intent, result.Resources.Length);
    }
    internal static object Write(GraphicsPostFrameSnapshotResult result, Metadata metadata, string root, string key, int repetition)
    {
        Validate(result);
        string prefix = key + "-readback-" + repetition.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + "-post-frame";
        var rows = new List<ResourceRow>();
        try
        {
            ulong total = 0;
            for (int index = 0; index < result.Resources.Length; index++)
            {
                var resource = result.Resources[index]; var description = resource.Description;
                if (description.LocalId != index + 1 || resource.CanonicalBytes.Length != description.Mips.Length || description.Mips.Length is < 1 or > 32)
                    throw new InvalidDataException("Late resource ordinal/mip shape differs.");
                var refs = new List<RawReference>();
                rows.Add(new(description, []));
                for (int mip = 0; mip < description.Mips.Length; mip++)
                {
                    byte[] raw = resource.CanonicalBytes[mip]; var layout = description.Mips[mip];
                    if (layout.MipIndex != (description.Texture is null ? -1 : mip) || (ulong)raw.LongLength != layout.CanonicalBytes) throw new InvalidDataException("Canonical raw mip length differs.");
                    total = checked(total + (ulong)raw.LongLength);
                    if (total > GraphicsPostFrameLimits.RawBytes) throw new InvalidDataException("Total canonical raw capacity exceeded.");
                    string file = prefix + "-resource-" + (index + 1).ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + "-mip-" + mip.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + ".raw";
                    _ = WriteNew(root, file, raw); refs.Add(new(file, Hash(raw), raw.LongLength, layout.MipIndex));
                    rows[^1] = new(description, refs.ToArray());
                }
            }
            if (total != result.CanonicalBytes) throw new InvalidDataException("Canonical total differs from completed snapshot.");
            _ = GraphicsPostFrameJson.Measure(new { resources = rows, intent = result.Intent }, GraphicsPostFrameLimits.BodyBytes);
            var value = new Wire(1, "reference-post-final-color-resident-resources", true, metadata, result.OriginalFrameReceipt, result.CopyReceipt, Scope, rows.ToArray(), result.Intent);
            CheckEnvelope(metadata, result.OriginalFrameReceipt, result.CopyReceipt);
            return WriteNew(root, prefix + ".json", GraphicsPostFrameJson.Serialize(value, GraphicsPostFrameLimits.WireBytes));
        }
        catch (Exception original)
        {
            try { Retain(original, root, prefix + "-export", metadata, result.OriginalFrameReceipt, result.CopyReceipt, rows.ToArray(), result.Intent); }
            catch { }
            throw;
        }
    }
    private static void CheckEnvelope(object metadata, GraphicsPostFrameOriginalReceipt? original, GraphicsPostFrameCopyReceipt? copy)
        => _ = GraphicsPostFrameJson.Measure(new { metadata, originalFrameReceipt = original, copyReceipt = copy, coverage = Scope }, GraphicsPostFrameLimits.EnvelopeBytes);
    private static void Retain(Exception original, string root, string prefix, object metadata, GraphicsPostFrameOriginalReceipt? receipt,
        GraphicsPostFrameCopyReceipt? copy, ResourceRow[] rows, GraphicsPostFrameIntent? intent, object? failureDetails = null)
    {
        try
        {
            object context = new { context = metadata, failure = failureDetails ?? FailureFacts(original, "export-incomplete", copy?.MayHaveExecuted, []) };
            try { CheckEnvelope(context, receipt, copy); }
            catch { context = new { contextUnavailable = true, failure = failureDetails ?? FailureFacts(original, "export-incomplete", copy?.MayHaveExecuted, []) }; }
            _ = WriteNew(root, prefix + ".partial.json", GraphicsPostFrameJson.Serialize(
                new Wire(1, "reference-post-final-color-resident-resources", false, context, receipt, copy, Scope, rows, intent), GraphicsPostFrameLimits.WireBytes));
        }
        catch { }
    }
    internal static void RetainFailure(Exception original, string root, string key, GraphicsPostFrameReceipt? receipt, Metadata? metadata,
        string phase, string? imageFile, GraphicsPostFrameSnapshotResult? current)
    {
        try
        {
            var prefix = GraphicsPostFrameSnapshots.Failure(original, receipt);
            bool knownCurrent = receipt is not null && current is not null && ReferenceEquals(receipt.Original, current.OriginalFrameReceipt);
            object? image = null;
            if (imageFile is not null)
            {
                try
                {
                    string path = Path.Combine(root, imageFile);
                    if (File.Exists(path)) image = new { file = imageFile, sha256 = VisualReferenceCapture.FileHash(path), bytes = new FileInfo(path).Length,
                        meaning = "Retained file only; incomplete evidence does not assert valid PNG publication or a successful receipt." };
                }
                catch { }
            }
            var copy = prefix?.CopyReceipt ?? (knownCurrent ? current!.CopyReceipt : null);
            object context = new { context = metadata, image, captureReceiptAvailable = receipt is not null };
            Retain(original, root, key + "-post-frame-failure", context, receipt?.Original, copy, [], null,
                FailureFacts(original, prefix?.Phase ?? phase, prefix?.MayHaveExecuted ?? copy?.MayHaveExecuted,
                    prefix?.SecondaryCleanupErrors ?? (knownCurrent ? current!.SecondaryCleanupErrors : [])));
        }
        catch { }
    }
    internal static void CleanupAssets(Action submit, Action retire)
    {
        Exception? primary = null;
        try { submit(); } catch (Exception error) { primary = error; }
        try { retire(); }
        catch (Exception error)
        {
            if (primary is null) primary = error;
            else { try { primary.Data["postFrameRetireCleanup"] = error.ToString(); } catch { } }
        }
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
    }
    internal static Exception SelectPrimary(Exception caught, Exception? selected)
    {
        if (selected is null || ReferenceEquals(selected, caught)) return caught;
        return Remember(selected, caught);
    }
    internal static Exception Remember(Exception? primary, Exception error)
    {
        if (primary is null) return error;
        if (ReferenceEquals(primary, error)) return primary;
        try
        {
            for (int i = 0; i < 32; i++)
            {
                string key = "postFrameSecondary-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (ReferenceEquals(primary.Data[key], error)) break;
                if (!primary.Data.Contains(key)) { primary.Data[key] = error; break; }
            }
        }
        catch { }
        return primary;
    }
    private static string BoundedError(object? error)
    {
        try
        {
            string value = error?.ToString() ?? "unavailable";
            // At most4096 UTF8 bytes even for supplementary-plane/surrogate-rich text.
            return value.Length <= 1024 ? value : value[..1024] + " [truncated]";
        }
        catch { return "secondary-error-description-unavailable"; }
    }
    internal static object FailureFacts(Exception primary, string phase, bool? mayHaveExecuted, string[] nativeCleanupErrors)
    {
        var secondary = new List<string>();
        try
        {
            foreach (System.Collections.DictionaryEntry entry in primary.Data)
            {
                if (secondary.Count == 32) break;
                if (entry.Key is string key && (key.StartsWith("postFrame", StringComparison.Ordinal) || key.Contains("PostFrame", StringComparison.Ordinal)))
                    secondary.Add(BoundedError(entry.Value));
            }
        }
        catch { secondary.Add("secondary-enumeration-unavailable"); }
        foreach (string error in nativeCleanupErrors.Take(Math.Max(0, 64 - secondary.Count))) secondary.Add(BoundedError(error));
        return new { phase, mayHaveExecuted, primary = BoundedError(primary), secondary = secondary.ToArray(),
            capacity = 64, scope = "Bounded failure descriptions only; no successful snapshot, absent entries are not proof of no cleanup work." };
    }
    internal static void RetainReportingFailure(Exception primary, string root)
    {
        try { _ = WriteNew(root, "post-frame-error-reporting.partial.json", GraphicsPostFrameJson.Serialize(
            new { schemaVersion = 1, complete = false, failure = FailureFacts(primary, "outer-reporting-failure", null, []) }, GraphicsPostFrameLimits.EnvelopeBytes)); }
        catch { }
    }
}
