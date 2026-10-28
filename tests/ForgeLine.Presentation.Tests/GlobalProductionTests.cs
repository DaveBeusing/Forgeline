using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;
using static ForgeLine.Presentation.Tests.SelectionOverlayRenderingTests;

namespace ForgeLine.Presentation.Tests;

public sealed class GlobalProductionTests
{
    [Fact]
    public void SkippedPauseFrameCannotRearmAnOldHeldCancelGesture()
    {
        var controller=new GlobalProductionController(GameplayBindingRegistry.Default);controller.Open();
        var snapshot=Snapshot();var input=new InputState();var layout=GameplayHudLayout.Create(1600,900,96);
        controller.Update(input,snapshot,layout,[],false);
        var row=GlobalProductionLayout.Control(layout,10);
        Edge(input,PlatformInputEventKind.MouseButtonDown,row);controller.Update(input,snapshot,layout,[],false);
        Edge(input,PlatformInputEventKind.MouseButtonUp,row);controller.Update(input,snapshot,layout,[],false);
        var cancel=GlobalProductionLayout.Control(layout,23);
        Edge(input,PlatformInputEventKind.MouseButtonDown,cancel);
        controller.CancelInput(input); // The parent skips the subpanel update while paused.
        input.BeginFrame();controller.Update(input,snapshot,layout,[],false);
        Edge(input,PlatformInputEventKind.MouseButtonUp,cancel);controller.Update(input,snapshot,layout,[],false);
        Assert.False(controller.TryTakeCommand(out _));
    }

    [Fact]
    public void QueueFeedbackRequiresMatchingSubmissionCorrelationAndSession()
    {
        var controller=new GlobalProductionController(GameplayBindingRegistry.Default); controller.Open();
        controller.Submitted(true,44);
        var receipt=default(PlayerCommandSubmissionReceipt) with {SessionId=new(1),CorrelationId=new(8),Accepted=true};
        controller.ObserveSubmission(43,receipt);
        var result=default(PlayerCommandResultReadModel) with {SessionId=new(1),CorrelationId=new(8),State=PlayerCommandFeedbackState.Rejected};
        controller.ObserveResult(result); Assert.StartsWith("SUBMITTED",controller.View.Feedback);
        controller.ObserveSubmission(44,receipt);
        controller.ObserveResult(result with {SessionId=new(2)}); Assert.StartsWith("SUBMITTED",controller.View.Feedback);
        controller.ObserveResult(result); Assert.StartsWith("REJECTED",controller.View.Feedback);
        controller.ObserveResult(result with {State=PlayerCommandFeedbackState.Accepted}); Assert.StartsWith("REJECTED",controller.View.Feedback);
    }

    [Fact]
    public void SharedInventoryIsCountedOnceForAllAndSelectedRolesAndCopiesCannotBeMutated()
    {
        var amounts = new[] { new GlobalProductionStockAmount(ResourceIds.Steel,"Steel",100,40) };
        var materials = new[] { new GlobalProductionMaterial(ResourceIds.Steel,"Steel",20) };
        var orders = new[] { new GlobalProductionOrder(new(90,1),UnitIds.RifleSquad,"Rifle Squad",ProductionPriority.Normal,false,true,true,materials) };
        var factories = new[] { Factory(new(1,1), orders), Factory(new(2,1), orders) };
        var data = new GlobalProductionSnapshot(new(1),new(1),new(1),factories,[new(new(1),true,amounts)],2,2);
        amounts[0] = amounts[0] with { Quantity=999 }; materials[0] = materials[0] with { Required=999 };
        factories[0] = factories[0] with { Name="changed" }; orders[0] = orders[0] with { Name="changed" };
        Assert.Equal((60d,40d),data.Sum(ResourceIds.Steel,GlobalProductionFilter.All,[]));
        Assert.Equal((60d,40d),data.Sum(ResourceIds.Steel,GlobalProductionFilter.Selected,[new(1,1),new(2,1)]));
        Assert.Equal((0d,0d),data.Sum(ResourceIds.Steel,GlobalProductionFilter.Vehicles,[]));
        Assert.Equal(20,data.Factories[0].Orders[0].Materials[0].Required);
        Assert.Equal("Factory",data.Factories[0].Name);
        Assert.Equal("Rifle Squad",data.Factories[0].Orders[0].Name);
    }

