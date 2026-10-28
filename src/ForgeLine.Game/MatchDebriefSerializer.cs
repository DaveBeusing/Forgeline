using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeLine.Game;

public enum MatchDebriefReadFailure : byte { None, Missing, TooLarge, UnsupportedVersion, ForeignPlayer, InvalidData, StorageUnavailable }

/// <summary>One player report. No raw telemetry, entity IDs, positions or arbitrary debug properties.</summary>
public static class MatchDebriefSerializer
{
    public const int MaximumBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Serialize(MatchDebriefSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(summary, Options);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Debrief exceeds its byte limit.");
        return bytes;
    }

    public static MatchDebriefSummary Deserialize(ReadOnlyMemory<byte> bytes, ulong expectedPlayer)
    {
        if (bytes.Length is <= 0 or > MaximumBytes) throw new InvalidDataException("Invalid debrief byte count.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(nameof(MatchDebriefSummary.SchemaVersion), out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int schema) || schema != MatchDebriefSummary.CurrentSchemaVersion)
            throw new MatchDebriefFormatException(MatchDebriefReadFailure.UnsupportedVersion);
        if (!root.TryGetProperty(nameof(MatchDebriefSummary.Player), out var player) || player.ValueKind != JsonValueKind.Number || !player.TryGetUInt64(out ulong owner) || owner == 0 || owner != expectedPlayer)
            throw new MatchDebriefFormatException(MatchDebriefReadFailure.ForeignPlayer);
        if (!root.TryGetProperty(nameof(MatchDebriefSummary.Events), out var entries) || entries.ValueKind != JsonValueKind.Array ||
            entries.GetArrayLength() > MatchDebriefSummary.MaximumEvents)
            throw new InvalidDataException("Invalid debrief event count.");
        return JsonSerializer.Deserialize<MatchDebriefSummary>(bytes.Span, Options) ?? throw new InvalidDataException("Missing debrief payload.");
    }

    public static void Write(string path, MatchDebriefSummary summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = Serialize(summary);
        string destination = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".writing-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(flushToDisk: true); }
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static bool TryRead(string path, ulong expectedPlayer, out MatchDebriefSummary? summary, out MatchDebriefReadFailure failure)
    {
        summary = null;
        try
        {
            if (!File.Exists(path)) { failure = MatchDebriefReadFailure.Missing; return false; }
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaximumBytes) { failure = MatchDebriefReadFailure.TooLarge; return false; }
            byte[] bytes = new byte[checked((int)file.Length)];
            file.ReadExactly(bytes);
            summary = Deserialize(bytes, expectedPlayer);
            failure = MatchDebriefReadFailure.None; return true;
        }
        catch (MatchDebriefFormatException error) { failure = error.Failure; }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException)
        { failure = MatchDebriefReadFailure.InvalidData; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { failure = MatchDebriefReadFailure.StorageUnavailable; }
        return false;
    }

    private sealed class MatchDebriefFormatException(MatchDebriefReadFailure failure) : IOException("Unsupported or foreign player debrief.")
    { public MatchDebriefReadFailure Failure { get; } = failure; }
}
