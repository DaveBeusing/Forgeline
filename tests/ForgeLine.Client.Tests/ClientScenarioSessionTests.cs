using ForgeLine.Game;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ClientScenarioSessionTests
{
    [Fact]
    public void OwnedNewLoadAndRestartUseTheSelectedResolverAndIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var composition = CentralDivideScenario.CreateComposition() with { Key = "test.selected-scenario.v1" };
            int resolutions = 0;
            MatchComposition Resolve(string key) { Assert.Equal(composition.Key, key); resolutions++; return composition; }
            using var first = ClientSessionFactory.CreateOwned(ClientSessionRequest.NewGame(731, composition.Key), _ => { }, TestContext.Current.CancellationToken, Resolve);
            Assert.Equal(composition.Identity, first.Runtime.RuntimeSettings.Composition.Identity);
            first.Runtime.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
            string before = MatchAuthoritativeSnapshot.Capture(first.Runtime).ComputeSha256();
            string path = Path.Combine(root, "selected.save.json");
            MatchPersistenceSerializer.WriteSaveCancellable(path, MatchPersistenceService.CaptureSave(first.Runtime), TestContext.Current.CancellationToken);
            using var restored = ClientSessionFactory.CreateOwned(ClientSessionRequest.Load(new("selected", "SELECTED", path, 2, LoadGameEntryState.Available)), _ => { }, TestContext.Current.CancellationToken, Resolve);
            Assert.Equal(composition.Key, restored.Runtime.RuntimeSettings.Composition.Key);
            Assert.Equal(before, MatchAuthoritativeSnapshot.Capture(restored.Runtime).ComputeSha256());
            var restart = ClientSessionRequest.Restart(restored.Runtime.RuntimeSettings);
            using var restarted = ClientSessionFactory.CreateOwned(restart, _ => { }, TestContext.Current.CancellationToken, Resolve);
            Assert.Equal(composition.Identity, restarted.Runtime.RuntimeSettings.Composition.Identity);
            Assert.Equal(731UL, restarted.Runtime.RuntimeSettings.Seed);
            Assert.Equal(0UL, restarted.Runtime.Simulation.CurrentTick.Value);
            Assert.Equal(3, resolutions);
            Assert.Equal(before, MatchAuthoritativeSnapshot.Capture(first.Runtime).ComputeSha256());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RegisteredSplitBasinStartsLoadsAndRestartsWithoutCustomResolver()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-split-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var first = ClientSessionFactory.CreateOwned(ClientSessionRequest.NewGame(731, SplitBasinBattlefield.CompositionKey), _ => { }, TestContext.Current.CancellationToken);
            Assert.Equal(ScenarioRegistry.SplitBasinDescriptor.Identity, first.Runtime.RuntimeSettings.Composition.Identity);
            first.Runtime.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
            string path = Path.Combine(root, "split.save.json");
            var save = MatchPersistenceService.CaptureSave(first.Runtime);
            MatchPersistenceSerializer.WriteSaveCancellable(path, save, TestContext.Current.CancellationToken);
            using var loaded = ClientSessionFactory.CreateOwned(ClientSessionRequest.Load(new("split", "SPLIT BASIN", path, 2, LoadGameEntryState.Available)), _ => { }, TestContext.Current.CancellationToken);
            Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(loaded.Runtime).ComputeSha256());
            using var restarted = ClientSessionFactory.CreateOwned(ClientSessionRequest.Restart(loaded.Runtime.RuntimeSettings), _ => { }, TestContext.Current.CancellationToken);
            Assert.Equal(ScenarioRegistry.SplitBasinDescriptor.Identity, restarted.Runtime.RuntimeSettings.Composition.Identity);
            Assert.Equal(0UL, restarted.Runtime.Simulation.CurrentTick.Value);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void OwnedNewAndRestartRejectTheWrongResolvedIdentity()
    {
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 9);
        var wrong = settings.Composition with { Identity = settings.Composition.Identity! with { MapRevision = settings.Composition.Identity.MapRevision + 1 } };
        Assert.Throws<InvalidOperationException>(() => ClientSessionFactory.CreateOwned(
            ClientSessionRequest.NewGame(settings), _ => { }, TestContext.Current.CancellationToken, _ => wrong));
        Assert.Throws<InvalidOperationException>(() => ClientSessionFactory.CreateOwned(
            ClientSessionRequest.Restart(settings), _ => { }, TestContext.Current.CancellationToken, _ => wrong));
    }

    [Fact]
    public void SetupShowsNamedChoicesAndDisablesUnavailableSelection()
    {
        NewGameScenarioOption[] choices = [new("ready", "Central Divide", true), new("missing", "Unavailable map", false, "Compiled map is missing.")];
        var model = new NewGameModel(choices);
        choices[0] = choices[0] with { Available = false };
        Assert.True(model.CanStartSelectedScenario);
        model.MoveScenario(1);
        Assert.False(model.CanStartSelectedScenario);
        Assert.Equal("missing", model.SelectedScenario.Key);
        Assert.Equal("Unavailable map", model.Configuration.MapName);
        Assert.Contains("missing", model.SelectedScenario.Error!);
        var view = FrontendPresentationAdapter.NewGame(model);
        Assert.Equal("START", view.DisabledPrimaryAction);
        model.RefreshScenarios([new("ready", "Central Divide", true), new("missing", "Unavailable map", true)]);
        Assert.Equal("missing", model.SelectedScenario.Key);
        Assert.True(model.CanStartSelectedScenario);
    }
}
