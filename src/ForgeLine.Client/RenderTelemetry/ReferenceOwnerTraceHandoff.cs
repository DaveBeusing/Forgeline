using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace ForgeLine.Client;

// Holds the actual producer alive only after its full measured window. The outer
// host command retains its original ten-minute deadline and owner-failure checks.
internal sealed record ReferenceOwnerTraceHandoff(string Directory, string CheckpointSha256, string RunId, int ProcessId, string ReadySha256, long ReadyStartedTick)
{
    internal static ReferenceOwnerTraceHandoff PublishReady(string directory, string checkpointDirectory, string recipeFingerprint)
    {
        byte[] checkpointBytes = File.ReadAllBytes(Path.Combine(checkpointDirectory, "checkpoint.json"));
        var checkpoint = JsonNode.Parse(checkpointBytes)!.AsObject();
        string hash = Convert.ToHexString(SHA256.HashData(checkpointBytes));
        string run = checkpoint["runId"]!.GetValue<string>();
        int pid = checkpoint["adapterIdentity"]!["processId"]!.GetValue<int>();
        if (pid != Environment.ProcessId) throw new InvalidDataException("Trace handoff PID differs from the actual producer.");
        string control = Path.Combine(directory, "trace-control.json");
        if (!File.Exists(control) || new FileInfo(control).Length > 65536) throw new InvalidDataException("Actual supervisor trace control is missing/unbounded.");
        var input = JsonNode.Parse(File.ReadAllBytes(control))!.AsObject();
        if (input["runId"]?.GetValue<string>() != run || input["processId"]?.GetValue<int>() != pid ||
            !string.Equals(input["checkpointSha256"]?.GetValue<string>(), hash, StringComparison.OrdinalIgnoreCase) ||
            input["sourceSha"]?.GetValue<string>() != ClientBuildIdentity.SourceRevision)
            throw new InvalidDataException("Trace control belongs to another actual checkpoint/source/PID.");
        long readyStarted = Stopwatch.GetTimestamp();
        string path = Path.Combine(directory, "trace-ready.json");
        string pending = Path.Combine(directory, "trace-ready.pending");
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, new { schemaVersion = 1, kind = "actual-owner-trace-ready", qualified = false,
                sourceSha = ClientBuildIdentity.SourceRevision, checkpointSha256 = hash, runId = run, processId = pid,
                recipeFingerprint, createdAtUtc = DateTimeOffset.UtcNow,
                ownerThread = checkpoint["ownerThread"]?.DeepClone(),
                scope = "After existing renderer/fixture initialization, before the first warmup; no sample-window timing" }, VisualReferenceCapture.JsonOptions);
        File.Move(pending, path);
        return new(directory, hash, run, pid, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), readyStarted);
    }
    internal bool Attached()
    {
        if (Stopwatch.GetElapsedTime(ReadyStartedTick) > TimeSpan.FromSeconds(10))
            throw new TimeoutException("Declared ten-second initialized-owner attach readiness exceeded; original command bound remains unchanged.");
        string path = Path.Combine(Directory, "trace-attached.json");
        if (!File.Exists(path)) return false;
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Attach acknowledgement exceeds bound.");
        var ack = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        if (ack["sessionEstablished"]?.GetValue<bool>() != true || ack["runId"]?.GetValue<string>() != RunId ||
            ack["processId"]?.GetValue<int>() != ProcessId ||
            !string.Equals(ack["readySha256"]?.GetValue<string>(), ReadySha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Trace attach acknowledgement does not bind the actual initialized producer.");
        return true;
    }
    internal void PublishWindowComplete()
    {
        string pending = Path.Combine(Directory, "window-complete.pending");
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, new { schemaVersion = 1, kind = "actual-owner-trace-window-complete", qualified = false,
                sourceSha = ClientBuildIdentity.SourceRevision, checkpointSha256 = CheckpointSha256, runId = RunId,
                processId = ProcessId, completedAtUtc = DateTimeOffset.UtcNow,
                scope = "After all contiguous ACKs, outside measurement; producer remains alive until trace completion" }, VisualReferenceCapture.JsonOptions);
        File.Move(pending, Path.Combine(Directory, "window-complete.json"));
    }
    internal bool Released()
    {
        string path = Path.Combine(Directory, "trace-release.json");
        if (!File.Exists(path)) return false;
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Trace release exceeds declared bound.");
        var release = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        if (release["completed"]?.GetValue<bool>() != true || release["runId"]?.GetValue<string>() != RunId ||
            release["processId"]?.GetValue<int>() != ProcessId ||
            !string.Equals(release["checkpointSha256"]?.GetValue<string>(), CheckpointSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Trace release is incomplete or belongs to another actual run.");
        string? digest = release["traceSha256"]?.GetValue<string>();
        if (digest is null || digest.Length != 64 || digest.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Actual completed trace digest is unavailable.");
        return true;
    }
}
