using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ForgeLine.Graphics;

namespace ForgeLine.Client;

// Producer-generated early checkpoint. The command remains alive until its exact
// process/device session is queried by the external launcher; no GPU work is submitted.
internal sealed record ReferenceAdapterCheckpoint(object Evidence)
{
    internal static ReferenceAdapterCheckpoint? PublishAndWait(string? directory, IGraphicsDevice graphics,
        string runtimeFingerprint, long nativeWindowHandle, Func<bool>? continueWaiting = null)
    {
        if (directory is null) return null;
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidDataException("Adapter handshake directory cannot be empty.");
        var source = graphics as IGraphicsRenderTelemetry ?? throw new NotSupportedException("Actual adapter identity unavailable.");
        var identity = source.DeviceIdentity ?? throw new NotSupportedException("Actual adapter identity unavailable.");
        if (Directory.Exists(directory)) throw new IOException("Adapter checkpoint directory must be new; old sessions cannot be replayed.");
        Directory.CreateDirectory(directory);
        var checkpoint = new { schemaVersion = 1, kind = "actual-renderer-identity-checkpoint", runId = Guid.NewGuid().ToString("N"),
            sourceSha = ClientBuildIdentity.SourceRevision, runtimeFingerprint, adapterIdentity = identity,
            nativeWindowHandle, createdAtUtc = DateTimeOffset.UtcNow, clientAssemblySha256 = VisualReferenceCapture.FileHash(typeof(Program).Assembly.Location) };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint, VisualReferenceCapture.JsonOptions);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string pending = Path.Combine(directory, "checkpoint.pending");
        using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None)) file.Write(bytes);
        File.Move(pending, Path.Combine(directory, "checkpoint.json"));
        var elapsed = Stopwatch.StartNew();
        string answer = Path.Combine(directory, "binding.json");
        while (!File.Exists(answer))
        {
            if (continueWaiting?.Invoke() == false) throw new OperationCanceledException("Adapter identity handshake cancelled.");
            if (elapsed.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Declared adapter-identity handshake deadline exceeded.");
            Thread.Sleep(10);
        }
        byte[] bindingBytes = File.ReadAllBytes(answer);
        JsonObject binding = JsonNode.Parse(bindingBytes) as JsonObject ?? throw new InvalidDataException("Invalid adapter binding.");
        if (binding["exactPhysicalMappingAvailable"]?.GetValue<bool>() != true ||
            !string.Equals(binding["nativeManifestSha256"]?.GetValue<string>(), hash, StringComparison.OrdinalIgnoreCase) ||
            !JsonNode.DeepEquals(binding["rendererIdentity"], JsonSerializer.SerializeToNode(identity, VisualReferenceCapture.JsonOptions)))
            throw new InvalidDataException("Adapter proof does not bind this actual producer checkpoint/session.");
        return new(new { checkpoint, checkpointSha256 = hash, binding, bindingSha256 = Convert.ToHexString(SHA256.HashData(bindingBytes)),
            waitMilliseconds = elapsed.Elapsed.TotalMilliseconds, scope = "Pre-window query-only identity handshake; not frame/GPU timing" });
    }
}
