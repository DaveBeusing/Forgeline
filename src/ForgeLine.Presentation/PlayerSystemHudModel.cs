using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal static class PlayerSystemHudModel
{
    public const int HelpLineCount = 7;

    public const string OnboardingHint =
        "F12 HELP  ESC PAUSE  GOAL DESTROY ENEMY COMMAND CORE";

    public static string ResolveMatchResultLabel(
        PlayerMatchStatus status) =>
        status switch
        {
            PlayerMatchStatus.Victory =>
                "VICTORY",
            PlayerMatchStatus.Defeat =>
                "DEFEAT",
            PlayerMatchStatus.Draw =>
                "DRAW",
            _ =>
                "MATCH COMPLETE"
        };

    public static string GetHelpLine(
        int index) =>
        index switch
        {
            0 =>
                "WASD CAMERA  QE ROTATE  ARROWS PITCH  WHEEL ZOOM",
            1 =>
                "LEFT CLICK SELECT  SHIFT LEFT CLICK MULTI SELECT",
            2 =>
                "RIGHT CLICK MOVE  K COMBAT  H TECHNOLOGY",
            3 =>
                "B BUILD  P PROCESS  U UNITS  L LOGISTICS  Y SUPPLY",
            4 =>
                "F10 STRATEGIC OVERLAY  F11 MINIMAP  F12 CLOSE HELP",
            5 =>
                "ESC OR SPACE PAUSE  F1 METRICS  F2 WORLD DEBUG",
            6 =>
                "OBJECTIVE DESTROY THE ENEMY COMMAND CORE",
            _ =>
                string.Empty
        };
}
