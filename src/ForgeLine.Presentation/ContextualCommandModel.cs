using System.Numerics;
using ForgeLine.Game;
using ForgeLine.Input;

namespace ForgeLine.Presentation;

internal readonly record struct ContextualCommand(
    PlayerActionPanelMode Mode, int ItemIndex, string Label, string Shortcut,
    PlayerActionDockItemState Availability)
{
    public bool OpensMode => ItemIndex < 0;
}

internal static class ContextualCommandModel
{
    public static bool HasPending(in PlayerActionPanelView panel, PresentationSnapshot? snapshot) =>
        panel.ContextualPending && panel.ContextualActivationTick == snapshot?.Tick &&
        panel.ContextualSessionId == snapshot?.SessionId;

    public static PlayerActionSnapshot? ResolveActions(PresentationSnapshot? snapshot) =>
        PlayerActionDockHudModel.ResolveActions(snapshot) is { } actions &&
        snapshot!.PlayerExperience is { IsMatchComplete: false } experience && experience.Tick == snapshot.Tick
            ? actions : null;

    public static string Status(PresentationSnapshot? snapshot)
    {
        if (snapshot?.PlayerExperience is { IsMatchComplete: true }) return "MATCH COMPLETE";
        if (ResolveActions(snapshot) is null) return "UPDATING COMMANDS";
        if (snapshot!.PlayerExperience!.Value.Selection.Count == 0) return "SELECT UNIT OR BUILDING";
        return snapshot.PlayerExperience.Value.Selection.Kind == PlayerSelectionKind.Mixed
            ? "MIXED SELECTION - ELIGIBLE UNITS ONLY" : "SELECTION COMMANDS";
    }

    public static int Count(PresentationSnapshot? snapshot)
    {
        int count = 0;
        while (TryGet(snapshot, count, out _)) count++;
        return count;
    }

    public static bool TryGet(PresentationSnapshot? snapshot, int index, out ContextualCommand command,
        bool localPending = false, bool selectionPending = false, GameplayBindingRegistry? bindings = null)
    {
        bindings ??= GameplayBindingRegistry.Default;
        command = default;
        var actions = ResolveActions(snapshot);
        if (index < 0 || actions is null || snapshot!.PlayerExperience is not { } experience ||
            experience.Selection.Count == 0) return false;

        if (actions.Tactical is { CombatEligibleCount: > 0 })
        {
            if (index >= 6) return false;
            command = new(PlayerActionPanelMode.Tactical, index,
                PlayerActionDockHudModel.ResolveItemTitle(PlayerActionPanelMode.Tactical, index, actions),
                bindings.TacticalPrompt,
                selectionPending ? PlayerActionDockItemState.Disabled("UPDATING SELECTION") :
                localPending || actions.PendingCommandCount > 0 ? PlayerActionDockItemState.Disabled("REQUEST PENDING") :
                    PlayerActionDockHudModel.ResolveItemState(PlayerActionPanelMode.Tactical, index, actions));
            return true;
        }

        PlayerActionPanelMode mode = PlayerActionPanelMode.Closed;
        if (experience.Selection.CommonBuildingId == BuildingIds.CommandCore && actions.Construction.Count > 0 && index-- == 0)
            mode = PlayerActionPanelMode.Construction;
        else if (actions.UnitProduction is not null && index-- == 0) mode = PlayerActionPanelMode.UnitProduction;
        else if (actions.Production is { IsAutomatic: false } && index-- == 0) mode = PlayerActionPanelMode.Production;
        else if (actions.Logistics is not null && index-- == 0) mode = PlayerActionPanelMode.Logistics;
        else if (actions.Supply is not null && index-- == 0) mode = PlayerActionPanelMode.Supply;
        if (mode == PlayerActionPanelMode.Closed) return false;
        command = new(mode, -1, PlayerActionDockHudModel.ResolveModeLabel(mode),
            PlayerActionDockHudModel.ResolveModeShortcut(mode, bindings), selectionPending ?
                PlayerActionDockItemState.Disabled("UPDATING SELECTION") : PlayerActionDockItemState.Enabled);
        return true;
    }

}
