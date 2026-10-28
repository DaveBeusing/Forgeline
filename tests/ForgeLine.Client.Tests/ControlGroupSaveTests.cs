using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ControlGroupSaveTests
{
    [Fact]
    public void LoadedSessionTransfersGroupsExactlyOnceWithoutChangingSimulation()
    {
        using var original = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 7302)
            with { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var player = new PlayerId(1);
        var members = original.GetBase(player).StartingUnits;
        var registry = new CombatGroupRegistry();
        registry.Synchronize(original.Simulation.SessionId, members);
        registry.Assign(3, members, members);
        registry.Rename(3, "COMBINED ARMS");
        original.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        var save = MatchPersistenceService.CaptureSave(original) with { ControlGroups = registry.Capture(player) };
        var file = Path.Combine(Path.GetTempPath(), "forgeline-control-group-" + Guid.NewGuid().ToString("N") + ".save.json");
        try
        {
            MatchPersistenceSerializer.WriteSave(file, save);
            using var loaded = ClientSaveCatalog.Restore(new("groups", "GROUPS", file, save.SavedTick, LoadGameEntryState.Available));
            Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(loaded).ComputeSha256());
            var transferred = Assert.IsType<PlayerControlGroupSaveData>(ClientSaveCatalog.TakeControlGroups(loaded));
            Assert.Null(ClientSaveCatalog.TakeControlGroups(loaded));
            var restored = new CombatGroupRegistry();
            Assert.True(restored.Restore(loaded.Simulation.SessionId, player, transferred, loaded.GetBase(player).StartingUnits));
            Assert.Equal("COMBINED ARMS", restored.GetSlot(3).Label);
            Assert.Equal(members, restored.GetValidMembers(3));
            Assert.Equal(3, restored.ActiveSlot);
        }
        finally { File.Delete(file); }
    }
}
