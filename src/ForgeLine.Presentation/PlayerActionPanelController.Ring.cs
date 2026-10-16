using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public sealed partial class PlayerActionPanelController
{
    public bool SingleObjectActions => _ringSingle;
    private bool _ringSingle, _ringOpen;
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
    private ActionRingSlots _itemSlots;
    private PlayerActionPanelMode _itemSlotsMode;
    private int _itemSlotsPage = -1;
    private ActionRingKey _parameterItem;


    /// <summary>Called after selection is decided. The opening gesture never dispatches a ring action.</summary>
    public void SynchronizeObjectRing(InputState input, PresentationSnapshot? snapshot, RenderWorld world,
        SelectionSet selection, bool allowSelectionOpen, bool selectionResolved = false)
    {
        EntityId entity = default;
        if (selection.Count == 1) foreach (var selected in selection.Entities) entity = selected;
        bool eligible = snapshot?.PlayerExperience is { IsMatchComplete: false } experience &&
            world.TryGetInterpolatedInstance(entity, 1, out var instance) && (instance.Visibility & RenderVisibilityMask.World) != 0 && instance.Selectable.IsSelectable &&
            instance.Selectable.Owner == experience.Player;
        var session = snapshot?.SessionId ?? default;
        bool changed = entity != _ringEntity || session != _ringSession || selection.Revision != _ringSelection;
        if (changed || !eligible) { Close(); _presentedRing = default; }
        _ringSingle = eligible;
        _ringEntity = eligible ? entity : default;
        _ringSession = session;
        _ringSelection = selection.Revision;
        ulong sequence = input.MousePressSequence(PlatformMouseButton.Left);
        bool freshSelection = allowSelectionOpen && sequence != _selectionClickSequence &&
            (input.WasMouseButtonReleased(PlatformMouseButton.Left) || !input.IsMouseButtonDown(PlatformMouseButton.Left));
        if (eligible && freshSelection && input.TryGetMousePressPosition(PlatformMouseButton.Left, out _))
        {
            _ringOpen = true; _ringLevel = ActionRingLevel.Categories; Mode = PlayerActionPanelMode.Closed;
            _itemSlotsPage = -1;
            _ringRevision++; _ringArmed = false;
        }
        if (selectionResolved)
            _selectionClickSequence = sequence;
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
        var ring = new ObjectActionRingView(_ringEntity, _ringSession, _ringSelection, _ringRevision, _ringSingle,
            _ringOpen, _presentedRing.Geometry, _presentedRing.Resolved, HoveredSlot: _presentedRing.HoveredSlot, Pressed: _pointerPressed)
            { Level = _ringLevel };
        if (!_ringSingle || !_ringOpen) return ring;
        ActionRingSlots slots = default;
        if (_ringLevel == ActionRingLevel.Categories)
        {
            for (int i = 0; i < 7; i++)
            {
                var mode = PlayerActionDockInteractionLayout.ModeForButton(i);
                bool present = PlayerActionDockInteractionLayout.GetItemCount(mode, actions) > 0 &&
                    (mode != PlayerActionPanelMode.Tactical || actions?.Tactical is { } tactical &&
                        (tactical.CombatEligibleCount > 0 || tactical.Artillery.Count > 0));
                slots[i] = new(new(PlayerActionDockControlKind.Mode, mode), default, Present: present);
            }
            slots[7] = new(default, default, ActionRingNavigation.Close);
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
        if (!_ringSingle || !_ringOpen || _ringLevel != ActionRingLevel.Parameters) return;
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
        PointerCaptured = hasHit || PlayerActionDockInteractionLayout.CapturesPointer(input.PointerPosition, layout, false);
        if (freshPress && input.TryGetMousePressPosition(PlatformMouseButton.Left, out var origin))
        {
            _ringArmed = _presentedRing.TryHit(origin, out _pressedRingSlot);
            _pressedRing = _presentedRing;
            if (!_ringArmed && PlayerActionDockInteractionLayout.TryHit(origin, layout, false, 0, out var mode))
            { HandlePointerHit(mode, actions, panel); PointerCaptured = true; }
        }
        if (!input.WasMouseButtonReleased(PlatformMouseButton.Left)) return;
        bool armed = _ringArmed; _ringArmed = false;
        if (!armed || input.WheelDelta != 0 || !hasHit ||
            _pressedRing.Revision != _ringRevision || _pressedRing.Session != snapshot?.SessionId ||
            _currentSelection?.Revision != _pressedRing.SelectionRevision ||
            !PlayerActionDockHudModel.MatchesSelection(actions, _currentSelection) ||
            !_presentedRing.TryHit(input.PointerPosition, out var slot) || slot.Key != _pressedRingSlot.Key ||
            slot.Navigation != _pressedRingSlot.Navigation || slot.Target.Kind != _pressedRingSlot.Target.Kind ||
            slot.Target.Mode != _pressedRingSlot.Target.Mode) return;
        if (slot.Target.Kind != PlayerActionDockControlKind.Mode && slot.Navigation == ActionRingNavigation.None &&
            slot.Key != ObjectActionRingView.ItemKey(Mode, slot.Target.ItemIndex, actions)) return;
        PointerCaptured = true;
        int count = PlayerActionDockInteractionLayout.GetItemCount(Mode, actions);
        switch (slot.Navigation)
        {
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
