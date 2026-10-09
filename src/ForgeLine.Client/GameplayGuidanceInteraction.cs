using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

internal sealed class GameplayGuidanceInteraction
{
    private SimulationSessionId _session;
    private bool _held;
    public bool Hidden { get; private set; }
    public bool BlocksGameplayThisFrame { get; private set; }

    public void Update(InputState input, SimulationSessionId session, bool enabled)
    {
        if (session.IsSpecified && session != _session) { _session = session; Hidden = false; }
        bool down = input.IsKeyDown(PlatformKey.F12) &&
            (input.IsKeyDown(PlatformKey.LeftShift) || input.IsKeyDown(PlatformKey.RightShift));
        BlocksGameplayThisFrame = enabled && down && !_held;
        _held = down;
        if (!BlocksGameplayThisFrame) return;
        Hidden = !Hidden;
        input.SuppressHeldInput();
    }
}
