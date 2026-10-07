namespace ForgeLine.Presentation;

internal static class RtsStrategicOverlayHudModel
{
    public const int SelectorButtonCount = 7;

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
