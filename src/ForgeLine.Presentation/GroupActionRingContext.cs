using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Immutable authorized selection facts. Saved groups and simulation routes are never edited.</summary>
public sealed class GroupActionRingContext
{
    private readonly EntityId[] _members;
    private readonly int[] _counts;
    private GroupActionRingContext(EntityId[] members, int selected, int[] counts, SimulationTick tick, bool hasFacts)
    { _members = members; SelectedCount = selected; _counts = counts; Tick = tick; HasOperationalFacts = hasFacts; }
    public bool HasOperationalFacts { get; }
    public int Count => _members.Length;
    public int SelectedCount { get; }
    public SimulationTick Tick { get; }
    public EntityId Representative => Count == 0 ? default : _members[0];
    public int Participants(int control) => control is >= 0 and < 12 ? _counts[control] : 0;
    public static string Label(int control) => control < 8 ? SelectedCombatGroup.TypeLabel(control) : control switch
    { 8 => "Damaged", 9 => "Supply focus", 10 => "Formation", _ => "Focus group" };

    public static GroupActionRingContext? Capture(PresentationSnapshot? snapshot, RenderWorld world, SelectionSet selection)
    {
        if (snapshot?.PlayerExperience is not { IsMatchComplete: false } experience || !snapshot.SessionId.IsSpecified || !experience.Player.IsSpecified) return null;
        var members = new List<EntityId>(selection.Count);
        var counts = new int[12];
        var operational = SelectedCombatGroup.Resolve(snapshot);
        foreach (var entity in selection.Entities)
        {
            if (!world.TryGetInterpolatedInstance(entity, 1, out var instance) || !instance.Selectable.IsSelectable ||
                instance.Selectable.Owner != experience.Player) continue;
            members.Add(entity);
            if (operational is null || !operational.TryGet(entity, out var member)) continue;
            counts[SelectedCombatGroup.TypeIndex(member.Unit)]++;
            if (member.HasHealth && member.HealthFraction <= .25) counts[8]++;
            if (member.HasSupply && member.SupplyStatus is BattlefieldSupplyStatus.Critical or BattlefieldSupplyStatus.Unsupplied) counts[9]++;
            if (member.CombatEligible) counts[10]++;
        }
        counts[11] = operational is null ? 0 : members.Count;
        return new(members.ToArray(), selection.Count, counts, snapshot.Tick, operational is not null);
    }

    public bool MatchesSelection(IReadOnlyList<EntityId>? selected)
    {
        if (selected is null || selected.Count != SelectedCount || Count != SelectedCount) return false;
        // Presentation selection snapshots use the canonical sorted, unique identity order.
        for (int i=0;i<selected.Count;i++) if (_members[i] != selected[i]) return false;
        return true;
    }

    public bool SameMembers(GroupActionRingContext? other) => other is not null && _members.AsSpan().SequenceEqual(other._members);

    /// <summary>Only selected identities are looked up. Render interpolation supplies one position per simulation entity.</summary>
    public bool TryAnchor(RenderWorld world, RtsCamera camera, float alpha, PlayerId player, in GameplayHudLayout layout,
        out Vector2 anchor, out int visible)
    {
        Vector3 total = default; Vector2 shown = default; int authorized = 0; visible = 0;
        foreach (var entity in _members)
        {
            if (!world.TryGetInterpolatedInstance(entity, alpha, out var instance) || !instance.Selectable.IsSelectable ||
                instance.Selectable.Owner != player) continue;
            authorized++;
            if ((instance.Visibility & RenderVisibilityMask.World) == 0) continue;
            total += instance.Transform.Position;
            var projected = camera.WorldToScreen(instance.Transform.Position, layout.ViewportWidth, layout.ViewportHeight);
            if (projected.IsVisible) { shown += projected.Position; visible++; }
        }
        anchor = default;
        if (visible == 0 || authorized != Count) return false;
        var center = camera.WorldToScreen(total / authorized, layout.ViewportWidth, layout.ViewportHeight);
        anchor = visible == Count && center.IsVisible ? center.Position : shown / visible;
        return true;
    }
}