    [Theory]
    [InlineData(1)] [InlineData(16)] [InlineData(300)] [InlineData(1000)]
    public void CompletedTickCaptureIsBoundedOwnedAndMatchesTheSelectedFactory(int count)
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var interaction = new PresentationInteractionState(); interaction.SetOperationsOpen(true);
        var buffer = WorldHoverExtractionTests.Observe(scenario,interaction);
        scenario.Simulation.AdvanceOneTick(); Assert.True(buffer.TryReadLatest(out var baseline));
        int initial = GlobalProductionSnapshot.Resolve(baseline)!.FactoryCount;
        var entities = scenario.Simulation.Entities;
        var inventory = scenario.Inventories.CreateInventory(new InventorySpecification(10000000));
        foreach (var resource in scenario.Services.Resources.Definitions)
            Assert.True(scenario.Inventories.Add(inventory,resource.Id,100000).Succeeded);
        EntityId last = default;
        for (int i=0;i<count;i++)
        {
            last = Create(new(1));
            Queue(last,ProductionPriority.Normal);
        }
        for(int i=0;i<24;i++) Queue(last,ProductionPriority.Low);
        var foreign = Create(new(2)); Queue(foreign,ProductionPriority.High);
        interaction.SetSelection([last]);
        scenario.Simulation.AdvanceOneTick(); Assert.True(buffer.TryReadLatest(out var snapshot));
        var data = Assert.IsType<GlobalProductionSnapshot>(GlobalProductionSnapshot.Resolve(snapshot));
        Assert.Equal(initial+count,data.FactoryCount);
        Assert.InRange(data.Factories.Count,1,GlobalProductionSnapshot.MaximumFactories);
        Assert.Null(data.Find(foreign));
        var factory = Assert.IsType<GlobalProductionFactory>(data.Find(last));
        var selected = snapshot.PlayerActions!.UnitProduction!;
        Assert.Equal(selected.Entity,factory.Entity); Assert.Equal(selected.Progress,factory.Progress);
        Assert.Equal(selected.Status,factory.Status); Assert.Equal(selected.BlockReason,factory.BlockReason);
        Assert.Equal(selected.RallyPoint,factory.RallyPoint);
        Assert.Equal(25,factory.QueueCount); Assert.Equal(16,factory.Orders.Count);
        Assert.True(factory.Orders[0].Active); Assert.Equal(selected.ActiveRequest,factory.Orders[0].Request);
        Assert.False(factory.Orders[0].InputsReserved);
        Assert.Equal(UnitProductionBlockReason.NoPower,factory.BlockReason);
        Assert.Equal((scenario.Inventories.GetAvailableQuantity(inventory,ResourceIds.Steel),
            scenario.Inventories.GetReservedQuantity(inventory,ResourceIds.Steel)),
            data.Sum(ResourceIds.Steel,GlobalProductionFilter.Selected,[last]));
        double progress = factory.Progress;
        entities.DestroyEntity(last);
        scenario.Simulation.AdvanceOneTick(); Assert.True(buffer.TryReadLatest(out var removed));
        Assert.Null(GlobalProductionSnapshot.Resolve(removed)!.Find(last));
        Assert.Equal(progress,factory.Progress); // An older published copy remains independent.
        interaction.SetOperationsOpen(false);
        scenario.Simulation.AdvanceOneTick(); Assert.True(buffer.TryReadLatest(out var closed));
        Assert.Null(closed.Operations);

