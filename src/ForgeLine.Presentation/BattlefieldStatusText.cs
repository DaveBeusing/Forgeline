using ForgeLine.Combat;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal static class BattlefieldStatusText
{
    public static string Suppression(PlayerBattlefieldStatus status) => status.Suppression switch
    {
        SuppressionLevel.Pinned => "PINNED / MOVEMENT AND DIRECT FIRE BLOCKED",
        SuppressionLevel.Suppressed => "SUPPRESSED / MOVEMENT REDUCED",
        _ => string.Empty
    };

    public static string Fire(PlayerBattlefieldStatus status, bool hasSupply, double ammunition)
    {
        if (!status.IsArtillery) return string.Empty;
        if (status.FireMission == FireMissionStatus.NoAmmo)
            return "CANNOT FIRE / INSUFFICIENT AMMO FOR A SHOT / RESUPPLY";
        if (hasSupply && ammunition <= 0)
            return "AMMO EMPTY / RESUPPLY BEFORE FIRE";
        return status.FireMission switch
        {
            FireMissionStatus.Ordered => "FIRE MISSION ORDERED",
            FireMissionStatus.Acquiring => "FIRE MISSION / ACQUIRING",
            FireMissionStatus.WaitingReload => "CANNOT FIRE YET / RELOADING",
            FireMissionStatus.Firing => "FIRE MISSION / FIRING",
            FireMissionStatus.Complete => "FIRE MISSION COMPLETE",
            FireMissionStatus.Cancelled => "FIRE MISSION CANCELLED",
            _ => "NO ACTIVE FIRE MISSION"
        };
    }
}
