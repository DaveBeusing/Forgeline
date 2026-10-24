using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TechnologyPaidProductionTests
{
    [Fact]
    public void NormalStockFundsTwoInvestmentsAndPhysicallySuppliedArtillery()
    {
        using var runtime = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(
            MatchScenarioProfile.Gameplay, 4417) with
        {
            Participants = CentralDivideScenario.CreateDefaultParticipants(westComputerControlled: false, eastComputerControlled: false)
        }, TestContext.Current.CancellationToken);
        var side = runtime.GetBase(new PlayerId(1));
        var entities = runtime.Simulation.Entities;
        EntityId power = Build(BuildingIds.PowerPlant);
        EntityId factory = Build(BuildingIds.VehicleFactory);
        Assert.True(entities.IsAlive(power));
        var denied = PlayerUnitProductionActionCommand.Queue(side.Player, factory, UnitIds.MobileArtillery, runtime.Simulation.CurrentTick);
        Submit(denied);
        Advance(1);
        Assert.False(entities.IsAlive(denied.CreatedRequestEntity));
        double steelBefore = runtime.Inventories.GetQuantity(side.StartingInventory, ResourceIds.Steel);
        double electronicsBefore = runtime.Inventories.GetQuantity(side.StartingInventory, ResourceIds.Electronics);
        Research(TechnologyIds.IndustrialStandardization);
        Research(TechnologyIds.MechanizedSystems);
        Assert.Equal(steelBefore - 180, runtime.Inventories.GetQuantity(side.StartingInventory, ResourceIds.Steel));
        Assert.Equal(electronicsBefore - 70, runtime.Inventories.GetQuantity(side.StartingInventory, ResourceIds.Electronics));
        using (var restored = MatchPersistenceService.Restore(MatchPersistenceService.CaptureSave(runtime)))
        using (var replay = MatchPersistenceService.PlayReplay(MatchPersistenceService.CaptureReplay(runtime)))
        {
            Advance(9);
            restored.Simulation.RunTicks(9, TestContext.Current.CancellationToken);
            replay.Simulation.RunTicks(9, TestContext.Current.CancellationToken);
            string hash = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
            Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
            Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(replay).ComputeSha256());
        }
        var artillery = runtime.Services.UnitDefinitions[UnitIds.MobileArtillery];
        foreach (var cost in artillery.Costs)
            Submit(PlayerLogisticsActionCommand.SetStockPolicy(side.Player, factory, cost.ResourceId,
                cost.Quantity, cost.Quantity, cost.Quantity, LogisticsStockPriority.High, true, runtime.Simulation.CurrentTick));
        var production = PlayerUnitProductionActionCommand.Queue(side.Player, factory, UnitIds.MobileArtillery, runtime.Simulation.CurrentTick);
        Submit(production);
        bool produced = false;
        for (int step = 0; step < 100 && !produced; step++)
        {
            Advance(100);
            foreach (EntityId entity in entities.Query<UnitIdentity>())
                if (entities.GetComponent<UnitIdentity>(entity).UnitId == UnitIds.MobileArtillery &&
                    entities.TryGetComponent(entity, out ControllableEntity owned) && owned.Owner == side.Player)
                    produced = true;
        }
        Assert.True(production.Accepted);
        Assert.True(produced, "Artillery must use normal inventory reservation and physical Cargo Truck deliveries.");

        void Research(TechnologyId id)
        {
            var definition = runtime.Services.TechnologyDefinitions[id];
            EntityId facility = id == TechnologyIds.IndustrialStandardization ? side.CommandCore : factory;
            var start = PlayerTechnologyActionCommand.Start(side.Player, id, facility, side.CommandCore,
                runtime.Simulation.CurrentTick, runtime.Services.TechnologyDefinitions);
            Submit(start);
            Advance(definition.ResearchTicks + 1UL);
            Assert.True(start.Accepted);
            Assert.True(TechnologyStateQueries.IsCompleted(entities, side.Player, id));
        }
        EntityId Build(BuildingId id)
        {
            Vector3 center = entities.GetComponent<WorldTransform>(side.CommandCore).Position;
            for (int radius = 2; radius <= 9; radius++)
            for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            {
                if (Math.Abs(x) != radius && Math.Abs(z) != radius) continue;
                var preview = runtime.Services.BuildingPlacement.CreatePreview(entities, side.Player, id,
                    center + new Vector3(x * 36, 0, z * 36), BuildingOrientation.North);
                if (!preview.IsValid) continue;
                Submit(new BuildCommand(side.Player, id, preview.GroundPosition, BuildingOrientation.North,
                    side.CommandCore, runtime.Simulation.CurrentTick));
                Advance(runtime.Services.BuildingDefinitions[id].ConstructionTicks + 2UL);
                foreach (EntityId entity in entities.Query<CompletedBuilding>())
                    if (entities.GetComponent<CompletedBuilding>(entity) is var building && building.Owner == side.Player && building.BuildingId == id)
                        return entity;
                throw new InvalidOperationException($"Paid construction did not complete: {id}.");
            }
            throw new InvalidOperationException($"No valid research building placement: {id}.");
        }
        void Submit(ISimulationCommand command) => runtime.Simulation.SubmitCommand(command,
            runtime.Simulation.CurrentTick.Next(), new SimulationCommandSource(side.Player.Value));
        void Advance(ulong ticks) => runtime.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
    }
}
