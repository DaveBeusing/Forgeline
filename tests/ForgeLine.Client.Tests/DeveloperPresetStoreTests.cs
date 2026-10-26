#if FORGELINE_DEVELOPER_TOOLS
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class DeveloperPresetStoreTests
{
    private static DeveloperPresetDocument CreateDocument()
    {
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 77);
        return DeveloperPresetCodec.Create("store-test", "Store test", DeveloperRestartBuilder.ForExisting(settings).InitialOverrides);
    }

    [Fact]
    public void ImportAndExportAreBoundedAndDuplicateKeysDoNotReplaceFiles()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "preset-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var document = CreateDocument();
            var store = new DeveloperPresetStore(root: root);
            byte[] canonical = DeveloperPresetCodec.Serialize(document);
            store.Import(canonical);
            byte[] before = File.ReadAllBytes(Path.Combine(root, "store-test.json"));

            Assert.Throws<IOException>(() => store.Import(canonical));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "store-test.json")));
            Assert.Equal(document.Key, store.Load(document.Key).Key);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidReplacementLeavesPreviouslyExportedPresetIntact()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "preset-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var document = CreateDocument();
            new DeveloperPresetStore(root: root).Export(document);
            string path = Path.Combine(root, "store-test.json");
            byte[] before = File.ReadAllBytes(path);

            var incompatibleStore = new DeveloperPresetStore(_ => throw new InvalidOperationException("Scenario unavailable."), root);
            Assert.Throws<DeveloperPresetException>(() => incompatibleStore.Export(document));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ImportDoesNotCreateCapabilityAndPresetRequestBuildsAnOwnedNewSession()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "preset-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var capability = DeveloperToolsCapability.Current;
            var document = CreateDocument();
            var store = new DeveloperPresetStore(root: root);
            store.Import(DeveloperPresetCodec.Serialize(document));
            Assert.Same(capability, DeveloperToolsCapability.Current);

            var adapter = new ClientDeveloperPresetSessionAdapter(store);
            ClientSessionRequest request = adapter.CreatePresetRequest(document.Key);
            using ClientLoadedSession original = ClientSessionFactory.CreateOwned(
                ClientSessionRequest.NewGame(13), _ => { }, CancellationToken.None);
            using ClientLoadedSession restarted = ClientSessionFactory.CreateOwned(request, _ => { }, CancellationToken.None);

            Assert.NotEqual(original.Runtime.Simulation.SessionId, restarted.Runtime.Simulation.SessionId);
            Assert.Equal(document.Profile.Metadata.Seed, restarted.Runtime.RuntimeSettings.Seed);
            Assert.Equal(document.Profile.Metadata.BaseIdentity, restarted.Runtime.RuntimeSettings.Composition.Identity);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
#endif
