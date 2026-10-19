using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct CombatGroupCardResult(bool Captured, bool SelectionChanged = false,
    EntityId FocusMember = default, bool FocusSelection = false, bool CycleFormation = false);

/// <summary>Presentation selection/focus only. Never edits saved slots or issues simulation orders.</summary>
public sealed class CombatGroupCardController
{
    private SimulationSessionId _session;
    private readonly List<EntityId> _filtered = [];
    private EntityId _lastDamaged;
    private EntityId _lastUnsupplied;

    /// <summary>Shared selection/focus adapter for the ring. No gameplay command is submitted here.</summary>
    public CombatGroupCardResult Activate(int index, PresentationSnapshot? snapshot, SelectionSet selection)
    {
        if (index is < 0 or > 11 || selection.Count < 2 ||
            SelectedCombatGroup.Resolve(snapshot) is not { } operational ||
            snapshot?.PlayerExperience?.Selection.Count != selection.Count) return default;
        if (_session != snapshot.SessionId) { _lastDamaged = default; _lastUnsupplied = default; _session = snapshot.SessionId; }
        var group = SelectedCombatGroup.Create(operational, selection);
        if (index < 8)
        {
            _filtered.Clear();
            foreach (EntityId entity in selection.Entities)
                if (operational.TryGet(entity, out var member) && SelectedCombatGroup.TypeIndex(member.Unit) == index)
                _filtered.Add(entity);
            if (_filtered.Count == 0) return new(true);
            selection.Replace(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_filtered));
            return new(true, SelectionChanged: true);
        }
        return index switch
        {
            8 => new(true, FocusMember: NextAttention(operational, selection, damaged: true)),
            9 => new(true, FocusMember: NextAttention(operational, selection, damaged: false)),
            10 => new(true, CycleFormation: group.CombatCount > 0),
            _ => new(true, FocusSelection: group.LiveCount > 0)
        };
    }

    private EntityId NextAttention(CombatGroupOperationalSnapshot operational, SelectionSet selection, bool damaged)
    {
        EntityId previous = damaged ? _lastDamaged : _lastUnsupplied;
        EntityId first = default, next = default;
        foreach (EntityId entity in selection.Entities)
        {
            if (!operational.TryGet(entity, out var member) || !(damaged
                ? member.HasHealth && member.HealthFraction <= 0.25
                : member.HasSupply && member.SupplyStatus is BattlefieldSupplyStatus.Critical or BattlefieldSupplyStatus.Unsupplied)) continue;
            if (!first.IsValid) first = entity;
            if (!next.IsValid && entity.CompareTo(previous) > 0) next = entity;
        }
        if (!next.IsValid) next = first;
        if (damaged) _lastDamaged = next; else _lastUnsupplied = next;
        return next;
    }

}
