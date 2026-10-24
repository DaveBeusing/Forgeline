using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Logistics;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OperationsTests
{
    [Fact]
    public void DenseCopiedChainRendersWithinExistingBudgetsWithoutWarmAllocation()
    {
        var facilities = Enumerable.Range(1, 1000).Select(index => Facility(new((uint)index, 1))).ToArray();
        var data = new OperationsSnapshot(new(1), new(4), new(1), facilities,
            [new(ResourceIds.Electronics, "Electronics", 0, null)], [], 1000, 0);
        var rows = Enumerable.Range(1, 128).Select(index => new OperationsDiagnosisRow(
            OperationsDiagnosisSubject.ProductionInput, new((uint)index, 1), ResourceIds.Electronics,
            default, "NoInput", 10, 0, 0, 0, null, null, null, null,
            default, default, default, null, null, OperationsDiagnosisReason.NoInput)).ToArray();
        var links = Enumerable.Range(1, 64).Select(index => new OperationsDiagnosisLink(default,
            new((uint)index + 1, 1), new((uint)index, 1), ResourceIds.Electronics,
            OperationsDiagnosisLinkKind.ConveyorConnection)).ToArray();
        var instances = facilities.Select((facility, index) => Visible(facility.Entity) with
        { Transform = RenderTransform.Identity with { Position = new(index % 16 - 8, 0, index / 16 % 16 - 8) } }).ToArray();
        EntityId[] selection = [new(1, 1)];
        var experience = default(PlayerExperienceSnapshot) with { Player = new(1), Tick = new(4) };
        var snapshot = new PresentationSnapshot(new(4), TimeSpan.FromMilliseconds(50), 1000, instances,
            sessionId: new(1), playerExperience: experience, operations: data,
            playerActions: new(new(1), new(4), [], 0, null, null))
        {
            Diagnosis = new(new(1), new(4), new(1), rows, links, root: selection[0], omittedSteps: 4, branches: 2),
            SelectedOrders = new(new(1), new(4), selection, [], string.Empty)
        };
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var graphics = new SelectionOverlayRenderingTests.RecordingContext { Width = 1600, Height = 900 };
        var information = RtsInformationLayerView.Empty with { SelectedEntities = selection };
        var context = new GameplayHudRenderContext(graphics, new RtsCamera(), snapshot, default, information,
            default, default, default, CombatGroupOverviewView.Empty, default, layout, 96, 1, null,
            Operations: new(true, Selected: selection[0], Session: new(1)));
        using var device = new RetainedDevice();
        using var renderer = new OperationsHudSurface(device);
        var overlay = new OperationsWorldOverlay();
        for (int index = 0; index < 128; index++) { renderer.Render(context); overlay.Build(context); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 256; index++) { renderer.Render(context); overlay.Build(context); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.InRange(renderer.LastRenderedVertexCount, 1, OperationsHudSurface.MaxVertices);
        Assert.Equal(OperationsWorldOverlay.MaximumMarkers, overlay.Markers.Length);
        Assert.Equal(OperationsWorldOverlay.MaximumLinks, overlay.Links.Length);
        Assert.All(overlay.Links.ToArray(), link => { Assert.True(link.Conveyor); Assert.False(link.Request); Assert.False(link.Loaded); });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void HeldChainFocusCannotActivateAChangedUpstreamTarget(int refusal)
    {
        EntityId root = new(500, 3), first = new(501, 4), replacement = new(502, 8);
        var input = new InputState();
        var interaction = new PresentationInteractionState();
        var controller = new OperationsController();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        PresentationSnapshot Make(EntityId source, ulong tick, int rejected = 0)
        {
            var data = new OperationsSnapshot(new(1), new(tick), new(1),
                [Facility(root), Facility(first), Facility(replacement)], [], [], 3, 0);
            var experience = default(PlayerExperienceSnapshot) with { Player = new(1), Tick = new(tick) };
            var seen = Visible(first);
            if (rejected == 1) seen = seen with { Visibility = RenderVisibilityMask.None };
            if (rejected == 2) seen = seen with { Selectable = new(new PlayerId(2), ControllableEntityCategory.Building) };
            if (rejected == 3) seen = seen with { Entity = new(first.Index, first.Generation + 1) };
            return new PresentationSnapshot(new(tick), TimeSpan.FromMilliseconds(50), 3,
                [Visible(root), seen, Visible(replacement)], sessionId: new(1),
                playerExperience: experience, operations: data,
                playerActions: new(new(1), new(tick), [], 0, null, null))
            {
                Diagnosis = new(new(1), new(tick), new(1), [],
                    [new(default, source, root, ResourceIds.Electronics, OperationsDiagnosisLinkKind.ConveyorConnection)],
                    root: root)
            };
        }
        var before = Make(first, 4);
        controller.Update(input, before, layout, interaction);
        controller.OpenCategory(OperationsCategory.All, before.SessionId, interaction);
        Click(input, OperationsLayout.Control(layout, 3));
        controller.Update(input, before, layout, interaction);
        Assert.Equal(root, controller.View.Selected);
        Edge(input, PlatformInputEventKind.MouseButtonDown, OperationsLayout.Control(layout, 11));
        controller.Update(input, before, layout, interaction);
        var after = Make(refusal == 0 ? replacement : first, 5, refusal);
        Edge(input, PlatformInputEventKind.MouseButtonUp, OperationsLayout.Control(layout, 11));
        Assert.False(controller.Update(input, after, layout, interaction).Navigate.IsValid);
        Assert.Equal(root, controller.View.Selected);
        after = Make(replacement, 5);
        Click(input, OperationsLayout.Control(layout, 11));
        Assert.Equal(replacement, controller.Update(input, after, layout, interaction).Navigate);
        Assert.Equal(replacement, controller.View.Selected);
        Click(input, OperationsLayout.Control(layout, 10));
        var controls = controller.Update(input, after, layout, interaction);
        Assert.Equal(replacement, controls.Navigate);
        Assert.Equal(PlayerActionPanelMode.Production, controls.Controls);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(320, 180)]
    public void CollapsedViewportsHaveNoOperationsPointerReservation(int width, int height)
    {
        var layout = GameplayHudLayout.Create(width, height, 192, 2);
        var controller = new OperationsController();
        var input = new InputState();
        var interaction = new PresentationInteractionState();
        var snapshot = Snapshot();
        controller.Update(input, snapshot, layout, interaction);
        Assert.False(controller.ContainsSurface(Vector2.Zero, layout));
        Assert.False(controller.Update(input, snapshot, layout, interaction).Captured);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void LargeOwnedTopologyKeepsBoundedRoutesAndExcludesForeignLinks(int count)
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var interaction = new PresentationInteractionState(); interaction.SetOperationsOpen(true);
        var buffer = WorldHoverExtractionTests.Observe(scenario, interaction);
        var entities = scenario.Simulation.Entities;
        var owned = new HashSet<EntityId>();
        LogisticsNodeId previous = default;
        EntityId focused = default;
        for (int i = 0; i < count + 1; i++)
        {
            var entity = entities.CreateEntity(); bool foreign = i == count;
            entities.AddComponent(entity, new ControllableEntity(new(foreign ? 2U : 1U), ControllableEntityCategory.Building));
            var inventory = scenario.Inventories.CreateInventory(new InventorySpecification(1000));
            entities.AddComponent(entity, new InventoryStorage(inventory));
            if (!foreign) { owned.Add(entity); focused = entity; }
            var node = scenario.Logistics.AddNode(entity, new Vector3(i, 0, 0), LogisticsNodeKind.StorageDepot,
                LogisticsNodeCapabilities.Storage);
            if (previous.IsSpecified) scenario.Logistics.AddEdge(previous, node, LogisticsTransportMode.GroundRoad, 1, 1, 10, enabled: i % 2 == 0);
            previous = node;
        }
        entities.AddComponent(focused, new WorldTransform(Vector3.Zero, Quaternion.Identity, Vector3.One));
        entities.AddComponent(focused, new VisualIdentity(1));
        interaction.SetSelection([focused]);
        scenario.Simulation.AdvanceOneTick(); Assert.True(buffer.TryReadLatest(out var snapshot));
        var data = snapshot.Operations!;
        Assert.Contains(data.Facilities, row => row.Entity == focused);
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var controller = new OperationsController(); var input = new InputState();
        var single = new OperationsSnapshot(snapshot.SessionId, snapshot.Tick, new(1), [Facility(focused)], [], [], 1, 0);
        var initial = Snapshot(single, tick: snapshot.Tick.Value, session: snapshot.SessionId.Value, instances: [Visible(focused)]);
        controller.Update(input, initial, layout, interaction);
        controller.OpenCategory(OperationsCategory.All, snapshot.SessionId, interaction);
        Click(input, OperationsLayout.Control(layout, 3)); controller.Update(input, initial, layout, interaction);
        Click(input, OperationsLayout.Control(layout, 10));
        var controls = controller.Update(input, snapshot, layout, interaction);
        Assert.Equal(focused, controls.Navigate);
        Assert.Equal(PlayerActionPanelMode.Logistics, controls.Controls);
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
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
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
    [MemberData(nameof(GuidancePresentationTests.Displays), MemberType = typeof(GuidancePresentationTests))]
    public void SharedControlsFitSafeAreaAndKeepDockMinimapAvailable(int width, int height, uint dpi, float uiScale)
    {
        var layout = GameplayHudLayout.Create(width, height, dpi, uiScale);
        var panel = OperationsLayout.Panel(layout);
        Assert.False(panel.Intersects(layout.AlertStack)); Assert.False(panel.Intersects(layout.Minimap));
        for (int index = 0; index < 12; index++)
        {
            var control = OperationsLayout.Control(layout, index);
            Assert.True(panel.Contains(new(control.X, control.Y)));
            Assert.True(panel.Contains(new(control.Right, control.Bottom)));
        }
        Assert.False(panel.Intersects(layout.Minimap));
        var data = new OperationsSnapshot(new(1), new(4), new(1), [Facility(new(1, 1))], [], [], 1, 0);
        using var device = new SelectionOverlayRenderingTests.RecordingDevice();
        using var renderer = new OperationsHudSurface(device);
        var graphics = new SelectionOverlayRenderingTests.RecordingContext { Width = width, Height = height };
        var context = new GameplayHudRenderContext(graphics, new RtsCamera(), Snapshot(data), default, default, default, default,
            default, CombatGroupOverviewView.Empty, default, layout, dpi, uiScale, null, Operations: new(true, Selected: new(1, 1), Session: new(1)));
        renderer.Render(context);
        if (uiScale >= 1) Assert.True(renderer.LastPanelTextSize >= 14);
        Assert.InRange(renderer.LastRenderedVertexCount, 100, OperationsHudSurface.MaxVertices);
        Assert.All(device.Buffer!.Vertices, vertex =>
        { Assert.InRange(vertex.Position.X, -1, 1); Assert.InRange(vertex.Position.Y, -1, 1); });
    }

    [Theory]
    [InlineData(VehicleQueueWaitReason.GeometryBlocked, true)]
    [InlineData(VehicleQueueWaitReason.Unavailable, false)]
    public void ConfirmedServiceBlockFocusesTheRelevantEntityThroughExistingControls(VehicleQueueWaitReason reason, bool focusBuilding)
    {
        var truck = new EntityId(41,2); var building = new EntityId(42,3);
        var service = new PlayerWorkSummary(default,default,0,"","",new(building,5,VehicleQueueService.CargoPickup,1,1,
            VehicleDockStatus.Blocked,reason,focusBuilding ? VehicleDockBlockReason.Obstructed : VehicleDockBlockReason.None,new(4)));
        var expectedControls = focusBuilding ? PlayerActionPanelMode.Logistics : PlayerActionPanelMode.Supply;
        var row = Facility(truck) with { Cause = LogisticsStatusCatalog.BlockedCause(service), Service = service, Controls = expectedControls };
        var data = new OperationsSnapshot(new(1),new(4),new(1),[row],[],[],1,0);
        var snapshot = Snapshot(data, instances: [Visible(truck, ControllableEntityCategory.Logistics), Visible(building)]);
        var layout = GameplayHudLayout.Create(1600,900,96);
        var input = new InputState(); var interaction = new PresentationInteractionState(); var controller = new OperationsController();
        controller.Update(input,snapshot,layout,interaction);
        controller.OpenCategory(OperationsCategory.Blocked,new(1),interaction);
        Click(input,OperationsLayout.Control(layout,3)); controller.Update(input,snapshot,layout,interaction);
        Assert.Equal(truck,controller.View.Selected);
        Click(input,OperationsLayout.Control(layout,10)); var result = controller.Update(input,snapshot,layout,interaction);
        Assert.Equal(focusBuilding ? building : truck,result.Navigate);
        Assert.Equal(expectedControls,result.Controls);
    }

    [Fact]
    public void OperationsClickRequiresReleaseInThePressedControl()
    {
        var entity = new EntityId(3, 2);
        var data = new OperationsSnapshot(new(1), new(4), new(1), [Facility(entity)], [], [], 1, 0);
        var snapshot = Snapshot(data, instances: [Visible(entity)]);
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var input = new InputState();
        var interaction = new PresentationInteractionState();
        var controller = new OperationsController();
        controller.Update(input, snapshot, layout, interaction);
        controller.OpenCategory(OperationsCategory.All, snapshot.SessionId, interaction);

        var firstRow = OperationsLayout.Control(layout, 3);
        var secondRow = OperationsLayout.Control(layout, 4);
        Edge(input, PlatformInputEventKind.MouseButtonDown, firstRow);
        Assert.False(controller.Update(input, snapshot, layout, interaction).Navigate.IsValid);
        Assert.False(controller.View.Selected.IsValid);
        Edge(input, PlatformInputEventKind.MouseButtonUp, secondRow);
        Assert.False(controller.Update(input, snapshot, layout, interaction).Navigate.IsValid);
        Assert.False(controller.View.Selected.IsValid);

        Edge(input, PlatformInputEventKind.MouseButtonDown, firstRow);
        controller.Update(input, snapshot, layout, interaction);
        Edge(input, PlatformInputEventKind.MouseButtonUp, firstRow);
        controller.Update(input, snapshot, layout, interaction);
        Assert.Equal(entity, controller.View.Selected);

        var open = OperationsLayout.Control(layout, 10);
        Edge(input, PlatformInputEventKind.MouseButtonDown, open);
        Assert.False(controller.Update(input, snapshot, layout, interaction).Navigate.IsValid);
        Edge(input, PlatformInputEventKind.MouseButtonUp, open);
        var result = controller.Update(input, snapshot, layout, interaction);
        Assert.Equal(entity, result.Navigate);
        Assert.Equal(PlayerActionPanelMode.Production, result.Controls);
        Assert.False(controller.Update(input, snapshot, layout, interaction).Navigate.IsValid);
    }

    [Fact]
    public void OperationsDisabledAndStaleGesturesDoNotNavigate()
    {
        var entity = new EntityId(3, 2);
        var data = new OperationsSnapshot(new(1), new(4), new(1),
            [Facility(entity) with { Controls = PlayerActionPanelMode.Closed }], [], [], 1, 0);
        var snapshot = Snapshot(data);
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var input = new InputState();
        var interaction = new PresentationInteractionState();
        var controller = new OperationsController();
        controller.Update(input, snapshot, layout, interaction);
        controller.OpenCategory(OperationsCategory.All, snapshot.SessionId, interaction);
        Click(input, OperationsLayout.Control(layout, 3));
        controller.Update(input, snapshot, layout, interaction);
        Assert.Equal(entity, controller.View.Selected);

        Click(input, OperationsLayout.Control(layout, 10));
        Assert.False(controller.Update(input, snapshot, layout, interaction).Navigate.IsValid);
        Edge(input, PlatformInputEventKind.MouseButtonDown, OperationsLayout.Control(layout, 9));
        controller.Update(input, snapshot, layout, interaction);
        var resized = GameplayHudLayout.Create(1920, 1080, 144);
        Edge(input, PlatformInputEventKind.MouseButtonUp, OperationsLayout.Control(resized, 9));
        Assert.False(controller.Update(input, snapshot, resized, interaction).Navigate.IsValid);

        Edge(input, PlatformInputEventKind.MouseButtonDown, OperationsLayout.Control(resized, 9));
        controller.Update(input, snapshot, resized, interaction);
        Edge(input, PlatformInputEventKind.MouseButtonUp, OperationsLayout.Control(resized, 9));
        Assert.False(controller.Update(input, Snapshot(data, session: 2), resized, interaction).Navigate.IsValid);
    }

    [Fact]
    public void ReplacedFacilityGenerationCannotAcquireHeldRowPress()
    {
        var oldEntity = new EntityId(3, 2);
        var replacement = new EntityId(3, 3);
        var oldData = new OperationsSnapshot(new(1), new(4), new(1), [Facility(oldEntity)], [], [], 1, 0);
        var newData = new OperationsSnapshot(new(1), new(4), new(1), [Facility(replacement)], [], [], 1, 0);
        var oldSnapshot = Snapshot(oldData);
        var newSnapshot = Snapshot(newData);
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var input = new InputState();
        var interaction = new PresentationInteractionState();
        var controller = new OperationsController();
        controller.Update(input, oldSnapshot, layout, interaction);
        controller.OpenCategory(OperationsCategory.All, oldSnapshot.SessionId, interaction);
        Edge(input, PlatformInputEventKind.MouseButtonDown, OperationsLayout.Control(layout, 3));
        controller.Update(input, oldSnapshot, layout, interaction);
        Edge(input, PlatformInputEventKind.MouseButtonUp, OperationsLayout.Control(layout, 3));
        controller.Update(input, newSnapshot, layout, interaction);
        Assert.False(controller.View.Selected.IsValid);

        Click(input, OperationsLayout.Control(layout, 3));
        controller.Update(input, newSnapshot, layout, interaction);
        Assert.Equal(replacement, controller.View.Selected);
    }

    [Fact]
    public void ShortClicksSelectAndOpenExistingControlsWithoutSubmittingCommands()
    {
        var entity = new EntityId(3, 2);
        var data = new OperationsSnapshot(new(1), new(4), new(1), [Facility(entity)], [], [], 1, 0);
        var snapshot = Snapshot(data, instances: [Visible(entity)]); var controller = new OperationsController();
        var input = new InputState(); var interaction = new PresentationInteractionState();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        controller.Update(input, snapshot, layout, interaction);
        Click(input, layout.TopStatusBar); controller.Update(input, snapshot, layout, interaction);
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

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("hidden")]
    [InlineData("generation")]
    public void FocusAndControlsRequireCurrentOwnedVisibleEntity(string unavailable)
    {
        var entity = new EntityId(3, 2);
        var data = new OperationsSnapshot(new(1), new(4), new(1), [Facility(entity)], [], [], 1, 0);
        RenderInstance[] instances = unavailable switch
        {
            "missing" => [],
            "foreign" => [Visible(entity) with { Selectable = new(new PlayerId(2), ControllableEntityCategory.Building) }],
            "hidden" => [Visible(entity) with { Visibility = RenderVisibilityMask.Debug }],
            "generation" => [Visible(new(entity.Index, entity.Generation + 1))],
            _ => throw new ArgumentOutOfRangeException(nameof(unavailable))
        };
        var snapshot = Snapshot(data, instances: instances);
        var controller = new OperationsController();
        var input = new InputState();
        var interaction = new PresentationInteractionState();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        controller.Update(input, snapshot, layout, interaction);
        controller.OpenCategory(OperationsCategory.All, snapshot.SessionId, interaction);
        Click(input, OperationsLayout.Control(layout, 3));
        controller.Update(input, snapshot, layout, interaction);
        Assert.Equal(entity, controller.View.Selected);

        for (int control = 9; control <= 10; control++)
        {
            Click(input, OperationsLayout.Control(layout, control));
            var result = controller.Update(input, snapshot, layout, interaction);
            Assert.False(result.Navigate.IsValid);
            Assert.Equal(PlayerActionPanelMode.Closed, result.Controls);
        }
    }

    [Fact]
    public void ModalResizeAndSessionChangesConsumeRetainedEdges()
    {
        var input = new InputState(); var interaction = new PresentationInteractionState();
        var controller = new OperationsController(); var snapshot = Snapshot();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        controller.Update(input, snapshot, layout, interaction);
        Click(input, layout.TopStatusBar); controller.Update(input, snapshot, layout, interaction, blocked: true);
        controller.Update(input, snapshot, layout, interaction); Assert.False(controller.View.Open);
        Click(input, layout.TopStatusBar); controller.Update(input, snapshot, layout, interaction);
        Assert.True(controller.View.Open);
        var changed = GameplayHudLayout.Create(1920, 1080, 144);
        Click(input, OperationsLayout.Control(layout, 0)); controller.Update(input, snapshot, changed, interaction);
        Assert.True(controller.View.Open);
        controller.Update(input, Snapshot(session: 2), changed, interaction); Assert.False(controller.View.Open);
    }

    [Fact]
    public void TargetingSuppressesViewAndModalSkippedFramesDiscardClicks()
    {
        var input = new InputState(); var interaction = new PresentationInteractionState();
        var controller = new OperationsController(); var snapshot = Snapshot();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        controller.Update(input, snapshot, layout, interaction);
        Click(input, layout.TopStatusBar); controller.Update(input, snapshot, layout, interaction);
        Assert.True(controller.View.Open);
        controller.Update(input, snapshot, layout, interaction, blocked: true);
        Assert.True(controller.View.Suppressed); Assert.False(interaction.Capture().OperationsOpen);
        Click(input, OperationsLayout.Control(layout, 0)); controller.CancelInput(input);
        controller.Update(input, snapshot, layout, interaction);
        Assert.True(controller.View.Open); Assert.False(controller.View.Suppressed);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void WorldOverlayProjectionHasZeroWarmAllocation(bool visible, bool selectedDiagnosis, bool chain)
    {
        EntityId facility = new(12, 3), source = new(14, 2), truck = new(18, 7);
        var data = new OperationsSnapshot(new(1), new(4), new(1),
            [Facility(facility), Facility(source)], [new(ResourceIds.Steel, "STEEL", 100, null)],
            [new(source, facility, 100, true, .7)], 2, 1);
        var order = new PlayerLogisticsOrderReadModel(new(17), ResourceIds.Steel, "STEEL",
            LogisticsTransportRequestState.InTransit, LogisticsTransportRequestFailureReason.None,
            LogisticsBottleneckReason.None, 90, 10, truck, source, facility, "SOURCE", "FACILITY");
        var actions = selectedDiagnosis ? new PlayerActionSnapshot(new(1), new(4), [], 0,
            null, null, new PlayerLogisticsActionReadModel(facility, [], null, orders: [order])) : null;
        EntityId[] selection = selectedDiagnosis ? [facility] : [];
        RenderInstance[] instances = visible
            ? [Visible(facility) with { Transform = RenderTransform.Identity with { Position = new(-10, 0, 0) } },
               Visible(source) with { Transform = RenderTransform.Identity with { Position = new(10, 0, 0) } }]
            : [];
        var experience = default(PlayerExperienceSnapshot) with { Player = new(1), Tick = new(4) };
        var snapshot = new PresentationSnapshot(new(4), TimeSpan.FromMilliseconds(50), instances.Length, instances,
            sessionId: new(1), playerExperience: experience, operations: data, playerActions: actions)
        {
            Diagnosis = chain ? new OperationsDiagnosisSnapshot(new(1), new(4), new(1),
                OperationsDiagnosisSnapshot.Capture(new(1), new(4), actions, experience)!.Rows,
                [new(default, source, facility, ResourceIds.Steel, OperationsDiagnosisLinkKind.ConveyorConnection)], root: facility)
                : OperationsDiagnosisSnapshot.Capture(new(1), new(4), actions, experience),
            SelectedOrders = selectedDiagnosis ? new(new(1), new(4), selection, [], string.Empty) : null
        };
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var graphics = new SelectionOverlayRenderingTests.RecordingContext { Width = 1600, Height = 900 };
        var information = RtsInformationLayerView.Empty with { SelectedEntities = selection };
        var context = new GameplayHudRenderContext(graphics, new RtsCamera(), snapshot, default, information,
            default, default, default, CombatGroupOverviewView.Empty, default, layout, 96, 1, null,
            Operations: new(true, Selected: facility, Session: new(1)));
        var overlay = new OperationsWorldOverlay();
        overlay.Build(context);
        Assert.Equal(visible ? 2 : 0, overlay.Markers.Length);
        Assert.Equal(visible ? 1 : 0, overlay.Links.Length);
        if (visible)
        {
            Assert.Equal(facility, overlay.Markers[0].Entity);
            Assert.Equal(selectedDiagnosis, overlay.Markers[0].Diagnosis.HasValue);
            Assert.Equal(selectedDiagnosis && !chain, overlay.Links[0].Request);
            Assert.Equal(chain, overlay.Links[0].Conveyor);
            Assert.Equal(selectedDiagnosis && !chain, overlay.Links[0].Loaded);
            Assert.Equal(source, overlay.Links[0].Source);
            Assert.Equal(facility, overlay.Links[0].Destination);
        }
        for (int i = 0; i < 128; i++) overlay.Build(context);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) overlay.Build(context);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
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
    [Fact]
    public void ContextualResourceEntryOpensPanelAndCloseReleasesInputImmediately()
    {
        var input = new InputState();
        var interaction = new PresentationInteractionState();
        var controller = new OperationsController();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var snapshot = Snapshot();
        controller.Update(input, snapshot, layout, interaction);
        HudRect oldStrip = new(layout.SecondaryView.X,
            layout.SecondaryView.Bottom - 19, 126, 18);
        Vector2 formerToggle = new(oldStrip.X + 5, oldStrip.Y + 5);
        input.Apply(PlatformInputEvent.PointerMoved((int)formerToggle.X, (int)formerToggle.Y));
        Assert.False(controller.Update(input, snapshot, layout, interaction).Captured);
        Click(input, oldStrip);
        Assert.False(controller.Update(input, snapshot, layout, interaction).Captured);
        Assert.False(controller.View.Open);

        Click(input, layout.TopStatusBar);
        Assert.True(controller.Update(input, snapshot, layout, interaction).Captured);
        Assert.True(controller.View.Open);
        Assert.True(interaction.Capture().OperationsOpen);
        controller.Update(input, snapshot, layout, interaction);
        Assert.True(controller.View.Open);

        Click(input, OperationsLayout.Control(layout, 0));
        Assert.True(controller.Update(input, snapshot, layout, interaction).Captured);
        Assert.False(controller.View.Open);
        Assert.False(interaction.Capture().OperationsOpen);
        Assert.False(controller.ContainsSurface(
            new Vector2(OperationsLayout.Panel(layout).X + 10,
                OperationsLayout.Panel(layout).Y + 50), layout));

        Click(input, layout.TopStatusBar);
        controller.Update(input, snapshot, layout, interaction);
        Assert.True(controller.View.Open);
    }

    [Fact]
    public void ClosedOperationsHudDoesNotDrawPersistentToggle()
    {
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        using var device = new SelectionOverlayRenderingTests.RecordingDevice();
        using var renderer = new OperationsHudSurface(device);
        var graphics = new SelectionOverlayRenderingTests.RecordingContext { Width = 1600, Height = 900 };
        var context = new GameplayHudRenderContext(graphics, new RtsCamera(), Snapshot(), default,
            default, default, default, default, CombatGroupOverviewView.Empty, default,
            layout, 96, 1, null);
        renderer.Render(context);
        Assert.Equal(0, renderer.LastRenderedVertexCount);
        Assert.Equal(0, renderer.TextDiagnostics.Glyphs);
    }

    [Fact]
    public void EmptyOpenedPanelAreaCapturesPointerWhileClosedAreaIsWorldAccessible()
    {
        var input = new InputState();
        var interaction = new PresentationInteractionState();
        var controller = new OperationsController();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var snapshot = Snapshot();
        controller.Update(input, snapshot, layout, interaction);
        var panel = OperationsLayout.Panel(layout);
        var emptySpace = new System.Numerics.Vector2(panel.X + panel.Width * .5f, panel.Y + 50);
        Assert.False(controller.ContainsSurface(emptySpace, layout));

        controller.OpenCategory(OperationsCategory.Power, snapshot.SessionId, interaction);
        Assert.True(controller.ContainsSurface(emptySpace, layout));
        input.Apply(PlatformInputEvent.PointerMoved((int)emptySpace.X, (int)emptySpace.Y));
        Assert.True(controller.Update(input, snapshot, layout, interaction).Captured);
        Assert.Equal(OperationsCategory.Power, controller.View.Filter);

        controller.Close(input, interaction);
        Assert.False(controller.ContainsSurface(emptySpace, layout));
        Assert.False(controller.Update(input, snapshot, layout, interaction).Captured);
    }

    [Fact]
    public void AlertDetailsAndFilteredOperationsRemainAvailableWithoutPersistentStrip()
    {
        var input = new InputState();
        var interaction = new PresentationInteractionState();
        var controller = new OperationsController();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var snapshot = Snapshot();
        controller.Update(input, snapshot, layout, interaction);

        controller.OpenNotifications(snapshot.SessionId, interaction);
        Assert.True(controller.View.Open);
        Assert.True(controller.View.Notifications);
        Assert.True(controller.ContainsSurface(
            new Vector2(OperationsLayout.Panel(layout).X + 12,
                OperationsLayout.Panel(layout).Y + 35), layout));

        controller.Close(input, interaction);
        controller.OpenCategory(OperationsCategory.Blocked, snapshot.SessionId, interaction);
        Assert.Equal(OperationsCategory.Blocked, controller.View.Filter);
        Assert.True(interaction.Capture().OperationsOpen);
        Assert.True(controller.View.Open);
    }

    [Fact]
    public void StrategicOverlaySelectionAndMinimapVisibilityAreIndependentOfOperations()
    {
        var overlays = new RtsInformationLayerController();
        Assert.True(overlays.MinimapEnabled);
        overlays.CycleOverlay();
        Assert.Equal(StrategicOverlayMode.Logistics, overlays.OverlayMode);
        Assert.False(string.IsNullOrWhiteSpace(RtsStrategicOverlayHudModel.Legend(overlays.OverlayMode)));
        overlays.ToggleMinimap();
        Assert.False(overlays.MinimapEnabled);
        overlays.ToggleMinimap();
        Assert.True(overlays.MinimapEnabled);
        Assert.Equal(StrategicOverlayMode.Logistics, overlays.OverlayMode);
    }

    private static OperationsFacility Facility(EntityId entity) => new(entity, "FACILITY", OperationsCategory.Production,
        "NoInput", "NoInput", "INPUT STOCK INSUFFICIENT", 2, null, null, null, null, null, null, PlayerActionPanelMode.Production);
    private static RenderInstance Visible(EntityId entity, ControllableEntityCategory category = ControllableEntityCategory.Building) =>
        new(entity, RenderTransform.Identity, new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
            Selectable: new(new PlayerId(1), category));
    private static PresentationSnapshot Snapshot(OperationsSnapshot? data = null, ulong tick = 4, ulong session = 1,
        uint player = 1, bool terminal = false, RenderInstance[]? instances = null)
    {
        var experience = default(PlayerExperienceSnapshot) with
        {
            Player = new(player),
            Tick = new(tick),
            MatchStatus = terminal ? PlayerMatchStatus.Victory : default
        };
        return new(new(tick), TimeSpan.FromSeconds(.05), instances?.Length ?? 0, instances ?? [],
            sessionId: new(session), playerExperience: experience, operations: data);
    }
    private static void Edge(InputState input, PlatformInputEventKind kind, HudRect rect)
    {
        input.BeginFrame();
        int x = (int)(rect.X + rect.Width / 2), y = (int)(rect.Y + rect.Height / 2);
        input.Apply(PlatformInputEvent.MouseButtonChanged(kind, PlatformMouseButton.Left, x, y));
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
        public void Resize(int width, int height) { }
        public void WaitForIdle() { }
        public void Dispose() { }
    }
    private sealed class Pipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    { public GraphicsPipelineDescription Description => description; public void Dispose() { } }
    private sealed class Buffer(GraphicsBufferDescription description) : IGraphicsBuffer
    { public GraphicsBufferDescription Description => description; public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged { } public void Dispose() { } }
}
