using ForgeLine.Input;
using ForgeLine.Platform;

namespace ForgeLine.Client;

internal sealed class GameplayHelpController
{
    private bool _helpHeld;
    private bool _escapeHeld;

    public bool Visible { get; private set; }

    public bool BlocksGameplayThisFrame { get; private set; }

    public void Dismiss(InputState input)
    {
        BlocksGameplayThisFrame = Visible;
        Visible = false;
        input.SuppressHeldInput();
    }

    public bool Update(InputState input, bool enabled)
    {
        bool shift = input.IsKeyDown(PlatformKey.LeftShift) || input.IsKeyDown(PlatformKey.RightShift);
        bool help = !shift && (input.IsKeyDown(PlatformKey.F1) || input.IsKeyDown(PlatformKey.F12));
        bool escape = input.IsKeyDown(PlatformKey.Escape);
        bool wasVisible = Visible;

        if (enabled && help && !_helpHeld)
            Visible = !Visible;
        else if (Visible && escape && !_escapeHeld)
            Visible = false;
        if (!enabled)
            Visible = false;

        _helpHeld = help;
        _escapeHeld = escape;
        BlocksGameplayThisFrame = wasVisible || Visible;
        if (Visible != wasVisible)
            input.SuppressHeldInput();
        return Visible != wasVisible;
    }
}
