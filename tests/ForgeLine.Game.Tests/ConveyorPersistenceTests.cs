using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorPersistenceTests
{
    private const string CompositionKey = "conveyor-recovery.directorate.v1";
    private static readonly PlayerId Player = new(1);
    private static readonly ulong[] ContinuationTicks = [1, 7, 24];

    [Fact]
    public void PaidOverpassCrossingReconstructsAsIndependentAuthoredPartsThroughSaveAndReplay()
    {
        using MatchRuntime runtime = CreateRuntime(7401);
        var center = new Vector3(160, 0, 80);
        ExecuteConveyor(runtime, new(ConveyorEdit.Build, ConveyorModule.Standard, center, center,
            BuildingOrientation.East, 0, true));
        ExecuteConveyor(runtime, new(ConveyorEdit.Build, ConveyorModule.Overpass, center, center,
            BuildingOrientation.North, 0, true));
        var carriers = Carriers(runtime);
        Assert.Equal(4, carriers.Length);
        Assert.DoesNotContain(Pieces(runtime), row => row.Piece.Placement.Module == ConveyorModule.Overpass);
        AssertRoundTripsAndContinues(runtime);
    }

    [Fact]
    public void PlayerDemolitionRemovesConveyorAndReplaysThroughTheNormalCommandBoundary()
    {
        using MatchRuntime runtime = CreateRuntime(17);
        var gateway = new PlayerCommandGateway(runtime.Simulation, runtime.Services.BuildingCommands,
            runtime.BattlefieldRuntime.MatchStateEntity, demolition: runtime.Services.Demolition);
        runtime.Simulation.RegisterTickObserver(gateway);
        ExecuteConveyor(runtime, new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Standard,
            new(160, 0, 80), new(160, 0, 80), BuildingOrientation.North, 0, true));
        ConveyorOwnedPiece target = Assert.Single(Carriers(runtime));
        InventoryId buffer = runtime.Simulation.Entities.GetComponent<ConveyorFlowState>(target.Entity).Buffer;

        Assert.True(gateway.SubmitDemolition(Player, [target.Entity], runtime.Simulation.CurrentTick).Accepted);
        runtime.Simulation.AdvanceOneTick();

        Assert.True(gateway.Results.TryRead(out var result));
        Assert.Equal(1, result.AcceptedTargets);
        Assert.Equal(0, result.RejectedTargets);
        Assert.Empty(Assert.Single(result.DemolitionTargets!).Refunded);
        Assert.False(runtime.Simulation.Entities.IsAlive(target.Entity));
        Assert.False(runtime.Inventories.Contains(buffer));
        Assert.Empty(Pieces(runtime));
        Assert.Contains(runtime.Replay.CaptureCommands(), command => command.Kind == ReplayCommandKind.Demolition);
        AssertRoundTripsAndContinues(runtime);
    }

    [Fact]
    public void LoadedConveyorDemolitionReconstructsCargoSalvageThroughSaveAndReplay()
    {
        using MatchRuntime runtime = CreateRuntime(17);
        var gateway = new PlayerCommandGateway(runtime.Simulation, runtime.Services.BuildingCommands,
            runtime.BattlefieldRuntime.MatchStateEntity, demolition: runtime.Services.Demolition);
        runtime.Simulation.RegisterTickObserver(gateway);
        BuildNetwork(runtime);
        RunUntil(runtime, HasCargo);
        ConveyorOwnedPiece target = Carriers(runtime).First(row => BufferQuantity(runtime, row.Entity) > 0);
        double held = BufferQuantity(runtime, target.Entity);

        Assert.True(gateway.SubmitDemolition(Player, [target.Entity], runtime.Simulation.CurrentTick).Accepted);
        runtime.Simulation.AdvanceOneTick();

        Assert.True(gateway.Results.TryRead(out var result));
        BuildingDemolitionTargetResult accepted = Assert.Single(result.DemolitionTargets!);
        Assert.True(accepted.Accepted);
        ConveyorCargoRecovery[] recovered = Assert.IsType<ConveyorCargoRecovery[]>(accepted.CargoRecovery);
        Assert.NotEmpty(recovered);
        Assert.Equal(held, recovered.Sum(item => item.Held), 8);
        Assert.Equal(held * 0.5, recovered.Sum(item => item.Recovered), 8);
        Assert.Equal(held * 0.5, recovered.Sum(item => item.Lost), 8);
        Assert.False(runtime.Simulation.Entities.IsAlive(target.Entity));
        Assert.True(ConveyorSalvageStock.FindInventory(runtime.Simulation.Entities, Player).IsSpecified);
        AssertRoundTripsAndContinues(runtime);
    }

    [Fact]
    public void InterimFuelAndVolatilesBeltTransportReconstructsFromCommands()
    {
        using MatchRuntime runtime = CreateRuntime(17);
        BuildNetwork(runtime);
        EntityId commandCore = runtime.GetBase(Player).CommandCore;

        // The starting Command Core has physical, authoritative Fluid stock.
        // Reuse its paid conveyor route, with no fixture-created material.
        var policies = new List<EntityId>();
        foreach (EntityId entity in runtime.Simulation.Entities.Query<LogisticsStockPolicy>(
                     QueryIterationOrder.StableByEntityIndex))
            policies.Add(entity);
        foreach (EntityId entity in policies)
        {
            LogisticsStockPolicy policy = runtime.Simulation.Entities.GetComponent<LogisticsStockPolicy>(entity);
            if (policy.TargetEntity != commandCore ||
                policy.ResourceId != ResourceIds.Fuel && policy.ResourceId != ResourceIds.Volatiles)
                continue;
            var remove = PlayerLogisticsActionCommand.RemoveStockPolicy(Player, entity, runtime.Simulation.CurrentTick);
            Submit(runtime, remove);
            Assert.True(remove.Accepted);
        }
        Assert.True(runtime.RunUntil(match => Carriers(match).Any(row =>
            match.Inventories.GetQuantity(row.Flow!.Value.Buffer, ResourceIds.Fuel) > 0 ||
            match.Inventories.GetQuantity(row.Flow!.Value.Buffer, ResourceIds.Volatiles) > 0),
            maximumTicks: 500, cancellationToken: TestContext.Current.CancellationToken));
        AssertRoundTripsAndContinues(runtime);
    }

    [Theory]
    [InlineData(17UL)]
    [InlineData(831UL)]
    public void PaidNetworkWithCargoReconstructsAndContinuesThroughSaveAndReplay(ulong seed)
    {
        using MatchRuntime runtime = CreateRuntime(seed);
        BuildNetwork(runtime);
        RunUntil(runtime, HasCargo);
        ConveyorOwnedPiece[] carriers = Carriers(runtime);
        Assert.Contains(carriers, row => BufferQuantity(runtime, row.Entity) > 0);
        Assert.All(carriers, row => Assert.InRange(BufferQuantity(runtime, row.Entity),
            0, runtime.Simulation.Entities.GetComponent<ConveyorFlowState>(row.Entity).Capacity));
        Assert.Contains(runtime.Replay.CaptureCommands(), command =>
            command.Kind == ReplayCommandKind.ConveyorConstruction);

        AssertRoundTripsAndContinues(runtime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticRouteBetweenFreelyPlacedBuildingsDeliversAndReplaysPaidAcceptedAndRejectedRequests(bool sloped)
    {
        using MatchRuntime runtime = CreateRuntime(seed: 4137,sloped:sloped);
        _ = Build(runtime, BuildingIds.PowerPlant, new(48, 0, 64));
        EntityId destination = Build(runtime, BuildingIds.StorageDepot, new(145.25f, 0, 112.5f));
        EntityId source = runtime.GetBase(Player).CommandCore;
        IReadOnlyList<ConveyorBuildingPort> ports = ConveyorConstructionService.CapturePorts(
            runtime.Simulation.Entities, Player);
        ConveyorBuildingPort output = Assert.Single(ports, port => port.Building == source && !port.Input &&
            (!port.Adaptable || port.Key == "east.primary"));
        ConveyorBuildingPort input = Assert.Single(ports, port => port.Building == destination && port.Input &&
            (!port.Adaptable || port.Key == "west.primary"));
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            output.Position - Vector3.UnitY * 5.8f, input.Position - Vector3.UnitY * 5.8f,
            BuildingOrientation.North, 0, true, SourcePortBuilding: source,
            DestinationPortBuilding: destination, AutomaticRouting: true,AutomaticHeight:sloped);

        // The unchanged legacy request cannot connect this deliberately non-grid layout.
        Assert.Equal(ConveyorFailure.PortMismatch, runtime.Services.Conveyors.Preview(
            runtime.Simulation.Entities, Player, request with { AutomaticRouting = false }, source).Failure);
        double steel = TotalSteel(runtime);
        var rejected = new ConveyorConstructionCommand(runtime.Services.Conveyors, Player,
            request with { DestinationPortBuilding = runtime.GetBase(new PlayerId(2)).CommandCore },
            source, runtime.Simulation.CurrentTick);
        Submit(runtime, rejected);
        Assert.False(rejected.Accepted);
        Assert.Equal(ConveyorFailure.PortMismatch, rejected.Result);
        Assert.Empty(Pieces(runtime));
        Assert.Equal(steel, TotalSteel(runtime), precision: 8);

        ConveyorPlan plan = runtime.Services.Conveyors.Preview(
            runtime.Simulation.Entities, Player, request, source);
        Assert.True(plan.IsValid, plan.Failure.ToString());
        if(sloped)
        {
            Assert.Contains(plan.Pieces,p=>p.RiseMeters!=0);
            Assert.Contains(plan.Pieces,p=>p.SupportHeightMeters>0);
        }
        ExecuteConveyor(runtime, request);
        Assert.Equal(steel - plan.Steel, TotalSteel(runtime), precision: 8);
        Assert.Contains(Carriers(runtime), row => row.Piece.Placement.SpanMeters > 0);
        InventoryId destinationInventory = runtime.Simulation.Entities
            .GetComponent<InventoryStorage>(destination).InventoryId;
        RunUntil(runtime, match => match.Inventories.GetQuantity(destinationInventory, ResourceIds.Steel) > 0);
        Assert.Equal(steel - plan.Steel, TotalSteel(runtime), precision: 8);

        RecordedSimulationCommand[] recorded = runtime.Replay.CaptureCommands()
            .Where(command => command.Kind == ReplayCommandKind.ConveyorConstruction).ToArray();
        Assert.Equal(2, recorded.Length);
        Assert.Equal(rejected.Request, recorded[0].ConveyorRequest);
        Assert.Equal(request, recorded[1].ConveyorRequest);
        Assert.All(recorded, command => Assert.True(command.ConveyorRequest.AutomaticRouting));
        AssertRoundTripsAndContinues(runtime);
    }

    [Fact]
    public void RealImpactPaidRepairsAndRebuiltConnectionReconstructAtEveryCheckpoint()
    {
        using MatchRuntime runtime = CreateRuntime(seed: 9221);
        BuildNetwork(runtime);
        RunUntil(runtime, HasCargo);
        ConveyorOwnedPiece[] carriers = Carriers(runtime);
        EntityId target = carriers[carriers.Length / 2].Entity;
        EntityId artillery = Assert.Single(runtime.GetBase(Player).StartingUnits,
            entity => runtime.Simulation.Entities.HasComponent<ArtilleryCapability>(entity));
        Vector3 impact = runtime.Simulation.Entities.GetComponent<WorldTransform>(target).Position;
        var fire = PlayerTacticalActionCommand.FireMissionCoordinate(Player, [artillery], impact, 3,
            runtime.Simulation.CurrentTick, runtime.Intelligence, runtime.Services.Weapons,
            runtime.Services.ArtilleryWeapons);
        Submit(runtime, fire);
        Assert.Equal(1, fire.AcceptedTargetCount);
        RunUntil(runtime, match =>
            match.Simulation.Entities.GetComponent<FireMissionState>(artillery).Status == FireMissionStatus.Complete &&
            match.Simulation.Entities.GetComponentCount<IndirectFireProjectileState>() == 0);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(runtime.Simulation.Entities.GetComponent<HealthState>(target).IsDepleted);
        Assert.False(runtime.Simulation.Entities.GetComponent<PowerConsumer>(target).Enabled);
        Assert.Equal(ConveyorLinkStatus.Destroyed,
            runtime.Simulation.Entities.GetComponent<ConveyorFlowState>(target).Status);
        Assert.True(BufferQuantity(runtime, target) > 0);
        Assert.True(HasCargo(runtime));

        ConveyorOwnedPiece wreck = Assert.Single(Carriers(runtime), row => row.Entity == target);
        var removeWreck = new ConveyorConstructionCommand(runtime.Services.Conveyors, Player,
            EditRequest(wreck, ConveyorEdit.Replace), runtime.GetBase(Player).CommandCore, runtime.Simulation.CurrentTick);
        Submit(runtime, removeWreck);
        Assert.Equal(ConveyorFailure.Occupied, removeWreck.Result);
        Assert.True(runtime.Simulation.Entities.IsAlive(target));

        AssertRoundTripsAndContinues(runtime);

        ConveyorOwnedPiece[] damaged = Pieces(runtime).Where(row =>
            runtime.Simulation.Entities.GetComponent<HealthState>(row.Entity).Fraction < 1).ToArray();
        Assert.NotEmpty(damaged);
        foreach (ConveyorOwnedPiece row in damaged)
            ExecuteConveyor(runtime, EditRequest(row, ConveyorEdit.Repair));
        Assert.All(Pieces(runtime), row =>
            Assert.Equal(1, runtime.Simulation.Entities.GetComponent<HealthState>(row.Entity).Fraction));
        AssertRoundTripsAndContinues(runtime);

        // Retention commands stop fresh input while the finite in-flight cargo drains.
        // All setup after tick zero is replayable; no inventory or health state is patched.
        EntityId core = runtime.GetBase(Player).CommandCore;
        InventoryId stock = runtime.GetBase(Player).StartingInventory;
        foreach (ResourceId resource in ConveyorSimulationSystem.EligibleResources)
        {
            double retain = Math.Max(1, runtime.Inventories.GetQuantity(stock, resource));
            var policy = PlayerLogisticsActionCommand.SetStockPolicy(Player, core, resource,
                0, retain, retain, LogisticsStockPriority.Normal, true, runtime.Simulation.CurrentTick);
            Submit(runtime, policy);
            Assert.True(policy.Accepted, policy.FailureReason.ToString());
        }
        RunUntil(runtime, match => !HasCargo(match));

        ConveyorOwnedPiece adapter = Assert.Single(Carriers(runtime), row =>
            row.Piece.Placement.Module == ConveyorModule.InputAdapter);
        InventoryId adapterBuffer = runtime.Simulation.Entities.GetComponent<ConveyorFlowState>(adapter.Entity).Buffer;
        ExecuteConveyor(runtime, EditRequest(adapter, ConveyorEdit.Replace));
        ConveyorOwnedPiece newAdapter = Assert.Single(Carriers(runtime), row =>
            row.Piece.Placement.Module == ConveyorModule.InputAdapter);
        Assert.NotEqual(adapter.Entity, newAdapter.Entity);
        Assert.Equal(core, newAdapter.Piece.Attachment);
        Assert.False(runtime.Inventories.Contains(adapterBuffer));

        ConveyorOwnedPiece middle = Carriers(runtime).First(row => row.Piece.Placement.Module == ConveyorModule.Standard);
        InventoryId removedBuffer = runtime.Simulation.Entities.GetComponent<ConveyorFlowState>(middle.Entity).Buffer;
        ConveyorSocket[] sockets = middle.Piece.Placement.Sockets;
        ExecuteConveyor(runtime, EditRequest(middle, ConveyorEdit.Remove));
        Assert.False(runtime.Inventories.Contains(removedBuffer));
        Assert.Contains(Carriers(runtime), row =>
        {
            ConveyorFlowState flow = runtime.Simulation.Entities.GetComponent<ConveyorFlowState>(row.Entity);
            return !flow.InputA.IsValid || !flow.OutputA.IsValid;
        });
        ExecuteConveyor(runtime, new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            sockets[0].Position - Vector3.UnitY * 5.8f, sockets[^1].Position - Vector3.UnitY * 5.8f,
            middle.Piece.Placement.Orientation, middle.Piece.Placement.Tier, true));
        ConveyorOwnedPiece rebuilt = Assert.Single(Carriers(runtime), row =>
            row.Piece.Placement.Module == ConveyorModule.Standard &&
            Vector3.DistanceSquared(row.Piece.Placement.Pivot, middle.Piece.Placement.Pivot) < 0.0004f);
        Assert.NotEqual(middle.Entity, rebuilt.Entity);

        var policies = new List<EntityId>();
        foreach (EntityId entity in runtime.Simulation.Entities.Query<LogisticsStockPolicy>())
            if (runtime.Simulation.Entities.GetComponent<LogisticsStockPolicy>(entity).TargetEntity == core)
                policies.Add(entity);
        Assert.NotEmpty(policies);
        foreach (EntityId policy in policies)
        {
            var remove = PlayerLogisticsActionCommand.RemoveStockPolicy(Player, policy, runtime.Simulation.CurrentTick);
            Submit(runtime, remove);
            Assert.True(remove.Accepted, remove.FailureReason.ToString());
        }
        RunUntil(runtime, HasCargo);
        AssertRoundTripsAndContinues(runtime);

        IReadOnlyList<RecordedSimulationCommand> history = runtime.Replay.CaptureCommands();
        Assert.Contains(history, command => command.Kind == ReplayCommandKind.Tactical &&
            command.TacticalOperation == PlayerTacticalActionOperation.FireMissionCoordinate);
        Assert.Contains(history, command => command.Kind == ReplayCommandKind.ConveyorConstruction &&
            command.ConveyorRequest.Edit == ConveyorEdit.Repair);
        Assert.Contains(history, command => command.Kind == ReplayCommandKind.ConveyorConstruction &&
            command.ConveyorRequest.Edit == ConveyorEdit.Replace);
        Assert.Contains(history, command => command.Kind == ReplayCommandKind.ConveyorConstruction &&
            command.ConveyorRequest.Edit == ConveyorEdit.Remove);
    }

    private static void BuildNetwork(MatchRuntime runtime)
    {
        _ = Build(runtime, BuildingIds.PowerPlant, new(48, 0, 64));
        EntityId destination = Build(runtime, BuildingIds.StorageDepot, new(145, 0, 96));
        EntityId source = runtime.GetBase(Player).CommandCore;
        IReadOnlyList<ConveyorBuildingPort> ports = ConveyorConstructionService.CapturePorts(runtime.Simulation.Entities, Player);
        ConveyorBuildingPort output = Assert.Single(ports, port => port.Building == source && !port.Input &&
            (!port.Adaptable || port.Key == "east.primary"));
        ConveyorBuildingPort input = Assert.Single(ports, port => port.Building == destination && port.Input &&
            (!port.Adaptable || port.Key == "west.primary"));
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            output.Position - Vector3.UnitY * 5.8f, input.Position - Vector3.UnitY * 5.8f,
            BuildingOrientation.North, 0, true, SourcePortBuilding: source, DestinationPortBuilding: destination);
        ConveyorPlan plan = runtime.Services.Conveyors.Preview(runtime.Simulation.Entities, Player, request, source);
        Assert.True(plan.IsValid, plan.Failure.ToString());
        double steel = TotalSteel(runtime);
        ExecuteConveyor(runtime, request);
        Assert.Equal(steel - plan.Steel, TotalSteel(runtime), precision: 8);
        Assert.Contains(runtime.Services.RegisteredSystemTypes, type => type == typeof(ConveyorSimulationSystem));
    }

    private static EntityId Build(MatchRuntime runtime, BuildingId building, Vector3 position)
    {
        var command = new BuildCommand(Player, building, position, BuildingOrientation.North,
            runtime.GetBase(Player).CommandCore, runtime.Simulation.CurrentTick);
        Submit(runtime, command);
        Assert.True(runtime.Services.BuildingCommands.TryGetLastResult(Player, out BuildCommandResult result));
        Assert.True(result.Accepted, $"{building}: {result.RejectionReason}, {result.PlacementFailure}");
        RunUntil(runtime, match => match.Simulation.Entities.HasComponent<CompletedBuilding>(result.CreatedSite));
        return result.CreatedSite;
    }

    private static void ExecuteConveyor(MatchRuntime runtime, ConveyorBuildRequest request)
    {
        var command = new ConveyorConstructionCommand(runtime.Services.Conveyors, Player, request,
            runtime.GetBase(Player).CommandCore, runtime.Simulation.CurrentTick);
        Submit(runtime, command);
        Assert.True(command.Accepted, $"{request.Edit}/{request.Module}: {command.Result}");
    }

    private static ConveyorBuildRequest EditRequest(ConveyorOwnedPiece row, ConveyorEdit edit) =>
        new(edit, row.Piece.Placement.Module, row.Piece.Placement.Pivot, row.Piece.Placement.Pivot,
            row.Piece.Placement.Orientation, row.Piece.Placement.Tier, true, Target: row.Entity);

    private static void Submit(MatchRuntime runtime, ISimulationCommand command)
    {
        runtime.Simulation.SubmitCommand(command, runtime.Simulation.CurrentTick.Next(),
            new SimulationCommandSource(Player.Value));
        runtime.Simulation.AdvanceOneTick();
    }

    private static void RunUntil(MatchRuntime runtime, Func<MatchRuntime, bool> condition)
    {
        bool completed = runtime.RunUntil(condition, maximumTicks: 500,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(completed, string.Join("; ", Carriers(runtime).Select(row =>
            $"{row.Entity} {row.Piece.Placement.Module}: {row.Status}, cargo={BufferQuantity(runtime, row.Entity)}, " +
            $"power={runtime.Simulation.Entities.GetComponent<PowerConsumer>(row.Entity).SupplyFraction}, " +
            $"cause={row.Flow.GetValueOrDefault().Cause}")));
    }

    private static bool HasCargo(MatchRuntime runtime) =>
        Carriers(runtime).Any(row => BufferQuantity(runtime, row.Entity) > 0);

    private static double BufferQuantity(MatchRuntime runtime, EntityId entity) =>
        runtime.Inventories.GetResourceQuantities(runtime.Simulation.Entities
            .GetComponent<ConveyorFlowState>(entity).Buffer).Sum(resource => resource.Quantity);

    private static ConveyorOwnedPiece[] Carriers(MatchRuntime runtime) =>
        Pieces(runtime).Where(row => row.Flow.HasValue).ToArray();

    private static IReadOnlyList<ConveyorOwnedPiece> Pieces(MatchRuntime runtime) =>
        ConveyorConstructionService.Capture(runtime.Simulation.Entities, Player, runtime.Inventories);

    private static double TotalSteel(MatchRuntime runtime) => runtime.Inventories.CaptureSnapshot().Inventories
        .Sum(inventory => inventory.Resources.Where(resource => resource.ResourceId == ResourceIds.Steel)
            .Sum(resource => resource.Quantity));

    private static void AssertRoundTripsAndContinues(MatchRuntime original)
    {
        MatchSaveData save = MatchPersistenceSerializer.DeserializeSave(
            MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(original)));
        MatchReplayData replay = MatchPersistenceSerializer.DeserializeReplay(
            MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(original)));
        using MatchRuntime restored = MatchPersistenceService.Restore(save, ResolveComposition);
        using MatchRuntime playback = MatchPersistenceService.PlayReplay(replay, ResolveComposition);
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        Assert.Equal(save.StateSha256, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        foreach (var recovered in new[] { restored, playback })
        {
            Assert.NotEqual(original.Simulation.SessionId, recovered.Simulation.SessionId);
            var expectedEvents = original.Progress.CaptureFirst(Player, original.Simulation.CurrentTick);
            Assert.Equal(expectedEvents.Select(item => item with { SessionId = recovered.Simulation.SessionId }),
                recovered.Progress.CaptureFirst(Player, recovered.Simulation.CurrentTick));
        }
        foreach (ulong ticks in ContinuationTicks)
        {
            original.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            restored.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            playback.Simulation.RunTicks(ticks, TestContext.Current.CancellationToken);
            string expected = MatchAuthoritativeSnapshot.Capture(original).ComputeSha256();
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
            Assert.Equal(expected, MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256());
        }
    }

    private static MatchRuntime CreateRuntime(ulong seed,bool sloped=false) => MatchRuntime.Create(
        CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, seed) with
        {
            Composition = CreateComposition(sloped),
            Participants = CentralDivideScenario.CreateDefaultParticipants(false, false),
            Scenario = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Gameplay) with
            {
                NavigationCellSizeMeters = 4
            }
        }, TestContext.Current.CancellationToken);

    private static MatchComposition ResolveComposition(string key) => key == CompositionKey
        ? CreateComposition() : key==CompositionKey+".sloped"?CreateComposition(true):
        throw new InvalidOperationException($"Unknown conveyor composition: {key}");

    private static MatchComposition CreateComposition(bool sloped=false)
    {
        MatchComposition original = CentralDivideScenario.CreateComposition();
        BattlefieldStartPosition[] starts =
        [
            new(Player, new(80, 0, 112), new(80, 0, 96),
                new AxisAlignedBounds(new(0, -10, 0), new(256, 50, 256))),
            new(new PlayerId(2), new(432, 0, 400), new(432, 0, 416),
                new AxisAlignedBounds(new(256, -10, 256), new(512, 50, 512)))
        ];
        var battlefield = new BattlefieldDefinition(
            new BattlefieldMapMetadata("conveyor-recovery", "Conveyor recovery", 512, 512, 2),
            starts, [], [], [], [], [], [],
            starts.Select(start => new BattlefieldObjectiveDefinition($"core.{start.Player.Value}",
                start.Player, start.CommandCorePosition)).ToArray(), [], original.Battlefield.TerrainVisual);
        return original with
        {
            Key = sloped?CompositionKey+".sloped":CompositionKey,
            Battlefield = battlefield,
            CreateTerrain = _ => CreateTerrain(sloped),
            CreateStartingBase = CreateStartingBase
        };
    }

    // A bounded authored test map and a starting artillery unit make damage reproducible.
    // Catalogs, standard stock, construction prices and every runtime system remain active.
    private static SkirmishStartingBase CreateStartingBase(EntityRegistry entities, InventoryStore inventories,
        UnitFactory factory, TerrainWorld terrain, BattlefieldStartPosition start, MatchContent content,
        SkirmishStartingStock stock)
    {
        SkirmishStartingBase side = CentralDivideStartingBaseFactory.Create(entities, inventories, factory,
            terrain, start, stock, content.Units);
        if (start.Player != Player) return side;
        UnitDefinition artillery = content.Units[UnitIds.MobileArtillery];
        Vector3 position = start.CommandCorePosition + new Vector3(0, artillery.Movement.HeightOffset, 140);
        EntityId unit = factory.Create(artillery, position, start.Player);
        return side with { StartingUnits = [.. side.StartingUnits, unit] };
    }

    private static TerrainWorld CreateTerrain(bool sloped=false)
    {
        var grid = new WorldGridSettings { ChunkSizeMeters = 256, HeightSamplesPerSide = 17 };
        var chunks = new List<TerrainChunk>();
        for (int x = 0; x < 2; x++)
        for (int z = 0; z < 2; z++)
        {
            var heights=new float[grid.HeightSamplesPerSide*grid.HeightSamplesPerSide];
            if(sloped)
                for(int row=0;row<grid.HeightSamplesPerSide;row++)
                for(int column=0;column<grid.HeightSamplesPerSide;column++)
                    heights[row*grid.HeightSamplesPerSide+column]=MathF.Max(0,(x*256+column*16-96)*.025f);
            chunks.Add(new TerrainChunk(new ChunkCoordinate(x, z),
                new TerrainHeightfield(grid.HeightSamplesPerSide, grid.ChunkSizeMeters,
                    heights)));
        }
        return new TerrainWorld(grid, chunks);
    }
}
