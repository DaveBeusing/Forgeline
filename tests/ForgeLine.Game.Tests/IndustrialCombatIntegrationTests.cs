using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class IndustrialCombatIntegrationTests
{
    private static readonly PlayerId Owner = new(1);
    private static readonly PlayerId Enemy = new(2);
    private static readonly Vector3 IndustryPosition = new(420, 0, 1440);
    private static readonly Vector3 PowerPosition = new(500, 0, 1440);

    [Theory]
    [InlineData(2u)]
    [InlineData(4u)]
    [InlineData(5u)]
    public void NormallyBuiltIndustryCanBeIdentifiedDamagedAndDestroyedByAnEnemy(uint buildingValue)
    {
        MatchComposition composition = CreateComposition();
        using MatchRuntime runtime = CreateRuntime(composition);
        EntityId target = Build(runtime, new BuildingId(buildingValue), IndustryPosition);
        HealthState original = runtime.Simulation.Entities.GetComponent<HealthState>(target);

        Attack(runtime, target);

        Assert.True(runtime.RunUntil(r => r.Simulation.Entities.IsAlive(target) &&
            r.Simulation.Entities.GetComponent<HealthState>(target).Current < original.Current,
            200, TestContext.Current.CancellationToken));
        Assert.True(runtime.RunUntil(r => !r.Simulation.Entities.IsAlive(target),
            600, TestContext.Current.CancellationToken));
        Assert.False(runtime.Logistics.TryGetNodeForEntity(target, out _));
        Assert.Equal(MatchLifecyclePhase.Running, runtime.GetMatchState().Lifecycle);
    }

    [Fact]
    public void DestroyedProductionRebuildsAndResumesFromRealDeliveriesWithMatchingSaveAndReplayHashes()
    {
        MatchComposition composition = CreateComposition();
        using MatchRuntime runtime = CreateRuntime(composition);
        Build(runtime, BuildingIds.PowerPlant, PowerPosition);
        EntityId smelter = Build(runtime, BuildingIds.Smelter, IndustryPosition);
        EntityId request = StartSteelProduction(runtime, smelter);
        ProductionFacility original = runtime.Simulation.Entities.GetComponent<ProductionFacility>(smelter);
        WaitForSteel(runtime, original.OutputInventory);
        Assert.True(runtime.Inventories.GetQuantity(original.InputInventory, ResourceIds.FerrousOre) > 0);

        Attack(runtime, smelter);
        Assert.True(runtime.RunUntil(r => r.Simulation.Entities.IsAlive(smelter) &&
            r.Simulation.Entities.GetComponent<HealthState>(smelter).Current <
                r.Services.BuildingDefinitions[BuildingIds.Smelter].MaximumHealth,
            200, TestContext.Current.CancellationToken));
        AssertRoundTrip(runtime, composition);
        Assert.True(runtime.RunUntil(r => !r.Simulation.Entities.IsAlive(smelter),
            600, TestContext.Current.CancellationToken));
        Assert.False(runtime.Simulation.Entities.IsAlive(request));
        Assert.False(runtime.Inventories.Contains(original.InputInventory));
        Assert.False(runtime.Inventories.Contains(original.OutputInventory));
        long completedAtLoss = runtime.Production.Metrics.CompletedCycles;
        runtime.Simulation.RunTicks(80, TestContext.Current.CancellationToken);
        Assert.Equal(completedAtLoss, runtime.Production.Metrics.CompletedCycles);

        EntityId rebuilt = Build(runtime, BuildingIds.Smelter, IndustryPosition);
        Assert.NotEqual(smelter, rebuilt);
        ProductionFacility replacement = runtime.Simulation.Entities.GetComponent<ProductionFacility>(rebuilt);
        Assert.NotEqual(original.InputInventory, replacement.InputInventory);
        Assert.NotEqual(original.OutputInventory, replacement.OutputInventory);
        Assert.Equal(0, runtime.Inventories.GetTotalQuantity(replacement.InputInventory));
        Assert.Equal(0, runtime.Inventories.GetTotalQuantity(replacement.OutputInventory));
        StartSteelProduction(runtime, rebuilt);
        WaitForSteel(runtime, replacement.OutputInventory);
        Assert.True(runtime.Production.Metrics.CompletedCycles > completedAtLoss);
        AssertRoundTrip(runtime, composition);
    }

    [Fact]
    public void DestroyedPowerPlantStopsTheExistingProductionBranchUntilRegularRebuild()
    {
        MatchComposition composition = CreateComposition();
        using MatchRuntime runtime = CreateRuntime(composition);
        EntityId power = Build(runtime, BuildingIds.PowerPlant, IndustryPosition);
        EntityId smelter = Build(runtime, BuildingIds.Smelter, PowerPosition);
        StartSteelProduction(runtime, smelter);
        ProductionFacility facility = runtime.Simulation.Entities.GetComponent<ProductionFacility>(smelter);
        WaitForSteel(runtime, facility.OutputInventory);

        Attack(runtime, power);
        Assert.True(runtime.RunUntil(r => !r.Simulation.Entities.IsAlive(power),
            600, TestContext.Current.CancellationToken));
        runtime.Simulation.AdvanceOneTick();
        Assert.Equal(0, runtime.Simulation.Entities.GetComponent<PowerConsumer>(smelter).OperationalScale);
        double output = runtime.Inventories.GetQuantity(facility.OutputInventory, ResourceIds.Steel);
        runtime.Simulation.RunTicks(100, TestContext.Current.CancellationToken);
        Assert.Equal(output, runtime.Inventories.GetQuantity(facility.OutputInventory, ResourceIds.Steel));

        EntityId rebuilt = Build(runtime, BuildingIds.PowerPlant, IndustryPosition);
        Assert.NotEqual(power, rebuilt);
        Assert.True(runtime.RunUntil(r => r.Inventories.GetQuantity(facility.OutputInventory, ResourceIds.Steel) > output,
            2000, TestContext.Current.CancellationToken));
        Assert.Equal(1, runtime.Simulation.Entities.GetComponent<PowerConsumer>(smelter).OperationalScale);
        AssertRoundTrip(runtime, composition);
    }

    [Fact]
    public void EnemyCoreDestructionRecordsOneOutcome()
    {
        using MatchRuntime runtime = CreateRuntime(CreateComposition());
        EntityId core = runtime.GetBase(Owner).CommandCore;
        Attack(runtime, core);
        bool destroyed = runtime.RunUntil(r => !r.Simulation.Entities.IsAlive(core),
            700, TestContext.Current.CancellationToken);
        EntityId tank = runtime.GetBase(Enemy).StartingUnits[^1];
        if (!destroyed)
            Assert.Fail($"Core health: {runtime.Simulation.Entities.GetComponent<HealthState>(core)}; " +
                $"tank combat: {runtime.Simulation.Entities.GetComponent<TacticalCombatState>(tank)}; " +
                $"weapon: {runtime.Simulation.Entities.GetComponent<WeaponState>(tank)}");
        MatchState terminal = runtime.GetMatchState();
        Assert.Equal(MatchLifecyclePhase.Ending, terminal.Lifecycle);
        Assert.Equal(MatchTerminationReason.CommandCoreDestroyed, terminal.TerminationReason);
        Assert.Equal(Enemy, terminal.Winner);
        // Raw coordinator steps exercise repeated objective evaluation; the client host owns
        // terminal tick suspension, covered separately by its lifecycle contract tests.
        runtime.Simulation.RunTicks(5, TestContext.Current.CancellationToken);
        Assert.Equal(terminal, runtime.GetMatchState());
    }

    private static MatchComposition CreateComposition()
    {
        MatchComposition standard = CentralDivideScenario.CreateComposition();
        return standard with
        {
            Key = "test.industrial-combat-recovery.v1",
            CreateStartingBase = (entities, inventories, factory, terrain, start, content, stock) =>
            {
                SkirmishStartingBase starting = standard.CreateStartingBase(
                    entities, inventories, factory, terrain, start, content, stock);
                if (start.Player != Enemy) return starting;

                // A normal catalog unit in a reproducible scenario, waiting for a player attack order.
                Assert.True(terrain.TrySampleHeight(350, 1440, out float height));
                EntityId tank = factory.Create(content.Units[UnitIds.MainBattleTank],
                    new Vector3(350, height + 1.5f, 1440), Enemy);
                entities.SetComponent(tank, new AutoTargetState(false));
                return starting with { StartingUnits = [.. starting.StartingUnits, tank] };
            }
        };
    }

    private static MatchRuntime CreateRuntime(MatchComposition composition) =>
        MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 73101) with
        {
            Composition = composition,
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false)
        }, TestContext.Current.CancellationToken);

    private static EntityId Build(MatchRuntime runtime, BuildingId id, Vector3 position)
    {
        var command = new BuildCommand(Owner, id, position, BuildingOrientation.North,
            runtime.GetBase(Owner).CommandCore, runtime.Simulation.CurrentTick);
        Submit(runtime, command, Owner);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(runtime.Services.BuildingCommands.TryGetLastResult(Owner, out BuildCommandResult result));
        Assert.True(result.Accepted, $"{result.RejectionReason}: {result.PlacementFailure}");
        Assert.False(runtime.Simulation.Entities.HasComponent<HealthState>(result.CreatedSite));
        runtime.Simulation.RunTicks(runtime.Services.BuildingDefinitions[id].ConstructionTicks,
            TestContext.Current.CancellationToken);
        Assert.True(runtime.Simulation.Entities.HasComponent<CompletedBuilding>(result.CreatedSite));
        Assert.True(runtime.Simulation.Entities.HasComponent<HealthState>(result.CreatedSite));
        return result.CreatedSite;
    }

    private static void Attack(MatchRuntime runtime, EntityId target)
    {
        EntityId tank = runtime.GetBase(Enemy).StartingUnits[^1];
        Assert.True(runtime.RunUntil(r => r.Intelligence.IsEntityCurrentlyIdentified(new FactionId(2), target),
            40, TestContext.Current.CancellationToken));
        var attack = PlayerTacticalActionCommand.Attack(Enemy, [tank], target,
            runtime.Simulation.CurrentTick, runtime.Intelligence,
            runtime.Services.Weapons, runtime.Services.ArtilleryWeapons);
        Submit(runtime, attack, Enemy);
        runtime.Simulation.AdvanceOneTick();
        Assert.Equal(PlayerTacticalActionFailureReason.None, attack.FailureReason);
        Assert.Equal(1, attack.AcceptedTargetCount);
    }

    private static EntityId StartSteelProduction(MatchRuntime runtime, EntityId smelter)
    {
        var supply = PlayerLogisticsActionCommand.SetStockPolicy(Owner, smelter, ResourceIds.FerrousOre,
            20, 100, 100, LogisticsStockPriority.Normal, true, runtime.Simulation.CurrentTick);
        var production = PlayerProductionActionCommand.Queue(Owner, smelter, RecipeIds.Steel,
            runtime.Simulation.CurrentTick, mode: ProductionRequestMode.Repeat);
        Submit(runtime, supply, Owner);
        Submit(runtime, production, Owner);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(supply.Accepted);
        Assert.True(production.Accepted);
        return production.CreatedRequestEntity;
    }

    private static void WaitForSteel(MatchRuntime runtime, InventoryId output) =>
        Assert.True(runtime.RunUntil(r => r.Inventories.GetQuantity(output, ResourceIds.Steel) >= 10,
            3000, TestContext.Current.CancellationToken), "Regular truck delivery did not feed steel production.");

    private static void AssertRoundTrip(MatchRuntime runtime, MatchComposition composition)
    {
        MatchSaveData save = MatchPersistenceSerializer.DeserializeSave(
            MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(runtime)));
        MatchReplayData replay = MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(runtime)));
        MatchComposition Resolve(string key)
        {
            Assert.Equal(composition.Key, key);
            return composition;
        }
        using MatchRuntime restored = MatchPersistenceService.Restore(save, Resolve);
        using MatchRuntime playback = MatchPersistenceService.PlayReplay(replay, Resolve);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        runtime.Simulation.RunTicks(10, TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(10, TestContext.Current.CancellationToken);
        playback.Simulation.RunTicks(10, TestContext.Current.CancellationToken);
        string expected = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
    }

    private static void Submit(MatchRuntime runtime, ISimulationCommand command, PlayerId issuer) =>
        runtime.Simulation.SubmitCommand(command, runtime.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(issuer.Value));
}
