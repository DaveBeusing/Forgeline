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
    private ulong _pressSequence;
    private SimulationSessionId _session;
    private GameplayHudLayout _layout;
    private bool _hasLayout;
    private readonly List<EntityId> _filtered = [];

    public CombatGroupCardResult Update(InputState input, PresentationSnapshot? snapshot,
        SelectionSet selection, in GameplayHudLayout layout, bool blocked = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(selection);
        ulong sequence = input.MousePressSequence(PlatformMouseButton.Left);
        bool fresh = sequence != _pressSequence;
        _pressSequence = sequence;
        bool transition = _hasLayout && (_layout != layout || _session != snapshot?.SessionId);
        _layout = layout;
        _hasLayout = true;
        _session = snapshot?.SessionId ?? default;
        bool captured = input.HasPointerPosition && layout.SelectionInspector.Contains(input.PointerPosition);
        bool pressedInside = input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin) &&
            layout.SelectionInspector.Contains(origin);
        captured |= pressedInside;
        if (blocked || transition || input.FocusLostThisFrame || !input.HasPointerPosition || !fresh ||
            !pressedInside || selection.Count < 2 || snapshot?.PlayerExperience?.Selection.Count != selection.Count || SelectedCombatGroup.Resolve(snapshot) is not { } operational)
            return new(captured);
        var group = SelectedCombatGroup.Create(operational, selection);
        for (int index = 0; index < 12; index++)
        {
            if (!CombatGroupCardLayout.Control(layout, index).Contains(origin)) continue;
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
                8 => new(true, FocusMember: group.DamagedMember),
                9 => new(true, FocusMember: group.UnsuppliedMember),
                10 => new(true, CycleFormation: group.CombatCount > 0),
                _ => new(true, FocusSelection: group.LiveCount > 0)
            };
        }
        return new(captured);
    }
}
