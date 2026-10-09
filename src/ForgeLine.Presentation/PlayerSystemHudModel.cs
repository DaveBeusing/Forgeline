using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal static class PlayerSystemHudModel
{
    public const int HelpLineCount = 10;

    public const string OnboardingHint =
        "HOME BASE  F1 HELP  SPACE PAUSE  SHIFT+F12 GUIDE";

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
                "WASD/ARROWS PAN  QE ROTATE  RF PITCH  WHEEL ZOOM",
            1 =>
                "LEFT SELECT  DOUBLE LEFT VISIBLE SAME-TYPE UNITS  SHIFT LEFT MULTI",
            2 =>
                "RIGHT CLICK MOVE  K COMBAT  H TECHNOLOGY",
            3 =>
                "B BUILD  P PROCESS  U UNITS  L LOGISTICS  Y SUPPLY",
            4 =>
                "F10 STRATEGIC OVERLAY  F11 MINIMAP  F1/F12/ESC CLOSE HELP",
            5 =>
                "ESC OR SPACE PAUSE  SHIFT+F1 METRICS  F2 WORLD DEBUG",
            6 => "SHIFT+F12 SHOW / HIDE OPTIONAL MATCH GUIDE",
            7 => "HOME BASE FOCUS  CTRL+DIGIT ASSIGN  DOUBLE DIGIT GROUP FOCUS",
            8 => "SHIFT+CLICK REPEAT BUILD AFTER ACCEPTANCE  F9 ROTATE  ESC CANCEL",
            9 => "OBJECTIVE DESTROY THE ENEMY COMMAND CORE",
            _ =>
                string.Empty
        };
}
