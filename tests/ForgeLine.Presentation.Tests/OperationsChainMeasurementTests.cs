using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OperationsChainMeasurementTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    [Fact]
    public void DenseOwnedInventoryFixtureKeepsProjectionReadOnlyAndCompositionBounded()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var interaction = new PresentationInteractionState();
        interaction.SetOperationsOpen(true);
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction);
        var probe = new ContextProbe();
        scenario.Simulation.RegisterTickObserver(probe);
        var entities = scenario.Simulation.Entities;
        var network = new PowerNetworkId(999);
        EntityId generator = entities.CreateEntity();
        entities.AddComponent(generator, new PowerNetworkMembership(network));
        entities.AddComponent(generator, new PowerGenerator(1000));
        EntityId root = default;
        for (int index = 0; index < 1000; index++)
        {
            EntityId entity = entities.CreateEntity();
            var inventory = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
            entities.AddComponent(entity, new ControllableEntity(new(1), ControllableEntityCategory.Building));
            entities.AddComponent(entity, new InventoryStorage(inventory));
            entities.AddComponent(entity, new WorldTransform(new Vector3(index % 32 * 3, 0, index / 32 * 3),
                Quaternion.Identity, Vector3.One));
            entities.AddComponent(entity, new VisualIdentity(1));
            if (index != 0) continue;
            root = entity;
            entities.AddComponent(entity, new PowerNetworkMembership(network));
            entities.AddComponent(entity, new PowerConsumer(10));
            var output = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
            entities.AddComponent(entity, new ProductionFacility(inventory, output, ProductionCapability.SteelProcessing, SimulationTick.Zero));
            scenario.Simulation.SubmitCommand(new QueueProductionCommand(entity, RecipeIds.Steel, SimulationTick.Zero), new(1));
        }
        interaction.SetSelection([root]);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        Assert.Equal(ProductionBlockReason.NoInput, entities.GetComponent<ProductionFacility>(root).BlockReason);
        var extraction = new PresentationExtractionContext(scenario, new(1), interaction,
            new PlayerCommandGateway(scenario.Simulation, scenario.Services.BuildingCommands, scenario.BattlefieldRuntime.MatchStateEntity));
        var instances = snapshot.Instances.ToArray();
        EntityId[] selection = [root];
        string hash = MatchAuthoritativeSnapshot.Capture(scenario).ComputeSha256();
        var baseline = Measure(() => _ = OperationsDiagnosisSnapshot.Capture(snapshot));
        var candidate = Measure(() => _ = OperationsChainCapture.Capture(probe.Context!, extraction,
            snapshot.PlayerActions, snapshot.PlayerExperience, instances, selection));
        Assert.Equal(hash, MatchAuthoritativeSnapshot.Capture(scenario).ComputeSha256());
        Assert.InRange(snapshot.Diagnosis!.Rows.Count, 1, OperationsDiagnosisSnapshot.MaximumRows);

        var graphics = new SelectionOverlayRenderingTests.RecordingContext { Width = 1600, Height = 900 };
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var information = RtsInformationLayerView.Empty with { SelectedEntities = selection };
        var context = new GameplayHudRenderContext(graphics, new RtsCamera(), snapshot, default, information,
            default, default, default, CombatGroupOverviewView.Empty, default, layout, 96, 1, null,
            Operations: new(true, Selected: root, Session: snapshot.SessionId));
        var overlay = new OperationsWorldOverlay();
        var composition = Measure(() => overlay.Build(context));
        Assert.Equal(0, composition.BytesPerOperation);
        Assert.InRange(overlay.Markers.Length, 0, OperationsWorldOverlay.MaximumMarkers);
        Assert.InRange(overlay.Links.Length, 0, OperationsWorldOverlay.MaximumLinks);

        string? path = Environment.GetEnvironmentVariable("FORGELINE_OPERATIONS_MEASUREMENTS");
        if (!string.IsNullOrWhiteSpace(path))
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
                LogicalProcessors = Environment.ProcessorCount, DenseOwnedFacilities = 1000,
                Instances = instances.Length, Tick = snapshot.Tick.Value, Rows = snapshot.Diagnosis.Rows.Count,
                Links = snapshot.Diagnosis.Links.Count, Warmups = 32, Samples = 128,
                BaselineSelectedDiagnosis = baseline, BoundedChainProjection = candidate,
                CopiedWorldOverlayComposition = composition,
                Notes = "Single in-process observations; fixture adds no conveyor network. CPU composition is not GPU, frame-rate or human readability acceptance."
            }, ReportOptions));
    }

    private static Measurement Measure(Action action)
    {
        for (int index = 0; index < 32; index++) action();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int index = 0; index < 128; index++) action();
        return new(Stopwatch.GetElapsedTime(start).TotalMilliseconds / 128,
            (GC.GetAllocatedBytesForCurrentThread() - bytes) / 128);
    }

    private readonly record struct Measurement(double MillisecondsPerOperation, long BytesPerOperation);
    private sealed class ContextProbe : ISimulationTickObserver
    {
        public SimulationContext? Context { get; private set; }
        public void OnTickCompleted(SimulationContext context) => Context = context;
    }
}
