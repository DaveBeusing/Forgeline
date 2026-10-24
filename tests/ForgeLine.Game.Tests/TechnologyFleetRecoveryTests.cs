using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TechnologyFleetRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalOpponentRestoresCriticalFleetBeforeNewResearch(bool supplyLost)
    {
        using var runtime = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(
            MatchScenarioProfile.Gameplay, 2026) with
        {
            EnableDebugCapture = true,
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, true)
        }, TestContext.Current.CancellationToken);
        var player = new PlayerId(2);
        var entities = runtime.Simulation.Entities;
        Assert.True(runtime.RunUntil(SupplyEstablished, 12_000, TestContext.Current.CancellationToken));
        Assert.False(TechnologyStateQueries.TryGetActiveResearch(entities, player, out _, out _));
        UnitId lost = supplyLost ? UnitIds.SupplyTruck : UnitIds.CargoTruck;
        var trucks = new List<EntityId>();
        foreach (EntityId entity in entities.Query<UnitIdentity, ControllableEntity>())
            if (entities.GetComponent<ControllableEntity>(entity).Owner == player &&
                entities.GetComponent<UnitIdentity>(entity).UnitId == lost) trucks.Add(entity);
        Assert.NotEmpty(trucks);
        foreach (EntityId truck in trucks) Assert.True(entities.DestroyEntity(truck));
        runtime.Simulation.RunTicks(21, TestContext.Current.CancellationToken);
        Assert.Equal(SkirmishResearchReason.Priority,
            runtime.Opponents.DebugSnapshot.Single(entry => entry.Player == player).Research.Reason);
        Assert.False(TechnologyStateQueries.TryGetActiveResearch(entities, player, out _, out _));
        bool replacementQueued = false;
        foreach (EntityId entity in entities.Query<UnitProductionRequest>())
        {
            var request = entities.GetComponent<UnitProductionRequest>(entity);
            replacementQueued |= request.UnitId == lost &&
                                 entities.GetComponent<CompletedBuilding>(request.Facility).Owner == player;
        }
        Assert.True(replacementQueued);

        bool SupplyEstablished(MatchRuntime current)
        {
            foreach (EntityId entity in current.Simulation.Entities.Query<SkirmishResearchInfrastructure>())
                if (current.Simulation.Entities.GetComponent<SkirmishOpponentController>(entity).Player == player &&
                    current.Simulation.Entities.GetComponent<SkirmishResearchInfrastructure>(entity).SupplyEstablished) return true;
            return false;
        }
    }
}
