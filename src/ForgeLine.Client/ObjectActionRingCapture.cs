using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

/// <summary>Reference states use the production menu/controller and copied permitted render instances.</summary>
internal static class ObjectActionRingCapture
{
    internal static bool IsRing(UiReferenceScenario state) => state is >= UiReferenceScenario.RingConstruction and <= UiReferenceScenario.GroupRingStatus;
    internal static PlayerActionPanelMode Mode(UiReferenceScenario state) => state switch
    {
        UiReferenceScenario.RingConstruction or UiReferenceScenario.RingOpening50 or UiReferenceScenario.RingClosing50 or
            UiReferenceScenario.RingTooltip or UiReferenceScenario.RingEdge or UiReferenceScenario.RingOffscreen => PlayerActionPanelMode.Construction,
        UiReferenceScenario.RingUnits => PlayerActionPanelMode.UnitProduction,
        UiReferenceScenario.RingLogistics => PlayerActionPanelMode.Logistics,
        UiReferenceScenario.RingSupply => PlayerActionPanelMode.Supply,
        UiReferenceScenario.RingTactical => PlayerActionPanelMode.Tactical,
        UiReferenceScenario.RingTechnology => PlayerActionPanelMode.Technology,
        _ => PlayerActionPanelMode.Production
    };
    internal static (PresentationSnapshot Snapshot, PlayerActionPanelView Panel) Fixture(PresentationSnapshot source,
        UiReferenceScenario state, int width, int height, uint dpi, float scale)
    {
        if (state is >= UiReferenceScenario.GroupRing) return (GroupActionRingCapture.Fixture(source,state),default);
        var player=source.PlayerExperience!.Value.Player;
        bool unit = Mode(state) is PlayerActionPanelMode.Tactical or PlayerActionPanelMode.Supply;
        var category = unit ? ControllableEntityCategory.Unit : ControllableEntityCategory.Building;
        var entity=source.Instances.ToArray().Where(i=>i.Selectable.IsSelectable && i.Selectable.Owner==player && (i.Selectable.Category & category) != 0)
            .OrderBy(i=>Vector3.DistanceSquared(i.Transform.Position, source.Instances[0].Transform.Position)).First().Entity;
        var fixture=CommandDockCapture.Fixture(source,Mode(state),state==UiReferenceScenario.RingPending ? "Pending":"Ready",width,height,dpi,scale,entity);
        var a=fixture.Snapshot.PlayerActions!;
        var tactical=new PlayerTacticalActionReadModel([entity],1,a.Tactical?.CombatEligibleCount ?? 0,0,0,0,false,false,default,default,
            a.Tactical?.Targets ?? [],a.Tactical?.Artillery ?? []);
        var actions=new PlayerActionSnapshot(a.SessionId,a.Tick,a.Construction,a.PendingCommandCount,a.Production,a.UnitProduction,a.Logistics,a.Supply,tactical,a.Technology);
        var e=fixture.Snapshot.PlayerExperience!.Value;
        return (new(source.Tick,source.TickDuration,source.SimulationEntityCount,source.Instances,source.Intelligence,source.SessionId,
            e with { Selection=e.Selection with { Count=1,PrimaryEntity=entity,Kind=unit ? PlayerSelectionKind.Unit : PlayerSelectionKind.Building,DisplayName=unit ? "Selected unit" : "Selected building" } },playerActions:actions,vfxMetrics:source.VfxMetrics),default);
    }

