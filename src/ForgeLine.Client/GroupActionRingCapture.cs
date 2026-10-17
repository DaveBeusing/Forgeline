using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

/// <summary>Bounded group references use the production controller and copied owned identities.</summary>
internal static class GroupActionRingCapture
{
    internal static PresentationSnapshot Fixture(PresentationSnapshot source, UiReferenceScenario state)
    {
        source = UiReferenceStates.Hud(source,UiReferenceScenario.NotificationsCollapsed,1920,1080,96,1).Snapshot;
        var experience = source.PlayerExperience!.Value;
        var actors = source.Instances.ToArray().Where(i => i.Selectable.IsSelectable && i.Selectable.Owner == experience.Player && (i.Selectable.Category & ControllableEntityCategory.Unit) != 0)
            .DistinctBy(i => i.Entity).Take(3).ToArray();
        if (actors.Length < 2) throw new InvalidDataException("Group reference requires at least two permitted actors.");
        var selected = actors.Select(i => i.Entity).ToArray();
        var tactical = new PlayerTacticalActionReadModel(selected,selected.Length,1,0,1,0,false,true,default,default,[],[]);
        var actions = new PlayerActionSnapshot(source.SessionId,source.Tick,[],0,null,null,tactical:tactical);
        var members = actors.Select((i,n) => new CombatGroupMemberReadModel(i.Entity,i.Selectable.Category,true,.2,true,
            BattlefieldSupplyStatus.Critical,.2,.3,false,0,0,false,default,false,default,false,default,
            i.UnitFeature.Unit,n==0)).ToArray();
        var instances = source.Instances.ToArray();
        return new(source.Tick,source.TickDuration,source.SimulationEntityCount,instances,source.Intelligence,source.SessionId,
            experience with { Selection = experience.Selection with { Count=selected.Length,PrimaryEntity=selected[0],Kind=PlayerSelectionKind.Mixed,DisplayName="Mixed selection" } },
            playerActions:actions,combatGroups:new(source.Tick,members,source.SessionId),vfxMetrics:source.VfxMetrics,alerts:source.Alerts,operations:source.Operations,
            guidance:new PlayerGuidanceSummary(source.SessionId,source.Tick,experience.Player,default));
    }

    internal static PlayerActionPanelView View(PresentationSnapshot snapshot, UiReferenceScenario state, RenderWorld world,
        RtsCamera camera, in GameplayHudLayout layout, uint dpi, float scale)
    {
        var entities = snapshot.PlayerActions!.Tactical!.SelectedEntities;
        world.TryGetInterpolatedInstance(entities[0],1,out var actor);
        camera.CenterOn(actor.Transform.Position);
        if (state==UiReferenceScenario.GroupRingEdge && camera.TryScreenPointToWorldOnHorizontalPlane(
            new(layout.SafeArea.X+24,layout.ViewportHeight*.5f),actor.Transform.Position.Y,layout.ViewportWidth,layout.ViewportHeight,out var edge))
            camera.CenterOn(camera.Target+actor.Transform.Position-edge);
        if (state==UiReferenceScenario.GroupRingPartial) camera.ApplyState(camera.CaptureState() with { Distance=camera.Settings.MinimumDistance });
        var input=new InputState();var selection=new SelectionSet();selection.Replace(entities.ToArray());
        var controller=new PlayerActionPanelController();
        controller.Update(input,snapshot,layout.ViewportWidth,layout.ViewportHeight,dpi,scale,currentSelection:selection);
        controller.SynchronizeObjectRing(input,snapshot,world,selection,false);
        input.BeginFrame();input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,PlatformMouseButton.Left,500,400));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp,PlatformMouseButton.Left,500,400));
        controller.SynchronizeObjectRing(input,snapshot,world,selection,true,true);
        HudRect[] occupied=[layout.TopStatusBar,layout.SelectionInspector,layout.Minimap,layout.VisibleActionDock(false),layout.NotificationBounds(false),layout.OnboardingBounds,OperationsLayout.Entry(layout)];
        var panel=controller.CreateView(layout.ViewportWidth,layout.ViewportHeight,snapshot.PlayerActions,dpi,scale);
        var ring=panel.Ring.Project(world,camera,1,snapshot,layout,occupied);
        if (state is UiReferenceScenario.GroupRingTools or UiReferenceScenario.GroupRing && ring.Geometry.IsValid)
        {
            controller.AdoptPresentedRing(ring);var point=ring.Geometry.LabelCenter(state==UiReferenceScenario.GroupRingTools ? 7 : 0);
            input.BeginFrame();input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,PlatformMouseButton.Left,(int)point.X,(int)point.Y));
            controller.Update(input,snapshot,layout.ViewportWidth,layout.ViewportHeight,dpi,scale,currentSelection:selection);
            input.BeginFrame();input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp,PlatformMouseButton.Left,(int)point.X,(int)point.Y));
            controller.Update(input,snapshot,layout.ViewportWidth,layout.ViewportHeight,dpi,scale,currentSelection:selection);
            panel=controller.CreateView(layout.ViewportWidth,layout.ViewportHeight,snapshot.PlayerActions,dpi,scale);
            ring=panel.Ring.Project(world,camera,1,snapshot,layout,occupied);
        }
        return panel with {Ring=ring};
    }
}
