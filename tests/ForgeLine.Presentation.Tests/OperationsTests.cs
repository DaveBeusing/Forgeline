using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Logistics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OperationsTests
{
    [Theory]
    [InlineData(1)] [InlineData(10)] [InlineData(100)] [InlineData(1000)]
    public void LargeOwnedTopologyKeepsBoundedRoutesAndExcludesForeignLinks(int count)
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var interaction = new PresentationInteractionState(); interaction.SetOperationsOpen(true);
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction);
        var entities = scenario.Simulation.Entities;
        var owned = new HashSet<EntityId>();
        LogisticsNodeId previous = default;
        for (int i = 0; i < count + 1; i++)
        {
            var entity = entities.CreateEntity(); bool foreign = i == count;
            entities.AddComponent(entity, new ControllableEntity(new(foreign ? 2U : 1U), ControllableEntityCategory.Building));
            var inventory = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
            entities.AddComponent(entity, new InventoryStorage(inventory));
            if (!foreign) owned.Add(entity);
            var node = scenario.Logistics.AddNode(entity, new Vector3(i, 0, 0), LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.Storage);
            if (previous.IsSpecified) scenario.Logistics.AddEdge(previous, node, LogisticsTransportMode.GroundRoad, 1, 1, 10, enabled: i % 2 == 0);
            previous = node;
        }
        scenario.Simulation.AdvanceOneTick(); Assert.True(buffer.TryReadLatest(out var snapshot));
        var data = snapshot.Operations!;
        Assert.InRange(data.Facilities.Count, 1, OperationsSnapshot.MaximumFacilities);
        Assert.InRange(data.Routes.Count, 0, OperationsSnapshot.MaximumRoutes);
        Assert.True(data.FacilityCount >= count);
        Assert.All(data.Routes, route => { Assert.Contains(route.Source, owned); Assert.Contains(route.Destination, owned); });
        Assert.Equal(Math.Max(0, count - 1), data.RouteCount);
        Assert.All(data.Routes, route => Assert.Equal(10, route.CapacityPerSecond));
    }

    [Fact]
    public void RealFactoriesPublishDistinctBlocksAndMatchingQueueCounts()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var interaction = new PresentationInteractionState(); interaction.SetOperationsOpen(true);
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction);
        var entities = scenario.Simulation.Entities;
        var network = new PowerNetworkId(999);
        var generator = entities.CreateEntity();
        entities.AddComponent(generator, new PowerNetworkMembership(network));
        entities.AddComponent(generator, new PowerGenerator(1000));
        EntityId Make(bool powered)
        {
            var entity = entities.CreateEntity();
            var input = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
            var output = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
            entities.AddComponent(entity, new ControllableEntity(new(1), ControllableEntityCategory.Building));
            entities.AddComponent(entity, new ProductionFacility(input, output, ProductionCapability.SteelProcessing, SimulationTick.Zero));
            entities.AddComponent(entity, new PowerNetworkMembership(network));
            entities.AddComponent(entity, new PowerConsumer(10, enabled: powered));
            scenario.Simulation.SubmitCommand(new QueueProductionCommand(entity, RecipeIds.Steel, SimulationTick.Zero), new(1));
            return entity;
        }
        var inputBlocked = Make(true); var powerBlocked = Make(false);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot)); var data = snapshot.Operations!;
        var input = Assert.Single(data.Facilities, row => row.Entity == inputBlocked);
        var power = Assert.Single(data.Facilities, row => row.Entity == powerBlocked);
        Assert.Equal("NoInput", input.Cause); Assert.Equal("NoPower", power.Cause);
        Assert.Equal(1, input.QueueCount); Assert.Equal(1, power.QueueCount);
        Assert.Equal(snapshot.Tick, data.Tick);
    }

    [Fact]
    public void OutputFullAndBrownoutRemainAuthoritativeAndPauseUsesExistingCommand()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var interaction = new PresentationInteractionState(); interaction.SetOperationsOpen(true);
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction); var entities = scenario.Simulation.Entities;
        EntityId Make(uint network, double generation, double outputCapacity, bool inputs)
        {
            var generator = entities.CreateEntity(); entities.AddComponent(generator, new PowerNetworkMembership(new(network)));
            entities.AddComponent(generator, new PowerGenerator(generation));
            var input = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
            var output = scenario.Inventories.CreateInventory(new InventorySpecification(outputCapacity));
            if (inputs) scenario.Inventories.Add(input, ResourceIds.FerrousOre, 10);
            var entity = entities.CreateEntity();
            entities.AddComponent(entity, new ControllableEntity(new(1), ControllableEntityCategory.Building));
            entities.AddComponent(entity, new PowerNetworkMembership(new(network))); entities.AddComponent(entity, new PowerConsumer(10));
            entities.AddComponent(entity, new ProductionFacility(input, output, ProductionCapability.SteelProcessing, SimulationTick.Zero));
            return entity;
        }
        var full = Make(991, 100, 9, true); var brownout = Make(992, 5, 1000, false);
        var queue = new QueueProductionCommand(full, RecipeIds.Steel, SimulationTick.Zero);
        scenario.Simulation.SubmitCommand(queue, new(1));
        scenario.Simulation.SubmitCommand(new QueueProductionCommand(brownout, RecipeIds.Steel, SimulationTick.Zero), new(1));
        for (int i = 0; i < 40; i++) scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot)); var data = snapshot.Operations!;
        Assert.Equal("OutputFull", Assert.Single(data.Facilities, row => row.Entity == full).Cause);
        var constrained = Assert.Single(data.Facilities, row => row.Entity == brownout);
        Assert.Equal("NoPower", constrained.Cause); Assert.Equal(5, constrained.AllocatedPower);
        var paused = PlayerProductionActionCommand.SetPaused(new(1), queue.RequestEntity, true, scenario.Simulation.CurrentTick);
        scenario.Simulation.SubmitCommand(paused, scenario.Simulation.CurrentTick.Next()); scenario.Simulation.AdvanceOneTick();
        Assert.True(paused.Accepted); Assert.True(buffer.TryReadLatest(out var next));
        Assert.Equal("Paused", Assert.Single(next.Operations!.Facilities, row => row.Entity == full).Cause);
        Assert.Equal("OutputFull", Assert.Single(data.Facilities, row => row.Entity == full).Cause);
    }

    [Fact]
    public void ExtractionIsOptInOwnedAndRetainedWithoutAliasingInventory()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var interaction = new PresentationInteractionState();
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction);
        var entities = scenario.Simulation.Entities;
        InventoryId inventory = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
        scenario.Inventories.Add(inventory, ResourceIds.Steel, 123);
        EntityId local = Storage(1, inventory), duplicate = Storage(1, inventory), foreign = Storage(2, inventory), dead = Storage(1, inventory);
        entities.AddComponent(dead, new HealthState(0, 100));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var closed)); Assert.Null(closed.Operations);
        interaction.SetOperationsOpen(true);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var open));
        var data = Assert.IsType<OperationsSnapshot>(OperationsSnapshot.Resolve(open));
        Assert.Equal(open.Tick, data.Tick); Assert.Equal(open.SessionId, data.Session);
        Assert.Contains(data.Facilities, row => row.Entity == local);
        Assert.Contains(data.Facilities, row => row.Entity == duplicate);
        Assert.DoesNotContain(data.Facilities, row => row.Entity == foreign || row.Entity == dead);
        var quantity = Assert.Single(data.Resources, row => row.Resource == ResourceIds.Steel).Quantity;
        Assert.True(quantity >= 123);
        scenario.Inventories.Remove(inventory, ResourceIds.Steel, 23);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var next));
        Assert.Equal(quantity - 23, Assert.Single(next.Operations!.Resources, row => row.Resource == ResourceIds.Steel).Quantity, 6);
        Assert.Equal(quantity, Assert.Single(data.Resources, row => row.Resource == ResourceIds.Steel).Quantity);
        Assert.All(data.Resources, row => Assert.Null(row.NetRate));
        Assert.Throws<NotSupportedException>(() => ((IList<OperationsFacility>)data.Facilities)[0] = default);
        interaction.SetOperationsOpen(false); scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var disabled)); Assert.Null(disabled.Operations);
        EntityId Storage(uint owner, InventoryId inv)
        {
            var entity = entities.CreateEntity();
            entities.AddComponent(entity, new ControllableEntity(new(owner), ControllableEntityCategory.Building));
            entities.AddComponent(entity, new InventoryStorage(inv)); return entity;
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(10)] [InlineData(100)] [InlineData(1000)]
    public void SnapshotBoundsListsAndPreservesTotalAndGeneration(int count)
    {
        var input = Enumerable.Range(1, count).Select(i => Facility(new((uint)i, 7))).ToArray();
        var data = new OperationsSnapshot(new(1), new(9), new(1), input, [], [], count, 0);
        Assert.Equal(count, data.FacilityCount);
        Assert.Equal(Math.Min(count, OperationsSnapshot.MaximumFacilities), data.Facilities.Count);
        input[0] = Facility(new(1, 8)); Assert.Equal(new EntityId(1, 7), data.Facilities[0].Entity);
        Assert.Null(data.Facilities[0].InventoryQuantity); Assert.Null(data.Facilities[0].Utilization);
    }

    [Theory]
    [InlineData("NoInput", "INPUT STOCK")]
    [InlineData("NoPower", "ALLOCATED POWER")]
    [InlineData("OutputFull", "OUTPUT STORAGE")]
    [InlineData("NoRoute", "ROUTE UNAVAILABLE")]
    [InlineData("CapacitySaturated", "CAPACITY SATURATED")]
    public void CauseExplanationsStayWithinReportedSemantics(string cause, string text) =>
        Assert.Contains(text, OperationsSnapshotFactory.Explain(cause), StringComparison.Ordinal);

    [Fact]
    public void MismatchedTickSessionPlayerAndTerminalCapturesAreUnavailable()
    {
        var data = new OperationsSnapshot(new(1), new(4), new(1), [], [], [], 0, 0);
        Assert.NotNull(OperationsSnapshot.Resolve(Snapshot(data)));
        Assert.Null(OperationsSnapshot.Resolve(Snapshot(data, tick: 5)));
        Assert.Null(OperationsSnapshot.Resolve(Snapshot(data, session: 2)));
        Assert.Null(OperationsSnapshot.Resolve(Snapshot(data, player: 2)));
        Assert.Null(OperationsSnapshot.Resolve(Snapshot(data, terminal: true)));
    }

    [Theory]
    [InlineData(1024, 720, 96, 1)] [InlineData(1600, 900, 144, 1)]
    [InlineData(1920, 1080, 192, 2)] [InlineData(3840, 2160, 192, 1)]
    public void SharedControlsFitSafeAreaAndKeepDockMinimapAvailable(int width, int height, uint dpi, float uiScale)
    {
        var layout = GameplayHudLayout.Create(width, height, dpi, uiScale);
        var panel = OperationsLayout.Panel(layout);
        Assert.False(panel.Intersects(layout.ActionDock)); Assert.False(panel.Intersects(layout.Minimap));
        Assert.False(panel.Intersects(layout.SelectionInspector));
        for (int index = 0; index < 12; index++)
        {
            var control = OperationsLayout.Control(layout, index);
            Assert.True(panel.Contains(new(control.X, control.Y)));
            Assert.True(panel.Contains(new(control.Right, control.Bottom)));
        }
        Assert.False(OperationsLayout.Entry(layout).Intersects(layout.SecondaryView));
        var data = new OperationsSnapshot(new(1), new(4), new(1), [Facility(new(1, 1))], [], [], 1, 0);
        using var device = new SelectionOverlayRenderingTests.RecordingDevice();
        using var renderer = new OperationsHudSurface(device);
        var graphics = new SelectionOverlayRenderingTests.RecordingContext { Width = width, Height = height };
        var context = new GameplayHudRenderContext(graphics, new RtsCamera(), Snapshot(data), default, default, default, default,
            default, CombatGroupOverviewView.Empty, default, layout, dpi, uiScale, null, Operations: new(true, Selected: new(1, 1), Session: new(1)));
        renderer.Render(context);
        Assert.InRange(renderer.LastRenderedVertexCount, 100, OperationsHudSurface.MaxVertices);
        Assert.All(device.Buffer!.Vertices, vertex =>
        { Assert.InRange(vertex.Position.X, -1, 1); Assert.InRange(vertex.Position.Y, -1, 1); });
    }

    [Fact]
    public void ShortClicksSelectAndOpenExistingControlsWithoutSubmittingCommands()
    {
        var entity = new EntityId(3, 2);
        var data = new OperationsSnapshot(new(1), new(4), new(1), [Facility(entity)], [], [], 1, 0);
        var snapshot = Snapshot(data); var controller = new OperationsController();
        var input = new InputState(); var interaction = new PresentationInteractionState();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        controller.Update(input, snapshot, layout, interaction);
        Click(input, OperationsLayout.Entry(layout)); controller.Update(input, snapshot, layout, interaction);
        Assert.True(controller.View.Open);
        Click(input, OperationsLayout.Control(layout, 3)); controller.Update(input, snapshot, layout, interaction);
        Assert.Equal(entity, controller.View.Selected);
        Click(input, OperationsLayout.Control(layout, 10));
        var result = controller.Update(input, snapshot, layout, interaction);
        Assert.Equal(entity, result.Navigate); Assert.Equal(PlayerActionPanelMode.Production, result.Controls);
        Assert.False(controller.Update(input, snapshot, layout, interaction).Navigate.IsValid);
        var dock = new PlayerActionPanelController(); dock.OpenOperationsControls(result.Controls);
        Assert.Equal(PlayerActionPanelMode.Production, dock.Mode); Assert.False(dock.TryTakeRequest(out _));
    }

    [Fact]
    public void ModalResizeAndSessionChangesConsumeRetainedEdges()
    {
        var input = new InputState(); var interaction = new PresentationInteractionState();
        var controller = new OperationsController(); var snapshot = Snapshot();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        controller.Update(input, snapshot, layout, interaction);
        Click(input, OperationsLayout.Entry(layout)); controller.Update(input, snapshot, layout, interaction, blocked: true);
        controller.Update(input, snapshot, layout, interaction); Assert.False(controller.View.Open);
        Click(input, OperationsLayout.Entry(layout)); controller.Update(input, snapshot, layout, interaction);
        Assert.True(controller.View.Open);
        var changed = GameplayHudLayout.Create(1920, 1080, 144);
        Click(input, OperationsLayout.Control(layout, 0)); controller.Update(input, snapshot, changed, interaction);
        Assert.True(controller.View.Open);
        controller.Update(input, Snapshot(session: 2), changed, interaction); Assert.False(controller.View.Open);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(10)] [InlineData(100)] [InlineData(1000)]
    public void OverviewAndRenderInputHaveZeroWarmAllocation(int count)
    {
        var facilities = Enumerable.Range(1, count).Select(i => Facility(new((uint)i, 1))).ToArray();
        var data = new OperationsSnapshot(new(1), new(4), new(1), facilities, [], [], count, 0);
        var snapshot = Snapshot(data); var layout = GameplayHudLayout.Create(1600, 900, 96);
        var input = new InputState(); var interaction = new PresentationInteractionState(); var controller = new OperationsController();
        using var device = new RetainedDevice(); using var renderer = new OperationsHudSurface(device);
        var graphics = new SelectionOverlayRenderingTests.RecordingContext { Width = 1600, Height = 900 };
        var context = new GameplayHudRenderContext(graphics, new RtsCamera(), snapshot, default, default, default, default,
            default, CombatGroupOverviewView.Empty, default, layout, 96, 1, null, Operations: new(true, Selected: new(1, 1), Session: new(1)));
        for (int i = 0; i < 128; i++) { renderer.Render(context); controller.Update(input, snapshot, layout, interaction); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) { renderer.Render(context); controller.Update(input, snapshot, layout, interaction); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.InRange(renderer.LastRenderedVertexCount, 1, OperationsHudSurface.MaxVertices);
    }
    private static OperationsFacility Facility(EntityId entity) => new(entity, "FACILITY", OperationsCategory.Production,
        "NoInput", "NoInput", "INPUT STOCK INSUFFICIENT", 2, null, null, null, null, null, null, PlayerActionPanelMode.Production);
    private static PresentationSnapshot Snapshot(OperationsSnapshot? data = null, ulong tick = 4, ulong session = 1, uint player = 1, bool terminal = false)
    {
        var experience = default(PlayerExperienceSnapshot) with { Player = new(player), Tick = new(tick),
            MatchStatus = terminal ? PlayerMatchStatus.Victory : default };
        return new(new(tick), TimeSpan.FromSeconds(.05), 0, [], sessionId: new(session), playerExperience: experience, operations: data);
    }
    private static void Click(InputState input, HudRect rect)
    {
        input.BeginFrame(); int x = (int)(rect.X + rect.Width / 2), y = (int)(rect.Y + rect.Height / 2);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, x, y));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, x, y));
    }
    private sealed class RetainedDevice : IGraphicsDevice
    {
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => new Pipeline(description);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) => new Buffer(description);
        public void RenderFrame(GraphicsColor color, Action<IGraphicsCommandContext>? commands = null) { }
        public void Resize(int width, int height) { } public void WaitForIdle() { } public void Dispose() { }
    }
    private sealed class Pipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    { public GraphicsPipelineDescription Description => description; public void Dispose() { } }
    private sealed class Buffer(GraphicsBufferDescription description) : IGraphicsBuffer
    { public GraphicsBufferDescription Description => description; public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged { } public void Dispose() { } }
}
