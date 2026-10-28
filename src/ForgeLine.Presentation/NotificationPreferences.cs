using ForgeLine.Game;

namespace ForgeLine.Presentation;

public enum NotificationMode : byte { All, Batched, Quiet }

public sealed record NotificationPreferences
{
    public static NotificationPreferences Default { get; } = new();
    public NotificationMode Mode { get; init; }
    public bool ShowInformational { get; init; } = true;
    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) throw new InvalidDataException("Unknown notification mode.");
    }
    public PlayerExperienceSnapshot Apply(in PlayerExperienceSnapshot experience)
    {
        PlayerAlertState visible = experience.Alerts;
        if (!ShowInformational || Mode == NotificationMode.Quiet) visible &= ~PlayerAlertState.WorkCompleted;
        if (Mode == NotificationMode.Quiet)
            visible &= PlayerAlertState.CommandCoreDestroyed | PlayerAlertState.CommandCoreUnderAttack |
                PlayerAlertState.CommandCoreDamaged | PlayerAlertState.SupplyCritical;
        return experience with { Alerts = visible };
    }
}