        EntityId Create(PlayerId owner)
        {
            var entity=entities.CreateEntity();
            entities.AddComponent(entity,new ControllableEntity(owner,ControllableEntityCategory.Building));
            entities.AddComponent(entity,new WorldTransform(Vector3.Zero,Quaternion.Identity,Vector3.One));
            entities.AddComponent(entity,new UnitProductionFacility(owner == new PlayerId(1) ? inventory : scenario.Inventories.CreateInventory(new InventorySpecification(1000)),UnitProductionCapability.Infantry,owner,Vector3.Zero,scenario.Simulation.CurrentTick));
            return entity;
        }
        void Queue(EntityId facility, ProductionPriority priority)
        {
            var entity=entities.CreateEntity();
            entities.AddComponent(entity,new UnitProductionRequest(facility,UnitIds.RifleSquad,priority,scenario.Simulation.CurrentTick));
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void HeldCancelCannotRetargetChangedOrderFactorySessionOrDisplay(int change)
    {
        var controller=new GlobalProductionController(GameplayBindingRegistry.Default); controller.Open();
        var input=new InputState(); var layout=GameplayHudLayout.Create(1600,900,96);
        var before=Snapshot(); controller.Update(input,before,layout,[],false);
        Click(10); Assert.Equal(new EntityId(1,1),controller.View.Factory);
        var cancel=GlobalProductionLayout.Control(layout,23);
        Edge(input,PlatformInputEventKind.MouseButtonDown,cancel);
        controller.Update(input,before,layout,[],false);
        var after=change switch
        {
            0=>Snapshot(request:new(91,2)), 1=>Snapshot(factory:new(1,2)),
            2=>Snapshot(session:2), _=>before
        };
        var nextLayout=change==3?GameplayHudLayout.Create(1920,1080,144):layout;
        Edge(input,PlatformInputEventKind.MouseButtonUp,cancel);
        controller.Update(input,after,nextLayout,[],false);
        Assert.False(controller.TryTakeCommand(out _));

        void Click(int index)
        {
            var rect=GlobalProductionLayout.Control(layout,index);
            Edge(input,PlatformInputEventKind.MouseButtonDown,rect); controller.Update(input,before,layout,[],false);
            Edge(input,PlatformInputEventKind.MouseButtonUp,rect); controller.Update(input,before,layout,[],false);
        }
    }

    [Fact]
    public void CollapseReleasesHiddenPointerAreaAndMissingDataStillAllowsClose()
    {
        var controller=new GlobalProductionController(GameplayBindingRegistry.Default); controller.Open();
        var input=new InputState(); var layout=GameplayHudLayout.Create(1600,900,96); var snapshot=Snapshot();
        controller.Update(input,snapshot,layout,[],false);
        Click(3); Assert.True(controller.View.Collapsed);
        var oldRow=GlobalProductionLayout.Control(layout,12);
        input.BeginFrame(); input.Apply(PlatformInputEvent.PointerMoved((int)oldRow.X+1,(int)oldRow.Y+1));
        Assert.False(controller.Update(input,snapshot,layout,[],false).Captured);
        var absent=new PresentationSnapshot(snapshot.Tick,TimeSpan.FromMilliseconds(50),0,[],
            sessionId:snapshot.SessionId,playerExperience:snapshot.PlayerExperience);
        var close=GlobalProductionLayout.Control(layout,0);
        Edge(input,PlatformInputEventKind.MouseButtonDown,close); controller.Update(input,absent,layout,[],false);
        Edge(input,PlatformInputEventKind.MouseButtonUp,close);
        Assert.True(controller.Update(input,absent,layout,[],false).Close);
        void Click(int index)
        {
            var rect=GlobalProductionLayout.Control(layout,index);
            Edge(input,PlatformInputEventKind.MouseButtonDown,rect); controller.Update(input,snapshot,layout,[],false);
            Edge(input,PlatformInputEventKind.MouseButtonUp,rect); controller.Update(input,snapshot,layout,[],false);
        }
    }

    [Theory]
    [InlineData(1280,720,96u,1f)] [InlineData(1280,720,192u,1.5f)]
    [InlineData(1920,1080,144u,1.25f)] [InlineData(3440,1440,144u,1.5f)]
    public void ExpandedAndCollapsedControlsStayWithinTheirPublishedPanel(int width,int height,uint dpi,float scale)
    {
        var layout=GameplayHudLayout.Create(width,height,dpi,scale);
        foreach(bool collapsed in new[]{false,true})
        {
            var panel=GlobalProductionLayout.Panel(layout,collapsed);
            foreach(int index in collapsed?new[]{0,3,26}:new[]{0,1,2,3,4,10,11,12,13,14,15,20,21,22,23,24,25,26})
            {
                var rect=GlobalProductionLayout.Control(layout,index);
                Assert.InRange(rect.X,panel.X,panel.Right); Assert.InRange(rect.Right,panel.X,panel.Right+.01f);
                Assert.InRange(rect.Y,panel.Y,panel.Bottom); Assert.InRange(rect.Bottom,panel.Y,panel.Bottom+.01f);
            }
        }
    }

    internal static GlobalProductionFactory Factory(EntityId entity,IReadOnlyList<GlobalProductionOrder>? orders=null) =>
        new(entity,"Factory",UnitProductionCapability.Infantry,UnitProductionStatus.Running,UnitProductionBlockReason.None,
            new(90,1),.25,orders?.Count??1,0,new Vector3(10,0,20),orders??[Order(new(90,1))]);
    internal static GlobalProductionOrder Order(EntityId request)=>new(request,UnitIds.RifleSquad,"Rifle Squad",ProductionPriority.Normal,
        false,true,true,[new(ResourceIds.Steel,"Steel",20)]);
    internal static PresentationSnapshot Snapshot(EntityId factory=default,EntityId request=default,ulong session=1)
    {
        if(!factory.IsValid)factory=new(1,1);if(!request.IsValid)request=new(90,1);
        var row=Factory(factory,[Order(request)]) with {ActiveRequest=request};
        var data=new GlobalProductionSnapshot(new(session),new(4),new(1),[row],[new(new(1),true,[new(ResourceIds.Steel,"Steel",100,40)])],1,1);
        var ops=new OperationsSnapshot(new(session),new(4),new(1),[],[],[],0,0){Production=data};
        var instance=new RenderInstance(factory,RenderTransform.Identity,new(1),RenderMaterialHandle.Default,
            RenderVisibilityMask.World,Selectable:new(new PlayerId(1),ControllableEntityCategory.Building));
        return new(new(4),TimeSpan.FromMilliseconds(50),1,[instance],sessionId:new(session),
            playerExperience:default(PlayerExperienceSnapshot) with {Player=new(1),Tick=new(4)},operations:ops);
    }
    internal static void Edge(InputState input,PlatformInputEventKind kind,HudRect rect)
    {
        input.BeginFrame(); int x=(int)(rect.X+rect.Width/2), y=(int)(rect.Y+rect.Height/2);
        input.Apply(PlatformInputEvent.MouseButtonChanged(kind,PlatformMouseButton.Left,x,y));
    }
}
