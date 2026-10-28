using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public sealed partial class PresentationExtractor
{
    private MatchDebriefSummary? _matchDebrief;
    private SimulationSessionId _debriefSession;
    private bool _debriefAttempted;

    private MatchDebriefSummary? CaptureMatchDebrief(PlayerExperienceSnapshot? experience)
    {
        if (_extraction is null) return null;
        var session = _extraction.Scenario.Simulation.SessionId;
        if (_debriefSession != session)
        { _debriefSession = session; _matchDebrief = null; _debriefAttempted = false; }
        if (experience?.IsMatchComplete != true) return null;
        if (_debriefAttempted) return _matchDebrief;
        _debriefAttempted = true;
        try
        {
            var summary = MatchDebriefCapture.Capture(_extraction.Scenario, _extraction.Player);
            if (summary.Outcome != MatchDebriefOutcome.Incomplete) _matchDebrief = summary;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        { _matchDebrief = null; } // Terminal acknowledgement never depends on optional evidence.
        return _matchDebrief;
    }
}
