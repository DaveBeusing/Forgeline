using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class BridgeRepairFlowTests
{
    [Fact]
    public void CombatBridgeRequiresPaidRepairAndReopensOnlyAfterWorkCompletes()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
            { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var entities = runtime.Simulation.Entities; var player = new PlayerId(1);
        var bridge = runtime.BattlefieldRuntime.CrossingEntities.Values.First(entity => entities.HasComponent<BridgeCombat>(entity));
        var edge = entities.GetComponent<StrategicInfrastructure>(bridge).LogisticsEdge;
        var bounds = entities.GetComponent<StrategicInfrastructure>(bridge).NavigationBlocker;
        Vector3 position = (bounds.Minimum + bounds.Maximum) * .5f;
        Assert.True(runtime.Terrain.TrySampleHeight(position.X, position.Z, out float height)); position.Y = height;
        // Finite test fixture: no production system receives free stock or repair authority.
        var source = entities.CreateEntity();
        var inventory = runtime.Inventories.CreateInventory(new InventorySpecification(200));
        Assert.True(runtime.Inventories.Add(inventory, ResourceIds.Steel, BridgeCombat.SteelCost).Succeeded);
        Assert.True(runtime.Inventories.Add(inventory, ResourceIds.Electronics, BridgeCombat.ElectronicsCost).Succeeded);
        entities.AddComponent(source, new CompletedBuilding(BuildingIds.SupplyDepot, player, SimulationTick.Zero));
        entities.AddComponent(source, new InventoryStorage(inventory));
        entities.AddComponent(source, HealthState.Full(100));
        entities.AddComponent(source, new WorldTransform(position, Quaternion.Identity, Vector3.One));
        var engineer = runtime.UnitFactory.Create(DirectorateContent.CreateUnitCatalog()[UnitIds.CombatEngineer], position, player);
        var disable = new DisableStrategicInfrastructureCommand(bridge, runtime.Simulation.CurrentTick);
        runtime.Simulation.SubmitCommand(disable, runtime.Simulation.CurrentTick.Next());
        runtime.Simulation.AdvanceOneTick(); Assert.True(disable.Accepted);
        var free = new RestoreStrategicInfrastructureCommand(bridge, runtime.Simulation.CurrentTick);
        runtime.Simulation.SubmitCommand(free, runtime.Simulation.CurrentTick.Next());
        runtime.Simulation.AdvanceOneTick(); Assert.False(free.Accepted);
        var repair = new RepairBridgeCommand(runtime.Services.StrategicInfrastructure, player, engineer, bridge, source,
            entities.GetComponent<BridgeCombat>(bridge).Revision, runtime.Simulation.CurrentTick);
        runtime.Simulation.SubmitCommand(repair, runtime.Simulation.CurrentTick.Next());
        runtime.Simulation.AdvanceOneTick(); Assert.True(repair.Accepted, repair.Result.ToString());
        Assert.Equal(0, runtime.Inventories.GetTotalQuantity(inventory));
        Assert.True(runtime.Logistics.TryGetEdge(edge, out var closed)); Assert.False(closed.Enabled);
        uint duration = entities.GetComponent<StrategicInfrastructure>(bridge).RestorationTicks;
        runtime.Simulation.RunTicks(duration, TestContext.Current.CancellationToken);
        Assert.True(runtime.Logistics.TryGetEdge(edge, out var reopened)); Assert.True(reopened.Enabled);
        Assert.Equal(BridgeCombat.MaximumHealth, entities.GetComponent<HealthState>(bridge).Current);
        Assert.False(entities.HasComponent<BridgeRepairWork>(bridge));
        Assert.Equal(0, runtime.Inventories.GetTotalQuantity(inventory));
    }
}
