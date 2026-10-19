using System.Numerics;
using System.Runtime.CompilerServices;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

internal enum ActionRingLevel : byte { Categories, Items, Parameters, Group }
internal enum ActionRingNavigation : byte { None, Back, Previous, Next, Parameters, NextItem, Close, Group, GroupControl }
internal readonly record struct ActionRingKey(byte Kind, uint Id, uint Generation = 0);
internal readonly record struct ActionRingSlot(PlayerActionDockHitTarget Target, ActionRingKey Key,
    ActionRingNavigation Navigation = ActionRingNavigation.None, bool Present = true, bool Enabled = true);
[InlineArray(8)] internal struct ActionRingSlots { private ActionRingSlot _first; }

/// <summary>Copied menu state; no mutable collections cross the input/render boundary.</summary>
public readonly record struct ObjectActionRingView(EntityId Entity, SimulationSessionId Session, ulong SelectionRevision,
    ulong Revision, bool SingleOwned, bool Open, RadialActionLayout Geometry = default, bool Resolved = false,
    float Opacity = 1, int HoveredSlot = -1, bool Pressed = false)
{
    public GroupActionRingContext? Group { get; init; }
    public bool GroupFactsCurrent { get; init; }
    public int VisibleMembers { get; init; }
    public bool Global { get; init; }
    public Vector2 GlobalAnchor { get; init; }
    public bool UsesRing => SingleOwned || Group is not null || Global;
    internal ActionRingSlots Slots { get; init; }
    internal ActionRingLevel Level { get; init; }
    internal bool TryHit(Vector2 point, out ActionRingSlot slot)
    {
        int index = Open && Resolved ? Geometry.Hit(point) : -1;
        slot = index < 0 ? default : Slots[index];
        return index >= 0 && slot.Present;
    }

    public ObjectActionRingView Project(RenderWorld world, RtsCamera camera, float alpha,
        PresentationSnapshot snapshot, in GameplayHudLayout layout, ReadOnlySpan<HudRect> occupied)
    {
        if (!Open) return this with { Resolved = true };
        if (snapshot.SessionId != Session || snapshot.PlayerExperience is not { IsMatchComplete: false } experience)
            return this with { Geometry = default, Resolved = true };
        Vector2 anchor; float radius = 0; int visible = 0;
        if (Global)
        {
            if (!Session.IsSpecified || !experience.Player.IsSpecified || experience.Tick != snapshot.Tick || experience.Selection.Count != 0)
                return this with { Geometry = default, Resolved = true };
            if (!layout.SafeArea.Contains(GlobalAnchor))
                return this with { Geometry = default, Resolved = true };
            anchor = GlobalAnchor;
        }
        else if (Group is not null)
        {
            if (!Group.TryAnchor(world, camera, alpha, experience.Player, layout, out anchor, out visible))
                return this with { Geometry = default, Resolved = true };
        }
        else
        {
            if (!world.TryGetInterpolatedInstance(Entity, alpha, out var instance) ||
                (instance.Visibility & RenderVisibilityMask.World) == 0 || !instance.Selectable.IsSelectable || instance.Selectable.Owner != experience.Player)
                return this with { Geometry = default, Resolved = true };
            var projected = camera.WorldToScreen(instance.Transform.Position, layout.ViewportWidth, layout.ViewportHeight);
            if (!projected.IsVisible) return this with { Geometry = default, Resolved = true };
            anchor = projected.Position;
            for (int i = 0; i < 4; i++)
            {
                var corner = camera.WorldToScreen(TacticalFootprint.Corner(instance, i), layout.ViewportWidth, layout.ViewportHeight);
                if (corner.IsVisible) radius = MathF.Max(radius, Vector2.Distance(anchor, corner.Position));
            }
        }
        ActionRingSlots slots = Slots;
        var actions = PlayerActionDockHudModel.ResolveActions(snapshot);
        bool selectionCurrent = Group is null || Group.MatchesSelection(actions?.Tactical?.SelectedEntities);
        bool groupFactsCurrent = selectionCurrent && Group is { HasOperationalFacts: true } && Group.Tick == snapshot.Tick;
        for (int i = 0; i < 8; i++)
        {
            var saved = slots[i];
            if (saved.Navigation == ActionRingNavigation.GroupControl)
            {
                slots[i] = saved with { Enabled = groupFactsCurrent && Group!.Participants(saved.Target.ItemIndex) > 0 };
                continue;
            }
            if (!selectionCurrent) { slots[i] = saved with { Enabled = saved.Navigation != ActionRingNavigation.None }; continue; }
            if (!saved.Present || saved.Key.Kind == 255 ||
                saved.Navigation != ActionRingNavigation.None || saved.Target.Kind == PlayerActionDockControlKind.Mode) continue;
            int resolved = -1;
            int count = PlayerActionDockInteractionLayout.GetItemCount(saved.Target.Mode, actions);
            for (int item = 0; item < count; item++)
                if (ItemKey(saved.Target.Mode,item,actions) == saved.Key) { resolved = item; break; }
            slots[i] = resolved < 0 ? saved with { Present = false } : saved with { Target = saved.Target with { ItemIndex = resolved },
                Enabled = saved.Target.Kind != PlayerActionDockControlKind.Item || PlayerActionDockHudModel.ResolveItemState(saved.Target.Mode,resolved,actions,false).CanActivate };
        }
        var geometry = Global
            ? RadialActionLayout.Fit(anchor, radius, layout, occupied)
            : RadialActionLayout.FitWithRightInspector(anchor, radius, layout, occupied,
                330 * Math.Clamp(layout.Scale, 1, 1.25f));
        return this with { Geometry = geometry, Resolved = true, Slots = slots, VisibleMembers = visible, GroupFactsCurrent = groupFactsCurrent };
    }

    internal static ActionRingKey ItemKey(PlayerActionPanelMode mode, int index, PlayerActionSnapshot? actions)
    {
        if (index < 0 || index >= PlayerActionDockInteractionLayout.GetItemCount(mode, actions) || actions is null) return default;
        uint id; byte kind = 1;
        switch (mode)
        {
            case PlayerActionPanelMode.Construction: id = actions.Construction[index].BuildingId.Value; break;
            case PlayerActionPanelMode.Production:
                var production = actions.Production!;
                if (index < production.Recipes.Count) { id = production.Recipes[index].RecipeId.Value; break; }
                var request = production.Requests[index - production.Recipes.Count].RequestEntity;
                return new(2, request.Index, request.Generation);
            case PlayerActionPanelMode.UnitProduction:
                var units = actions.UnitProduction!;
                if (index < units.Units.Count) { id = units.Units[index].UnitId.Value; break; }
                var unitRequest = units.Requests[index - units.Units.Count].RequestEntity;
                return new(2, unitRequest.Index, unitRequest.Generation);
            case PlayerActionPanelMode.Logistics:
                var logistics = actions.Logistics!;
                if (index < logistics.Policies.Count) { id = logistics.Policies[index].ResourceId.Value; break; }
                var requirement = logistics.Requirements[index - logistics.Policies.Count];
                return new((byte)(4 + requirement.Kind), requirement.Id);
            case PlayerActionPanelMode.Technology: id = actions.Technology[index].TechnologyId.Value; break;
            default: kind = 3; id = (uint)index; break;
        }
        return new(kind, id);
    }
}
