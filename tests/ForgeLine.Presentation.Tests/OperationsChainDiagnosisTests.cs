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
    public void AmmunitionWaitsForElectronicsWhoseSilicateInputRecoversThroughAPaidConnection()
    {
        using var runtime = CreateRuntime();
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(runtime, interaction);
        _ = Build(runtime, BuildingIds.PowerPlant, new(48, 0, 48));
        EntityId electronics = Build(runtime, BuildingIds.ElectronicsPlant, new(63, 0, 112));
        EntityId ammunition = Build(runtime, BuildingIds.AmmunitionPlant, new(97, 0, 112));
        EntityId core = runtime.GetBase(Player).CommandCore;
        var input = runtime.Simulation.Entities.GetComponent<ProductionFacility>(ammunition).InputInventory;
        // Fixture setup relocates real finite starting Steel; no stock is created.
        Assert.True(runtime.Inventories.Transfer(runtime.GetBase(Player).StartingInventory,
            input, ResourceIds.Steel, 30).Succeeded);
        Connect(runtime, electronics, ammunition);
        Submit(runtime, PlayerProductionActionCommand.Queue(Player, electronics, RecipeIds.Electronics, runtime.Simulation.CurrentTick));
        Submit(runtime, PlayerProductionActionCommand.Queue(Player, ammunition, RecipeIds.Ammunition, runtime.Simulation.CurrentTick));

        interaction.SetSelection([ammunition]); interaction.SetOperationsOpen(true);
        runtime.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var stopped));
        var diagnosis = Assert.IsType<OperationsDiagnosisSnapshot>(stopped.Diagnosis);
        Assert.Equal(ammunition, diagnosis.Root);
        Assert.Contains(diagnosis.Rows, row => row.Entity == electronics &&
            row.Subject == OperationsDiagnosisSubject.ProductionInput && row.Resource == ResourceIds.Silicates &&
            row.FreeQuantity == 0 && row.PrimaryReason == OperationsDiagnosisReason.NoInput);
        Assert.Contains(diagnosis.Links, link => link.Source == electronics &&
            link.Kind == OperationsDiagnosisLinkKind.ConveyorConnection && !link.RequestId.IsSpecified);
        Assert.Contains(diagnosis.Rows, row => row.Source == electronics &&
            row.Resource == ResourceIds.Electronics && row.SourceFreeQuantity == 0);
        string hash = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        _ = diagnosis.TraceUpstream(ammunition);
        _ = diagnosis.ToDiagnosticLines();
        Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256());

        Connect(runtime, core, electronics);
        Assert.True(runtime.RunUntil(match => match.Inventories.GetQuantity(match.Simulation.Entities
            .GetComponent<ProductionFacility>(electronics).InputInventory, ResourceIds.Silicates) >= 10,
            100, TestContext.Current.CancellationToken));
        Assert.True(buffer.TryReadLatest(out var connected));
        Assert.Contains(connected.Diagnosis!.Rows, row => row.PrimaryReason == OperationsDiagnosisReason.NoPower);
        _ = Build(runtime, BuildingIds.PowerPlant, new(96, 0, 48));
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
        Assert.Contains(diagnosis.Rows, row => row.Entity == electronics && row.Resource == ResourceIds.Silicates &&
            row.FreeQuantity == 0);
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
        var output = Assert.Single(ports, port => port.Building == source && !port.Input);
        var input = Assert.Single(ports, port => port.Building == destination && port.Input);
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
