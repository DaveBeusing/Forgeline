using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class MatchPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CombatGroupReplacementAndRealCasualtyPreserveSaveReplay(bool casualty)
    {
        MatchRuntimeSettings settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 7521);
        MatchComposition basis = settings.Composition;
        EntityId attacker = EntityId.Invalid, partner = EntityId.Invalid, victim = EntityId.Invalid;
        Vector3 home = basis.Battlefield.Starts[0].CommandCorePosition;
        MatchComposition composition = basis with
        {
            Key = "unit-lifecycle-recovery",
            CreateStartingBase = (entities, inventories, factory, terrain, start, content, stock) =>
            {
                var result = basis.CreateStartingBase(entities, inventories, factory, terrain, start, content, stock);
                if (start.Player == new PlayerId(1))
                {
                    attacker = factory.Create(content.Units[UnitIds.MainBattleTank], home + new Vector3(100, 0, 80), start.Player);
                    partner = factory.Create(content.Units[UnitIds.MainBattleTank], home + new Vector3(100, 0, 100), start.Player);
                }
                else if (casualty)
                    victim = factory.Create(content.Units[UnitIds.ScoutVehicle], home + new Vector3(150, 0, 80), start.Player);
                return result;
            }
        };
        settings = settings with { Composition = composition,
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) };
        using MatchRuntime original = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        InventoryId lostSupply = casualty
            ? original.Simulation.Entities.GetComponent<UnitFuelState>(victim).InventoryId : default;
        for (int index = 0; index < 32; index++)
        {
            original.Simulation.SubmitCommand(PlayerTacticalActionCommand.HoldPosition(new(1), [attacker, partner],
                original.Simulation.CurrentTick, original.Intelligence, composition.Content.Weapons,
                composition.Content.Artillery), original.Simulation.CurrentTick.Next());
            original.Simulation.AdvanceOneTick();
            Assert.Equal(1, original.Simulation.Entities.GetComponentCount<CombatGroupIntent>());
        }
        if (casualty)
        {
            if (original.Simulation.Entities.IsAlive(victim))
                original.Simulation.SubmitCommand(PlayerTacticalActionCommand.Attack(new(1), [attacker], victim,
                    original.Simulation.CurrentTick, original.Intelligence, composition.Content.Weapons,
                    composition.Content.Artillery), original.Simulation.CurrentTick.Next());
            for (int tick = 0; tick < 600 && original.Simulation.Entities.IsAlive(victim); tick++)
                original.Simulation.AdvanceOneTick();
            Assert.False(original.Simulation.Entities.IsAlive(victim));
            Assert.False(original.Inventories.Contains(lostSupply));
            original.Simulation.AdvanceOneTick();
        }
        MatchComposition Resolve(string key) => key == composition.Key ? composition : throw new InvalidOperationException(key);
        MatchSaveData save = MatchPersistenceService.CaptureSave(original);
        using MatchRuntime restored = MatchPersistenceService.Restore(
            MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(save)), Resolve);
        using MatchRuntime replay = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(original))), Resolve);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(replay).ComputeSha256());
        original.Simulation.RunTicks(10, TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(10, TestContext.Current.CancellationToken);
        replay.Simulation.RunTicks(10, TestContext.Current.CancellationToken);
        string expected = MatchAuthoritativeSnapshot.Capture(original).ComputeSha256();
        Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(replay).ComputeSha256());
    }

    [Fact]
    public void AcceptedLoadedConvoyAndManualEscortReplacementRoundTrip()
    {
        MatchRuntimeSettings settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 2026);
        MatchComposition basis = settings.Composition;
        EntityId destination = EntityId.Invalid;
        MatchComposition composition = basis with
        {
            Key = "loaded-convoy-recovery",
            CreateStartingBase = (entities, inventories, factory, terrain, start, content, stock) =>
            {
                var result = basis.CreateStartingBase(entities, inventories, factory, terrain, start, content, stock);
                if (start.Player != new PlayerId(1)) return result;
                Vector3 corePosition = entities.GetComponent<WorldTransform>(result.CommandCore).Position;
                destination = entities.CreateEntity();
                InventoryId inventory = inventories.CreateInventory(new InventorySpecification(1000));
                entities.AddComponent(destination, new WorldTransform(corePosition + new Vector3(300, 0, 0), Quaternion.Identity, Vector3.One));
                entities.AddComponent(destination, new ControllableEntity(start.Player, ControllableEntityCategory.Building));
                entities.AddComponent(destination, new CompletedBuilding(BuildingIds.StorageDepot, start.Player, SimulationTick.Zero));
                entities.AddComponent(destination, new InventoryStorage(inventory));
                entities.AddComponent(destination, new StorageDepot(inventory, result.Faction));
                entities.AddComponent(destination, new BuildingLoadingDock(Vector3.Zero, Vector3.UnitX, 12));
                BuildingCombatInitialization.Initialize(entities, destination, content.Buildings[BuildingIds.StorageDepot]);
                return result;
            }
        };
        settings = settings with { Composition = composition,
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) };
        using MatchRuntime original = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        var issuer = new PlayerId(1);
        foreach (var resource in new[] { ResourceIds.Fuel, ResourceIds.Ammunition })
            original.Simulation.SubmitCommand(PlayerLogisticsActionCommand.SetStockPolicy(issuer, destination, resource,
                150, 200, 400, LogisticsStockPriority.High, true, SimulationTick.Zero), SimulationTick.Zero.Next());
        EntityId escort = original.GetBase(issuer).StartingUnits[0];
        Vector3 escortPosition = original.Simulation.Entities.GetComponent<WorldTransform>(escort).Position;
        original.Simulation.SubmitCommand(new MoveEntitiesCommand(issuer, [escort], escortPosition, SimulationTick.Zero), SimulationTick.Zero.Next());
        EntityId[] trucks = [];
        string trace = string.Empty;
        bool found = original.RunUntil(runtime =>
        {
            var activeTrucks = new List<EntityId>();
            foreach (EntityId entity in runtime.Simulation.Entities.Query<CargoTransportOrder, CargoTransportRouteState>()) activeTrucks.Add(entity);
            trucks = activeTrucks
                .Where(entity => runtime.Simulation.Entities.TryGetComponent(entity, out ControllableEntity control) && control.Owner == issuer &&
                    runtime.Simulation.Entities.TryGetComponent(entity, out CargoTransportRuntimeState state) &&
                    state.Lifecycle == CargoTransportLifecycleState.ToDestination && state.ObservedNetworkVersion == runtime.Logistics.Version &&
                    (!runtime.Simulation.Entities.TryGetComponent(entity, out VehicleDockVisit visit) ||
                     !visit.LaneEntered && visit.Status is VehicleDockStatus.Reserved or VehicleDockStatus.Busy) &&
                    !runtime.Simulation.Entities.HasComponent<CargoFuelServiceState>(entity) &&
                    !runtime.Simulation.Entities.HasComponent<ResupplyOrder>(entity) &&
                    !runtime.Simulation.Entities.HasComponent<SupplyRescueAssignment>(entity) &&
                    !runtime.Simulation.Entities.HasComponent<CargoConvoyMember>(entity))
                .Where(entity => runtime.Simulation.Entities.GetComponent<CargoTransportRouteState>(entity).Route is not null)
                .GroupBy(entity =>
                {
                    var order = runtime.Simulation.Entities.GetComponent<CargoTransportOrder>(entity);
                    var route = runtime.Simulation.Entities.GetComponent<CargoTransportRouteState>(entity);
                    return (order.Origin, order.Destination, route.NextSegmentIndex,
                        Edges: string.Join(',', route.Route!.Segments.Select(segment => segment.EdgeId)));
                })
                .Where(group => group.Count() >= 2).Select(group => group.Take(2).ToArray()).FirstOrDefault() ?? [];
            if (runtime.Simulation.CurrentTick.Value % 500 == 0)
                foreach (EntityId unit in runtime.GetBase(issuer).StartingUnits.Skip(1))
                {
                    var state = runtime.Simulation.Entities.GetComponent<CargoTransportRuntimeState>(unit);
                    trace += $"tick={runtime.Simulation.CurrentTick.Value} truck={unit} state={state} fuelService={runtime.Simulation.Entities.HasComponent<CargoFuelServiceState>(unit)} dock={runtime.Simulation.Entities.HasComponent<VehicleDockVisit>(unit)}; ";
                }
            return trucks.Length == 2;
        }, 5000, TestContext.Current.CancellationToken);
        Assert.True(found, trace);
        var create = PlayerLogisticsActionCommand.CreateCargoConvoy(issuer, trucks, [escort], original.Simulation.CurrentTick);
        original.Simulation.SubmitCommand(new MoveEntitiesCommand(issuer, [escort], escortPosition, original.Simulation.CurrentTick), original.Simulation.CurrentTick.Next());
        original.Simulation.SubmitCommand(create, original.Simulation.CurrentTick.Next());
        original.Simulation.AdvanceOneTick();
        Assert.True(create.Accepted, create.FailureReason.ToString());
        Assert.True(original.Simulation.Entities.HasComponent<CargoConvoyEscort>(escort));
        VerifyCheckpoint(continueTicks: false);
        var manual = new MoveEntitiesCommand(issuer, [escort], escortPosition + new Vector3(40, 0, 0), original.Simulation.CurrentTick);
        original.Simulation.SubmitCommand(manual, original.Simulation.CurrentTick.Next());
        original.Simulation.AdvanceOneTick();
        Assert.Equal(1, manual.AcceptedTargetCount);
        Assert.False(original.Simulation.Entities.HasComponent<CargoConvoyEscort>(escort));
        VerifyCheckpoint(continueTicks: true);
        InventoryId received = original.Simulation.Entities.GetComponent<InventoryStorage>(destination).InventoryId;
        Assert.True(original.RunUntil(runtime =>
            runtime.Inventories.GetQuantity(received, ResourceIds.Fuel) == 200 &&
            runtime.Inventories.GetQuantity(received, ResourceIds.Ammunition) == 200 &&
            trucks.All(truck => !runtime.Simulation.Entities.HasComponent<CargoTransportOrder>(truck)),
            5000, TestContext.Current.CancellationToken));
        Assert.False(original.Simulation.Entities.IsAlive(create.CreatedConvoy));
        InventoryId source = original.GetBase(issuer).StartingInventory;
        Assert.Equal(0, original.Inventories.GetReservedQuantity(source, ResourceIds.Fuel));
        Assert.Equal(0, original.Inventories.GetReservedQuantity(source, ResourceIds.Ammunition));
        Assert.All(trucks, truck => Assert.False(original.Simulation.Entities.HasComponent<CargoConvoyMember>(truck)));
        VerifyCheckpoint(continueTicks: true);


        void VerifyCheckpoint(bool continueTicks)
        {
            var save = MatchPersistenceService.CaptureSave(original);
            var replay = MatchPersistenceService.CaptureReplay(original);
            MatchComposition Resolve(string key) => key == composition.Key ? composition : throw new InvalidOperationException(key);
            using MatchRuntime restored = MatchPersistenceService.Restore(MatchPersistenceSerializer.DeserializeSave(
                MatchPersistenceSerializer.SerializeSave(save)), Resolve);
            using MatchRuntime playback = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(
                MatchPersistenceSerializer.SerializeReplay(replay)), Resolve);
            Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
            Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
            if (!continueTicks) return;
            foreach (ulong ticks in new ulong[] { 1, 7, 12 })
            {
                original.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
                restored.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
                playback.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
                string expected = MatchAuthoritativeSnapshot.Capture(original).ComputeSha256();
                Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
                Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
            }
        }
    }

    [Fact]
    public void GroupedResupplyAndReplacementRoundTripThroughSaveAndReplay()
    {
        MatchRuntimeSettings settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 7419);
        MatchComposition basis = settings.Composition;
        EntityId recipient = EntityId.Invalid, partner = EntityId.Invalid;
        Vector3 position = default;
        MatchComposition composition = basis with
        {
            Key = "grouped-supply-recovery",
            CreateStartingBase = (entities, inventories, factory, terrain, start, content, stock) =>
            {
                SkirmishStartingBase result = basis.CreateStartingBase(entities, inventories, factory, terrain, start, content, stock);
                if (start.Player != new PlayerId(1)) return result;
                position = entities.GetComponent<WorldTransform>(result.StartingUnits[0]).Position;
                recipient = factory.Create(content.Units[UnitIds.RifleSquad], position, start.Player);
                partner = factory.Create(content.Units[UnitIds.RifleSquad], position + new Vector3(4, 0, 0), start.Player);
                EntityId enemy = factory.Create(content.Units[UnitIds.RifleSquad], position + new Vector3(20, 0, 0), new PlayerId(2));
                entities.SetComponent(enemy, new FirePolicyState(FirePolicy.HoldFire));
                entities.SetComponent(enemy, new HealthState(1000, 1000));
                if (entities.HasComponent<AutomaticResupplyPolicy>(recipient)) entities.RemoveComponent<AutomaticResupplyPolicy>(recipient);
                AmmunitionState ammunition = entities.GetComponent<AmmunitionState>(recipient);
                double available = inventories.GetQuantity(ammunition.InventoryId, ResourceIds.Ammunition);
                Assert.True(inventories.Remove(ammunition.InventoryId, ResourceIds.Ammunition, available - 1).Succeeded);
                InventoryId supply = inventories.CreateInventory(new InventorySpecification(100, [ResourceIds.Ammunition]));
                Assert.True(inventories.Add(supply, ResourceIds.Ammunition, 1).Succeeded);
                EntityId provider = entities.CreateEntity();
                entities.AddComponent(provider, new WorldTransform(position + new Vector3(1, 0, 0), Quaternion.Identity, Vector3.One));
                entities.AddComponent(provider, new SupplyProvider(supply, start.Player, 10));
                return result;
            }
        };
        settings = settings with { Composition = composition,
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) };
        using MatchRuntime original = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        var issuer = new PlayerId(1);
        original.Simulation.SubmitCommand(PlayerTacticalActionCommand.HoldPosition(issuer, [recipient, partner], SimulationTick.Zero,
            original.Intelligence, composition.Content.Weapons, composition.Content.Artillery), SimulationTick.Zero.Next());
        original.Simulation.SubmitCommand(PlayerLogisticsActionCommand.RequestResupply(issuer, recipient, SimulationTick.Zero), SimulationTick.Zero.Next());
        original.Simulation.AdvanceOneTick();
        Assert.True(original.Simulation.Entities.HasComponent<ResupplyOrder>(recipient));
        Assert.True(original.Simulation.Entities.HasComponent<CombatGroupMember>(recipient));
        Assert.Equal(CombatOrderStatus.Resupplying, original.Simulation.Entities.GetComponent<TacticalCombatState>(recipient).Status);
        VerifyCheckpoint();
        original.Simulation.SubmitCommand(PlayerTacticalActionCommand.HoldPosition(issuer, [recipient], original.Simulation.CurrentTick,
            original.Intelligence, composition.Content.Weapons, composition.Content.Artillery), original.Simulation.CurrentTick.Next());
        original.Simulation.AdvanceOneTick();
        Assert.False(original.Simulation.Entities.HasComponent<ResupplyOrder>(recipient));
        VerifyCheckpoint();
        original.Simulation.SubmitCommand(new MoveEntitiesCommand(issuer, [partner], position + new Vector3(160, 0, 0),
            original.Simulation.CurrentTick), original.Simulation.CurrentTick.Next());
        original.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        Assert.True(original.Simulation.Entities.HasComponent<NavigationPendingPath>(partner) ||
            original.Simulation.Entities.HasComponent<NavigationRouteState>(partner));
        VerifyCheckpoint();

        void VerifyCheckpoint()
        {
            MatchSaveData save = MatchPersistenceService.CaptureSave(original);
            MatchReplayData replay = MatchPersistenceService.CaptureReplay(original);
            MatchComposition Resolve(string key) => key == composition.Key ? composition : throw new InvalidOperationException(key);
            using MatchRuntime restored = MatchPersistenceService.Restore(MatchPersistenceSerializer.DeserializeSave(
                MatchPersistenceSerializer.SerializeSave(save)), Resolve);
            using MatchRuntime playback = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(
                MatchPersistenceSerializer.SerializeReplay(replay)), Resolve);
            Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
            Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
            foreach (ulong ticks in new ulong[] { 1, 7, 12 })
            {
                original.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
                restored.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
                playback.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
                string expected = MatchAuthoritativeSnapshot.Capture(original).ComputeSha256();
                Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
                Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
            }
        }
    }

    [Fact]
    public void ActiveDetourRouteRoundTripsThroughSaveAndReplay()
    {
        MatchRuntimeSettings settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 7420);
        MatchComposition basis = settings.Composition;
        BattlefieldStartPosition[] starts = [CreateStart(1, new Vector3(8, 0, 8)), CreateStart(2, new Vector3(56, 0, 56))];
        var wall = new AxisAlignedBounds(new Vector3(28.1f, -1, 16.1f), new Vector3(28.9f, 2, 39.9f));
        var map = new BattlefieldDefinition(new BattlefieldMapMetadata("recovery.detour", "Detour Recovery", 64, 64, 2),
            starts, [], [], [], [], [], [],
            starts.Select(start => new BattlefieldObjectiveDefinition($"core.{start.Player.Value}", start.Player, start.CommandCorePosition)).ToArray(),
            [wall], basis.Battlefield.TerrainVisual);
        EntityId unit = EntityId.Invalid;
        Vector3 destination = new(30.5f, 0, 26.5f);
        MatchComposition composition = basis with
        {
            Key = "detour-route-recovery",
            Battlefield = map,
            CreateTerrain = _ =>
            {
                var grid = new WorldGridSettings { ChunkSizeMeters = 32, HeightSamplesPerSide = 9 };
                return new TerrainWorld(grid, Enumerable.Range(0, 4).Select(index => new TerrainChunk(
                    new ChunkCoordinate(index % 2, index / 2), new TerrainHeightfield(9, 32, new float[81]))));
            },
            CreateStartingBase = (entities, inventories, factory, terrain, start, content, stock) =>
            {
                EntityId core = entities.CreateEntity();
                entities.AddComponent(core, new CompletedBuilding(BuildingIds.CommandCore, start.Player, SimulationTick.Zero));
                entities.AddComponent(core, new CommandFacility());
                EntityId[] units = [];
                if (start.Player == new PlayerId(1))
                {
                    unit = factory.Create(content.Units[UnitIds.RifleSquad], new Vector3(26.5f, 0, 26.5f), start.Player);
                    units = [unit];
                }
                return new SkirmishStartingBase(start.Player, new FactionId((uint)start.Player.Value), core,
                    EntityId.Invalid, inventories.CreateInventory(new InventorySpecification(1)), units);
            }
        };
        settings = settings with { Composition = composition,
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false),
            Scenario = settings.Scenario with { NavigationCellSizeMeters = 1, NavigationSectorSizeCells = 8 } };
        using MatchRuntime original = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        original.Simulation.SubmitCommand(new MoveEntitiesCommand(new PlayerId(1), [unit], destination, SimulationTick.Zero), SimulationTick.Zero.Next());
        original.Simulation.RunTicks(2, TestContext.Current.CancellationToken);
        NavigationRouteState route = original.Simulation.Entities.GetComponent<NavigationRouteState>(unit);
        Assert.Contains(route.Path.RefinedCells, cell => cell.Z < 16 || cell.Z > 39);
        MatchSaveData save = MatchPersistenceService.CaptureSave(original);
        MatchReplayData replay = MatchPersistenceService.CaptureReplay(original);
        MatchComposition Resolve(string key) => key == composition.Key ? composition : throw new InvalidOperationException(key);
        using MatchRuntime restored = MatchPersistenceService.Restore(MatchPersistenceSerializer.DeserializeSave(
            MatchPersistenceSerializer.SerializeSave(save)), Resolve);
        using MatchRuntime playback = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(replay)), Resolve);
        VerifyHashes();
        foreach (ulong ticks in new ulong[] { 1, 7, 12, 600 })
        {
            original.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            restored.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            playback.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            VerifyHashes();
        }
        Assert.False(original.Simulation.Entities.HasComponent<NavigationPendingPath>(unit));
        Assert.False(original.Simulation.Entities.HasComponent<NavigationRouteState>(unit));
        Assert.InRange(Vector3.Distance(destination, original.Simulation.Entities.GetComponent<WorldTransform>(unit).Position), 0, 2);

        void VerifyHashes()
        {
            string expected = MatchAuthoritativeSnapshot.Capture(original).ComputeSha256();
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        }
        static BattlefieldStartPosition CreateStart(ulong player, Vector3 position) => new(new PlayerId(player), position, position,
            new AxisAlignedBounds(position - new Vector3(4, 4, 4), position + new Vector3(4, 4, 4)));
    }

    [Fact]
    public void PreRaidCheckpointsAreRejectedBeforeReconstruction()
    {
        using var runtime = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 17), cancellationToken: TestContext.Current.CancellationToken);
        var save = MatchPersistenceService.CaptureSave(runtime);
        var state = save.State with { SchemaVersion = 6 };
        var oldSave = save with { State = state, StateSha256 = state.ComputeSha256() };
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(oldSave));
        var replay = MatchPersistenceService.CaptureReplay(runtime) with { AuthoritativeSchemaVersion = 6 };
        Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplay(replay));
    }

    [Fact]
    public void PreIndustrialCombatCheckpointsAreRejectedBeforeReconstruction()
    {
        using MatchRuntime runtime = CreateScenario(seed: 831);
        MatchSaveData save = MatchPersistenceService.CaptureSave(runtime);
        MatchAuthoritativeSnapshot oldState = save.State with { SchemaVersion = 5 };
        MatchSaveData oldSave = save with { State = oldState, StateSha256 = oldState.ComputeSha256() };
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(oldSave)).Reason);
        MatchReplayData oldReplay = MatchPersistenceService.CaptureReplay(runtime) with { AuthoritativeSchemaVersion = 5 };
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplay(oldReplay)).Reason);
    }

    [Fact]
    public void ConvoyCommandsRoundTripThroughSaveAndReplayWithoutLosingMembers()
    {
        using MatchRuntime original = CreateScenario(seed: 9221);
        PlayerId issuer = original.GetBase(new PlayerId(1)).Player;
        EntityId owned = original.GetBase(issuer).StartingUnits[0];
        EntityId foreign = original.GetBase(new PlayerId(2)).StartingUnits[0];
        EntityId escort = original.GetBase(issuer).StartingUnits[1];

        var create = PlayerLogisticsActionCommand.CreateCargoConvoy(
            issuer, [owned, foreign], [escort], original.Simulation.CurrentTick);
        original.Simulation.SubmitCommand(create,
            original.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(issuer.Value));
        original.Simulation.AdvanceOneTick();
        Assert.False(create.Accepted);
        Assert.Equal(PlayerLogisticsActionFailureReason.ConvoyIneligible, create.FailureReason);

        var dissolve = PlayerLogisticsActionCommand.DissolveCargoConvoy(
            issuer, EntityId.Invalid, original.Simulation.CurrentTick);
        original.Simulation.SubmitCommand(dissolve,
            original.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(issuer.Value));
        original.Simulation.AdvanceOneTick();
        Assert.False(dissolve.Accepted);

        MatchSaveData save = MatchPersistenceService.CaptureSave(original);
        MatchReplayData replay = MatchPersistenceService.CaptureReplay(original);
        var convoyCommand = Assert.Single(replay.Commands,
            command => command.LogisticsOperation ==
                PlayerLogisticsActionOperation.CreateCargoConvoy);
        Assert.Equal(2, convoyCommand.IntValue);
        Assert.Equal(new[] { owned, foreign, escort }, convoyCommand.Entities);
        Assert.Contains(replay.Commands, command =>
            command.LogisticsOperation == PlayerLogisticsActionOperation.DissolveCargoConvoy);

        using MatchRuntime restored = MatchPersistenceService.Restore(
            MatchPersistenceSerializer.DeserializeSave(
                MatchPersistenceSerializer.SerializeSave(save)));
        using MatchRuntime playback = MatchPersistenceService.PlayReplay(
            MatchPersistenceSerializer.DeserializeReplay(
                MatchPersistenceSerializer.SerializeReplay(replay)));
        Assert.Equal(save.StateSha256,
            MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256,
            MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WaitingAndExitingQueuesRoundTripWithoutLosingAdmissionOrder(bool exiting)
    {
        using var original = CreateScenario(seed: 2026);
        Assert.True(original.RunUntil(runtime =>
        {
            foreach (var entity in runtime.Simulation.Entities.Query<VehicleDockVisit, VehicleQueueEntry>())
            {
                var visit = runtime.Simulation.Entities.GetComponent<VehicleDockVisit>(entity);
                if (exiting ? visit.Status == VehicleDockStatus.Exiting : visit.Generation == 0) return true;
            }
            return false;
        }, maximumTicks: 5000, cancellationToken: TestContext.Current.CancellationToken));
        var save = MatchPersistenceService.CaptureSave(original);
        var replay = MatchPersistenceService.CaptureReplay(original);
        using var restored = MatchPersistenceService.Restore(MatchPersistenceSerializer.DeserializeSave(
            MatchPersistenceSerializer.SerializeSave(save)));
        using var playback = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(replay)));
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        original.Simulation.RunTicks(200, TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(200, TestContext.Current.CancellationToken);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(original).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
    }

    [Fact]
    public void PreQueueCheckpointsAreRejectedWithTheirOriginalSchema()
    {
        using var runtime = CreateScenario(seed: 831);
        var save = MatchPersistenceService.CaptureSave(runtime);
        var state = save.State with { SchemaVersion = 2 };
        var oldSave = save with { SchemaVersion = 3, State = state, StateSha256 = state.ComputeSha256() };
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.Restore(oldSave)).Reason);
        var replay = MatchPersistenceService.CaptureReplay(runtime) with { SchemaVersion = 3, AuthoritativeSchemaVersion = 2 };
        Assert.Equal(MatchPersistenceFailureReason.IncompatibleVersion,
            Assert.Throws<MatchPersistenceException>(() => MatchPersistenceService.PlayReplay(replay)).Reason);
    }

    [Fact]
    public void TimedDockServiceRoundTripsThroughSaveAndReplay()
    {
        using var original = CreateScenario(seed: 2026);
        Assert.True(original.RunUntil(runtime =>
        {
            foreach (var entity in runtime.Simulation.Entities.Query<VehicleDockVisit>())
                if (runtime.Simulation.Entities.GetComponent<VehicleDockVisit>(entity).ServiceStartedTick > 0) return true;
            return false;
        }, maximumTicks: 5000, cancellationToken: TestContext.Current.CancellationToken));
        var save = MatchPersistenceService.CaptureSave(original);
        var replay = MatchPersistenceService.CaptureReplay(original);
        using var restored = MatchPersistenceService.Restore(MatchPersistenceSerializer.DeserializeSave(
            MatchPersistenceSerializer.SerializeSave(save)));
        using var playback = MatchPersistenceService.PlayReplay(MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(replay)));
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        original.Simulation.RunTicks(200, TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(200, TestContext.Current.CancellationToken);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(original).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
    }

    [Fact]
    public void ActiveMatchSaveRoundTripsAndContinuesDeterministically()
    {
        using MatchRuntime original =
            CreateScenario(
                seed: 2026);

        bool reachedActiveState =
            original.RunUntil(
                scenario =>
                    scenario.Simulation.Entities
                        .GetComponentCount<ProductionRequest>() > 0 ||
                    scenario.Simulation.Entities
                        .GetComponentCount<CargoTransportOrder>() > 0,
                maximumTicks: 5_000,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(
            reachedActiveState);

        MatchSaveData save =
            MatchPersistenceService.CaptureSave(
                original);
        string document =
            MatchPersistenceSerializer.SerializeSave(
                save);
        MatchSaveData restoredData =
            MatchPersistenceSerializer.DeserializeSave(
                document);

        using MatchRuntime restored =
            MatchPersistenceService.Restore(
                restoredData);

        Assert.Equal(
            original.Simulation.CurrentTick,
            restored.Simulation.CurrentTick);
        Assert.Equal(
            original.Simulation.Random.State,
            restored.Simulation.Random.State);
        Assert.Equal(
            original.GetMatchState(),
            restored.GetMatchState());

        MatchAuthoritativeSnapshot expected =
            MatchAuthoritativeSnapshot.Capture(
                original);
        MatchAuthoritativeSnapshot actual =
            MatchAuthoritativeSnapshot.Capture(
                restored);

        Assert.True(
            MatchPersistenceService.Compare(
                expected,
                actual).Compatible);

        original.Simulation.RunTicks(
            250,
            TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(
            250,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            MatchAuthoritativeSnapshot
                .Capture(original)
                .ComputeSha256(),
            MatchAuthoritativeSnapshot
                .Capture(restored)
                .ComputeSha256());
    }

    [Fact]
    public void PendingPlayerCommandSurvivesSaveAndRecovery()
    {
        using MatchRuntime original =
            CreateScenario(
                seed: 77,
                westComputerControlled: false);

        EntityId unit =
            Assert.Single(
                original.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits.Take(1));

        var command =
            new MoveEntitiesCommand(
                original.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                new Vector3(
                    900.0f,
                    0.0f,
                    1_500.0f),
                SimulationTick.Zero,
                FormationTemplate.Compact);

        SimulationCommandEnvelope submitted =
            original.Simulation.SubmitCommand(
                command,
                new SimulationTick(200),
                new SimulationCommandSource(
                    original.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value));

        Assert.Equal(
            1UL,
            submitted.Sequence);

        original.Simulation.RunTicks(
            100,
            TestContext.Current.CancellationToken);

        MatchSaveData save =
            MatchPersistenceService.CaptureSave(
                original);

        Assert.Single(
            save.Commands);
        Assert.Equal(
            1,
            original.Simulation.PendingCommandCount);

        using MatchRuntime restored =
            MatchPersistenceService.Restore(
                save);

        Assert.Equal(
            1,
            restored.Simulation.PendingCommandCount);

        original.Simulation.RunTicks(
            150,
            TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(
            150,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            MatchAuthoritativeSnapshot
                .Capture(original)
                .ComputeSha256(),
            MatchAuthoritativeSnapshot
                .Capture(restored)
                .ComputeSha256());
    }

    [Fact]
    public void ReplayReconstructsRecordedPlayerCommandAndFinalState()
    {
        using MatchRuntime original =
            CreateScenario(
                seed: 91,
                westComputerControlled: false);

        EntityId unit =
            original.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];

        original.Simulation.SubmitCommand(
            new MoveEntitiesCommand(
                original.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                new Vector3(
                    850.0f,
                    0.0f,
                    1_420.0f),
                SimulationTick.Zero,
                FormationTemplate.Wedge),
            new SimulationTick(1),
            new SimulationCommandSource(
                original.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value));

        original.Simulation.RunTicks(
            600,
            TestContext.Current.CancellationToken);

        MatchReplayData replay =
            MatchPersistenceService.CaptureReplay(
                original);
        string document =
            MatchPersistenceSerializer.SerializeReplay(
                replay);
        MatchReplayData restoredReplay =
            MatchPersistenceSerializer.DeserializeReplay(
                document);

        Assert.Single(
            restoredReplay.Commands);

        using MatchRuntime playback =
            MatchPersistenceService.PlayReplay(
                restoredReplay);

        Assert.Equal(
            replay.FinalTick,
            playback.Simulation.CurrentTick.Value);
        Assert.Equal(
            replay.FinalRandomState,
            playback.Simulation.Random.State);
        Assert.Equal(
            replay.FinalMatchState,
            playback.GetMatchState());
        Assert.Equal(
            replay.FinalStateSha256,
            MatchAuthoritativeSnapshot
                .Capture(playback)
                .ComputeSha256());
    }

    [Fact]
    public void CorruptDocumentFailsBeforeRecovery()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 123);

        scenario.Simulation.RunTicks(
            40,
            TestContext.Current.CancellationToken);

        string document =
            MatchPersistenceSerializer.SerializeSave(
                MatchPersistenceService.CaptureSave(
                    scenario));
        string corrupt =
            document.Replace(
                MatchPersistenceSerializer.Magic,
                "FORGELINE_BROKEN",
                StringComparison.Ordinal);

        MatchPersistenceException exception =
            Assert.Throws<MatchPersistenceException>(
                () =>
                    MatchPersistenceSerializer
                        .DeserializeSave(
                            corrupt));

        Assert.Equal(
            MatchPersistenceFailureReason.CorruptDocument,
            exception.Reason);
    }

    [Fact]
    public void FormatVersionMismatchFailsSafely()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 124);

        string document =
            MatchPersistenceSerializer.SerializeSave(
                MatchPersistenceService.CaptureSave(
                    scenario));
        string incompatible =
            document.Replace(
                "\"FormatVersion\": 1",
                "\"FormatVersion\": 999",
                StringComparison.Ordinal);

        Assert.NotEqual(
            document,
            incompatible);

        MatchPersistenceException exception =
            Assert.Throws<MatchPersistenceException>(
                () =>
                    MatchPersistenceSerializer
                        .DeserializeSave(
                            incompatible));

        Assert.Equal(
            MatchPersistenceFailureReason.IncompatibleVersion,
            exception.Reason);
    }

    [Fact]
    public void TamperedAuthoritativeCheckpointIsRejected()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 125);

        scenario.Simulation.RunTicks(
            160,
            TestContext.Current.CancellationToken);

        MatchSaveData save =
            MatchPersistenceService.CaptureSave(
                scenario);
        MatchSaveData tampered =
            save with
            {
                StateSha256 =
                    new string(
                        '0',
                        save.StateSha256.Length)
            };

        MatchPersistenceException exception =
            Assert.Throws<MatchPersistenceException>(
                () =>
                    MatchPersistenceService.Restore(
                        tampered));

        Assert.Equal(
            MatchPersistenceFailureReason.CorruptDocument,
            exception.Reason);
    }

    [Fact]
    public void ReplayDivergenceIsRejected()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 126);

        scenario.Simulation.RunTicks(
            180,
            TestContext.Current.CancellationToken);

        MatchReplayData replay =
            MatchPersistenceService.CaptureReplay(
                scenario);
        MatchReplayData incompatible =
            replay with
            {
                FinalRandomState =
                    unchecked(
                        replay.FinalRandomState + 1)
            };

        MatchPersistenceException exception =
            Assert.Throws<MatchPersistenceException>(
                () =>
                    MatchPersistenceService.PlayReplay(
                        incompatible));

        Assert.Equal(
            MatchPersistenceFailureReason.RandomStateMismatch,
            exception.Reason);
    }

    [Fact]
    public void ControlCommandsReplayAtTheirOriginalTickBoundary()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 127,
                westComputerControlled: false);

        scenario.Simulation.RunTicks(
            20,
            TestContext.Current.CancellationToken);

        scenario.Simulation.ExecuteControlCommand(
            new SetMatchPausedCommand(
                scenario.BattlefieldRuntime.MatchStateEntity,
                paused: true));
        scenario.Simulation.ExecuteControlCommand(
            new SetMatchPausedCommand(
                scenario.BattlefieldRuntime.MatchStateEntity,
                paused: false));

        MatchReplayData replay =
            MatchPersistenceService.CaptureReplay(
                scenario);

        Assert.Equal(
            2,
            replay.Commands.Count(
                static command =>
                    command.IsControl));

        using MatchRuntime playback =
            MatchPersistenceService.PlayReplay(
                replay);

        Assert.Equal(
            scenario.GetMatchState(),
            playback.GetMatchState());
        Assert.Equal(
            replay.FinalStateSha256,
            MatchAuthoritativeSnapshot
                .Capture(playback)
                .ComputeSha256());
    }

    [Fact]
    public void RestoreAndReplayResolvePauseControlsAgainstReconstructedMatchState()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 129,
                westComputerControlled: false);

        scenario.Simulation.RunTicks(
            20,
            TestContext.Current.CancellationToken);

        scenario.Simulation.ExecuteControlCommand(
            new SetMatchPausedCommand(
                scenario.BattlefieldRuntime.MatchStateEntity,
                paused: true));
        scenario.Simulation.ExecuteControlCommand(
            new SetMatchPausedCommand(
                scenario.BattlefieldRuntime.MatchStateEntity,
                paused: false));

        MatchSaveData save =
            MatchPersistenceService.CaptureSave(
                scenario);
        MatchReplayData replay =
            MatchPersistenceService.CaptureReplay(
                scenario);

        // The saved ID is a runtime-local reference, not a stable replay identity.
        RecordedSimulationCommand[] controlsWithoutRuntimeIds =
            save.Commands
                .Select(
                    static command =>
                        command.Kind == ReplayCommandKind.SetMatchPaused
                            ? command with { TargetEntity = default }
                            : command)
                .ToArray();

        using MatchRuntime restored =
            MatchPersistenceService.Restore(
                save with { Commands = controlsWithoutRuntimeIds });
        using MatchRuntime playback =
            MatchPersistenceService.PlayReplay(
                replay with { Commands = controlsWithoutRuntimeIds });

        Assert.Equal(
            save.StateSha256,
            MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(
            replay.FinalStateSha256,
            MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
    }

    [Fact]
    public void UnsupportedQueuedCommandPreventsUnsafeSave()
    {
        using MatchRuntime scenario =
            CreateScenario(
                seed: 128);

        scenario.Simulation.SubmitCommand(
            new UnsupportedRecoveryCommand(),
            new SimulationTick(1),
            SimulationCommandSource.None);

        MatchPersistenceException exception =
            Assert.Throws<MatchPersistenceException>(
                () =>
                    MatchPersistenceService.CaptureSave(
                        scenario));

        Assert.Equal(
            MatchPersistenceFailureReason.IncompleteCommandHistory,
            exception.Reason);
    }

    [Fact]
    public void RepeatedSaveLoadKeepsResourceStateStable()
    {
        MatchRuntime current =
            CreateScenario(
                seed: 2027);

        try
        {
            for (int cycle = 0;
                 cycle < 3;
                 cycle++)
            {
                current.Simulation.RunTicks(
            300,
            TestContext.Current.CancellationToken);

                InventoryStoreSnapshot before =
                    current.Inventories.CaptureSnapshot();
                MatchSaveData save =
                    MatchPersistenceService.CaptureSave(
                        current);
                MatchRuntime restored =
                    MatchPersistenceService.Restore(
                        save);

                AssertInventorySnapshotsEqual(
                    before,
                    restored.Inventories.CaptureSnapshot());

                current.Dispose();
                current = restored;
            }
        }
        finally
        {
            current.Dispose();
        }
    }

    private static void AssertInventorySnapshotsEqual(
        InventoryStoreSnapshot expected,
        InventoryStoreSnapshot actual)
    {
        Assert.Equal(
            expected.NextInventoryId,
            actual.NextInventoryId);
        Assert.Equal(
            expected.Metrics,
            actual.Metrics);
        Assert.Equal(
            expected.Inventories.Count,
            actual.Inventories.Count);

        for (int index = 0;
             index < expected.Inventories.Count;
             index++)
        {
            InventoryStateSnapshot expectedInventory =
                expected.Inventories[index];
            InventoryStateSnapshot actualInventory =
                actual.Inventories[index];

            Assert.Equal(
                expectedInventory.InventoryId,
                actualInventory.InventoryId);
            Assert.Equal(
                expectedInventory.TotalCapacity,
                actualInventory.TotalCapacity,
                precision: 9);
            Assert.Equal(
                expectedInventory.TotalQuantity,
                actualInventory.TotalQuantity,
                precision: 9);
            Assert.Equal(
                expectedInventory.Resources.ToArray(),
                actualInventory.Resources.ToArray());
        }
    }

    private static MatchRuntime CreateScenario(
        ulong seed,
        bool westComputerControlled = true)
    {
        MatchRuntimeSettings runtime =
            CentralDivideScenario.CreateHeadless(
                MatchScenarioProfile.Validation,
                seed) with
            {
                Participants =
                    CentralDivideScenario
                        .CreateDefaultParticipants(
                            westComputerControlled,
                            eastComputerControlled: true)
            };

        return CentralDivideScenario.Create(
            runtime,
            TestContext.Current.CancellationToken);
    }
    private sealed class UnsupportedRecoveryCommand : ISimulationCommand
    {
        public void Execute(
            SimulationContext context)
        {
            ArgumentNullException.ThrowIfNull(
                context);
        }
    }

}
