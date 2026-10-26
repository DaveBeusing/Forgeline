using ForgeLine.Combat;
using ForgeLine.Logistics;
using ForgeLine.Economy;
using System.Numerics;
using ForgeLine.World;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed class SkirmishRaidMatchTests
{
    [Fact]
    public void ActiveNormalMatchRaidReconstructsThroughSaveAndReplay()
    {
        using var scene = Scene.Create();
        var original = scene.Runtime;
        EntityId controller = scene.Controller;
        MatchComposition composition = scene.Composition;
        var save = MatchPersistenceService.CaptureSave(original);
        var replay = MatchPersistenceService.CaptureReplay(original);
        using var restored = MatchPersistenceService.Restore(MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(save)), Resolve);
        using var playback = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(replay)), Resolve);
        MatchComposition Resolve(string key) => key == composition.Key ? composition : throw new InvalidOperationException(key);
        foreach (ulong ticks in new ulong[] { 0, 1, 7, 24 })
        {
            original.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            restored.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            playback.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            string expected = MatchAuthoritativeSnapshot.Capture(original).ComputeSha256();
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
            Assert.Equal(original.Simulation.Entities.GetComponent<SkirmishRaidState>(controller), restored.Simulation.Entities.GetComponent<SkirmishRaidState>(controller));
        }
    }
    [Fact]
    public void OpponentExpiresTheRaidBeforeItsNextStrategicDecision()
    {
        using var scene = Scene.Create(reactionCadenceTicks: 40, activeTicks: 3);
        var entities = scene.Runtime.Simulation.Entities;
        var started = entities.GetComponent<SkirmishRaidState>(scene.Controller);
        scene.Runtime.Simulation.RunTicks(3, TestContext.Current.CancellationToken);
        var finished = entities.GetComponent<SkirmishRaidState>(scene.Controller);
        Assert.Equal(SkirmishRaidPhase.Complete, finished.Phase);
        Assert.Equal(SkirmishRaidReason.DurationExpired, finished.Reason);
        Assert.Equal(started.StartedAtTick, entities.GetComponent<SkirmishOpponentState>(scene.Controller).LastDecisionTick);
        Assert.Equal(CombatOrderKind.Retreat, entities.GetComponent<CombatOrderState>(started.First).Kind);
    }

    [Fact]
    public void ExposedLoadedTruckLosesCargoWhilePlayerReroutingPreservesIt()
    {
        using var exposed = Scene.Create();
        using var rerouted = Scene.Create();
        var original = exposed.Runtime;
        var protectedRuntime = rerouted.Runtime;
        EntityId target = original.Simulation.Entities.GetComponent<SkirmishRaidState>(exposed.Controller).Target;
        EntityId protectedTarget = protectedRuntime.Simulation.Entities.GetComponent<SkirmishRaidState>(rerouted.Controller).Target;
        Assert.Equal(target, protectedTarget);
        InventoryId cargo = original.Simulation.Entities.GetComponent<CargoTransport>(target).CargoInventory;
        InventoryId protectedCargo = protectedRuntime.Simulation.Entities.GetComponent<CargoTransport>(protectedTarget).CargoInventory;
        Assert.Equal(20, original.Inventories.GetQuantity(cargo, ResourceIds.Fuel));
        protectedRuntime.Simulation.SubmitCommand(new MoveEntitiesCommand(new PlayerId(2), [protectedTarget],
            rerouted.Home + new Vector3(600, 0, 450), protectedRuntime.Simulation.CurrentTick), protectedRuntime.Simulation.CurrentTick.Next());
        original.Simulation.RunTicks(1_200, TestContext.Current.CancellationToken);
        protectedRuntime.Simulation.RunTicks(1_200, TestContext.Current.CancellationToken);
        Assert.False(original.Simulation.Entities.IsAlive(target));
        Assert.False(original.Inventories.Contains(cargo));
        Assert.True(protectedRuntime.Simulation.Entities.IsAlive(protectedTarget));
        Assert.Equal(20, protectedRuntime.Inventories.GetQuantity(protectedCargo, ResourceIds.Fuel));
        var state = protectedRuntime.Simulation.Entities.GetComponent<SkirmishRaidState>(rerouted.Controller);
        Assert.True(state.Phase is SkirmishRaidPhase.Returning or SkirmishRaidPhase.Resupplying or SkirmishRaidPhase.Complete, state.ToString());
        Assert.True(protectedRuntime.Simulation.Entities.GetComponent<HealthState>(protectedTarget).Current > 0);
        Assert.Equal(MatchLifecyclePhase.Running, original.GetMatchState().Lifecycle);
    }

    private sealed record Scene(MatchRuntime Runtime, MatchComposition Composition, EntityId Controller, Vector3 Home) : IDisposable
    {
        public void Dispose() => Runtime.Dispose();
        public static Scene Create(uint reactionCadenceTicks = 20, uint activeTicks = 1_200)
        {
            var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026, enableDebugCapture: true);
            MatchComposition basis = settings.Composition;
            Vector3 west = default;
            MatchComposition composition = basis with
            {
                Key = "exposed-logistics-raid",
                CreateStartingBase = (entities, inventories, factory, terrain, start, content, stock) =>
                {
                    var result = basis.CreateStartingBase(entities, inventories, factory, terrain, start, content, stock);
                    if (start.Player == new PlayerId(1))
                    {
                        west = entities.GetComponent<WorldTransform>(result.CommandCore).Position;
                    }
                    else
                    {
                        int parkedTruck = 0;
                        foreach (var unit in result.StartingUnits)
                            if (entities.GetComponent<UnitIdentity>(unit).UnitId == UnitIds.CargoTruck)
                            {
                                // The two exposed targets must start as separate physical bodies.
                                // Stacking them makes ordinary collision admission reject either move.
                                entities.SetComponent(unit, new WorldTransform(west + new Vector3(600, 0, parkedTruck++ * 12), Quaternion.Identity, Vector3.One));
                                Assert.True(inventories.Transfer(result.StartingInventory,
                                    entities.GetComponent<CargoTransport>(unit).CargoInventory, ResourceIds.Fuel, 20).Succeeded);
                            }
                    }
                    return result;
                }
            };
            settings = settings with { Composition = composition,
                Scenario = settings.Scenario with { OpponentConfigurations = new Dictionary<ulong, SkirmishOpponentConfiguration>
                    { [1] = settings.Scenario.OpponentConfigurations[1] with
                        { DefensiveRadiusMeters = 180, ReactionCadenceTicks = reactionCadenceTicks,
                            Raids = settings.Scenario.OpponentConfigurations[1].Raids with { ActiveTicks = activeTicks } },
                        [2] = settings.Scenario.OpponentConfigurations[2] } },
                Participants = CentralDivideScenario.CreateDefaultParticipants(true, false) };
            var original = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
            Assert.True(original.RunUntil(runtime => runtime.CountUnits(new PlayerId(1), UnitIds.RifleSquad) >= 3,
                20_000, TestContext.Current.CancellationToken), string.Join("; ", original.Opponents.DebugSnapshot));
            EntityId observer = original.GetBase(new PlayerId(1)).StartingUnits[0];
            original.Simulation.SubmitCommand(PlayerTacticalActionCommand.AttackMove(new PlayerId(1),
                [observer], west + new Vector3(80, 0, 0), original.Simulation.CurrentTick, FormationTemplate.Compact, original.Intelligence, basis.Content.Weapons, basis.Content.Artillery), original.Simulation.CurrentTick.Next());
            Assert.True(original.RunUntil(runtime => Vector3.Distance(runtime.Simulation.Entities.GetComponent<WorldTransform>(observer).Position,
                west + new Vector3(80, 0, 0)) < 12, 3_000, TestContext.Current.CancellationToken));
            original.Simulation.SubmitCommand(PlayerTacticalActionCommand.Stop(new PlayerId(1), [observer], original.Simulation.CurrentTick, original.Intelligence, basis.Content.Weapons, basis.Content.Artillery), original.Simulation.CurrentTick.Next());
            foreach (EntityId truck in original.GetBase(new PlayerId(2)).StartingUnits.Skip(1))
                original.Simulation.SubmitCommand(new MoveEntitiesCommand(new PlayerId(2), [truck], west + new Vector3(230, 0, 0),
                    original.Simulation.CurrentTick), original.Simulation.CurrentTick.Next());
            EntityId controller = EntityId.Invalid;
            bool found = original.RunUntil(runtime =>
            {
                foreach (EntityId entity in runtime.Simulation.Entities.Query<SkirmishRaidState>())
                    if (runtime.Simulation.Entities.GetComponent<SkirmishRaidState>(entity).IsActive)
                    { controller = entity; return true; }
                return false;
            }, 12_000, TestContext.Current.CancellationToken);
            Assert.True(found, string.Join("; ", original.Opponents.DebugSnapshot));
                return new(original, composition, controller, west);
        }
    }

}
