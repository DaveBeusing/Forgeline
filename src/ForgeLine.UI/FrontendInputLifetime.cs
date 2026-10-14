using ForgeLine.Input;
using ForgeLine.Presentation;

namespace ForgeLine.UI;

/// <summary>Invalidates transient pointer/key gestures when their coordinate space or owner changes.</summary>
public sealed class FrontendInputLifetime
{
    private FrontendGeometry _layout;
    private int _owner = -1;
    private bool _active;
    public bool Update(InputState input, FrontendGeometry layout, int owner, bool active)
    {
        ArgumentNullException.ThrowIfNull(input);
        bool changed = layout != _layout || owner != _owner || active != _active || input.FocusLostThisFrame;
        _layout = layout; _owner = owner; _active = active;
        if (changed || !active || layout.IsEmpty) input.SuppressHeldInput();
        return changed;
    }
}
