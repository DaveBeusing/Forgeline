using System.Numerics;
using System.Runtime.CompilerServices;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

internal enum ActionRingLevel : byte { Categories, Items, Parameters }
internal enum ActionRingNavigation : byte { None, Back, Previous, Next, Parameters, NextItem, Close }
internal readonly record struct ActionRingKey(byte Kind, uint Id, uint Generation = 0);
internal readonly record struct ActionRingSlot(PlayerActionDockHitTarget Target, ActionRingKey Key,
    ActionRingNavigation Navigation = ActionRingNavigation.None, bool Present = true);
[InlineArray(8)] internal struct ActionRingSlots { private ActionRingSlot _first; }

/// <summary>Copied menu state; no mutable collections cross the input/render boundary.</summary>
public readonly record struct ObjectActionRingView(EntityId Entity, SimulationSessionId Session, ulong SelectionRevision,
    ulong Revision, bool SingleOwned, bool Open, RadialActionLayout Geometry = default, bool Resolved = false,
    float Opacity = 1, int HoveredSlot = -1, bool Pressed = false)
{
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
        if (!Open) return this with { Geometry = default, Resolved = true };
        if (snapshot.SessionId != Session || snapshot.PlayerExperience is not { IsMatchComplete: false } experience ||
            !world.TryGetInterpolatedInstance(Entity, alpha, out var instance) ||
            !instance.Selectable.IsSelectable || instance.Selectable.Owner != experience.Player)
            return this with { Geometry = default, Resolved = true };
        var projected = camera.WorldToScreen(instance.Transform.Position, layout.ViewportWidth, layout.ViewportHeight);
        if (!projected.IsVisible) return this with { Geometry = default, Resolved = true };
        float radius = 0;
        for (int i = 0; i < 4; i++)
        {
            var corner = camera.WorldToScreen(TacticalFootprint.Corner(instance, i), layout.ViewportWidth, layout.ViewportHeight);
            if (corner.IsVisible) radius = MathF.Max(radius, Vector2.Distance(projected.Position, corner.Position));
        }
        return this with { Geometry = RadialActionLayout.Fit(projected.Position, radius, layout, occupied), Resolved = true };
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
            case PlayerActionPanelMode.Logistics: id = actions.Logistics!.Policies[index].ResourceId.Value; break;
            case PlayerActionPanelMode.Technology: id = actions.Technology[index].TechnologyId.Value; break;
            default: kind = 3; id = (uint)index; break;
        }
        return new(kind, id);
    }
}
