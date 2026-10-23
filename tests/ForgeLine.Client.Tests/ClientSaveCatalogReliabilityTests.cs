using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ForgeLine.Game;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientSaveCatalogReliabilityTests
{
    [Fact]
    public void BrokenAndIncompatibleEntriesNeverHideAGoodSave()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ForgeLine.Catalog.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(
                MatchScenarioProfile.Validation, seed: 4242), TestContext.Current.CancellationToken);
            var save = MatchPersistenceService.CaptureSave(runtime);
            string valid = MatchPersistenceSerializer.SerializeSave(save);
            var cases = new (string Name, string Content, LoadGameEntryState State)[]
            {
                ("valid", valid, LoadGameEntryState.Available),
                ("null", ReplacePayload(valid, null), LoadGameEntryState.Corrupt),
                ("array", ReplacePayload(valid, "[]"), LoadGameEntryState.Corrupt),
                ("wrong-type", ReplacePayload(valid, "{\"SchemaVersion\":\"six\"}"), LoadGameEntryState.Corrupt),
                ("null-commands", ReplacePayload(valid, Payload(valid, p => p["Commands"] = null)), LoadGameEntryState.Corrupt),
                ("schema", MatchPersistenceSerializer.SerializeSave(save with { SchemaVersion = 999 }), LoadGameEntryState.Incompatible),
                ("state-schema", MatchPersistenceSerializer.SerializeSave(save with { State = save.State with { SchemaVersion = 5 } }), LoadGameEntryState.Incompatible),
                ("checksum", valid.Replace(save.StateSha256, new string('0', 64), StringComparison.Ordinal), LoadGameEntryState.Corrupt)
            };
            foreach (var item in cases) File.WriteAllText(Path.Combine(directory, item.Name + ".save.json"), item.Content);
            var original = cases.Select(item => File.ReadAllBytes(Path.Combine(directory, item.Name + ".save.json"))).ToArray();
            var entries = ClientSaveCatalog.Discover(directory, TestContext.Current.CancellationToken);
            Assert.Equal(cases.Length, entries.Count);
            for (int index = 0; index < cases.Length; index++)
            {
                var item = cases[index];
                var entry = Assert.Single(entries, entry => entry.Id == item.Name + ".save.json");
                Assert.Equal(item.State, entry.State);
                Assert.Equal(item.State == LoadGameEntryState.Available, entry.CanLoad);
                Assert.Equal(original[index], File.ReadAllBytes(entry.Path));
            }
            using var restored = ClientSaveCatalog.Restore(Assert.Single(entries, entry => entry.CanLoad));
            Assert.NotEqual(runtime.Simulation.SessionId, restored.Simulation.SessionId);
            Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string Payload(string document, Action<JsonObject> change)
    {
        var payload = JsonNode.Parse(JsonNode.Parse(document)!["Payload"]!.GetValue<string>())!.AsObject();
        change(payload);
        return payload.ToJsonString();
    }

    private static string ReplacePayload(string document, string? payload)
    {
        var envelope = JsonNode.Parse(document)!;
        envelope["Payload"] = payload;
        envelope["PayloadSha256"] = payload is null ? new string('0', 64) :
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return envelope.ToJsonString();
    }
}
