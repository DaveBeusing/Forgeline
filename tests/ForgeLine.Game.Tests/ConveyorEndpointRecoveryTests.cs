using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorEndpointRecoveryTests
{
    private static readonly PlayerId Owner = new(1);

    [Theory]
    [InlineData(ConveyorModule.InputAdapter)]
    [InlineData(ConveyorModule.OutputAdapter)]
    public void ExplicitReplacementPreservesBufferAndGenerationAcrossSaveAndReplay(ConveyorModule module)
    {
        MatchRuntimeSettings settings = CreateRecoverySettings(module);
        using MatchRuntime runtime = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        runtime.Simulation.RunTicks(3, TestContext.Current.CancellationToken);
        EntityRegistry entities = runtime.Simulation.Entities;
        ConveyorOwnedPiece adapter = ConveyorConstructionService.Capture(entities, Owner)
            .Single(piece => piece.Piece.Placement.Module == module);
        EntityId replacement = FindStorage(entities);
        EntityId obsolete = adapter.Piece.Attachment;
        Assert.Equal(obsolete.Index, replacement.Index);
        Assert.NotEqual(obsolete.Generation, replacement.Generation);
        Assert.False(entities.IsAlive(obsolete));
        InventoryId buffer = entities.GetComponent<ConveyorFlowState>(adapter.Entity).Buffer;
        InventoryId destination = entities.GetComponent<InventoryStorage>(replacement).InventoryId;
        HealthState health = entities.GetComponent<HealthState>(adapter.Entity);
        EntityId[] supports = ConveyorConstructionService.Capture(entities, Owner)
            .Where(piece => piece.Piece.Placement.IsSupport).Select(piece => piece.Entity).ToArray();
        Assert.Equal(25, runtime.Inventories.GetQuantity(buffer, ResourceIds.Steel));
        Assert.Equal(0, runtime.Inventories.GetQuantity(destination, ResourceIds.Steel));
        ConveyorFlowState disconnected = entities.GetComponent<ConveyorFlowState>(adapter.Entity);
        Assert.False((module == ConveyorModule.OutputAdapter ? disconnected.OutputA : disconnected.InputA).IsValid);
        InventoryId payer = runtime.GetBase(Owner).StartingInventory;
        double steel = runtime.Inventories.GetQuantity(payer, ResourceIds.Steel);

        ConveyorConstructionCommand command = CreateReplacement(runtime, adapter, replacement);
        runtime.Simulation.SubmitCommand(command, runtime.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(Owner.Value));
        runtime.Simulation.AdvanceOneTick();

        Assert.True(command.Accepted, command.Result.ToString());
        Assert.True(entities.IsAlive(adapter.Entity));
        Assert.Equal(replacement, entities.GetComponent<ConveyorPiece>(adapter.Entity).Attachment);
        Assert.Equal(adapter.Piece.Placement, entities.GetComponent<ConveyorPiece>(adapter.Entity).Placement);
        Assert.Equal(buffer, entities.GetComponent<ConveyorFlowState>(adapter.Entity).Buffer);
        Assert.Equal(health, entities.GetComponent<HealthState>(adapter.Entity));
        Assert.All(supports, entity => Assert.True(entities.IsAlive(entity)));
        Assert.Equal(steel - ConveyorGeometry.Cost(module), runtime.Inventories.GetQuantity(payer, ResourceIds.Steel), 6);
        Assert.Equal(25, runtime.Inventories.GetQuantity(buffer, ResourceIds.Steel) +
            runtime.Inventories.GetQuantity(destination, ResourceIds.Steel), 6);
        ConveyorFlowState connected = entities.GetComponent<ConveyorFlowState>(adapter.Entity);
        Assert.Equal(replacement, module == ConveyorModule.OutputAdapter ? connected.OutputA : connected.InputA);
        if (module == ConveyorModule.OutputAdapter)
            Assert.True(runtime.Inventories.GetQuantity(destination, ResourceIds.Steel) > 0);

        MatchSaveData save = MatchPersistenceSerializer.DeserializeSave(
            MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(runtime)));
        MatchReplayData replay = MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(runtime)));
        RecordedSimulationCommand recorded = Assert.Single(replay.Commands);
        Assert.Equal(ReplayCommandKind.ConveyorConstruction, recorded.Kind);
        Assert.Equal(command.Request, recorded.ConveyorRequest);
        Func<string, MatchComposition> resolver = key => key == settings.Composition.Key
            ? settings.Composition : throw new InvalidOperationException(key);
        using MatchRuntime restored = MatchPersistenceService.Restore(save, resolver);
        using MatchRuntime playback = MatchPersistenceService.PlayReplay(replay, resolver);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        runtime.Simulation.RunTicks(5, TestContext.Current.CancellationToken);
        restored.Simulation.RunTicks(5, TestContext.Current.CancellationToken);
        playback.Simulation.RunTicks(5, TestContext.Current.CancellationToken);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256(),
            MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
    }

    [Fact]
    public void OccupiedRecoveryRejectsWrongPortModuleOwnerAndRemovalWithoutLosingStock()
    {
        using MatchRuntime runtime = MatchRuntime.Create(CreateRecoverySettings(ConveyorModule.OutputAdapter),
            TestContext.Current.CancellationToken);
        EntityRegistry entities = runtime.Simulation.Entities;
        ConveyorOwnedPiece adapter = ConveyorConstructionService.Capture(entities, Owner)
            .Single(piece => piece.Piece.Placement.Module == ConveyorModule.OutputAdapter);
        InventoryId buffer = entities.GetComponent<ConveyorFlowState>(adapter.Entity).Buffer;
        EntityId payer = runtime.GetBase(Owner).CommandCore;
        EntityId replacement = FindStorage(entities);
        ConveyorBuildRequest valid = CreateReplacement(runtime, adapter, replacement).Request;
        ConveyorBuildRequest[] invalid =
        [
            valid with { Edit = ConveyorEdit.Remove },
            valid with { Module = ConveyorModule.Standard },
            valid with { DestinationPortBuilding = adapter.Piece.Attachment },
            valid with { DestinationPortBuilding = payer },
            valid with { DestinationPortBuilding = runtime.GetBase(new PlayerId(2)).CommandCore },
            valid with { SourcePortBuilding = payer }
        ];
        foreach (ConveyorBuildRequest request in invalid)
        {
            var command = new ConveyorConstructionCommand(runtime.Services.Conveyors, Owner, request,
                payer, runtime.Simulation.CurrentTick);
            runtime.Simulation.SubmitCommand(command, runtime.Simulation.CurrentTick.Next(),
                new SimulationCommandSource(Owner.Value));
            runtime.Simulation.AdvanceOneTick();
            Assert.False(command.Accepted);
            Assert.Equal(ConveyorFailure.Occupied, command.Result);
        }
        var foreign = new ConveyorConstructionCommand(runtime.Services.Conveyors, new PlayerId(2), valid,
            runtime.GetBase(new PlayerId(2)).CommandCore, runtime.Simulation.CurrentTick);
        runtime.Simulation.SubmitCommand(foreign, runtime.Simulation.CurrentTick.Next(), new SimulationCommandSource(2));
        runtime.Simulation.AdvanceOneTick();
        Assert.Equal(ConveyorFailure.NotOwned, foreign.Result);
        Assert.Equal(adapter.Piece.Attachment, entities.GetComponent<ConveyorPiece>(adapter.Entity).Attachment);
        Assert.Equal(25, runtime.Inventories.GetQuantity(buffer, ResourceIds.Steel));
        Assert.Equal(0, runtime.Inventories.GetQuantity(
            entities.GetComponent<InventoryStorage>(replacement).InventoryId, ResourceIds.Steel));

        InventoryId payment = runtime.GetBase(Owner).StartingInventory;
        Assert.True(runtime.Inventories.Reserve(payment, ResourceIds.Steel,
            runtime.Inventories.GetAvailableQuantity(payment, ResourceIds.Steel)).Succeeded);
        ConveyorConstructionCommand unpaid = CreateReplacement(runtime, adapter, replacement);
        runtime.Simulation.SubmitCommand(unpaid, runtime.Simulation.CurrentTick.Next(), new SimulationCommandSource(Owner.Value));
        runtime.Simulation.AdvanceOneTick();
        Assert.Equal(ConveyorFailure.InsufficientStock, unpaid.Result);
        Assert.Equal(adapter.Piece.Attachment, entities.GetComponent<ConveyorPiece>(adapter.Entity).Attachment);
        Assert.Equal(25, runtime.Inventories.GetQuantity(buffer, ResourceIds.Steel));
    }

    private static ConveyorConstructionCommand CreateReplacement(
        MatchRuntime runtime, ConveyorOwnedPiece adapter, EntityId replacement)
    {
        ConveyorPlacement placement = adapter.Piece.Placement;
        bool output = placement.Module == ConveyorModule.OutputAdapter;
        var request = new ConveyorBuildRequest(ConveyorEdit.Replace, placement.Module,
            placement.Pivot, placement.Pivot, placement.Orientation, placement.Tier, true, adapter.Entity,
            output ? EntityId.Invalid : replacement, output ? replacement : EntityId.Invalid);
        return new ConveyorConstructionCommand(runtime.Services.Conveyors, Owner, request,
            runtime.GetBase(Owner).CommandCore, runtime.Simulation.CurrentTick);
    }

    private static EntityId FindStorage(EntityRegistry entities)
    {
        foreach (EntityId entity in entities.Query<CompletedBuilding>())
        {
            CompletedBuilding building = entities.GetComponent<CompletedBuilding>(entity);
            if (building.Owner == Owner && building.BuildingId == BuildingIds.StorageDepot)
                return entity;
        }
        throw new InvalidOperationException("Recovery fixture storage is missing.");
    }

    private static MatchRuntimeSettings CreateRecoverySettings(ConveyorModule module)
    {
        MatchComposition standard = CentralDivideScenario.CreateComposition();
        MatchComposition composition = standard with
        {
            Key = $"test.conveyor-endpoint-recovery.{module}.v1",
            CreateStartingBase = (entities, inventories, factory, terrain, start, content, stock) =>
            {
                SkirmishStartingBase initial = standard.CreateStartingBase(
                    entities, inventories, factory, terrain, start, content, stock);
                if (start.Player != Owner) return initial;

                // A reproducible composition supplies a reused entity slot and retained carrier cargo.
                // Command execution and save/replay reconstruction use the production systems unchanged.
                EntityId previous = entities.CreateEntity();
                entities.DestroyEntity(previous);
                EntityId storage = entities.CreateEntity();
                Vector3 ground = start.CommandCorePosition + new Vector3(-48, 0, -48);
                Assert.True(terrain.TrySampleHeight(ground.X, ground.Z, out float height));
                ground.Y = height;
                var transform = new WorldTransform(ground + Vector3.UnitY * 4, Quaternion.Identity, new Vector3(14, 8, 14));
                entities.AddComponent(storage, new CompletedBuilding(BuildingIds.StorageDepot, Owner, SimulationTick.Zero));
                entities.AddComponent(storage, transform);
                entities.AddComponent(storage, new VisualIdentity(104));
                entities.AddComponent(storage, new SpatialPresence(new Vector3(7, 4, 7),
                    new SpatialEntryMetadata(Owner.Value, (ulong)ControllableEntityCategory.Building, SpatialMobility.Static)));
                entities.AddComponent(storage, new InventoryStorage(inventories.CreateInventory(new InventorySpecification(5_000))));
                var membership = new PowerNetworkMembership(new PowerNetworkId(1));
                entities.AddComponent(storage, membership);
                entities.AddComponent(storage, new PowerConsumer(5));
                EntityId generator = entities.CreateEntity();
                entities.AddComponent(generator, membership);
                entities.AddComponent(generator, new PowerGenerator(500));

                bool output = module == ConveyorModule.OutputAdapter;
                ConveyorBuildingPort port = ConveyorConstructionService.CapturePorts(entities, Owner)
                    .Single(item => item.Building == storage && item.Input == output);
                Vector3 pivot = port.Position - Vector3.UnitY * 5.8f + port.Facing * 2;
                var placement = new ConveyorPlacement(module, pivot,
                    ConveyorGeometry.Facing(output ? -port.Facing : port.Facing), 0);
                EntityId adapter = entities.CreateEntity();
                InventoryId buffer = inventories.CreateInventory(new InventorySpecification(100,
                    ConveyorSimulationSystem.EligibleResources));
                Assert.True(inventories.Transfer(initial.StartingInventory, buffer, ResourceIds.Steel, 25).Succeeded);
                entities.AddComponent(adapter, new ConveyorPiece(Owner, placement, ConveyorGeometry.Cost(module),
                    previous, BufferedQuantity: 25));
                entities.AddComponent(adapter, new ConveyorFlowState(buffer, 100));
                entities.AddComponent(adapter, new HealthState(150, 200));
                entities.AddComponent(adapter, membership);
                entities.AddComponent(adapter, new PowerConsumer(2));
                AddGeometry(entities, adapter, placement);

                ConveyorSocket freeSocket = output ? placement.Sockets[0] : placement.Sockets[^1];
                var foot = new ConveyorPlacement(ConveyorModule.Support,
                    freeSocket.Position - Vector3.UnitY * 5.8f, BuildingOrientation.North, 0);
                EntityId support = entities.CreateEntity();
                entities.AddComponent(support, new ConveyorPiece(Owner, foot, ConveyorGeometry.Cost(foot.Module), AutomaticSupport: true));
                entities.AddComponent(support, HealthState.Full(200));
                AddGeometry(entities, support, foot);
                return initial;
            }
        };
        return CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 71120) with
        {
            Composition = composition,
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false)
        };
    }

    private static void AddGeometry(EntityRegistry entities, EntityId entity, ConveyorPlacement placement)
    {
        entities.AddComponent(entity, new WorldTransform(placement.Bounds.Center, placement.Rotation, new Vector3(18)));
        entities.AddComponent(entity, new VisualIdentity(1));
        entities.AddComponent(entity, new ControllableEntity(Owner, ControllableEntityCategory.Logistics));
        entities.AddComponent(entity, new SpatialPresence((placement.Bounds.Maximum - placement.Bounds.Minimum) / 2,
            new SpatialEntryMetadata(Owner.Value, (ulong)ControllableEntityCategory.Logistics, SpatialMobility.Static)));
    }
}
