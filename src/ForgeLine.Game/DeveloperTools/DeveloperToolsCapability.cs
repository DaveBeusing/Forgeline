using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>Build/host authority, never reconstructed from match settings or persisted data.</summary>
public sealed class DeveloperToolsCapability
{
    private DeveloperToolsCapability(bool compiled) => IsCompiled = compiled;
    public bool IsCompiled { get; }
    public string BuildVariant => IsCompiled ? "Developer" : "Public";
    public static DeveloperToolsCapability Current { get; } = new(Compiled());
    private static bool Compiled()
    {
#if FORGELINE_DEVELOPER_TOOLS
        return true;
#else
        return false;
#endif
    }
    public DeveloperSessionAccess BindSession(SimulationSessionId session, bool activated)
    {
        if (!session.IsSpecified) throw new ArgumentException("A live session is required.", nameof(session));
        return new(this, session, activated && IsCompiled);
    }
}

/// <summary>Ephemeral host permission; a new/restored runtime must bind its own new session.</summary>
public sealed class DeveloperSessionAccess
{
    internal DeveloperSessionAccess(DeveloperToolsCapability capability, SimulationSessionId session, bool active)
    { Capability = capability; Session = session; IsActive = active; }
    public DeveloperToolsCapability Capability { get; }
    public SimulationSessionId Session { get; }
    public bool IsActive { get; }
}
