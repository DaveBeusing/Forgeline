using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class ObjectActionRingTests
{
    private static readonly EntityId Entity = new(10,1);
    private static readonly RadialActionLayout Geometry = new(new(600,400),new(600,400),60,150,8);

    [Theory]
    [InlineData(PlayerActionPanelMode.Construction, PlatformKey.B, PlayerActionRequestKind.BeginBuildingPlacement)]
    [InlineData(PlayerActionPanelMode.Production, PlatformKey.P, PlayerActionRequestKind.QueueProduction)]
    [InlineData(PlayerActionPanelMode.UnitProduction, PlatformKey.U, PlayerActionRequestKind.QueueUnitProduction)]
    [InlineData(PlayerActionPanelMode.Logistics, PlatformKey.L, PlayerActionRequestKind.SetStockPolicy)]
    [InlineData(PlayerActionPanelMode.Supply, PlatformKey.Y, PlayerActionRequestKind.SetAutomaticResupplyPolicy)]
    [InlineData(PlayerActionPanelMode.Tactical, PlatformKey.K, PlayerActionRequestKind.BeginAttackMoveTargeting)]
    [InlineData(PlayerActionPanelMode.Technology, PlatformKey.H, PlayerActionRequestKind.StartTechnologyResearch)]
    public void RingItemsDispatchExistingRequestsExactlyOnce(PlayerActionPanelMode mode, PlatformKey key, PlayerActionRequestKind expected)
    {
        var (controller,input,snapshot,selection) = Open(mode,key);
        ClickSlot(controller,input,snapshot,selection,mode == PlayerActionPanelMode.Tactical ? 1 : 0);
        Assert.True(controller.TryTakeRequest(out var request)); Assert.Equal(expected,request.Kind);
        Assert.False(controller.TryTakeRequest(out _));
        if (mode is PlayerActionPanelMode.Construction or PlayerActionPanelMode.Tactical) Assert.False(View(controller,snapshot).Ring.Open);
    }

    [Theory] [InlineData("Blocked")] [InlineData("Pending")]
    public void UnavailableItemStillOwnsItsSectorButDoesNotDispatch(string state)
    {
        var (controller,input,snapshot,selection)=Open(PlayerActionPanelMode.Construction,PlatformKey.B,state);
        var point=Geometry.LabelCenter(0);
        Assert.True(controller.ContainsControl(point,snapshot,GameplayHudLayout.Create(1920,1080,96)));
        ClickSlot(controller,input,snapshot,selection,0);
        Assert.False(controller.TryTakeRequest(out _));
    }

    [Fact]
    public void InnerHoleGapsAndRemovedRectanglesRemainWorldInput()
    {
        var (controller,_,snapshot,_)=Open(PlayerActionPanelMode.Production,PlatformKey.P);
        var layout=GameplayHudLayout.Create(1920,1080,96);
        Assert.False(controller.ContainsControl(Geometry.Center,snapshot,layout));
        Assert.False(controller.ContainsControl(Geometry.Center+RadialActionLayout.Direction(Geometry.Start(0)-RadialActionLayout.GapRadians/2)*100,snapshot,layout));
        Assert.False(controller.ContainsControl(new(layout.SafeArea.X+370,layout.SafeArea.Bottom-50),snapshot,layout));
        Assert.False(controller.DockExpanded); Assert.True(controller.SingleObjectActions);
    }

    [Fact]
    public void SelectionRevisionChangeDuringPressCannotDispatchAnotherObject()
    {
        var (controller,input,snapshot,selection)=Open(PlayerActionPanelMode.Production,PlatformKey.P);
        input.BeginFrame(); Mouse(input,PlatformInputEventKind.MouseButtonDown,Geometry.LabelCenter(0));
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        selection.SetSingle(new(10,2));
        input.BeginFrame(); Mouse(input,PlatformInputEventKind.MouseButtonUp,Geometry.LabelCenter(0));
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        Assert.False(controller.TryTakeRequest(out _));
    }

    [Fact]
    public void ParametersCanBeChangedBeforeQueueingAndQueueEntriesCanBeCancelled()
    {
        var (controller,input,snapshot,selection)=Open(PlayerActionPanelMode.Production,PlatformKey.P);
        ClickSlot(controller,input,snapshot,selection,6); // parameters, no implicit queue
        Assert.False(controller.TryTakeRequest(out _));
        ClickSlot(controller,input,snapshot,selection,2); // priority
        Assert.Equal(ForgeLine.Economy.ProductionPriority.High,controller.Priority);
        ClickSlot(controller,input,snapshot,selection,3); // repeat
        Assert.Equal(ForgeLine.Economy.ProductionRequestMode.Repeat,controller.ProductionMode);
        ClickSlot(controller,input,snapshot,selection,0);
        Assert.True(controller.TryTakeRequest(out var queued)); Assert.Equal(PlayerActionRequestKind.QueueProduction,queued.Kind);
        (controller,input,snapshot,selection)=Open(PlayerActionPanelMode.Production,PlatformKey.P);
        ClickSlot(controller,input,snapshot,selection,6); // parameters
        ClickSlot(controller,input,snapshot,selection,6); // first existing request
        ClickSlot(controller,input,snapshot,selection,1); // cancel
        Assert.True(controller.TryTakeRequest(out var cancel)); Assert.Equal(PlayerActionRequestKind.CancelProduction,cancel.Kind);
        Assert.Equal(new EntityId(20,1),cancel.RequestEntity);
    }

    [Fact]
    public void LostAnchorClosesAndReturningGeometryDoesNotReopen()
    {
        var (controller,_,snapshot,_)=Open(PlayerActionPanelMode.Production,PlatformKey.P);
        var ring=View(controller,snapshot).Ring;
        controller.AdoptPresentedRing(ring with { Geometry=default,Resolved=true });
        Assert.False(View(controller,snapshot).Ring.Open);
        controller.AdoptPresentedRing(ring);
        Assert.False(View(controller,snapshot).Ring.Open);
    }

    [Fact]
    public void OpeningSelectionReleaseNeverActivatesAnAppearingSector()
    {
        var snapshot=Snapshot(PlayerActionPanelMode.Production,"Ready"); var world=World(snapshot);
        var selection=new SelectionSet(); var input=new InputState(); var controller=new PlayerActionPanelController();
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        input.BeginFrame(); Mouse(input,PlatformInputEventKind.MouseButtonDown,Geometry.LabelCenter(0));
        Mouse(input,PlatformInputEventKind.MouseButtonUp,Geometry.LabelCenter(0));
        controller.SynchronizeObjectRing(input,snapshot,world,selection,false);
        selection.SetSingle(Entity);
        controller.SynchronizeObjectRing(input,snapshot,world,selection,true,selectionResolved:true);
        var ring=View(controller,snapshot).Ring; Assert.True(ring.Open);
        controller.AdoptPresentedRing(ring with { Geometry=Geometry,Resolved=true });
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        Assert.False(controller.TryTakeRequest(out _));
    }

    [Fact]
    public void QueueRemovalKeepsSurvivingRequestInItsExistingSector()
    {
        var (controller,input,snapshot,selection)=Open(PlayerActionPanelMode.Production,PlatformKey.P);
        var a=snapshot.PlayerActions!;var p=a.Production!;
        var remaining=new PlayerProductionFacilityActionReadModel(p.Entity,default,default,p.Status,p.BlockReason,p.Progress,p.Recipes,[p.Requests[1]]);
        var next=new PresentationSnapshot(new(5),snapshot.TickDuration,1,snapshot.Instances,sessionId:snapshot.SessionId,
            playerExperience:snapshot.PlayerExperience!.Value with {Tick=new(5)},
            playerActions:new(snapshot.SessionId,new(5),a.Construction,0,remaining,null,tactical:a.Tactical));
        input.BeginFrame();controller.Update(input,next,1920,1080,currentSelection:selection);
        Present(controller,next);
        Assert.False(controller.ContainsControl(Geometry.LabelCenter(1),next,GameplayHudLayout.Create(1920,1080,96)));
        Assert.True(controller.ContainsControl(Geometry.LabelCenter(2),next,GameplayHudLayout.Create(1920,1080,96)));
        ClickSlot(controller,input,next,selection,2);
        Assert.True(controller.TryTakeRequest(out var request));
        Assert.Equal(new EntityId(21,1),request.RequestEntity);
    }

    [Fact]
    public void CentralSelectionCommitDoesNotDismissTheNewRingOnTheFollowingFrame()
    {
        var snapshot=Snapshot(PlayerActionPanelMode.Production,"Ready");var world=World(snapshot);var selection=new SelectionSet();
        var input=new InputState();var controller=new PlayerActionPanelController();var menus=new TransientHudMenuController();
        var operations=new OperationsController();var interaction=new PresentationInteractionState();var layout=GameplayHudLayout.Create(1920,1080,96);
        Mouse(input,PlatformInputEventKind.MouseButtonDown,new(600,400));Mouse(input,PlatformInputEventKind.MouseButtonUp,new(600,400));
        menus.Update(input,snapshot,layout,selection,controller,operations,interaction,true,false);
        input.BeginFrame();Mouse(input,PlatformInputEventKind.MouseButtonDown,new(600,400));Mouse(input,PlatformInputEventKind.MouseButtonUp,new(600,400));
        selection.SetSingle(Entity);menus.SelectionCommitted(input,selection,controller,operations,interaction);
        controller.SynchronizeObjectRing(input,snapshot,world,selection,true,selectionResolved:true);
        Assert.True(View(controller,snapshot).Ring.Open);
        input.BeginFrame();menus.Update(input,snapshot,layout,selection,controller,operations,interaction,true,false);
        Assert.True(View(controller,snapshot).Ring.Open);
    }

    [Fact]
    public void OrdinarySelectionPressAndReleaseInSeparateFramesOpensExactlyOnce()
    {
        var snapshot=Snapshot(PlayerActionPanelMode.Production,"Ready");var world=World(snapshot);
        var input=new InputState();var controller=new PlayerActionPanelController();var selection=new SelectionSet();
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        input.BeginFrame();Mouse(input,PlatformInputEventKind.MouseButtonDown,new(600,400));
        controller.SynchronizeObjectRing(input,snapshot,world,selection,false);
        controller.SynchronizeObjectRing(input,snapshot,world,selection,true,selectionResolved:true);
        Assert.False(View(controller,snapshot).Ring.Open);
        input.BeginFrame();Mouse(input,PlatformInputEventKind.MouseButtonUp,new(600,400));
        Assert.False(input.TryGetMousePressPosition(PlatformMouseButton.Left,out _));
        controller.SynchronizeObjectRing(input,snapshot,world,selection,false);
        selection.SetSingle(Entity);
        new TransientHudMenuController().SelectionCommitted(input,selection,controller,new OperationsController(),new PresentationInteractionState());
        controller.SynchronizeObjectRing(input,snapshot,world,selection,true,selectionResolved:true);
        Assert.True(View(controller,snapshot).Ring.Open);Assert.False(controller.TryTakeRequest(out _));
        controller.Close();input.BeginFrame();
        controller.SynchronizeObjectRing(input,snapshot,world,selection,true,selectionResolved:true);
        Assert.False(View(controller,snapshot).Ring.Open);
    }

    [Fact]
    public void NewQueueRequestFillsRemovedSlotWithoutMovingSurvivorsOrReusingAnArmedPress()
    {
        var (controller,input,snapshot,selection)=Open(PlayerActionPanelMode.Production,PlatformKey.P);
        input.BeginFrame();Mouse(input,PlatformInputEventKind.MouseButtonDown,Geometry.LabelCenter(1));
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        var a=snapshot.PlayerActions!;var p=a.Production!;
        var replacement=p.Requests[0] with {RequestEntity=new(22,1),DisplayName="New request"};
        var production=new PlayerProductionFacilityActionReadModel(p.Entity,default,default,p.Status,p.BlockReason,p.Progress,p.Recipes,[p.Requests[1],replacement]);
        var next=new PresentationSnapshot(new(5),snapshot.TickDuration,1,snapshot.Instances,sessionId:snapshot.SessionId,
            playerExperience:snapshot.PlayerExperience!.Value with {Tick=new(5)},
            playerActions:new(snapshot.SessionId,new(5),a.Construction,0,production,null,tactical:a.Tactical));
        Present(controller,next);input.BeginFrame();Mouse(input,PlatformInputEventKind.MouseButtonUp,Geometry.LabelCenter(1));
        controller.Update(input,next,1920,1080,currentSelection:selection);
        Assert.False(controller.TryTakeRequest(out _));
        ClickSlot(controller,input,next,selection,1);
        Assert.True(controller.TryTakeRequest(out var request));Assert.Equal(new EntityId(22,1),request.RequestEntity);
    }

    [Fact]
    public void DismissalDuringAWorldPressCancelsThePendingSelectionOpen()
    {
        var (controller,input,snapshot,selection)=Open(PlayerActionPanelMode.Production,PlatformKey.P);var world=World(snapshot);
        controller.Close();input.BeginFrame();Mouse(input,PlatformInputEventKind.MouseButtonDown,new(600,400));
        controller.SynchronizeObjectRing(input,snapshot,world,selection,true,selectionResolved:true);
        controller.DismissTransient(input);
        input.BeginFrame();Mouse(input,PlatformInputEventKind.MouseButtonUp,new(600,400));
        controller.SynchronizeObjectRing(input,snapshot,world,selection,true,selectionResolved:true);
        Assert.False(View(controller,snapshot).Ring.Open);
    }

    [Fact]
    public void EmptySelectionKeepsExistingGlobalTechnologyShortcutAndDispatchesOnce()
    {
        var source=Snapshot(PlayerActionPanelMode.Technology,"Ready");
        var a=source.PlayerActions!;
        var actions=new PlayerActionSnapshot(a.SessionId,a.Tick,a.Construction,a.PendingCommandCount,
            null,null,technology:a.Technology);
        var snapshot=new PresentationSnapshot(source.Tick,source.TickDuration,source.SimulationEntityCount,source.Instances,
            sessionId:source.SessionId,playerExperience:source.PlayerExperience!.Value with {Selection=PlayerSelectionSummary.Empty},playerActions:actions);
        var selection=new SelectionSet();var input=new InputState();var controller=new PlayerActionPanelController();
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        controller.SynchronizeObjectRing(input,snapshot,World(snapshot),selection,true,true);
        Assert.False(View(controller,snapshot).Ring.Open);
        input.BeginFrame();input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,PlatformKey.H));
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        Assert.True(View(controller,snapshot).Ring.Global);
        Assert.True(View(controller,snapshot).Ring.Open);
        Assert.False(View(controller,snapshot).Ring.SingleOwned);
        input.BeginFrame();input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp,PlatformKey.H));
        ClickSlot(controller,input,snapshot,selection,0);
        Assert.True(controller.TryTakeRequest(out var request));
        Assert.Equal(PlayerActionRequestKind.StartTechnologyResearch,request.Kind);
        Assert.False(controller.TryTakeRequest(out _));
    }

    private static (PlayerActionPanelController,InputState,PresentationSnapshot,SelectionSet) Open(PlayerActionPanelMode mode,PlatformKey key,string state="Ready")
    {
        var snapshot=Snapshot(mode,state); var selection=new SelectionSet(); selection.SetSingle(Entity);
        var input=new InputState(); var controller=new PlayerActionPanelController();
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        controller.SynchronizeObjectRing(input,snapshot,World(snapshot),selection,false);
        input.BeginFrame(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,key));
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        Assert.Equal(mode,controller.Mode);
        input.BeginFrame(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp,key));
        Present(controller,snapshot);
        return (controller,input,snapshot,selection);
    }
    private static void ClickSlot(PlayerActionPanelController controller,InputState input,PresentationSnapshot snapshot,SelectionSet selection,int slot)
    {
        Present(controller,snapshot); input.BeginFrame();
        Mouse(input,PlatformInputEventKind.MouseButtonDown,Geometry.LabelCenter(slot));
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        input.BeginFrame(); Mouse(input,PlatformInputEventKind.MouseButtonUp,Geometry.LabelCenter(slot));
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
    }
    private static void Present(PlayerActionPanelController controller,PresentationSnapshot snapshot)=>
        controller.AdoptPresentedRing(View(controller,snapshot).Ring with { Geometry=Geometry,Resolved=true });
    private static PlayerActionPanelView View(PlayerActionPanelController controller,PresentationSnapshot snapshot)=>controller.CreateView(1920,1080,snapshot.PlayerActions);
    private static void Mouse(InputState input,PlatformInputEventKind kind,Vector2 p)=>input.Apply(PlatformInputEvent.MouseButtonChanged(kind,PlatformMouseButton.Left,(int)p.X,(int)p.Y));
    private static RenderWorld World(PresentationSnapshot snapshot) { var buffer=new PresentationSnapshotBuffer(); buffer.Publish(snapshot); var world=new RenderWorld(); world.Update(buffer); return world; }
    private static PresentationSnapshot Snapshot(PlayerActionPanelMode mode,string state)
    {
        var source=new PresentationSnapshot(new SimulationTick(4),TimeSpan.FromMilliseconds(50),1,
            [new RenderInstance(Entity,RenderTransform.Identity,default,default,RenderVisibilityMask.World,
                Selectable:new(new PlayerId(1),ControllableEntityCategory.Building))],sessionId:new(1), playerExperience:default(PlayerExperienceSnapshot) with { Player=new PlayerId(1),Tick=new SimulationTick(4) });
        var fixture=CommandDockCapture.Fixture(source,mode,state,1920,1080,96,1).Snapshot;
        var a=fixture.PlayerActions!;
        var tactical=a.Tactical ?? new PlayerTacticalActionReadModel([Entity],1,0,0,0,0,false,false,default,default,[],[]);
        var actions=new PlayerActionSnapshot(a.SessionId,a.Tick,a.Construction,a.PendingCommandCount,a.Production,a.UnitProduction,a.Logistics,a.Supply,tactical,a.Technology);
        return new(fixture.Tick,fixture.TickDuration,fixture.SimulationEntityCount,fixture.Instances,sessionId:fixture.SessionId,
            playerExperience:fixture.PlayerExperience,playerActions:actions);
    }
}
