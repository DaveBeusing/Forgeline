namespace ForgeLine.Presentation;

public enum PreAlphaUxMode
{
    None = 0,
    MatchSetup = 1,
    Paused = 2,
    Help = 3
}

public readonly record struct PreAlphaUxView(
    PreAlphaUxMode Mode,
    bool ShowOnboarding,
    string MapName,
    string PlayerFaction,
    string OpponentDescription,
    string PanForwardBinding,
    string PanBackwardBinding,
    string PanLeftBinding,
    string PanRightBinding,
    string RotateLeftBinding,
    string RotateRightBinding,
    string PitchUpBinding,
    string PitchDownBinding,
    string DragPanBinding,
    string SettingsPath,
    EarlyGameGuidanceView Guidance = default,
    PlacementContextFeedbackView Placement = default,
    string InteractionHint = "");
