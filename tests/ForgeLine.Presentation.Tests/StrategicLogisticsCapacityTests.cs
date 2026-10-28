using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Logistics;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class StrategicLogisticsCapacityTests
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    [Fact]
    public void ExtractionSeparatesNetworksRedactsForeignEndpointsAndCopiesCurrentBeltEvidence()
    {
        using var runtime = WorldHoverExtractionTests.CreateScenario();
        var interaction = new PresentationInteractionState(); interaction.SetStrategicOverlay(StrategicOverlayMode.Logistics);
        var buffer = WorldHoverExtractionTests.Observe(runtime, interaction);
        var entities = runtime.Simulation.Entities;
        var belts = new List<EntityId>();
        Vector3 pivot = new(-30,0,0);
        for (int i=0;i<3;i++)
        {
            var placement = new ConveyorPlacement(i == 1 ? ConveyorModule.Overpass : ConveyorModule.Standard, pivot, BuildingOrientation.East, 0);
            var entity = Visible(pivot, new(1)); belts.Add(entity);
            entities.AddComponent(entity, new ConveyorPiece(new(1), placement, 16));
            var inventory = runtime.Inventories.CreateInventory(new InventorySpecification(200));
            entities.AddComponent(entity, new ConveyorFlowState(inventory, 200));
            var output = placement.Sockets.Single(x=>!x.Input).Position;
            var next = new ConveyorPlacement(i==0 ? ConveyorModule.Overpass : ConveyorModule.Standard, Vector3.Zero, BuildingOrientation.East, 0);
            pivot = output - next.Sockets.Single(x=>x.Input).Position;
        }
        var a = Visible(new(0,0,30),new(1)); var b = Visible(new(30,0,30),new(1)); var enemy = Visible(new(60,0,30),new(2));
        var na = runtime.Logistics.AddNode(a,new(0,0,30),LogisticsNodeKind.StorageDepot,LogisticsNodeCapabilities.Storage);
        var nb = runtime.Logistics.AddNode(b,new(30,0,30),LogisticsNodeKind.StorageDepot,LogisticsNodeCapabilities.Storage);
        var ne = runtime.Logistics.AddNode(enemy,new(60,0,30),LogisticsNodeKind.StorageDepot,LogisticsNodeCapabilities.Storage);
        runtime.Logistics.AddEdge(na,nb,LogisticsTransportMode.GroundRoad,30,1,25,enabled:false);
        runtime.Logistics.AddEdge(nb,ne,LogisticsTransportMode.GroundRoad,30,1,25);
        var stock = runtime.Inventories.CreateInventory(new InventorySpecification(100));
        entities.AddComponent(a,new SupplyProvider(stock,new(1),50));
        entities.AddComponent(b,new ResupplyOrder(a,SimulationTick.Zero,SimulationTick.Zero));
        runtime.Simulation.AdvanceOneTick(); Assert.True(buffer.TryReadLatest(out var snapshot));
        var data = Assert.IsType<StrategicLogisticsCapacitySnapshot>(snapshot.StrategicOverlay!.LogisticsCapacity);
        Assert.Equal(snapshot.Tick,data.Tick);Assert.Equal(snapshot.SessionId,data.Session);
        Assert.DoesNotContain(data.Items,x=>x.Entity==enemy || x.Destination==enemy);
        Assert.Contains(data.Items,x=>x.Entity==a && x.Destination==b && x.Network==StrategicLogisticsNetwork.Cargo && x.State==StrategicLogisticsState.Offline);
        Assert.Contains(data.Items,x=>x.Entity==a && x.Destination==b && x.Network==StrategicLogisticsNetwork.Supply && x.OutputPerSecond==null);
        var middle = Assert.Single(data.Items,x=>x.Entity==belts[1] && !x.IsLink);
        Assert.Equal(StrategicLogisticsNetwork.Conveyor,middle.Network);
        Assert.Equal(entities.GetComponent<ConveyorFlowState>(belts[1]).OutputPerSecond,middle.OutputPerSecond);
        Assert.Contains(data.Items,x=>x.Entity==belts[0] && x.Destination==belts[1] && x.Network==StrategicLogisticsNetwork.Conveyor);
        var retained = data.Items.ToArray();
        string before = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        var overlay = new StrategicLogisticsCapacityOverlay();overlay.Build(snapshot,SameTypeSelectionTests.Camera(),GameplayHudLayout.Create(1600,900,96),StrategicOverlayMode.Logistics);
        Assert.Equal(before,MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256());
        entities.DestroyEntity(b);runtime.Simulation.AdvanceOneTick();Assert.True(buffer.TryReadLatest(out var after));
        Assert.DoesNotContain(after.StrategicOverlay!.LogisticsCapacity!.Items,x=>x.Entity==b || x.Destination==b);
        Assert.Equal(retained,data.Items);
        interaction.SetStrategicOverlay(StrategicOverlayMode.None);runtime.Simulation.AdvanceOneTick();Assert.True(buffer.TryReadLatest(out var off));Assert.Null(off.StrategicOverlay);

        EntityId Visible(Vector3 position, PlayerId owner)
        {
            var entity = entities.CreateEntity();
            entities.AddComponent(entity,new ControllableEntity(owner,ControllableEntityCategory.Logistics));
            entities.AddComponent(entity,new WorldTransform(position,Quaternion.Identity,Vector3.One));entities.AddComponent(entity,new VisualIdentity(1));return entity;
        }
    }

    [Fact]
    public void ThousandsOfOwnedSegmentsKeepBoundedCopiedSamplesAndForeignFactsAbsent()
    {
        using var runtime=WorldHoverExtractionTests.CreateScenario();
        var entities=runtime.Simulation.Entities;
        EntityId focus=default;
        for(int i=0;i<4096;i++)
        {
            var entity=entities.CreateEntity();focus=entity;
            var pos=new Vector3(i%64*3,0,i/64*3);
            entities.AddComponent(entity,new ControllableEntity(new(1),ControllableEntityCategory.Logistics));
            entities.AddComponent(entity,new WorldTransform(pos,Quaternion.Identity,Vector3.One));entities.AddComponent(entity,new VisualIdentity(1));
            entities.AddComponent(entity,new ConveyorPiece(new(1),new ConveyorPlacement(ConveyorModule.Standard,pos,BuildingOrientation.East,0),16));
        }
        var interaction=new PresentationInteractionState();interaction.SetStrategicOverlay(StrategicOverlayMode.Conveyors);
        var buffer=WorldHoverExtractionTests.Observe(runtime,interaction);
        var start=Stopwatch.GetTimestamp();runtime.Simulation.AdvanceOneTick();double elapsed=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Assert.True(buffer.TryReadLatest(out var snapshot));var data=snapshot.StrategicOverlay!.LogisticsCapacity!;
        Assert.Equal(StrategicLogisticsCapacitySnapshot.MaximumItemsPerNetwork,data.Items.Count(x=>x.Network==StrategicLogisticsNetwork.Conveyor));
        Assert.True(data.Omitted>=3072);Assert.All(data.Items.Where(x=>x.Network==StrategicLogisticsNetwork.Conveyor),x=>{Assert.Equal(StrategicLogisticsState.Unknown,x.State);Assert.Null(x.OutputPerSecond);});
        string? output=Environment.GetEnvironmentVariable("FORGELINE_LOGISTICS_EXTRACTION_MEASUREMENTS");
        if(!string.IsNullOrWhiteSpace(output))File.WriteAllText(output,JsonSerializer.Serialize(new{OwnedSegments=4096,Tick=snapshot.Tick.Value,Copied=data.Items.Count,data.Omitted,data.ScanLimited,TickAndExtractionMilliseconds=elapsed,Notes="One observed complete simulation tick including existing world/conveyor capture; not isolated extraction time or a comparative benchmark."},ReportOptions));
        var selection=new SelectionSet();var operations=new OperationsController();Assert.True(operations.OpenEntity(snapshot,focus,interaction,selection));
        entities.DestroyEntity(focus);Assert.False(operations.OpenEntity(snapshot,new(focus.Index,focus.Generation+1),interaction,selection));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void StaleOrForeignCapturesCannotRender(int refusal)
    {
        var mode=StrategicOverlayMode.Logistics;
        var row = Row(new(1,1),Vector3.Zero);
        var data=new StrategicLogisticsCapacitySnapshot(new(refusal==0?2UL:1UL),new(refusal==1?5UL:4UL),new(refusal==2?2U:1U),[row]);
        var snapshot=Snapshot([row],mode,capacity:data,terminal:refusal==3);
        if(refusal==4)mode=StrategicOverlayMode.Cargo;
        var overlay=new StrategicLogisticsCapacityOverlay();overlay.Build(snapshot,SameTypeSelectionTests.Camera(),GameplayHudLayout.Create(1600,900,96),mode);
        Assert.Empty(overlay.Items.ToArray());
    }

    [Theory]
    [InlineData(StrategicOverlayMode.Conveyors,StrategicLogisticsNetwork.Conveyor)]
    [InlineData(StrategicOverlayMode.Cargo,StrategicLogisticsNetwork.Cargo)]
    [InlineData(StrategicOverlayMode.FieldLogistics,StrategicLogisticsNetwork.Supply)]
    public void FiltersNeverRelabelTheOtherNetworks(StrategicOverlayMode mode,StrategicLogisticsNetwork expected)
    {
        var rows=Enum.GetValues<StrategicLogisticsNetwork>().Select((network,i)=>Row(new((uint)i+1,1),new(i*10,0,0)) with{Network=network}).ToArray();
        var overlay=new StrategicLogisticsCapacityOverlay();overlay.Build(Snapshot(rows,mode),SameTypeSelectionTests.Camera(),GameplayHudLayout.Create(1600,900,96),mode);
        Assert.NotEmpty(overlay.Items.ToArray());Assert.All(overlay.Items.ToArray(),x=>Assert.Equal(expected,x.Item.Network));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MarkerGestureOpensExistingOperationsAndRejectsChangedTargets(bool stale)
    {
        var mode=StrategicOverlayMode.Logistics;var row=Row(new(1,1),Vector3.Zero);var snapshot=Snapshot([row],mode);
        var camera=SameTypeSelectionTests.Camera();var layout=GameplayHudLayout.Create(1600,900,96);var overlay=new StrategicLogisticsCapacityOverlay();overlay.Build(snapshot,camera,layout,mode);
        var point=Assert.Single(overlay.Items.ToArray()).Marker;var input=new InputState();var controller=new StrategicLogisticsCapacityInteraction();
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,PlatformMouseButton.Left,(int)point.X,(int)point.Y));
        Assert.True(controller.Update(input,snapshot,camera,layout,mode,false,out var none));Assert.False(none.IsValid);
        input.BeginFrame();input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp,PlatformMouseButton.Left,(int)point.X,(int)point.Y));
        if(stale)snapshot=Snapshot([row with{Entity=new(1,2)}],mode);
        Assert.True(controller.Update(input,snapshot,camera,layout,mode,false,out var target));
        if(stale){Assert.False(target.IsValid);return;}
        var operations=new OperationsController();var selection=new SelectionSet();var interaction=new PresentationInteractionState();
        Assert.True(operations.OpenEntity(snapshot,target,interaction,selection));Assert.Equal(target,operations.View.Selected);Assert.True(interaction.Capture().OperationsOpen);
        Assert.Single(selection.Entities);Assert.Equal(target,Assert.Single(selection.Entities));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void InterruptedGesturesCannotActivate(int interruption)
    {
        var mode=StrategicOverlayMode.Logistics;var row=Row(new(1,1),Vector3.Zero);var snapshot=Snapshot([row],mode);
        var camera=SameTypeSelectionTests.Camera();var layout=GameplayHudLayout.Create(1600,900,96);var overlay=new StrategicLogisticsCapacityOverlay();overlay.Build(snapshot,camera,layout,mode);
        var point=overlay.Items[0].Marker;var input=new InputState();var controller=new StrategicLogisticsCapacityInteraction();
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,PlatformMouseButton.Left,(int)point.X,(int)point.Y));controller.Update(input,snapshot,camera,layout,mode,false,out _);
        input.BeginFrame();input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp,PlatformMouseButton.Left,(int)point.X,(int)point.Y));
        if(interruption==0)mode=StrategicOverlayMode.None;if(interruption==1)layout=GameplayHudLayout.Create(1280,720,144);if(interruption==2)snapshot=Snapshot([row],mode,session:2);
        controller.Update(input,snapshot,camera,layout,mode,interruption==3,out var target);Assert.False(target.IsValid);
    }

    [Fact]
    public void DenseSpatialProjectionIsBoundedAndDoesNotAllocateAfterWarmup()
    {
        var rows=Enumerable.Range(0,4096).Select(i=>Row(new((uint)i+1,1),new(i%64*3-96,0,i/64*3-96)) with{State=i%7==0?StrategicLogisticsState.Blocked:StrategicLogisticsState.Unknown}).ToArray();
        var snapshot=Snapshot(rows,StrategicOverlayMode.Logistics);var camera=SameTypeSelectionTests.Camera();var layout=GameplayHudLayout.Create(1600,900,96);var overlay=new StrategicLogisticsCapacityOverlay();
        for(int i=0;i<32;i++)overlay.Build(snapshot,camera,layout,StrategicOverlayMode.Logistics);
        long bytes=GC.GetAllocatedBytesForCurrentThread();long start=Stopwatch.GetTimestamp();
        for(int i=0;i<128;i++)overlay.Build(snapshot,camera,layout,StrategicOverlayMode.Logistics);
        double milliseconds=Stopwatch.GetElapsedTime(start).TotalMilliseconds/128;long allocated=(GC.GetAllocatedBytesForCurrentThread()-bytes)/128;
        Assert.Equal(0,allocated);Assert.InRange(overlay.Items.Length,1,80);Assert.True(overlay.Omitted>0);
        string? output=Environment.GetEnvironmentVariable("FORGELINE_LOGISTICS_MEASUREMENTS");
        if(!string.IsNullOrWhiteSpace(output))File.WriteAllText(output,JsonSerializer.Serialize(new{FixtureRows=4096,CopiedRows=snapshot.StrategicOverlay!.LogisticsCapacity!.Items.Count,Projected=overlay.Items.Length,overlay.Omitted,MillisecondsPerBuild=milliseconds,BytesPerBuild=allocated,Warmups=32,Samples=128,Notes="Copied representative projection CPU only; not GPU or human readability acceptance."},ReportOptions));
    }

    internal static StrategicLogisticsCapacityItem Row(EntityId entity,Vector3 position)=>new(entity,default,position,position,StrategicLogisticsNetwork.Conveyor,StrategicLogisticsState.Unknown);
    internal static PresentationSnapshot Snapshot(StrategicLogisticsCapacityItem[] rows,StrategicOverlayMode mode,
        StrategicLogisticsCapacitySnapshot? capacity=null,bool terminal=false,ulong session=1)
    {
        var data=capacity??new(new(session),new(4),new(1),rows);
        var overlay=new StrategicOverlaySnapshot(new(4),mode,[],[],[],[],[],[],[],[],new(session),new(1)){LogisticsCapacity=data};
        var experience=default(PlayerExperienceSnapshot) with{Player=new(1),Tick=new(4),MatchStatus=terminal?PlayerMatchStatus.Victory:default};
        var instances=rows.Select(x=>new RenderInstance(x.Entity,RenderTransform.Identity with{Position=x.Start},new(1),RenderMaterialHandle.Default,RenderVisibilityMask.World,Selectable:new(new PlayerId(1),ControllableEntityCategory.Logistics))).ToArray();
        return new(new(4),TimeSpan.FromMilliseconds(50),instances.Length,instances,sessionId:new(session),playerExperience:experience,strategicOverlay:overlay);
    }
}
