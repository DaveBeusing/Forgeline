using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class PersistenceBoundaryTests
{
    [Fact]
    public void PreCleanupAuthoritativeSchemaIsExplicitlyIncompatible()
    {
        using var runtime = CreateRuntime();
        MatchSaveData save = MatchPersistenceService.CaptureSave(runtime);
        MatchAuthoritativeSnapshot oldState = save.State with { SchemaVersion = 8 };
        var oldSave = save with { State = oldState, StateSha256 = oldState.ComputeSha256() };
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceSerializer.DeserializeSave(
                MatchPersistenceSerializer.SerializeSave(oldSave))).Reason);
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(oldSave)).Reason);
    }

    [Fact]
    public void NonFiniteStartingStockIsRejectedBeforeReconstruction()
    {
        using var runtime = CreateRuntime();
        var save = MatchPersistenceService.CaptureSave(runtime);
        var invalid = save with { Configuration = save.Configuration with
        {
            Scenario = save.Configuration.Scenario with
            {
                StartingStock = save.Configuration.Scenario.StartingStock with { Fuel = double.NaN }
            }
        } };
        Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(invalid)).Reason);
    }

    [Fact]
    public void Utf8BomRemainsReadableButInvalidUtf8IsCorrupt()
    {
        using var runtime = CreateRuntime();
        var save = MatchPersistenceService.CaptureSave(runtime);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".save.json");
        try
        {
            File.WriteAllText(path, MatchPersistenceSerializer.SerializeSave(save), new UTF8Encoding(true));
            Assert.Equal(save.StateSha256, MatchPersistenceSerializer.ReadSave(path).StateSha256);
            File.WriteAllBytes(path, [0xff, 0xfe, 0xff]);
            Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
                Assert.Throws<MatchPersistenceException>(() => MatchPersistenceSerializer.ReadSave(path)).Reason);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\n\t")]
    public void EmptyDocumentUsesPersistenceFailure(string document)
    {
        Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceSerializer.DeserializeSave(document)).Reason);
        Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceSerializer.DeserializeReplay(document)).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteEscapedUtf8DocumentUsesOneReadWriteBudget(bool replay)
    {
        using var runtime = CreateRuntime();
        var save = MatchPersistenceService.CaptureSave(runtime);
        // Composition identity is opaque during preflight and exercises escaping.
        save = save with { Configuration = save.Configuration with { CompositionKey = "Ä工\\\"\n" } };
        var recording = MatchPersistenceService.CaptureReplay(runtime) with { Configuration = save.Configuration };
        string document = replay ? MatchPersistenceSerializer.SerializeReplay(recording) : MatchPersistenceSerializer.SerializeSave(save);
        long budget = Encoding.UTF8.GetByteCount(document);
        Assert.Contains("\\u", document, StringComparison.Ordinal);
        Assert.Equal(document, replay ? MatchPersistenceSerializer.SerializeReplay(recording, budget) :
            MatchPersistenceSerializer.SerializeSave(save, budget));
        if (replay) MatchPersistenceSerializer.DeserializeReplay(document, budget);
        else MatchPersistenceSerializer.DeserializeSave(document, budget);
        Assert.Equal(MatchPersistenceFailureReason.DocumentTooLarge,
            Assert.Throws<MatchPersistenceException>(() =>
            {
                if (replay) MatchPersistenceSerializer.SerializeReplay(recording, budget - 1);
                else MatchPersistenceSerializer.SerializeSave(save, budget - 1);
            }).Reason);
        Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
            Assert.Throws<MatchPersistenceException>(() =>
            {
                if (replay) MatchPersistenceSerializer.DeserializeReplay(document, budget - 1);
                else MatchPersistenceSerializer.DeserializeSave(document, budget - 1);
            }).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedWritePreservesLastReadableFileAndCleansTemporaryFiles(bool replay)
    {
        using var runtime = CreateRuntime();
        var save = MatchPersistenceService.CaptureSave(runtime);
        var recording = MatchPersistenceService.CaptureReplay(runtime);
        string directory = Path.Combine(Path.GetTempPath(), "ForgeLine.Write.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "match.json");
        try
        {
            if (replay) MatchPersistenceSerializer.WriteReplayCancellable(path, recording, TestContext.Current.CancellationToken);
            else MatchPersistenceSerializer.WriteSaveCancellable(path, save, TestContext.Current.CancellationToken);
            byte[] original = File.ReadAllBytes(path);
            Assert.Equal(MatchPersistenceFailureReason.DocumentTooLarge,
                Assert.Throws<MatchPersistenceException>(() =>
                {
                    if (replay) MatchPersistenceSerializer.WriteReplayWithBudget(path, recording, original.Length - 1, TestContext.Current.CancellationToken);
                    else MatchPersistenceSerializer.WriteSaveWithBudget(path, save, original.Length - 1, TestContext.Current.CancellationToken);
                }).Reason);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
            {
                if (replay) MatchPersistenceSerializer.WriteReplayCancellable(path, recording, cancelled.Token);
                else MatchPersistenceSerializer.WriteSaveCancellable(path, save, cancelled.Token);
            });
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Exception? writeError = Record.Exception(() =>
                {
                    if (replay) MatchPersistenceSerializer.WriteReplayCancellable(path, recording, TestContext.Current.CancellationToken);
                    else MatchPersistenceSerializer.WriteSaveCancellable(path, save, TestContext.Current.CancellationToken);
                });
                Assert.True(writeError is IOException or UnauthorizedAccessException, writeError?.ToString() ?? "Locked replacement succeeded.");
            }
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory));
            if (replay) Assert.Equal(recording.FinalStateSha256, MatchPersistenceSerializer.ReadReplay(path).FinalStateSha256);
            else Assert.Equal(save.StateSha256, MatchPersistenceSerializer.ReadSave(path).StateSha256);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":\"six\"}")]
    [InlineData("{\"SchemaVersion\":6,\"Configuration\":[]}")]
    public void MalformedChecksummedPayloadUsesPersistenceFailure(string payload)
    {
        foreach (var kind in new[] { MatchPersistenceDocumentKind.Save, MatchPersistenceDocumentKind.Replay })
            Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
                Assert.Throws<MatchPersistenceException>(() => Read(Envelope(payload, kind), kind)).Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingEnvelopePayloadUsesPersistenceFailure(string? payload)
    {
        string document = JsonSerializer.Serialize(new { Magic = MatchPersistenceSerializer.Magic,
            FormatVersion = 1, Kind = 1, Payload = payload, PayloadSha256 = new string('0', 64) });
        Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceSerializer.DeserializeSave(document)).Reason);
    }

    [Theory]
    [InlineData("Configuration")]
    [InlineData("Configuration.Participants")]
    [InlineData("Configuration.Scenario")]
    [InlineData("Configuration.Scenario.OpponentConfigurations")]
    [InlineData("Commands")]
    [InlineData("State")]
    [InlineData("State.Entities")]
    [InlineData("State.Entities.AliveEntities")]
    [InlineData("State.Entities.ComponentStores")]
    [InlineData("State.Inventories")]
    [InlineData("State.Inventories.Inventories")]
    [InlineData("State.Domains")]
    [InlineData("StateSha256")]
    public void RequiredPayloadMembersCannotBeNull(string member)
    {
        using var runtime = CreateRuntime();
        string document = MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(runtime));
        string invalid = Mutate(document, root =>
        {
            string[] path = member.Split('.');
            JsonNode parent = root;
            foreach (string segment in path[..^1]) parent = parent[segment]!;
            parent[path[^1]] = null;
        });
        Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceSerializer.DeserializeSave(invalid)).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullCollectionItemsAndNonFiniteCommandsFailBeforeRuntimeCreation(bool replay)
    {
        using var runtime = CreateRuntime();
        string document = replay ? MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(runtime)) :
            MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(runtime));
        var kind = replay ? MatchPersistenceDocumentKind.Replay : MatchPersistenceDocumentKind.Save;
        var root = JsonNode.Parse(JsonNode.Parse(document)!["Payload"]!.GetValue<string>())!.AsObject();
        root["Commands"] = new JsonArray((JsonNode?)null);
        Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
            Assert.Throws<MatchPersistenceException>(() => Read(Envelope(root.ToJsonString(), kind), kind)).Reason);
        root["Commands"] = JsonNode.Parse("[{\"Kind\":1,\"TargetTick\":1,\"Sequence\":1,\"Source\":1,\"RecordingOrder\":1,\"Position\":{\"X\":\"NaN\",\"Y\":0,\"Z\":0}}]");
        Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
            Assert.Throws<MatchPersistenceException>(() => Read(Envelope(root.ToJsonString(), kind), kind)).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedSchemaIsRejectedBeforeRuntimeCreation(bool stateSchema)
    {
        using var runtime = CreateRuntime();
        string document = MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(runtime));
        string invalid = Mutate(document, root =>
        {
            if (stateSchema) root["State"]!["SchemaVersion"] = 999;
            else root["SchemaVersion"] = 999;
        });
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceSerializer.DeserializeSave(invalid)).Reason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8192)]
    [InlineData(MatchPersistenceValidation.MaximumRecoveryEntityCapacity)]
    public void SupportedRecoveryCapacitySurvivesPreflightWithoutAllocation(int capacity)
    {
        using var runtime = CreateRuntime();
        var save = MatchPersistenceService.CaptureSave(runtime);
        var configuration = save.Configuration with { InitialEntityCapacity = capacity };
        var recording = MatchPersistenceService.CaptureReplay(runtime) with { Configuration = configuration };
        Assert.Equal(capacity, MatchPersistenceSerializer.DeserializeSave(
            MatchPersistenceSerializer.SerializeSave(save with { Configuration = configuration })).Configuration.InitialEntityCapacity);
        Assert.Equal(capacity, MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(recording)).Configuration.InitialEntityCapacity);
    }

    [Theory]
    [InlineData(MatchPersistenceValidation.MaximumRecoveryEntityCapacity + 1)]
    [InlineData(int.MaxValue)]
    public void ExcessiveRecoveryCapacityFailsBeforeCompositionResolution(int capacity)
    {
        using var runtime = CreateRuntime();
        var save = MatchPersistenceService.CaptureSave(runtime);
        var configuration = save.Configuration with { InitialEntityCapacity = capacity };
        var invalid = save with { Configuration = configuration };
        var recording = MatchPersistenceService.CaptureReplay(runtime) with { Configuration = configuration };
        int resolutions = 0;
        MatchComposition Resolve(string key) { resolutions++; throw new InvalidOperationException("Must not resolve content."); }
        Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(invalid, Resolve)).Reason);
        Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplay(recording, Resolve)).Reason);
        foreach (var kind in new[] { MatchPersistenceDocumentKind.Save, MatchPersistenceDocumentKind.Replay })
        {
            string document = kind == MatchPersistenceDocumentKind.Save
                ? MatchPersistenceSerializer.SerializeSave(save)
                : MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(runtime));
            var payload = JsonNode.Parse(JsonNode.Parse(document)!["Payload"]!.GetValue<string>())!.AsObject();
            payload["Configuration"]!["InitialEntityCapacity"] = capacity;
            Assert.Equal(MatchPersistenceFailureReason.CorruptDocument,
                Assert.Throws<MatchPersistenceException>(() => Read(Envelope(payload.ToJsonString(), kind), kind)).Reason);
        }
        Assert.Equal(0, resolutions);
        Assert.Equal(save.StateSha256, MatchPersistenceService.CaptureSave(runtime).StateSha256);
    }

    private static MatchRuntime CreateRuntime() => MatchRuntime.Create(
        CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, seed: 4242), TestContext.Current.CancellationToken);

    private static void Read(string document, MatchPersistenceDocumentKind kind)
    {
        if (kind == MatchPersistenceDocumentKind.Save) MatchPersistenceSerializer.DeserializeSave(document);
        else MatchPersistenceSerializer.DeserializeReplay(document);
    }

    private static string Envelope(string payload, MatchPersistenceDocumentKind kind) => JsonSerializer.Serialize(new
    {
        Magic = MatchPersistenceSerializer.Magic, FormatVersion = 1, Kind = kind, Payload = payload,
        PayloadSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
    });

    private static string Mutate(string document, Action<JsonObject> mutation)
    {
        var envelope = JsonNode.Parse(document)!.AsObject();
        var root = JsonNode.Parse(envelope["Payload"]!.GetValue<string>())!.AsObject();
        mutation(root);
        return Envelope(root.ToJsonString(), MatchPersistenceDocumentKind.Save);
    }
}
