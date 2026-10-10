namespace ForgeLine.Presentation;

public static class RtsStrategicOverlayHudModel
{
    public const int SelectorButtonCount = 7;

    public static string Label(StrategicOverlayMode mode) => mode switch
    {
        StrategicOverlayMode.Logistics => "ROUTES", StrategicOverlayMode.Supply => "SUPPLY",
        StrategicOverlayMode.Sensors => "SENSORS", StrategicOverlayMode.Navigation => "NAVIGATION",
        StrategicOverlayMode.Power => "POWER", StrategicOverlayMode.All => "ALL LAYERS", _ => "OVERLAYS OFF"
    };
    public static string Legend(StrategicOverlayMode mode) => mode switch
    {
        StrategicOverlayMode.Logistics => "LINE LINK X OFF - LOAD N/A",
        StrategicOverlayMode.Supply => "RING RANGE X CRITICAL/OFF",
        StrategicOverlayMode.Sensors => "RINGS VISUAL/RADAR RANGE",
        StrategicOverlayMode.Navigation => "BOX SECTOR POINT PORTAL",
        StrategicOverlayMode.Power => "X OFF DIAMOND BROWNOUT",
        StrategicOverlayMode.All => "LOCAL STATES - LOAD N/A", _ => "F10 LAYERS F11 MINIMAP"
    };
    public static bool IsCurrent(PresentationSnapshot? snapshot, StrategicOverlayMode mode) =>
        snapshot is { SessionId.IsSpecified: true, PlayerExperience: { } experience, StrategicOverlay: { } overlay } &&
        !experience.IsMatchComplete && experience.Tick == snapshot.Tick && overlay.Tick == snapshot.Tick &&
        overlay.Session == snapshot.SessionId && overlay.Player == experience.Player && overlay.RequestedMode == mode;

    public static StrategicOverlayMode ModeForButton(
        int index) =>
        index switch
        {
            0 =>
                StrategicOverlayMode.None,
            1 =>
                StrategicOverlayMode.Logistics,
            2 =>
                StrategicOverlayMode.Supply,
            3 =>
                StrategicOverlayMode.Sensors,
            4 =>
                StrategicOverlayMode.Navigation,
            5 =>
                StrategicOverlayMode.Power,
            6 =>
                StrategicOverlayMode.All,
            _ =>
                StrategicOverlayMode.None
        };

    public static string ResolveShortLabel(
        StrategicOverlayMode mode) =>
        mode switch
        {
            StrategicOverlayMode.Logistics =>
                "LOG",
            StrategicOverlayMode.Supply =>
                "SUP",
            StrategicOverlayMode.Sensors =>
                "SEN",
            StrategicOverlayMode.Navigation =>
                "NAV",
            StrategicOverlayMode.Power =>
                "PWR",
            StrategicOverlayMode.All =>
                "ALL",
            _ =>
                "OFF"
        };

    public static RtsUiIcon ResolveIcon(
        StrategicOverlayMode mode) =>
        mode switch
        {
            StrategicOverlayMode.Logistics =>
                RtsUiIcon.UnitLogistics,
            StrategicOverlayMode.Supply =>
                RtsUiIcon.CommandSupply,
            StrategicOverlayMode.Sensors =>
                RtsUiIcon.UnitReconnaissance,
            StrategicOverlayMode.Navigation =>
                RtsUiIcon.CommandMove,
            StrategicOverlayMode.Power =>
                RtsUiIcon.StatusPower,
            StrategicOverlayMode.All =>
                RtsUiIcon.MinimapSelectedGroup,
            _ =>
                RtsUiIcon.CommandCancel
        };
}
