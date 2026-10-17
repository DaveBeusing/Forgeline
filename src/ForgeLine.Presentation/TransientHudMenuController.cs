using System.Numerics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Registration boundary for transient context/ring menus. Closed visuals own no input.</summary>
public interface ITransientHudMenu
{
    bool IsOpen { get; }
    bool ContainsControl(Vector2 point, in GameplayHudLayout layout);
    void Dismiss(InputState input);
}

public readonly record struct HudPointerGesture(int PointerId, PlatformMouseButton Button,
    Vector2 Origin, bool Shift, bool Control, ulong PressSequence, ulong LayoutRevision,
    SimulationSessionId Session, ulong SelectionRevision, bool MenuOwned);

/// <summary>Routes the existing input once; never replays input or submits gameplay commands.</summary>
public sealed class TransientHudMenuController
{
    private readonly ITransientHudMenu?[] _contextMenus = new ITransientHudMenu[4];
    private readonly ulong[] _press = new ulong[3];
    private readonly bool[] _owned = new bool[3];
    private readonly bool[] _menuOwned = new bool[3];
    private GameplayHudLayout _layout;
    private SimulationSessionId _session;
    private ulong _selection;
    private SelectionSet? _selectionOwner;
    private bool _initialized;
    public ulong LayoutRevision { get; private set; }
    public HudPointerGesture LastGesture { get; private set; }
    public bool PointerCaptured { get; private set; }

    public void RegisterContextMenu(ITransientHudMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        for (int i = 0; i < _contextMenus.Length; i++)
        {
            if (ReferenceEquals(_contextMenus[i], menu)) return;
            if (_contextMenus[i] is null) { _contextMenus[i] = menu; return; }
        }
        throw new InvalidOperationException("Transient context menu capacity exceeded.");
    }

    public bool HasOpenMenus(PlayerActionPanelController dock, OperationsController operations) =>
        dock.HasOpenMenu || operations.View.Open || HasOpenContextMenu();

    private bool HasOpenContextMenu()
    {
        foreach (var menu in _contextMenus) if (menu?.IsOpen == true) return true;
        return false;
    }

    public void Dismiss(InputState input, PlayerActionPanelController dock, OperationsController operations,
        PresentationInteractionState interaction, bool preserveSelectionGesture = false)
    {
        dock.DismissTransient(input, preserveSelectionGesture);
        operations.Close(input, interaction);
        foreach (var menu in _contextMenus) if (menu?.IsOpen == true) menu.Dismiss(input);
    }

    public bool ConsumeEscape(InputState input, PlayerActionPanelController dock, OperationsController operations,
        PresentationInteractionState interaction, bool blocked)
    {
        if (blocked || !input.WasKeyPressed(PlatformKey.Escape) || !HasOpenMenus(dock, operations)) return false;
        Dismiss(input, dock, operations, interaction);
        input.SuppressKeyUntilRelease(PlatformKey.Escape);
        return true;
    }

    public void Update(InputState input, PresentationSnapshot? snapshot, in GameplayHudLayout layout,
        SelectionSet selection, PlayerActionPanelController dock, OperationsController operations,
        PresentationInteractionState interaction, bool minimapEnabled, bool guidanceVisible, bool blocked = false, string localFeedback = "", OnboardingPresentation? onboarding = null)
    {
        var session = snapshot?.SessionId ?? default;
        ulong identity = selection.Revision;
        bool changed = _initialized && (_layout != layout || _session != session ||
            !ReferenceEquals(_selectionOwner, selection) || _selection != identity);
        if (!_initialized || _layout != layout) LayoutRevision++;
        _layout = layout; _session = session; _selection = identity; _selectionOwner = selection; _initialized = true;
        PointerCaptured = false;
        if (changed || blocked || input.FocusLostThisFrame || !input.HasPointerPosition ||
            !session.IsSpecified || snapshot?.PlayerExperience?.IsMatchComplete == true)
        {
            Dismiss(input, dock, operations, interaction);
            for (int i = 0; i < 3; i++)
            {
                var button = Button(i);
                _press[i] = input.MousePressSequence(button);
                _owned[i] = input.IsMouseButtonDown(button);
            }
            PointerCaptured = true;
            return;
        }
        for (int i = 0; i < 3; i++)
        {
            var button = Button(i);
            ulong press = input.MousePressSequence(button);
            bool fresh = press != _press[i]; _press[i] = press;
            if (fresh && input.TryGetMousePressPosition(button, out var origin))
            {
                bool menuOwned = dock.ContainsControl(origin, snapshot, layout) || operations.ContainsSurface(origin, layout);
                foreach (var menu in _contextMenus)
                    menuOwned |= menu?.IsOpen == true && menu.ContainsControl(origin, layout);
                bool persistent = HudInteractionContext.BlocksWorldPointer(origin, layout, minimapEnabled,
                    guidanceVisible: guidanceVisible, includeActionDock: false, onboarding: onboarding, includePrimaryCommands: !dock.ObjectActions) ||
                    OperationsLayout.Entry(layout).Contains(origin) ||
                    snapshot?.PlayerExperience is not null && ActionableAlertLayout.Resolve(snapshot, layout, dock.DockExpanded, localFeedback).Contains(origin);
                _menuOwned[i] = menuOwned;
                _owned[i] = menuOwned || persistent;
                LastGesture = new(0, button, origin, input.WasMousePressedWithShift(button),
                    input.WasMousePressedWithControl(button), press, LayoutRevision, session, identity, menuOwned);
                if (!menuOwned && !persistent) Dismiss(input, dock, operations, interaction);
            }
            if (_owned[i] && _menuOwned[i] && input.WasMouseButtonReleased(button))
            {
                bool inside = dock.ContainsControl(input.PointerPosition, snapshot, layout) || operations.ContainsSurface(input.PointerPosition, layout);
                foreach (var menu in _contextMenus)
                    inside |= menu?.IsOpen == true && menu.ContainsControl(input.PointerPosition, layout);
                if (!inside) Dismiss(input, dock, operations, interaction);
            }
            // Retain ownership through the release frame, even outside a dismissed menu.
            PointerCaptured |= _owned[i];
            if (!input.IsMouseButtonDown(button)) _owned[i] = false;
        }
    }

    public void SelectionCommitted(InputState input, SelectionSet selection, PlayerActionPanelController dock,
        OperationsController operations, PresentationInteractionState interaction)
    {
        if (!ReferenceEquals(_selectionOwner, selection) || _selection != selection.Revision)
            Dismiss(input, dock, operations, interaction, preserveSelectionGesture: true);
        _selectionOwner = selection;
        _selection = selection.Revision;
    }

    private static PlatformMouseButton Button(int index) => index switch
    { 0 => PlatformMouseButton.Left, 1 => PlatformMouseButton.Right, _ => PlatformMouseButton.Middle };
}
