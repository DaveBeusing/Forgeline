using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OperationsChainDiagnosisTests
{
    private static readonly PlayerId Player = new(1);



    [Fact]
    public void FuelShortageDoesNotBorrowAnExistingAmmunitionConveyorConnection()
    {
        using var runtime = CreateRuntime();
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(runtime, interaction);
        _ = Build(runtime, BuildingIds.PowerPlant, new(48, 0, 48));
        EntityId depot = Build(runtime, BuildingIds.SupplyDepot, new(66, 0, 112));
        Connect(runtime, runtime.GetBase(Player).CommandCore, depot);
        Submit(runtime, PlayerLogisticsActionCommand.SetStockPolicy(Player, depot, ResourceIds.Fuel,
            10, 20, 30, LogisticsStockPriority.Normal, true, runtime.Simulation.CurrentTick));
        interaction.SetSelection([depot]);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var diagnosis = Assert.IsType<OperationsDiagnosisSnapshot>(snapshot.Diagnosis);
        Assert.Contains(ConveyorConstructionService.Capture(runtime.Simulation.Entities, Player),
            row => row.Flow is { } flow && flow.OutputA == depot);
        Assert.Contains(diagnosis.Rows, row => row.Entity == depot && row.Resource == ResourceIds.Fuel && row.Demand > 0);
        Assert.DoesNotContain(diagnosis.Links, link => link.Kind == OperationsDiagnosisLinkKind.ConveyorConnection &&
            link.Resource == ResourceIds.Fuel && link.Destination == depot);
        Assert.DoesNotContain(diagnosis.Links, link => link.Kind == OperationsDiagnosisLinkKind.ConveyorConnection && link.Resource == ResourceIds.Fuel);
        Assert.All(diagnosis.Links.Where(link => link.Kind == OperationsDiagnosisLinkKind.ConveyorConnection),
            link => Assert.Equal(ResourceIds.Ammunition, link.Resource));
        var buffers = ConveyorConstructionService.Capture(runtime.Simulation.Entities, Player).Where(row => row.Flow.HasValue)
            .Select(row => row.Flow!.Value.Buffer).ToHashSet();
        Assert.DoesNotContain(diagnosis.Rows, row => row.SourceFreeQuantity.HasValue && row.Resource == ResourceIds.Fuel && buffers.Contains(row.Inventory));
    }
    [Fact]
    public void FuelDemandDoesNotInventAConveyorOutputFromAnAmmunitionPlant()
    {
        using var runtime = CreateRuntime();
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(runtime, interaction);
        _ = Build(runtime, BuildingIds.PowerPlant, new(48, 0, 48));
        EntityId ammunition = Build(runtime, BuildingIds.AmmunitionPlant, new(63, 0, 112));
        // Primary sockets are x=72 and x=88 on the authored 4m route grid.
        EntityId storage = Build(runtime, BuildingIds.StorageDepot, new(95, 0, 112));
        Connect(runtime, ammunition, storage);
        Submit(runtime, PlayerLogisticsActionCommand.SetStockPolicy(Player, storage, ResourceIds.Fuel,
            10, 20, 30, LogisticsStockPriority.Normal, true, runtime.Simulation.CurrentTick));
        Submit(runtime, PlayerLogisticsActionCommand.SetStockPolicy(Player, storage, ResourceIds.Ammunition,
            10, 20, 30, LogisticsStockPriority.Normal, true, runtime.Simulation.CurrentTick));
        interaction.SetSelection([storage]);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var diagnosis = Assert.IsType<OperationsDiagnosisSnapshot>(snapshot.Diagnosis);
        Assert.Contains(diagnosis.Rows, row => row.Entity == storage && row.Resource == ResourceIds.Fuel && row.Demand > 0);
        Assert.DoesNotContain(diagnosis.Links, link => link.Kind == OperationsDiagnosisLinkKind.ConveyorConnection &&
            link.Resource == ResourceIds.Fuel && link.Source == ammunition);
        Assert.Contains(diagnosis.Links, link => link.Kind == OperationsDiagnosisLinkKind.ConveyorConnection &&
            link.Resource == ResourceIds.Ammunition && link.Source == ammunition);
        Assert.Contains(diagnosis.Links, link => link.Kind == OperationsDiagnosisLinkKind.ConveyorConnection &&
            link.Resource == ResourceIds.Fuel && link.Destination == storage &&
            runtime.Simulation.Entities.HasComponent<ConveyorFlowState>(link.Source));
    }
    [Fact]
    public void UpstreamRequestWithNoObservedSourceRetainsItsRealFailureWithoutInventingALink()
    {
        using var runtime = CreateRuntime();
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(runtime, interaction);
        _ = Build(runtime, BuildingIds.PowerPlant, new(48, 0, 48));
        _ = Build(runtime, BuildingIds.PowerPlant, new(96, 0, 48));
        EntityId electronics = Build(runtime, BuildingIds.ElectronicsPlant, new(63, 0, 112));
        EntityId ammunition = Build(runtime, BuildingIds.AmmunitionPlant, new(97, 0, 112));
        Connect(runtime, electronics, ammunition);
        InventoryId core = runtime.GetBase(Player).StartingInventory;
        Assert.True(runtime.Inventories.Transfer(core,
            runtime.Simulation.Entities.GetComponent<ProductionFacility>(ammunition).InputInventory, ResourceIds.Steel, 30).Succeeded);
        double silicates = runtime.Inventories.GetAvailableQuantity(core, ResourceIds.Silicates);
        Assert.True(runtime.Inventories.Reserve(core, ResourceIds.Silicates, silicates).Succeeded);
        Submit(runtime, PlayerProductionActionCommand.Queue(Player, electronics, RecipeIds.Electronics, runtime.Simulation.CurrentTick));
        Submit(runtime, PlayerProductionActionCommand.Queue(Player, ammunition, RecipeIds.Ammunition, runtime.Simulation.CurrentTick));
        Submit(runtime, PlayerLogisticsActionCommand.SetStockPolicy(Player, electronics, ResourceIds.Silicates,
            10, 20, 30, LogisticsStockPriority.Normal, true, runtime.Simulation.CurrentTick));
        interaction.SetSelection([ammunition]);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var actual = Assert.Single(runtime.AutomatedDistribution.LastDebugSnapshot.Requests, request => request.ResourceId == ResourceIds.Silicates);
        Assert.Equal(LogisticsTransportRequestFailureReason.NoSourceSurplus, actual.FailureReason);
        var diagnosis = Assert.IsType<OperationsDiagnosisSnapshot>(snapshot.Diagnosis);
        var row = Assert.Single(diagnosis.Rows, observed => observed.Entity == electronics &&
            observed.Subject == OperationsDiagnosisSubject.TransportRequest && observed.RequestId == actual.RequestId);
        Assert.Equal(OperationsDiagnosisReason.NoSourceStock, row.PrimaryReason);
        Assert.Equal(actual.RequestedQuantity, row.OutstandingRequestQuantity);
        Assert.False(row.Source.IsValid);
        Assert.Null(row.SourceFreeQuantity);
        Assert.DoesNotContain(diagnosis.Links, link => link.RequestId == actual.RequestId);
    }
    [Fact]
    public void ActualClosedPaidCycleReportsCycleAndUnobservedResourceInsteadOfGuessedStock()
    {
        using var runtime = CreateRuntime();
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(runtime, interaction);
        _ = Build(runtime, BuildingIds.PowerPlant, new(48, 0, 48));
        BuildPiece(ConveyorModule.CurveLeft, new(160, 0, 168), BuildingOrientation.North);
        BuildPiece(ConveyorModule.CurveLeft, new(168, 0, 168), BuildingOrientation.East);
        BuildPiece(ConveyorModule.CurveLeft, new(168, 0, 160), BuildingOrientation.South);
        BuildPiece(ConveyorModule.CurveLeft, new(160, 0, 160), BuildingOrientation.West);
        BuildPiece(ConveyorModule.Standard, new(164, 0, 172), BuildingOrientation.East);
        BuildPiece(ConveyorModule.Standard, new(172, 0, 164), BuildingOrientation.South);
        BuildPiece(ConveyorModule.Standard, new(164, 0, 156), BuildingOrientation.West);
        BuildPiece(ConveyorModule.Standard, new(156, 0, 164), BuildingOrientation.North);
        EntityId first = ConveyorConstructionService.Capture(runtime.Simulation.Entities, Player)
            .Single(row => row.Piece.Placement.Module == ConveyorModule.CurveLeft &&
                row.Piece.Placement.Pivot == new Vector3(160, 0, 168)).Entity;
        interaction.SetSelection([first]);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var diagnosis = Assert.IsType<OperationsDiagnosisSnapshot>(snapshot.Diagnosis);
        Assert.True(diagnosis.HasCycle);
        Assert.Equal(0, diagnosis.OmittedSteps);
        Assert.Equal(8, diagnosis.Links.Count);
        Assert.All(diagnosis.Rows, row => Assert.Null(row.SourceFreeQuantity));
        Assert.All(diagnosis.Links, link => Assert.Equal(OperationsDiagnosisLinkKind.ConveyorConnection, link.Kind));

        void BuildPiece(ConveyorModule module, Vector3 position, BuildingOrientation orientation)
        {
            var command = new ConveyorConstructionCommand(runtime.Services.Conveyors, Player,
                new(ConveyorEdit.Build, module, position, position, orientation, 0, true),
                runtime.GetBase(Player).CommandCore, runtime.Simulation.CurrentTick);
            Submit(runtime, command);
            Assert.True(command.Accepted, command.Result.ToString());
        }
    }

    [Theory]
    [InlineData(97, false)]
    [InlineData(161, true)]
    public void AmmunitionWaitsForElectronicsWhoseSilicateInputRecoversThroughAPaidConnection(int ammunitionX, bool truncated)
    {
        using var runtime = CreateRuntime();
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(runtime, interaction);
        _ = Build(runtime, BuildingIds.PowerPlant, new(48, 0, 48));
        EntityId electronics = Build(runtime, BuildingIds.ElectronicsPlant, new(63, 0, 112));
        EntityId ammunition = Build(runtime, BuildingIds.AmmunitionPlant, new(ammunitionX, 0, 112));
        if (truncated) _ = Build(runtime, BuildingIds.PowerPlant, new(96, 0, 48));
        EntityId core = runtime.GetBase(Player).CommandCore;
        var input = runtime.Simulation.Entities.GetComponent<ProductionFacility>(ammunition).InputInventory;
        // Fixture setup relocates real finite starting Steel; no stock is created.
        Assert.True(runtime.Inventories.Transfer(runtime.GetBase(Player).StartingInventory,
            input, ResourceIds.Steel, 30).Succeeded);
        Connect(runtime, electronics, ammunition);
        Submit(runtime, PlayerProductionActionCommand.Queue(Player, ammunition, RecipeIds.Ammunition, runtime.Simulation.CurrentTick));

        interaction.SetSelection([ammunition]); interaction.SetOperationsOpen(true);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var idleSource));
        // Automatic electronics processing knows its recipe before any manual
        // queue command; report the actual missing silicate input immediately.
        if (!truncated)
            Assert.Contains(idleSource.Diagnosis!.Rows, row => row.Entity == electronics &&
                row.Subject == OperationsDiagnosisSubject.ProductionInput && row.Resource == ResourceIds.Silicates &&
                row.PrimaryReason == OperationsDiagnosisReason.NoInput);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var stopped));
        var diagnosis = Assert.IsType<OperationsDiagnosisSnapshot>(stopped.Diagnosis);
        Assert.Equal(ProductionBlockReason.NoInput, runtime.Simulation.Entities.GetComponent<ProductionFacility>(ammunition).BlockReason);
        Assert.Equal(ammunition, diagnosis.Root);
        if (truncated)
        {
            Assert.True(diagnosis.OmittedSteps > 0);
            Assert.DoesNotContain(diagnosis.Rows, row => row.Entity == electronics);
            Assert.InRange(diagnosis.TraceUpstream(ammunition).Count, 0, 8);
        }
        else
        {
        Assert.Contains(diagnosis.Rows, row => row.Entity == electronics &&
            row.Subject == OperationsDiagnosisSubject.ProductionInput && row.Resource == ResourceIds.Silicates &&
            row.FreeQuantity == 0 && row.PrimaryReason == OperationsDiagnosisReason.NoInput);
        Assert.Contains(diagnosis.Links, link => link.Source == electronics &&
            link.Kind == OperationsDiagnosisLinkKind.ConveyorConnection && !link.RequestId.IsSpecified);
        Assert.Contains(diagnosis.Rows, row => row.Source == electronics &&
            row.Resource == ResourceIds.Electronics && row.SourceFreeQuantity == 0);
            var originalVisual = runtime.Simulation.Entities.GetComponent<VisualIdentity>(electronics);
            runtime.Simulation.Entities.RemoveComponent<VisualIdentity>(electronics);
            runtime.Simulation.AdvanceOneTick();
            Assert.True(buffer.TryReadLatest(out var missing));
            Assert.DoesNotContain(missing.Diagnosis!.Rows, row => row.Entity == electronics || row.Source == electronics);
            Assert.DoesNotContain(missing.Diagnosis.Links, link => link.Source == electronics);
            runtime.Simulation.Entities.AddComponent(electronics, originalVisual);
            runtime.Simulation.AdvanceOneTick();
        }
        string hash = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        _ = diagnosis.TraceUpstream(ammunition);
        _ = diagnosis.ToDiagnosticLines();
        Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256());

        Connect(runtime, core, electronics);
        Assert.True(runtime.RunUntil(match => match.Inventories.GetQuantity(match.Simulation.Entities
            .GetComponent<ProductionFacility>(electronics).InputInventory, ResourceIds.Silicates) >= 10,
            100, TestContext.Current.CancellationToken));
        Assert.True(buffer.TryReadLatest(out var connected));
        if (!truncated)
        {
            Assert.Contains(connected.Diagnosis!.Rows, row => row.PrimaryReason == OperationsDiagnosisReason.NoPower);
            interaction.SetSelection([electronics]);
            runtime.Simulation.AdvanceOneTick();
            Assert.True(buffer.TryReadLatest(out var powerOnly));
            Assert.False(powerOnly.Diagnosis!.Root.IsValid);
            Assert.Equal(OperationsDiagnosisReason.NoPower, OperationsActionGuidance.ForEntity(powerOnly.Diagnosis, electronics)!.Value.PrimaryReason);
            interaction.SetSelection([ammunition]);
            runtime.Simulation.AdvanceOneTick();
        }
        if (!truncated) _ = Build(runtime, BuildingIds.PowerPlant, new(96, 0, 48));
        Assert.True(runtime.RunUntil(match => match.Simulation.Entities
            .GetComponent<ProductionFacility>(ammunition).CompletedCycles > 0, 1000, TestContext.Current.CancellationToken),
            string.Join("; ", new[] { electronics, ammunition }.Select(entity =>
            {
                var facility = runtime.Simulation.Entities.GetComponent<ProductionFacility>(entity);
                return entity + " " + facility.Status + " " + facility.BlockReason + " input " +
                    string.Join(",", runtime.Inventories.GetResourceQuantities(facility.InputInventory)) +
                    " output " + string.Join(",", runtime.Inventories.GetResourceQuantities(facility.OutputInventory));
            })) + " / " + string.Join("; ", ConveyorConstructionService.Capture(runtime.Simulation.Entities, Player, runtime.Inventories)
                .Where(row => row.Flow.HasValue).Select(row => row.Entity + " " + row.Status + " " + row.Flow!.Value.Cause)));
        Assert.True(buffer.TryReadLatest(out var recovered));
        Assert.DoesNotContain(recovered.Diagnosis!.Rows, row => row.Entity == ammunition &&
            row.PrimaryReason == OperationsDiagnosisReason.NoInput);
        Assert.False(recovered.Diagnosis.Root.IsValid);
        Assert.Empty(recovered.Diagnosis.Links);
        if (!truncated)
            Assert.Contains(diagnosis.Rows, row => row.Entity == electronics && row.Resource == ResourceIds.Silicates && row.FreeQuantity == 0);
        interaction.SetSelection([new EntityId(ammunition.Index, ammunition.Generation + 1)]);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var recycled));
        Assert.Null(recycled.Diagnosis);
    }

    private static MatchRuntime CreateRuntime()
    {
        var original = CentralDivideScenario.CreateComposition();
        BattlefieldStartPosition[] starts =
        [
            new(Player, new(24, 0, 96), new(24, 0, 112), new AxisAlignedBounds(new(0, -10, 0), new(256, 50, 256))),
            new(new PlayerId(2), new(432, 0, 400), new(432, 0, 416), new AxisAlignedBounds(new(256, -10, 256), new(512, 50, 512)))
        ];
        var battlefield = new BattlefieldDefinition(new BattlefieldMapMetadata("operations-chain", "Operations chain", 512, 512, 2),
            starts, [], [], [], [], [], [],
            starts.Select(start => new BattlefieldObjectiveDefinition("core." + start.Player.Value,
                start.Player, start.CommandCorePosition)).ToArray(), [], original.Battlefield.TerrainVisual);
        var composition = original with { Key = "operations-chain.directorate.v1", Battlefield = battlefield,
            CreateStartingBase = static (entities, inventories, factory, terrain, start, content, stock) =>
            {
                var side = CentralDivideStartingBaseFactory.Create(entities, inventories, factory, terrain, start, stock, content.Units);
                int index = 0;
                foreach (EntityId unit in side.StartingUnits)
                {
                    var transform = entities.GetComponent<WorldTransform>(unit);
                    entities.SetComponent(unit, transform with { Position = new Vector3(start.CommandCorePosition.X + index++ * 8, transform.Position.Y, start.CommandCorePosition.Z + 100) });
                }
                return side;
            },
            CreateTerrain = static _ =>
            {
                var grid = new WorldGridSettings { ChunkSizeMeters = 256, HeightSamplesPerSide = 17 };
                var chunks = new List<TerrainChunk>();
                for (int x = 0; x < 2; x++) for (int z = 0; z < 2; z++)
                    chunks.Add(new TerrainChunk(new ChunkCoordinate(x, z), new TerrainHeightfield(17, 256, new float[289])));
                return new TerrainWorld(grid, chunks);
            } };
        return MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 4209) with
        { Composition = composition, Participants = CentralDivideScenario.CreateDefaultParticipants(false, false),
            Scenario = CentralDivideScenario.CreateSettings(MatchScenarioProfile.Gameplay) with { NavigationCellSizeMeters = 4 } },
            TestContext.Current.CancellationToken);
    }

    private static EntityId Build(MatchRuntime runtime, BuildingId id, Vector3 position)
    {
        Submit(runtime, new BuildCommand(Player, id, position, BuildingOrientation.North,
            runtime.GetBase(Player).CommandCore, runtime.Simulation.CurrentTick));
        Assert.True(runtime.Services.BuildingCommands.TryGetLastResult(Player, out var result));
        Assert.True(result.Accepted, result.RejectionReason + "/" + result.PlacementFailure);
        Assert.True(runtime.RunUntil(match => match.Simulation.Entities.HasComponent<CompletedBuilding>(result.CreatedSite),
            500, TestContext.Current.CancellationToken));
        return result.CreatedSite;
    }

    private static void Connect(MatchRuntime runtime, EntityId source, EntityId destination)
    {
        var ports = ConveyorConstructionService.CapturePorts(runtime.Simulation.Entities, Player);
        var output = Assert.Single(ports, port => port.Building == source && !port.Input &&
            (!port.Adaptable || port.Key == "east.primary"));
        var input = Assert.Single(ports, port => port.Building == destination && port.Input &&
            (!port.Adaptable || port.Key == "west.primary"));
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route, output.Position - Vector3.UnitY * 5.8f,
                input.Position - Vector3.UnitY * 5.8f, BuildingOrientation.North, 0, true,
                SourcePortBuilding: source, DestinationPortBuilding: destination);
        var command = new ConveyorConstructionCommand(runtime.Services.Conveyors, Player, request,
            runtime.GetBase(Player).CommandCore, runtime.Simulation.CurrentTick);
        Submit(runtime, command);
        var diagnostics = new List<string>();
        if (!command.Accepted)
        {
            var plan = runtime.Services.Conveyors.Preview(runtime.Simulation.Entities, Player, request, runtime.GetBase(Player).CommandCore);
            var query = new SpatialQueryBuffer();
            foreach (var part in plan.Pieces)
            {
                runtime.Services.SpatialIndex.QueryAabb(part.Bounds, query);
                foreach (EntityId other in query.Results)
                    diagnostics.Add(part.Module + " " + part.Pivot + " hits " + other + " " +
                        runtime.Simulation.Entities.GetComponent<WorldTransform>(other).Position);
            }
        }
        Assert.True(command.Accepted, command.Result + ": " + string.Join("; ", diagnostics));
    }

    private static void Submit(MatchRuntime runtime, ISimulationCommand command)
    {
        runtime.Simulation.SubmitCommand(command, runtime.Simulation.CurrentTick.Next());
        runtime.Simulation.AdvanceOneTick();
    }
}