    internal static PlayerActionPanelView View(PresentationSnapshot snapshot, UiReferenceScenario state, RenderWorld world,
        RtsCamera camera, in GameplayHudLayout layout, uint dpi = 96, float scale = 1)
    {
        if (state is >= UiReferenceScenario.GroupRing) return GroupActionRingCapture.View(snapshot,state,world,camera,layout,dpi,scale);
        var entity=snapshot.PlayerExperience!.Value.Selection.PrimaryEntity;
        if (!world.TryGetInterpolatedInstance(entity,1,out var actor)) throw new InvalidDataException("Ring fixture anchor absent.");
        camera.CenterOn(actor.Transform.Position);
        if (state==UiReferenceScenario.RingEdge) camera.CenterOn(actor.Transform.Position + camera.GroundRight * 95);
        if (state==UiReferenceScenario.RingOffscreen) camera.CenterOn(actor.Transform.Position + camera.GroundRight * 1000);
        var input=new InputState(); var selection=new SelectionSet(); selection.SetSingle(entity);
        var controller=new PlayerActionPanelController();
        controller.Update(input,snapshot,layout.ViewportWidth,layout.ViewportHeight,currentSelection:selection);
        controller.SynchronizeObjectRing(input,snapshot,world,selection,false);
        var action=Mode(state) switch { PlayerActionPanelMode.Construction=>GameplayAction.Build,PlayerActionPanelMode.Production=>GameplayAction.Process,
            PlayerActionPanelMode.UnitProduction=>GameplayAction.Units,PlayerActionPanelMode.Logistics=>GameplayAction.Logistics,
            PlayerActionPanelMode.Supply=>GameplayAction.Supply,PlayerActionPanelMode.Tactical=>GameplayAction.Combat,_=>GameplayAction.Technology };
        input.BeginFrame(); input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,GameplayBindingRegistry.Default.Key(action)));
        controller.Update(input,snapshot,layout.ViewportWidth,layout.ViewportHeight,currentSelection:selection);
        var panel=controller.CreateView(layout.ViewportWidth,layout.ViewportHeight,snapshot.PlayerActions);
        var occupied=new HudRect[8];
        RadialActionLayout.ResolveObstacles(occupied,layout);
        var ring=panel.Ring.Project(world,camera,1,snapshot,layout,occupied);
        if (state==UiReferenceScenario.RingParameters && ring.Geometry.IsValid)
        {
            controller.AdoptPresentedRing(ring);
            var point=ring.Geometry.LabelCenter(6);
            input.BeginFrame();input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp,GameplayBindingRegistry.Default.Key(action)));
            input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,PlatformMouseButton.Left,(int)point.X,(int)point.Y));
            controller.Update(input,snapshot,layout.ViewportWidth,layout.ViewportHeight,currentSelection:selection);
            input.BeginFrame();input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp,PlatformMouseButton.Left,(int)point.X,(int)point.Y));
            controller.Update(input,snapshot,layout.ViewportWidth,layout.ViewportHeight,currentSelection:selection);
            panel=controller.CreateView(layout.ViewportWidth,layout.ViewportHeight,snapshot.PlayerActions);
            ring=panel.Ring.Project(world,camera,1,snapshot,layout,occupied);
        }
        return panel with { Ring=ring with { Opacity=state is UiReferenceScenario.RingOpening50 or UiReferenceScenario.RingClosing50 ? .5f:1,
            Open=state!=UiReferenceScenario.RingClosing50 } };
    }
    internal static PlayerActionPanelView Present(PresentationSnapshot snapshot, in PlayerActionPanelView authored,
        RenderWorld world, RtsCamera camera, in GameplayHudLayout layout, uint dpi, float scale)
    {
        if (!authored.IsOpen || snapshot.PlayerExperience is not { Selection.Count: 1 } experience ||
            !world.TryGetInterpolatedInstance(experience.Selection.PrimaryEntity,1,out var actor) ||
            !actor.Selectable.IsSelectable || actor.Selectable.Owner != experience.Player) return authored;
        camera.CenterOn(actor.Transform.Position);
        var selection=new SelectionSet();selection.SetSingle(experience.Selection.PrimaryEntity);
        var input=new InputState();var controller=new PlayerActionPanelController(authored.Bindings);
        controller.Update(input,snapshot,layout.ViewportWidth,layout.ViewportHeight,dpi,scale,selection);
        controller.SynchronizeObjectRing(input,snapshot,world,selection,false);
        var action=authored.Mode switch {
            PlayerActionPanelMode.Construction=>GameplayAction.Build,PlayerActionPanelMode.Production=>GameplayAction.Process,
            PlayerActionPanelMode.UnitProduction=>GameplayAction.Units,PlayerActionPanelMode.Logistics=>GameplayAction.Logistics,
            PlayerActionPanelMode.Supply=>GameplayAction.Supply,PlayerActionPanelMode.Tactical=>GameplayAction.Combat,_=>GameplayAction.Technology };
        input.BeginFrame();input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,
            (authored.Bindings??GameplayBindingRegistry.Default).Key(action)));
        controller.Update(input,snapshot,layout.ViewportWidth,layout.ViewportHeight,dpi,scale,selection);
        for(int i=0;i<authored.SelectedIndex;i++) {
            input.BeginFrame();input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,
                (authored.Bindings??GameplayBindingRegistry.Default).Key(GameplayAction.NextItem)));
            controller.Update(input,snapshot,layout.ViewportWidth,layout.ViewportHeight,dpi,scale,selection);
            input.BeginFrame();input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp,
                (authored.Bindings??GameplayBindingRegistry.Default).Key(GameplayAction.NextItem)));
            controller.Update(input,snapshot,layout.ViewportWidth,layout.ViewportHeight,dpi,scale,selection);
        }
        var panel=controller.CreateView(layout.ViewportWidth,layout.ViewportHeight,snapshot.PlayerActions,dpi,scale);
        Span<HudRect> occupied=stackalloc HudRect[8];RadialActionLayout.ResolveObstacles(occupied,layout);
        return authored with {Ring=panel.Ring.Project(world,camera,1,snapshot,layout,occupied) with {
            HoveredSlot=authored.HoveredIndex,Pressed=authored.PointerPressed}};
    }

}
