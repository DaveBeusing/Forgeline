using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public sealed partial class PlayerActionPanelController
{
    public bool SingleObjectActions => _ringContext && !_ringGlobal && _ringGroup is null;
    public bool ObjectActions => _ringContext;
    private GroupActionRingContext? _ringGroup;
    private SimulationTick? _groupTick;
    private bool _reopenSelection;
    private CombatGroupCardResult _groupResult;
    private readonly CombatGroupCardController _ringCard = new();
    public CombatGroupCardResult TakeGroupResult() { var result = _groupResult; _groupResult = default; return result; }
    private bool _ringContext, _ringOpen, _ringGlobal;
    private Vector2 _globalAnchor;
    private EntityId _ringEntity;
    private SimulationSessionId _ringSession;
    private ulong _ringSelection, _ringRevision;
    private ActionRingLevel _ringLevel;
    private int _ringPage;
    private ObjectActionRingView _presentedRing;
    private ObjectActionRingView _pressedRing;
    private ActionRingSlot _pressedRingSlot;
    private bool _ringArmed;
    private ulong _selectionClickSequence;
    private bool _selectionOpenArmed;
    private ActionRingSlots _itemSlots;
    private PlayerActionPanelMode _itemSlotsMode;
    private int _itemSlotsPage = -1;
    private ActionRingKey _parameterItem;


    /// <summary>Called after selection is decided. The opening gesture never dispatches a ring action.</summary>
    public void SynchronizeObjectRing(InputState input, PresentationSnapshot? snapshot, RenderWorld world,
        SelectionSet selection, bool allowSelectionOpen, bool selectionResolved = false)
    {
        var session = snapshot?.SessionId ?? default;
        bool selectionChanged = selection.Revision != _ringSelection;
        bool sessionChanged = session != _ringSession;
        GroupActionRingContext? context = _ringGroup;
        if (selection.Count > 1 && (_groupTick != snapshot?.Tick || selectionChanged || sessionChanged))
        { context = GroupActionRingContext.Capture(snapshot, world, selection); _groupTick = snapshot?.Tick; }
        if (selection.Count <= 1) context = null;
        bool wasGroup = _ringGroup is not null;
        EntityId entity = default;
        if (selection.Count == 1) foreach (var selected in selection.Entities) entity = selected;
        else if (context is not null) entity = context.Representative;
        bool global = session.IsSpecified && selection.Count == 0 && snapshot?.PlayerExperience is { IsMatchComplete: false } current &&
            current.Player.IsSpecified && current.Tick == snapshot.Tick && current.Selection.Count == 0;
        bool eligible = global || context is { Count: > 1 } || selection.Count == 1 &&
            snapshot?.PlayerExperience is { IsMatchComplete: false } experience &&
            world.TryGetInterpolatedInstance(entity, 1, out var instance) && (instance.Visibility & RenderVisibilityMask.World) != 0 &&
            instance.Selectable.IsSelectable && instance.Selectable.Owner == experience.Player;
        bool membershipChanged = wasGroup && !_ringGroup!.SameMembers(context);
        if (input.FocusLostThisFrame || sessionChanged) { _selectionOpenArmed = false; _reopenSelection = false; }
        bool changed = entity != _ringEntity || sessionChanged || selectionChanged || membershipChanged;
        if (changed || !eligible)
        {
            Close(); _presentedRing = default; _groupResult = default;
            if (!sessionChanged && eligible && (context is { Count: > 1 } || wasGroup)) _reopenSelection = true;
        }
        _ringContext = eligible;
        _ringGlobal = global;
        _ringGroup = eligible && context is { Count: > 1 } ? context : null;
        _ringEntity = eligible ? entity : default;
        _ringSession = session;
        _ringSelection = selection.Revision;
        ulong sequence = input.MousePressSequence(PlatformMouseButton.Left);
        if (!selectionResolved) return;
        if (sequence != _selectionClickSequence && input.TryGetMousePressPosition(PlatformMouseButton.Left, out _))
            _selectionOpenArmed = allowSelectionOpen;
        bool released = input.WasMouseButtonReleased(PlatformMouseButton.Left);
        if (eligible && !global && allowSelectionOpen && (_reopenSelection || _selectionOpenArmed && released && sequence != _selectionClickSequence))
        {
            _reopenSelection = false; _ringOpen = true; _ringLevel = ActionRingLevel.Categories; Mode = PlayerActionPanelMode.Closed;
            _itemSlotsPage = -1;
            _ringRevision++; _ringArmed = false;
        }
        if (!eligible) _reopenSelection = false;
        if (released || !input.IsMouseButtonDown(PlatformMouseButton.Left))
        { _selectionClickSequence = sequence; _selectionOpenArmed = false; }
    }

    /// <summary>A recalled saved selection is an explicit menu invocation even when its identities are unchanged.</summary>
    public void ReopenSelectionRing()
    {
        Close(); _pendingRequest = null; _groupResult = default; _selectionOpenArmed = false; _reopenSelection = true;
    }

    public void AdoptPresentedRing(in ObjectActionRingView ring)
    {
        if (!_ringOpen || ring.Revision != _ringRevision || ring.Entity != _ringEntity || ring.Session != _ringSession ||
            ring.SelectionRevision != _ringSelection) return;
        if (ring.Resolved && !ring.Geometry.IsValid) { Close(); return; }
        _presentedRing = ring;
    }

    private void CloseRing()
    {
        _ringOpen = false; _ringArmed = false; _itemSlotsPage = -1; _parameterItem = default; _ringRevision++; _presentedRing = default;
    }

    private ObjectActionRingView BuildRing(PlayerActionSnapshot? actions)
    {
        var ring = new ObjectActionRingView(_ringEntity, _ringSession, _ringSelection, _ringRevision, _ringContext && !_ringGlobal && _ringGroup is null,
            _ringOpen, _presentedRing.Geometry, _presentedRing.Resolved, HoveredSlot: _presentedRing.Resolved ? _presentedRing.HoveredSlot : -1, Pressed: _pointerPressed)
            { Global = _ringGlobal, GlobalAnchor = _globalAnchor, Level = _ringLevel, Group = _ringGroup, VisibleMembers = _presentedRing.VisibleMembers, GroupFactsCurrent = _presentedRing.GroupFactsCurrent };
        if (!_ringContext || !_ringOpen) return ring;
        ActionRingSlots slots = default;
        if (_ringLevel == ActionRingLevel.Categories)
        {
            for (int i = 0; i < 7; i++)
            {
                var mode = _ringGroup is not null && i < 6 ?
                    i == 0 ? PlayerActionPanelMode.Tactical : PlayerActionDockInteractionLayout.ModeForButton(i - 1) :
                    PlayerActionDockInteractionLayout.ModeForButton(i);
                bool present = PlayerActionDockInteractionLayout.GetItemCount(mode, actions) > 0 &&
                    (mode != PlayerActionPanelMode.Tactical || actions?.Tactical is { } tactical &&
                        (tactical.CombatEligibleCount > 0 || tactical.Artillery.Count > 0));
                slots[i] = new(new(PlayerActionDockControlKind.Mode, mode), default, Present: present);
            }
            slots[7] = new(default, default, _ringGroup is null ? ActionRingNavigation.Close : ActionRingNavigation.Group);
        }
        else if (_ringLevel == ActionRingLevel.Group)
        {
            for (int i = 0; i < 4; i++)
            {
                int control = _ringPage * 4 + i;
                slots[i] = new(new(PlayerActionDockControlKind.Page, ItemIndex: control), new(4, (uint)control), ActionRingNavigation.GroupControl);
            }
            slots[4] = new(default, default, ActionRingNavigation.Previous, _ringPage > 0);
            slots[5] = new(default, default, ActionRingNavigation.Next, _ringPage < 2);
            slots[7] = new(default, default, ActionRingNavigation.Back);
        }
        else if (_ringLevel == ActionRingLevel.Items)
        {
            int count = PlayerActionDockInteractionLayout.GetItemCount(Mode, actions);
            int first = _ringPage * 4;
            if (_itemSlotsMode != Mode || _itemSlotsPage != _ringPage)
            {
                _itemSlots = default; _itemSlotsMode = Mode; _itemSlotsPage = _ringPage;
                for (int i = 0; i < 4 && first + i < count; i++)
                    _itemSlots[i] = new(new(PlayerActionDockControlKind.Item, Mode, first + i),
                        ObjectActionRingView.ItemKey(Mode, first + i, actions));
            }
            for (int i = 0; i < 4; i++)
                if (_itemSlots[i].Present && FindRingItem(_itemSlots[i].Key, actions) < 0) _itemSlots[i] = default;
            for (int candidate = first; candidate < Math.Min(count, first + 4); candidate++)
            {
                var key = ObjectActionRingView.ItemKey(Mode, candidate, actions);
                bool exists = false;
                for (int j = 0; j < 4; j++) exists |= _itemSlots[j].Present && _itemSlots[j].Key == key;
                if (!exists) for (int j = 0; j < 4; j++) if (!_itemSlots[j].Present)
                { _itemSlots[j] = new(new(PlayerActionDockControlKind.Item, Mode, candidate), key); break; }
            }
            for (int i = 0; i < 4; i++)
            {
                var saved = _itemSlots[i];
                int index = FindRingItem(saved.Key, actions);
                // Removed requests leave an empty sector; surviving IDs do not jump into another position.
                if (saved.Present && index >= 0) slots[i] = saved with { Target = saved.Target with { ItemIndex = index } };
            }
            slots[4] = new(default, default, ActionRingNavigation.Previous, first > 0);
            slots[5] = new(default, default, ActionRingNavigation.Next, first + 4 < count);
            slots[6] = new(default, default, ActionRingNavigation.Parameters, count > 0);
            slots[7] = new(default, default, ActionRingNavigation.Back);
        }
        else
        {
            for (int i = 0; i < 6; i++)
            {
                var kind = PlayerActionDockInteractionLayout.FooterControl(i);
                slots[i] = new(new(kind, Mode, SelectedIndex), ObjectActionRingView.ItemKey(Mode, SelectedIndex, actions),
                    Present: PlayerActionDockHudModel.ResolveFooterLabel(kind, Mode).Length > 0);
            }
            slots[6] = new(default, default, ActionRingNavigation.NextItem);
            slots[7] = new(default, default, ActionRingNavigation.Back);
        }
        return ring with { Slots = slots };
    }

    private int FindRingItem(ActionRingKey key, PlayerActionSnapshot? actions)
    {
        if (key == default) return -1;
        int count = PlayerActionDockInteractionLayout.GetItemCount(Mode, actions);
        for (int i = 0; i < count; i++) if (ObjectActionRingView.ItemKey(Mode, i, actions) == key) return i;
        return -1;
    }

    private void SynchronizeRingParameter(PlayerActionSnapshot? actions)
    {
        if (!_ringContext || !_ringOpen || _ringLevel != ActionRingLevel.Parameters) return;
        int index = FindRingItem(_parameterItem, actions);
        if (index < 0) { _ringLevel = ActionRingLevel.Items; _ringRevision++; _presentedRing = default; }
        else SelectedIndex = index;
    }

    private void UpdateRingPointer(InputState input, PresentationSnapshot? snapshot, PlayerActionSnapshot? actions,
        in GameplayHudLayout layout, in PlayerActionPanelView panel, bool freshPress)
    {
        bool hasHit = input.HasPointerPosition && _presentedRing.TryHit(input.PointerPosition, out _);
        int hover = hasHit ? _presentedRing.Geometry.Hit(input.PointerPosition) : -1;
        _presentedRing = _presentedRing with { HoveredSlot = hover };
        _hoveredIndex = -1;
        _pointerPressed = hasHit && input.IsMouseButtonDown(PlatformMouseButton.Left);
        PointerCaptured = hasHit;
        if (freshPress && input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin))
        {
            _ringArmed = _presentedRing.TryHit(origin, out _pressedRingSlot) && _pressedRingSlot.Enabled;
            _pressedRing = _presentedRing;

        }
        if (!input.WasMouseButtonReleased(PlatformMouseButton.Left)) return;
        bool armed = _ringArmed; _ringArmed = false;
        if (!armed || input.WheelDelta != 0 || !hasHit ||
            _pressedRing.Revision != _ringRevision || _pressedRing.Session != snapshot?.SessionId ||
            _currentSelection?.Revision != _pressedRing.SelectionRevision ||
            !PlayerActionDockHudModel.MatchesSelection(actions, _currentSelection) ||
            _ringGroup is not null && !_ringGroup.MatchesSelection(actions?.Tactical?.SelectedEntities) ||
            !_presentedRing.TryHit(input.PointerPosition, out var slot) || slot.Key != _pressedRingSlot.Key ||
            slot.Navigation != _pressedRingSlot.Navigation || slot.Target.Kind != _pressedRingSlot.Target.Kind ||
            slot.Target.Mode != _pressedRingSlot.Target.Mode) return;
        if (slot.Target.Kind != PlayerActionDockControlKind.Mode && slot.Navigation == ActionRingNavigation.None &&
            slot.Key != ObjectActionRingView.ItemKey(Mode, slot.Target.ItemIndex, actions)) return;
        PointerCaptured = true;
        int count = _ringLevel == ActionRingLevel.Group ? 12 : PlayerActionDockInteractionLayout.GetItemCount(Mode, actions);
        switch (slot.Navigation)
        {
            case ActionRingNavigation.Group: _ringLevel = ActionRingLevel.Group; _ringPage = 0; break;
            case ActionRingNavigation.GroupControl:
                _groupResult = _ringCard.Activate(slot.Target.ItemIndex, snapshot, _currentSelection!);
                break;
            case ActionRingNavigation.Close: Close(); return;
            case ActionRingNavigation.Back:
                _ringLevel = _ringLevel == ActionRingLevel.Parameters ? ActionRingLevel.Items : ActionRingLevel.Categories;
                if (_ringLevel == ActionRingLevel.Categories) Mode = PlayerActionPanelMode.Closed;
                break;
            case ActionRingNavigation.Previous: _ringPage = Math.Max(0, _ringPage - 1); break;
            case ActionRingNavigation.Next: if ((_ringPage + 1) * 4 < count) _ringPage++; break;
            case ActionRingNavigation.Parameters: _ringLevel = ActionRingLevel.Parameters; _parameterItem = ObjectActionRingView.ItemKey(Mode, SelectedIndex, actions); break;
            case ActionRingNavigation.NextItem:
                if (count > 0) { SelectedIndex = (SelectedIndex + 1) % count; _ringPage = SelectedIndex / 4;
                    _stockResourceId = ResourceId.None; _desiredStockQuantity = 0; SynchronizeStockEditor(actions); SynchronizeSupplyEditor(actions);
                    _parameterItem = ObjectActionRingView.ItemKey(Mode, SelectedIndex, actions); }
                break;
            default:
                HandlePointerHit(slot.Target, actions, panel);
                if (_pendingRequest.HasValue) _contextualActivationTick = snapshot?.Tick;
                break;
        }
        _ringRevision++;
        _presentedRing = default;
    }
}
