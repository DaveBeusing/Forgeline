using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class PolicyTemplateSelectionCaptureTests
{
    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    public void StableFirstDuplicateLimitsAndFullEntityIdentityArePreserved(int count)
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 410) with
            { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        var observer = new Observer(); runtime.Simulation.RegisterTickObserver(observer);
        runtime.Simulation.AdvanceOneTick();
        var entities = runtime.Simulation.Entities;
        var facility = Facility(); EntityId first = default;
        for (int i = 0; i < count; i++)
        {
            var policy = entities.CreateEntity(); if (i == 0) first = policy;
            entities.AddComponent(policy, new LogisticsStockPolicy(facility, ResourceIds.FerrousOre, 25 + i, 50 + i, 100 + i));
            var request = entities.CreateEntity();
            entities.AddComponent(request, new ProductionRequest(facility, RecipeIds.Steel, ProductionPriority.Normal,
                ProductionRequestMode.Repeat, SimulationTick.Zero));
        }
        var gateway = new PlayerCommandGateway(runtime.Simulation, runtime.Services.BuildingCommands,
            runtime.BattlefieldRuntime.MatchStateEntity);
        var extraction = new PresentationExtractionContext(runtime, new(1), new PresentationInteractionState(), gateway);
        var captured = PolicyTemplateSelectionCapture.Capture(observer.Context!, extraction, [facility]);
        var target = Assert.Single(captured.Targets);
        var fact = Assert.Single(target.Stocks, item => item.Resource == ResourceIds.FerrousOre);
        Assert.Equal(first, fact.Policy); Assert.Equal(Math.Min(count, 32), fact.PolicyCount);
        Assert.Equal(25, fact.Minimum); Assert.Equal(50, fact.Target);
        Assert.Equal(Math.Min(count, 32), target.Production!.Requests.Count);
        Assert.Equal(count > 32 ? "POLICY LIMIT / USE INDIVIDUAL CONTROLS" : "", target.Unavailable);
        Assert.True(entities.DestroyEntity(facility)); var replacement = Facility();
        Assert.Equal(facility.Index, replacement.Index); Assert.NotEqual(facility, replacement);
        var fresh = Assert.Single(PolicyTemplateSelectionCapture.Capture(observer.Context!, extraction, [replacement]).Targets);
        Assert.All(fresh.Stocks, item => Assert.Equal(0, item.PolicyCount));
        Assert.Empty(fresh.Production!.Requests);

        EntityId Facility()
        {
            var entity = entities.CreateEntity();
            var input = runtime.Inventories.CreateInventory(new InventorySpecification(10000));
            var output = runtime.Inventories.CreateInventory(new InventorySpecification(10000));
            entities.AddComponent(entity, new ControllableEntity(new(1), ControllableEntityCategory.Building));
            entities.AddComponent(entity, new CompletedBuilding(BuildingIds.Smelter, new(1), SimulationTick.Zero));
            entities.AddComponent(entity, HealthState.Full(100));
            entities.AddComponent(entity, new WorldTransform(Vector3.Zero, Quaternion.Identity, Vector3.One));
            entities.AddComponent(entity, new ProductionFacility(input, output, ProductionCapability.SteelProcessing, SimulationTick.Zero));
            return entity;
        }
    }
    private sealed class Observer : ISimulationTickObserver
    {
        internal SimulationContext? Context { get; private set; }
        public void OnTickCompleted(SimulationContext context) => Context = context;
    }
}
