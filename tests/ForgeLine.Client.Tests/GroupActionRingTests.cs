using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Simulation;
using ForgeLine.World;
using ForgeLine.Intelligence;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GroupActionRingTests
{
    private static readonly RadialActionLayout Geometry = new(new(960,450),new(960,450),80,176,8);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(10)] [InlineData(50)] [InlineData(100)] [InlineData(1000)]
    public void OneBoundedContextRetainsTheEntireAuthorizedSelection(int count)
    {
        var (controller,input,snapshot,selection,world) = Open(count);
        var ring = View(controller,snapshot).Ring;
        Assert.Equal(count > 0,ring.Open);
        Assert.Equal(count > 1,ring.Group is not null);
        Assert.Equal(count == 1,ring.SingleOwned);
        Assert.False(controller.DockExpanded);
        if (count < 2) return;
        Assert.Equal(count,ring.Group!.Count);
        Assert.Equal(count,selection.Count);
        var layout = GameplayHudLayout.Create(1920,1080,96);
        var projected = ring.Project(world,new RtsCamera(),1,snapshot,layout,[]);
        Assert.True(projected.Geometry.IsValid);
        Assert.InRange(projected.Geometry.OuterRadius,176,308);
        var camera = new RtsCamera();
        int expectedVisible = snapshot.Instances.ToArray().Count(instance => camera.WorldToScreen(instance.Transform.Position,1920,1080).IsVisible);
        Assert.Equal(expectedVisible,projected.VisibleMembers);
        Assert.Equal(count,projected.Group!.Count);
        Assert.Equal(-1,projected.Geometry.Hit(projected.Geometry.Center));
    }

    [Fact]
    public void TacticalRingUsesExactlyTheExistingDispatcherRequestOnceWithAllMembers()
    {
        var (controller,input,snapshot,selection,_) = Open(10);
        Click(controller,input,snapshot,selection,0); // common tactical category
        Assert.False(View(controller,snapshot).DockExpanded);
        Click(controller,input,snapshot,selection,2); // Stop
        Assert.True(controller.TryTakeRequest(out var request));
        var expected = PlayerActionRequest.StopCombat(selection.ToArray());
        Assert.Equal(expected.Kind,request.Kind);
        Assert.Equal(expected.TacticalEntities,request.TacticalEntities);
        Assert.Equal(expected with {TacticalEntities=request.TacticalEntities},request);
        Assert.False(controller.TryTakeRequest(out _));
        Assert.False(View(controller,snapshot).Ring.Open);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void SelectionChangesCancelArmedCommandsAndTransitionTwoToOneToZero(int remaining)
    {
        var (controller,input,snapshot,selection,_) = Open(2);
        Click(controller,input,snapshot,selection,0);
        Present(controller,snapshot); input.BeginFrame(); Mouse(input,PlatformInputEventKind.MouseButtonDown,2);
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        selection.Replace(Enumerable.Range(1,remaining).Select(i=>new EntityId((uint)i,1)).ToArray());
        var next = Snapshot(remaining,tick:5); var world=World(next);
        controller.SynchronizeObjectRing(input,next,world,selection,false);
        input.BeginFrame(); Mouse(input,PlatformInputEventKind.MouseButtonUp,2);
        controller.Update(input,next,1920,1080,currentSelection:selection);
        controller.SynchronizeObjectRing(input,next,world,selection,true,true);
        bool dispatched = controller.TryTakeRequest(out var request);
        Assert.Equal(remaining == 2, dispatched);
        if (remaining == 2)
        {
            Assert.Equal(PlayerActionRequestKind.SubmitStopCombat, request.Kind);
            Assert.Equal(selection.ToArray(), request.TacticalEntities);
            Assert.False(controller.TryTakeRequest(out _));
        }
        Assert.Equal(remaining==1,View(controller,next).Ring.SingleOwned);
        Assert.Equal(remaining == 1,View(controller,next).Ring.Open);
    }

    [Fact]
    public void SameCountDifferentGenerationAndSessionInvalidatePressedGroupAction()
    {
        foreach (bool session in new[]{false,true})
        {
            var (controller,input,snapshot,selection,_) = Open(2);
            Click(controller,input,snapshot,selection,0);
            Present(controller,snapshot); input.BeginFrame(); Mouse(input,PlatformInputEventKind.MouseButtonDown,2);
            controller.Update(input,snapshot,1920,1080,currentSelection:selection);
            var next = Snapshot(2,tick:5,session:session?2ul:1ul,generation:session?1u:2u);
            selection.Replace(next.PlayerActions!.Tactical!.SelectedEntities.ToArray());
            controller.SynchronizeObjectRing(input,next,World(next),selection,false);
            input.BeginFrame(); Mouse(input,PlatformInputEventKind.MouseButtonUp,2);
            controller.Update(input,next,1920,1080,currentSelection:selection);
            Assert.False(controller.TryTakeRequest(out _));
            Assert.False(controller.TakeGroupResult().SelectionChanged);
        }
    }

    [Fact]
    public void AllTypeFiltersAndAttentionFormationFocusRemainAccessibleWithoutEditingSavedMembership()
    {
        for (int control=0;control<12;control++)
        {
            var (controller,input,snapshot,selection,_) = Open(8);
            var registry=new CombatGroupRegistry();
            registry.Synchronize(snapshot.SessionId,snapshot.CombatGroups!.EligibleEntities);
            registry.Assign(1,selection.Entities,snapshot.CombatGroups.EligibleEntities);
            Click(controller,input,snapshot,selection,7);
            for(int page=0;page<control/4;page++) Click(controller,input,snapshot,selection,5);
            Click(controller,input,snapshot,selection,control%4);
            var result=controller.TakeGroupResult();
            Assert.True(result.Captured);
            Assert.Equal(control<8,result.SelectionChanged);
            Assert.Equal(control==10,result.CycleFormation);
            Assert.Equal(control==11,result.FocusSelection);
            Assert.Equal(control is 8 or 9,result.FocusMember.IsValid);
            Assert.Equal(8,registry.GetSlot(1).Members.Length);
            Assert.False(controller.TryTakeRequest(out _));
        }
    }

    [Fact]
    public void OffscreenMemberDoesNotFilterTheCommandAndAnEntirelyOffscreenGroupCloses()
    {
        var (controller,input,snapshot,selection,_) = Open(2);
        var next=Snapshot(2,tick:5,spread:true);
        controller.SynchronizeObjectRing(input,next,World(next),selection,false);
        var ring=View(controller,next).Ring.Project(World(next),new RtsCamera(),1,next,GameplayHudLayout.Create(1920,1080,96),[]);
        Assert.True(ring.Geometry.IsValid); Assert.Equal(1,ring.VisibleMembers); Assert.Equal(2,ring.Group!.Count);
        Click(controller,input,next,selection,0); Click(controller,input,next,selection,2);
        Assert.True(controller.TryTakeRequest(out var request)); Assert.Equal(2,request.TacticalEntities!.Length);
        var gone=Snapshot(2,tick:6,allOffscreen:true);
        controller.SynchronizeObjectRing(input,gone,World(gone),selection,true,true);
        controller.OpenOperationsControls(PlayerActionPanelMode.Supply);
        var projected=View(controller,gone).Ring.Project(World(gone),new RtsCamera(),1,gone,GameplayHudLayout.Create(1920,1080,96),[]);
        controller.AdoptPresentedRing(projected);
        Assert.False(View(controller,gone).Ring.Open); Assert.Equal(2,selection.Count);
    }

    [Fact]
    public void ReconciliationPrunesLostAuthorityAndGenerationWithoutVisibilityFiltering()
    {
        var (_,_,snapshot,selection,world)=Open(3);
        var owner=new RtsSelectionController(new(new PlayerId(1),ControllableEntityCategory.Unit));
        owner.Selection.Replace(selection.ToArray());
        Assert.False(owner.ReconcileSelection(world));
        var next=Snapshot(2,tick:5,spread:true);
        Assert.True(owner.ReconcileSelection(World(next))); Assert.Equal(2,owner.Selection.Count);
        Assert.False(owner.ReconcileSelection(World(next)));
        Assert.True(owner.ReconcileSelection(World(Snapshot(1,tick:6)))); Assert.Equal(1,owner.Selection.Count);
        Assert.True(owner.ReconcileSelection(World(Snapshot(0,tick:7)))); Assert.Equal(0,owner.Selection.Count);
    }

    [Fact]
    public void UnchangedInputSnapshotReusesImmutableContextAndDoesNotAllocate()
    {
        var (controller,input,snapshot,selection,world)=Open(1000);
        var group=View(controller,snapshot).Ring.Group;
        for(int i=0;i<128;i++) controller.SynchronizeObjectRing(input,snapshot,world,selection,false);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<128;i++) controller.SynchronizeObjectRing(input,snapshot,world,selection,false);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        Assert.Same(group,View(controller,snapshot).Ring.Group);
    }

    [Fact]
    public void ForeignAndGenerationMismatchedActorsNeverContributeAnchorOrCapabilities()
    {
        var snapshot=Snapshot(2);var selection=new SelectionSet();selection.Replace([new(1,1),new(2,2),new(99,1)]);
        var group=GroupActionRingContext.Capture(snapshot,World(snapshot),selection)!;
        Assert.Equal(1,group.Count);Assert.Equal(3,group.SelectedCount);
        Assert.Equal(1,group.Participants(10));
        var foreign=snapshot.Instances.ToArray();
        foreign[0]=foreign[0] with{Selectable=new(new PlayerId(2),ControllableEntityCategory.Unit)};
        var changed=new PresentationSnapshot(snapshot.Tick,snapshot.TickDuration,2,foreign,sessionId:snapshot.SessionId,playerExperience:snapshot.PlayerExperience);
        var filtered=GroupActionRingContext.Capture(changed,World(changed),selection)!;
        Assert.Equal(0,filtered.Count);
        Assert.False(filtered.TryAnchor(World(changed),new RtsCamera(),1,new(1),GameplayHudLayout.Create(1920,1080,96),out _,out _));
    }

    [Fact]
    public void MissingOperationalFactsRemainUnknownAndCannotEnableGroupFilters()
    {
        var snapshot=Snapshot(2);
        var noFacts=new PresentationSnapshot(snapshot.Tick,snapshot.TickDuration,2,snapshot.Instances,sessionId:snapshot.SessionId,playerExperience:snapshot.PlayerExperience);
        var selection=new SelectionSet();selection.Replace([new(1,1),new(2,1)]);
        var context=GroupActionRingContext.Capture(noFacts,World(noFacts),selection)!;
        Assert.Equal(2,context.Count);Assert.False(context.HasOperationalFacts);Assert.Equal(0,context.Participants(10));Assert.Equal(0,context.Participants(8));
        var control=new CombatGroupCardController();Assert.False(control.Activate(3,noFacts,selection).SelectionChanged);
        Assert.Equal(2,selection.Count);
    }

    [Fact]
    public void SavedGroupRecallReopensOneContextAndDismissedContextDoesNotReopenAtNextTick()
    {
        var (controller,input,snapshot,selection,world)=Open(10);
        Click(controller,input,snapshot,selection,7);
        controller.DismissTransient(input);
        input.BeginFrame();
        controller.SynchronizeObjectRing(input,Snapshot(10,tick:5),World(Snapshot(10,tick:5)),selection,true,true);
        Assert.False(View(controller,snapshot).Ring.Open);
        controller.ReopenSelectionRing();
        controller.SynchronizeObjectRing(input,snapshot,world,selection,true,true);
        Assert.True(View(controller,snapshot).Ring.Open);
        Assert.Equal(10,View(controller,snapshot).Ring.Group!.Count);
        controller.DismissTransient(input);
        selection.Replace([new(2,1),new(3,1)]);
        controller.SynchronizeObjectRing(input,snapshot,world,selection,true,true);
        Assert.True(View(controller,snapshot).Ring.Open);Assert.Equal(2,View(controller,snapshot).Ring.Group!.Count);
        Assert.Equal(PlayerActionPanelMode.Closed,controller.Mode);
        Assert.False(controller.TryTakeRequest(out _));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OutsideRightClickClosesGroupSubmenusAndMovesTheWholeSelectionExactlyOnce(bool coalesced)
    {
        var (controller,input,snapshot,selected,world)=Open(10);
        Click(controller,input,snapshot,selected,7);
        var owner=new RtsSelectionController(new(new PlayerId(1),ControllableEntityCategory.Unit));
        var camera=new RtsCamera();var terrain=new FlatTerrain();
        input.BeginFrame();owner.Update(input,camera,world,terrain,1920,1080,1);
        owner.Selection.Replace(selected.ToArray());
        var menus=new TransientHudMenuController();var operations=new OperationsController();var interaction=new PresentationInteractionState();
        var layout=GameplayHudLayout.Create(1920,1080,96);
        menus.Update(input,snapshot,layout,owner.Selection,controller,operations,interaction,true,false);
        input.BeginFrame();input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,PlatformMouseButton.Right,960,450));
        if(coalesced) input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp,PlatformMouseButton.Right,960,450));
        menus.Update(input,snapshot,layout,owner.Selection,controller,operations,interaction,true,false);
        Assert.False(menus.PointerCaptured);Assert.False(controller.HasOpenMenu);
        owner.Update(input,camera,world,terrain,1920,1080,1,menus.PointerCaptured);
        Assert.True(owner.TryTakeMovementRequest(out var request));Assert.Equal(selected.ToArray(),request.Entities);
        Assert.True(camera.TryScreenPointToWorldOnHorizontalPlane(new(960,450),0,1920,1080,out var expectedTarget));
        Assert.InRange(Vector3.Distance(expectedTarget,request.WorldTarget),0,.001f);
        Assert.False(owner.TryTakeMovementRequest(out _));Assert.False(controller.TryTakeRequest(out _));
        input.BeginFrame();input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp,PlatformMouseButton.Right,960,450));
        owner.Update(input,camera,world,terrain,1920,1080,1);
        Assert.False(owner.TryTakeMovementRequest(out _));
    }
    private sealed class FlatTerrain : ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds => new(new Vector3(-1000),new Vector3(1000));
        public bool TrySampleHeight(float x,float z,out float height){height=0;return true;}
        public bool TrySampleNormal(float x,float z,out Vector3 normal){normal=Vector3.UnitY;return true;}
    }

    [Fact]
    public void NewRenderTickCannotPresentOldGroupCapabilitiesAsCurrent()
    {
        var (controller,_,snapshot,selection,world)=Open(2);
        var retained=View(controller,snapshot).Ring;
        var next=Snapshot(2,tick:5);
        var projected=retained.Project(World(next),new RtsCamera(),1,next,GameplayHudLayout.Create(1920,1080,96),[]);
        Assert.True(projected.Geometry.IsValid);
        Assert.False(projected.GroupFactsCurrent);
        Assert.Equal(snapshot.Tick,projected.Group!.Tick);
        Assert.True(projected.Group.MatchesSelection(next.PlayerActions!.Tactical!.SelectedEntities));
        Assert.False(projected.Group.MatchesSelection([new(1,1),new(3,1)]));
        Assert.False(projected.Group.MatchesSelection([new(1,1),new(1,1)]));
        Assert.Equal(2,selection.Count);
    }

    [Fact]
    public void DisabledPresentedActionCannotArmWhenCapabilityBecomesAvailableBeforeRelease()
    {
        var (controller,input,snapshot,selection,world)=Open(2);
        Click(controller,input,snapshot,selection,0);
        var layout=GameplayHudLayout.Create(1920,1080,96);
        controller.AdoptPresentedRing(View(controller,snapshot).Ring.Project(world,new RtsCamera(),1,snapshot,layout,[]) with {Geometry=Geometry});
        input.BeginFrame();Mouse(input,PlatformInputEventKind.MouseButtonDown,0);
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        var next=Snapshot(2,tick:5,hasTarget:true);var nextWorld=World(next);
        controller.SynchronizeObjectRing(input,next,nextWorld,selection,false);
        controller.AdoptPresentedRing(View(controller,next).Ring.Project(nextWorld,new RtsCamera(),1,next,layout,[]) with {Geometry=Geometry});
        input.BeginFrame();Mouse(input,PlatformInputEventKind.MouseButtonUp,0);
        controller.Update(input,next,1920,1080,currentSelection:selection);
        Assert.False(controller.TryTakeRequest(out _));
        Click(controller,input,next,selection,0);
        Assert.True(controller.TryTakeRequest(out var intentional));
        Assert.Equal(PlayerActionRequestKind.BeginAttackTargeting,intentional.Kind);
        Assert.Equal(selection.ToArray(),intentional.TacticalEntities);
    }

    [Fact]
    public void HiddenOwnedMemberRetainsCommandMembershipWithoutContributingItsPosition()
    {
        var snapshot=Snapshot(2);var instances=snapshot.Instances.ToArray();
        instances[1]=instances[1] with{Visibility=RenderVisibilityMask.None,Transform=instances[1].Transform with{Position=new(1000000,0,1000000)}};
        var hidden=new PresentationSnapshot(snapshot.Tick,snapshot.TickDuration,2,instances,sessionId:snapshot.SessionId,
            playerExperience:snapshot.PlayerExperience,playerActions:snapshot.PlayerActions,combatGroups:snapshot.CombatGroups);
        var selection=new SelectionSet();selection.Replace([new(1,1),new(2,1)]);var world=World(hidden);
        var group=GroupActionRingContext.Capture(hidden,world,selection)!;
        Assert.Equal(2,group.Count);Assert.True(group.MatchesSelection(hidden.PlayerActions!.Tactical!.SelectedEntities));
        Assert.True(group.TryAnchor(world,new RtsCamera(),1,new(1),GameplayHudLayout.Create(1920,1080,96),out var anchor,out int visible));
        Assert.Equal(1,visible);
        Assert.Equal(new RtsCamera().WorldToScreen(instances[0].Transform.Position,1920,1080).Position,anchor);
        var owner=new RtsSelectionController(new(new PlayerId(1),ControllableEntityCategory.Unit));owner.Selection.Replace(selection.ToArray());
        Assert.False(owner.ReconcileSelection(world));Assert.Equal(2,owner.Selection.Count);
    }

    [Fact]
    public void UnsupportedBuildingGroupDoesNotInventUnitFocusEligibility()
    {
        var snapshot=Snapshot(2);var instances=snapshot.Instances.ToArray();
        for(int i=0;i<instances.Length;i++) instances[i]=instances[i] with{Selectable=new(new PlayerId(1),ControllableEntityCategory.Building)};
        var buildings=new PresentationSnapshot(snapshot.Tick,snapshot.TickDuration,2,instances,sessionId:snapshot.SessionId,
            playerExperience:snapshot.PlayerExperience,playerActions:snapshot.PlayerActions,combatGroups:new(snapshot.Tick,[],snapshot.SessionId));
        var selection=new SelectionSet();selection.Replace([new(1,1),new(2,1)]);
        var context=GroupActionRingContext.Capture(buildings,World(buildings),selection)!;
        Assert.Equal(2,context.Count);Assert.True(context.HasOperationalFacts);
        Assert.Equal(0,context.Participants(11));Assert.Equal(0,context.Participants(10));
        Assert.False(new CombatGroupCardController().Activate(11,buildings,selection).FocusSelection);
    }

    private static (PlayerActionPanelController,InputState,PresentationSnapshot,SelectionSet,RenderWorld) Open(int count)
    {
        var snapshot=Snapshot(count); var world=World(snapshot); var selection=new SelectionSet();
        selection.Replace(snapshot.PlayerActions!.Tactical!.SelectedEntities.ToArray());
        var controller=new PlayerActionPanelController();var input=new InputState();
        input.BeginFrame(); Mouse(input,PlatformInputEventKind.MouseButtonDown,0);Mouse(input,PlatformInputEventKind.MouseButtonUp,0);
        controller.SynchronizeObjectRing(input,snapshot,world,selection,true,true);
        controller.Update(input,snapshot,1920,1080,currentSelection:selection);
        return (controller,input,snapshot,selection,world);
    }
    private static PlayerActionPanelView View(PlayerActionPanelController c,PresentationSnapshot s)=>c.CreateView(1920,1080,s.PlayerActions);
    private static void Present(PlayerActionPanelController c,PresentationSnapshot s)=>c.AdoptPresentedRing(View(c,s).Ring with{Geometry=Geometry,Resolved=true,GroupFactsCurrent=true});
    private static void Mouse(InputState i,PlatformInputEventKind kind,int slot){var p=Geometry.LabelCenter(slot);i.Apply(PlatformInputEvent.MouseButtonChanged(kind,PlatformMouseButton.Left,(int)p.X,(int)p.Y));}
    private static void Click(PlayerActionPanelController c,InputState input,PresentationSnapshot s,SelectionSet selected,int slot)
    {
        Present(c,s);input.BeginFrame();Mouse(input,PlatformInputEventKind.MouseButtonDown,slot);c.Update(input,s,1920,1080,currentSelection:selected);
        input.BeginFrame();Mouse(input,PlatformInputEventKind.MouseButtonUp,slot);c.Update(input,s,1920,1080,currentSelection:selected);
    }
    private static RenderWorld World(PresentationSnapshot s){var b=new PresentationSnapshotBuffer();b.Publish(s);var w=new RenderWorld();w.Update(b);return w;}
    private static PresentationSnapshot Snapshot(int count,ulong tick=4,ulong session=1,uint generation=1,bool spread=false,bool allOffscreen=false,bool hasTarget=false)
    {
        var entities=Enumerable.Range(1,count).Select(i=>new EntityId((uint)i,generation)).ToArray();
        var instances=entities.Select((e,i)=>new RenderInstance(e,RenderTransform.Identity with { Position=allOffscreen || spread && i>0 ? new(100000,0,100000) : new(i*.1f,0,0) },
            default,default,RenderVisibilityMask.World,Selectable:new(new PlayerId(1),ControllableEntityCategory.Unit))).ToArray();
        var tactical=new PlayerTacticalActionReadModel(entities,count,count/2+count%2,0,0,0,false,false,default,default,hasTarget ? [new(new EntityId(9999,1),default,new Vector3(10,0,10),IntelligenceState.Identified,new(tick),1)] : [],[]);
        var actions=new PlayerActionSnapshot(new(session),new(tick),[],0,null,null,tactical:tactical);
        var members=entities.Select((e,i)=>new CombatGroupMemberReadModel(e,ControllableEntityCategory.Unit,true,.2,true,BattlefieldSupplyStatus.Critical,.2,.3,
            false,0,0,false,default,false,default,false,default,new UnitId((uint)(i%8+1)),i%2==0)).ToArray();
        return new(new(tick),TimeSpan.FromMilliseconds(50),count,instances,sessionId:new(session),
            playerExperience:default(PlayerExperienceSnapshot) with{Player=new(1),Tick=new(tick),Selection=PlayerSelectionSummary.Empty with{Count=count,Kind=PlayerSelectionKind.Mixed}},
            playerActions:actions,combatGroups:new(new(tick),members,new(session)));
    }
}
