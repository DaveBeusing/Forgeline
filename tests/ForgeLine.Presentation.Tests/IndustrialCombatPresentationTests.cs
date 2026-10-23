using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class IndustrialCombatPresentationTests
{
    [Fact]
    public void CompletedIndustryPublishesCopiedPowerAndDamageAndClearsDeadSelection()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var owner = new PlayerId(1);
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction);
        Build(scenario, owner, BuildingIds.PowerPlant);
        EntityId building = Build(scenario, owner, BuildingIds.Smelter);
        var entities = scenario.Simulation.Entities;
        HealthState initialHealth = entities.GetComponent<HealthState>(building);
        Assert.Equal(1, initialHealth.Fraction);

        interaction.SetSelection([building]);
        interaction.SetHover(building, scenario.Simulation.SessionId);
        interaction.SetOperationsOpen(true);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var powered));
        var initialSelection = powered.PlayerExperience!.Value.Selection;
        Assert.Equal(building, initialSelection.PrimaryEntity);
        Assert.True(initialSelection.HasHealth);
        Assert.True(initialSelection.HasPower);
        Assert.Equal(1, initialSelection.HealthFraction);
        Assert.Equal(PowerOperationalState.Powered, initialSelection.PowerState);

        PowerConsumer consumer = entities.GetComponent<PowerConsumer>(building);
        entities.SetComponent(building, consumer.WithEnabled(false));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var offline));
        Assert.Equal(BuildingPresentationState.Unpowered, Instance(offline, building).BuildingFeature.State);
        Assert.Equal(PowerOperationalState.Offline, offline.PlayerExperience!.Value.Selection.PowerState);
        Assert.Equal(0, Assert.Single(offline.Operations!.Facilities, row => row.Entity == building).AllocatedPower);

        entities.SetComponent(building,
            initialHealth.ApplyDamage(new DamagePayload(initialHealth.Maximum * .5), out _));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var damaged));
        Assert.Equal(BuildingPresentationState.Damaged, Instance(damaged, building).BuildingFeature.State);
        Assert.Equal(.5, damaged.PlayerExperience!.Value.Selection.HealthFraction);
        Assert.Equal(.5, damaged.Hover!.Value.OwnedDetails.HealthFraction);
        Assert.Equal(PowerOperationalState.Offline, damaged.PlayerExperience.Value.Selection.PowerState);
        Assert.Equal(1, offline.PlayerExperience!.Value.Selection.HealthFraction);
        Assert.Equal(PowerOperationalState.Powered, initialSelection.PowerState);

        // This checks extraction after entity removal; combat lifecycle execution is qualified by game tests.
        Assert.True(entities.DestroyEntity(building));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var removed));
        Assert.DoesNotContain(removed.Instances.ToArray(), instance => instance.Entity == building);
        Assert.Equal(PlayerSelectionSummary.Empty, removed.PlayerExperience!.Value.Selection);
        Assert.Null(removed.Hover);
        Assert.DoesNotContain(removed.Operations!.Facilities, row => row.Entity == building);
        Assert.Equal(.5, damaged.PlayerExperience!.Value.Selection.HealthFraction);
    }

    [Fact]
    public void ForeignIndustrialDamageAndOutageDoNotPublishPrivateOrHiddenFacts()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var entities = scenario.Simulation.Entities;
        EntityId building = default;
        bool identified = false;
        scenario.Simulation.RegisterTickObserver(new Observation(context =>
        {
            if (!identified) return;
            scenario.Intelligence.Observe(new FactionId(1), building,
                entities.GetComponent<IntelligenceSignature>(building),
                entities.GetComponent<WorldTransform>(building).Position,
                IntelligenceState.Identified, context.Tick);
        }));
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction);
        building = Build(scenario, new PlayerId(2), BuildingIds.Smelter);
        HealthState health = entities.GetComponent<HealthState>(building);
        entities.SetComponent(building,
            health.ApplyDamage(new DamagePayload(health.Maximum * .8), out _));
        entities.SetComponent(building, entities.GetComponent<PowerConsumer>(building).WithEnabled(false));
        interaction.SetSelection([building]);
        interaction.SetHover(building, scenario.Simulation.SessionId);
        interaction.SetOperationsOpen(true);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var hidden));
        Assert.DoesNotContain(hidden.Instances.ToArray(), instance => instance.Entity == building);
        Assert.Null(hidden.Hover);
        Assert.Equal(PlayerSelectionSummary.Empty, hidden.PlayerExperience!.Value.Selection);
        Assert.DoesNotContain(hidden.Operations!.Facilities, row => row.Entity == building);

        identified = true;
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var seen));
        Assert.Equal(BuildingPresentationState.Critical, Instance(seen, building).BuildingFeature.State);
        var hover = Assert.IsType<PlayerHoverSummary>(seen.Hover);
        Assert.Equal(PlayerHoverCategory.IdentifiedContact, hover.Category);
        Assert.Equal(PlayerSelectionSummary.Empty, hover.OwnedDetails);
        Assert.Equal(PlayerSelectionSummary.Empty, seen.PlayerExperience!.Value.Selection);
        Assert.DoesNotContain(seen.Operations!.Facilities, row => row.Entity == building);

        identified = false;
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var lost));
        Assert.DoesNotContain(lost.Instances.ToArray(), instance => instance.Entity == building);
        Assert.Null(lost.Hover);
        Assert.Equal(hover, seen.Hover);
    }

    [Fact]
    public void CompletedIndustryCannotPublishSelectionForAnotherEntityGeneration()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction);
        EntityId building = Build(scenario, new PlayerId(1), BuildingIds.StorageDepot);
        var stale = new EntityId(building.Index, building.Generation + 1);
        interaction.SetSelection([stale]);
        interaction.SetHover(stale, scenario.Simulation.SessionId);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        Assert.Equal(PlayerSelectionSummary.Empty, snapshot.PlayerExperience!.Value.Selection);
        Assert.Null(snapshot.Hover);
        Assert.Equal(building, Instance(snapshot, building).Entity);
    }

    private static EntityId Build(MatchRuntime scenario, PlayerId owner, BuildingId id)
    {
        var simulation = scenario.Simulation;
        EntityId source = scenario.GetBase(owner).CommandCore;
        Vector3 origin = simulation.Entities.GetComponent<WorldTransform>(source).Position;
        Vector3? position = null;
        for (int x = -192; x <= 192 && position is null; x += 24)
            for (int z = -192; z <= 192; z += 24)
            {
                Vector3 candidate = origin + new Vector3(x, 0, z);
                if (!scenario.Services.BuildingPlacement.Evaluate(simulation.Entities,
                    owner, id, candidate, BuildingOrientation.North).IsValid) continue;
                position = candidate;
                break;
            }
        Assert.NotNull(position);
        simulation.SubmitCommand(new BuildCommand(owner, id, position.Value,
            BuildingOrientation.North, source, simulation.CurrentTick), simulation.CurrentTick.Next(),
            new SimulationCommandSource(owner.Value));
        simulation.AdvanceOneTick();
        Assert.True(scenario.Services.BuildingCommands.TryGetLastResult(owner, out var result));
        Assert.True(result.Accepted, $"Build failed: {result.RejectionReason} / {result.PlacementFailure}");
        for (uint tick = 0; tick < scenario.Services.BuildingDefinitions[id].ConstructionTicks; tick++)
            simulation.AdvanceOneTick();
        Assert.True(simulation.Entities.HasComponent<CompletedBuilding>(result.CreatedSite));
        Assert.False(simulation.Entities.HasComponent<ConstructionSite>(result.CreatedSite));
        return result.CreatedSite;
    }

    private static RenderInstance Instance(PresentationSnapshot snapshot, EntityId entity) =>
        Assert.Single(snapshot.Instances.ToArray(), item => item.Entity == entity);

    private sealed class Observation(Action<SimulationContext> action) : ISimulationTickObserver
    {
        public void OnTickCompleted(SimulationContext context) => action(context);
    }
}
